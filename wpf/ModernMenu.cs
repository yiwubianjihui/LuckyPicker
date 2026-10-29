// ================================================================
// ModernMenu.cs — Windows 11 风格现代弹出菜单（替代传统 ContextMenu）
//
// 特性：圆角卡片 + 系统阴影、统一间距与配色、跟随系统亮/暗主题、
//       支持普通项 / 勾选项（单选、多选）/ 分隔线，点击外部自动关闭。
//
// ---------------------------------------------------------------
// 【为什么用真实 Window 而不是 Popup】（26H2 Build 17 重写）
//
// 「托盘右键菜单跑到屏幕左上角」这个问题先后修了三次都没根治：
//   1) 用 Application.Current.MainWindow 当 PlacementTarget
//      → 主窗口 Hide() 后没有 PresentationSource，定位失效；
//   2) 改用常驻 1×1 透明宿主窗口当锚点 + PlacementMode.AbsolutePoint
//      → 仍然会错位（Popup 的绝对定位依赖锚点的 PresentationSource 与
//        其所在显示器的 DPI 上下文，主窗口隐藏、跨显示器、缩放非 100%
//        等组合下会退化成 (0,0)）。
//   Popup 的绝对定位本质上不可靠，因为真正决定位置的是 WPF 的 popup
//   定位引擎，而不是我们算出来的坐标。
//
// 现在改为**真实 Window**：Left/Top 由系统直接摆放，我们算出的 DIP 坐标
// 就是最终位置，不经过任何定位引擎。同时自检用 Win32 GetWindowRect 读
// **真实屏幕矩形**做断言（而不是断言我们自己算的中间值）——
// 这样定位一旦再退化，自检立刻报警。
//
// 投影也不再挂在卡片上：Effect 会把控件（含文字）先渲染成中间位图再合成，
// 非整数 DPI/布局下文字发虚（与 Build 17 修的「按钮模糊」同一个根因）。
// 这里改由底下那层**纯色 Border** 承担投影——它没有细节，被光栅化无所谓。
// ================================================================
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Microsoft.Win32;

namespace LuckyPickerWpf
{
    /// <summary>系统主题（亮/暗）跟随：读取 Windows 10/11 的 AppsUseLightTheme。</summary>
    public static class WinTheme
    {
        public static bool IsLight()
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var v = k?.GetValue("AppsUseLightTheme");
                if (v is int i) return i != 0;
            }
            catch { }
            return true;
        }

        // 主题色板
        public static Color CardBg => IsLight() ? Color.FromRgb(252, 252, 252) : Color.FromRgb(44, 44, 44);
        public static Color CardBorder => IsLight() ? Color.FromRgb(224, 224, 224) : Color.FromRgb(66, 66, 66);
        public static Color ItemFg => IsLight() ? Color.FromRgb(32, 32, 32) : Color.FromRgb(240, 240, 240);
        public static Color ItemHover => IsLight() ? Color.FromRgb(243, 244, 246) : Color.FromRgb(58, 58, 58);
        public static Color SepColor => IsLight() ? Color.FromRgb(229, 229, 229) : Color.FromRgb(72, 72, 72);
        public static Color Accent => ThemeManager.Current.Accent;
    }

    public class ModernMenu
    {
        /// <summary>投影留白（DIP）：窗口四周留出的空间，让投影不被窗口边界裁掉。</summary>
        const double ShadowPad = 10;

        readonly Window win;
        readonly StackPanel panel;
        readonly Border card;
        readonly Grid root;
        bool realized;

        public ModernMenu(double width = 220)
        {
            panel = new StackPanel { Margin = new Thickness(6) };

            card = new Border
            {
                Background = new SolidColorBrush(WinTheme.CardBg),
                BorderBrush = new SolidColorBrush(WinTheme.CardBorder),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(0),
                MinWidth = width,
                Child = panel
            };

            // 投影层：与卡片同尺寸同位置（被卡片完全盖住，只露出模糊的晕）。
            // 它是纯色块 —— 被 Effect 光栅化没有细节损失，所以文字保持锐利。
            var shadow = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Colors.Black),
                Opacity = 0.5,
                IsHitTestVisible = false,
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black, Opacity = 1.0, BlurRadius = 14,
                    ShadowDepth = 2, Direction = 270
                }
            };

            root = new Grid { Margin = new Thickness(ShadowPad) };
            root.Children.Add(shadow);
            root.Children.Add(card);

            win = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowInTaskbar = false,
                Topmost = true,
                ShowActivated = true,
                ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.Manual,
                UseLayoutRounding = true,
                SnapsToDevicePixels = true,
                Content = root,
                Opacity = 0            // 定位完成前不可见，避免闪一下再跳
            };
            win.Deactivated += (s, e) => Close();
        }

        /// <summary>普通菜单项。</summary>
        public ModernMenu AddItem(string text, Action onClick)
        {
            var b = MakeRow(text, false);
            b.Click += (s, e) => { Close(); onClick?.Invoke(); };
            panel.Children.Add(b);
            return this;
        }

        /// <summary>可勾选菜单项（checked 变化时回调）。</summary>
        public MenuCheckItem AddCheck(string text, bool isChecked, Action<bool> onChanged)
        {
            var item = new MenuCheckItem(text, isChecked, onChanged, this);
            panel.Children.Add(item.View);
            return item;
        }

        public ModernMenu AddSeparator()
        {
            panel.Children.Add(new Border
            {
                Height = 1,
                Background = new SolidColorBrush(WinTheme.SepColor),
                Margin = new Thickness(8, 4, 8, 4)
            });
            return this;
        }

        Button MakeRow(string text, bool bold)
        {
            var b = new Button
            {
                Content = text,
                FontSize = 13,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                Height = 34,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(2, 1, 2, 1),
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(WinTheme.ItemFg),
                BorderThickness = new Thickness(0)
            };
            // Win11 风格：悬停圆角高亮
            var tpl = new ControlTemplate(typeof(Button));
            var bd = new FrameworkElementFactory(typeof(Border));
            bd.Name = "bd";
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            bd.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            var tb = new FrameworkElementFactory(typeof(ContentPresenter));
            tb.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            tb.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            tb.SetValue(ContentPresenter.MarginProperty, new Thickness(12, 0, 12, 0));
            bd.AppendChild(tb);
            tpl.VisualTree = bd;
            var trig = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            trig.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(WinTheme.ItemHover), "bd"));
            tpl.Triggers.Add(trig);
            b.Template = tpl;
            return b;
        }

        // ---------------------------------------------------------------
        // 定位
        // ---------------------------------------------------------------

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int X, Y; }

        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hWnd, out RECT r);

        [DllImport("user32.dll")]
        static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        [DllImport("shcore.dll")]
        static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

        const uint MONITOR_DEFAULTTONEAREST = 2;
        const int MDT_EFFECTIVE_DPI = 0;

        /// <summary>最近一次使用的屏幕坐标（DIP，供自检/诊断使用）。</summary>
        public Point? LastScreenOffset { get; private set; }

        /// <summary>最近一次菜单的尺寸（DIP，供自检/诊断使用）。</summary>
        public Size? LastMenuSize { get; private set; }

        /// <summary>窗口句柄（自检用 Win32 读真实矩形）。</summary>
        public IntPtr Handle => new WindowInteropHelper(win).Handle;

        public bool IsOpen => realized && win.IsVisible;

        /// <summary>某个窗口的**真实**屏幕矩形（设备像素，Win32 口径）。</summary>
        public static Rect WindowRectPx(Window w)
        {
            try
            {
                var h = new WindowInteropHelper(w).Handle;
                if (h != IntPtr.Zero && GetWindowRect(h, out var r))
                    return new Rect(r.Left, r.Top, Math.Max(0, r.Right - r.Left), Math.Max(0, r.Bottom - r.Top));
            }
            catch { }
            return new Rect(0, 0, 0, 0);
        }

        /// <summary>菜单窗口的真实屏幕矩形（设备像素）——自检断言的依据。</summary>
        public Rect ActualRectPx() => WindowRectPx(win);

        /// <summary>菜单窗口所在显示器的缩放系数（窗口未创建时退回目标点推算）。</summary>
        double Scale
        {
            get
            {
                try
                {
                    var src = PresentationSource.FromVisual(win);
                    if (src?.CompositionTarget != null)
                    {
                        double m = src.CompositionTarget.TransformToDevice.M11;
                        if (m > 0.1) return m;
                    }
                }
                catch { }
                return 1.0;
            }
        }

        /// <summary>某个设备像素点所在显示器的缩放系数。</summary>
        static double DpiScaleAtDevicePoint(double px, double py)
        {
            try
            {
                var mon = MonitorFromPoint(
                    new POINT { X = (int)px, Y = (int)py }, MONITOR_DEFAULTTONEAREST);
                if (mon != IntPtr.Zero && GetDpiForMonitor(mon, MDT_EFFECTIVE_DPI, out uint dx, out _) == 0 && dx > 0)
                    return dx / 96.0;
            }
            catch { }
            return 1.0;
        }

        /// <summary>某个设备像素点所在显示器的工作区（设备像素）。</summary>
        static Rect WorkAreaPx(double px, double py)
        {
            try
            {
                var wa = System.Windows.Forms.Screen.FromPoint(
                    new System.Drawing.Point((int)px, (int)py)).WorkingArea;
                return new Rect(wa.Left, wa.Top, wa.Width, wa.Height);
            }
            catch { return new Rect(0, 0, 1920, 1080); }
        }

        /// <summary>
        /// 让窗口落地：先按目标显示器缩放把它摆到目标附近（避免出现在别的屏），
        /// 再 Show 出来完成布局。整个过程窗口是透明的（Opacity=0）。
        /// </summary>
        void Realize(double nearPx, double nearPy)
        {
            if (realized) return;
            realized = true;
            double s0 = DpiScaleAtDevicePoint(nearPx, nearPy);
            win.Left = nearPx / s0;
            win.Top = nearPy / s0;
            win.Show();
            win.UpdateLayout();   // 确保 ActualWidth/Height 已就绪，才能算边界翻转
        }

        /// <summary>把窗口摆到指定设备像素位置并显形；随后用 Win32 回读真实矩形。</summary>
        void PlacePx(double xPx, double yPx)
        {
            double s = Scale;
            win.Left = xPx / s;
            win.Top = yPx / s;
            win.Opacity = 1;

            LastScreenOffset = new Point(win.Left, win.Top);
            LastMenuSize = new Size(win.ActualWidth, win.ActualHeight);
        }

        /// <summary>菜单实际占用的设备像素尺寸（含投影留白）。</summary>
        void MenuSizePx(out double w, out double h)
        {
            double s = Scale;
            w = (win.ActualWidth > 0 ? win.ActualWidth : card.MinWidth + ShadowPad * 2) * s;
            h = (win.ActualHeight > 0 ? win.ActualHeight : 160) * s;
        }

        /// <summary>
        /// 在屏幕绝对坐标弹出（用于系统托盘菜单）：菜单贴着托盘图标弹出。
        /// </summary>
        /// <param name="screenX">设备像素 X（来自 NotifyIcon 的 e.Location）</param>
        /// <param name="screenY">设备像素 Y</param>
        public void ShowAtScreen(double screenX, double screenY)
        {
            Realize(screenX, screenY);
            MenuSizePx(out double mw, out double mh);
            var wa = WorkAreaPx(screenX, screenY);

            // 水平：左缘对齐图标；右侧放不下则翻到图标左边
            double x = screenX;
            if (x + mw > wa.Right - 4) x = screenX - mw;
            if (x < wa.Left + 4) x = wa.Left + 4;
            if (x + mw > wa.Right - 4) x = wa.Right - mw - 4;

            // 垂直：托盘在屏幕底部，优先贴着图标上方弹出；上方不够再落到下方
            double y = screenY - mh - 8;
            if (y < wa.Top + 4) y = Math.Min(screenY + 8, wa.Bottom - mh - 4);
            if (y < wa.Top + 4) y = wa.Top + 4;

            PlacePx(x, y);
        }

        /// <summary>在某个窗口（悬浮球）旁边弹出：优先右侧，放不下则左侧；垂直居中对齐。</summary>
        public void ShowBeside(Window anchor, double gap = 10)
        {
            var a = WindowRectPx(anchor);
            Realize(a.Left + a.Width / 2, a.Top + a.Height / 2);
            MenuSizePx(out double mw, out double mh);
            var wa = WorkAreaPx(a.Left + a.Width / 2, a.Top + a.Height / 2);
            double g = gap * Scale;

            double x = a.Right + g;
            if (x + mw > wa.Right - 4) x = a.Left - mw - g;
            if (x < wa.Left + 4) x = wa.Left + 4;
            if (x + mw > wa.Right - 4) x = wa.Right - mw - 4;

            double y = a.Top + (a.Height - mh) / 2;
            if (y < wa.Top + 4) y = wa.Top + 4;
            if (y + mh > wa.Bottom - 4) y = wa.Bottom - mh - 4;
            if (y < wa.Top + 4) y = wa.Top + 4;

            PlacePx(x, y);
        }

        /// <summary>在指定元素下方显示菜单（保留旧接口）。</summary>
        public void Show(UIElement target)
        {
            var p = new Point(0, 0);
            try { p = target.PointToScreen(new Point(0, target.RenderSize.Height)); } catch { }
            ShowAtScreen(p.X, p.Y + 4);
        }

        public void Close()
        {
            try { if (win.IsVisible) win.Hide(); } catch { }
        }

        /// <summary>供自检用：菜单项集合。</summary>
        public UIElementCollection Children => panel.Children;
    }

    /// <summary>菜单中的勾选项（自绘，保持 Win11 风格）。</summary>
    public class MenuCheckItem
    {
        public Button View { get; }
        public bool IsChecked { get; private set; }
        readonly TextBlock mark;
        readonly Action<bool> onChanged;
        readonly ModernMenu owner;

        public MenuCheckItem(string text, bool isChecked, Action<bool> onChanged, ModernMenu ownerMenu)
        {
            IsChecked = isChecked;
            this.onChanged = onChanged;
            owner = ownerMenu;

            var grid = new Grid { Margin = new Thickness(2, 1, 2, 1) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            mark = new TextBlock
            {
                Text = isChecked ? "✓" : "",
                FontSize = 13,
                Foreground = new SolidColorBrush(WinTheme.Accent),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(mark, 0);
            grid.Children.Add(mark);

            var label = new TextBlock
            {
                Text = text,
                FontSize = 13,
                Foreground = new SolidColorBrush(WinTheme.ItemFg),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(label, 1);
            grid.Children.Add(label);

            View = new Button
            {
                Content = grid,
                Height = 34,
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                UseLayoutRounding = true,
                SnapsToDevicePixels = true
            };
            var tpl = new ControlTemplate(typeof(Button));
            var bd = new FrameworkElementFactory(typeof(Border));
            bd.Name = "bd";
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            bd.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            bd.AppendChild(cp);
            tpl.VisualTree = bd;
            var trig = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            trig.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(WinTheme.ItemHover), "bd"));
            tpl.Triggers.Add(trig);
            View.Template = tpl;

            View.Click += (s, e) =>
            {
                SetChecked(!IsChecked);
                this.onChanged?.Invoke(IsChecked);
            };
        }

        public void SetChecked(bool v)
        {
            IsChecked = v;
            mark.Text = v ? "✓" : "";
        }
    }
}
