// ================================================================
// ModernMenu.cs — Windows 11 风格现代弹出菜单（替代传统 ContextMenu）
//
// 特性：圆角卡片 + 系统阴影、统一间距与配色、跟随系统亮/暗主题、
//       支持普通项 / 勾选项（单选、多选）/ 分隔线，点击外部自动关闭。
// ================================================================
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Controls.Primitives;
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
        public static Color Accent => Color.FromRgb(37, 99, 235);
    }

    public class ModernMenu
    {
        readonly Popup popup;
        readonly StackPanel panel;
        readonly Border card;

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
                Child = panel,
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black, Opacity = 0.18, BlurRadius = 16,
                    ShadowDepth = 2, Direction = 270
                }
            };
            popup = new Popup
            {
                Child = card,
                AllowsTransparency = true,
                StaysOpen = false,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
                PopupAnimation = System.Windows.Controls.Primitives.PopupAnimation.Fade
            };
        }

        /// <summary>普通菜单项。</summary>
        public ModernMenu AddItem(string text, Action onClick)
        {
            var b = MakeRow(text, false);
            b.Click += (s, e) => { popup.IsOpen = false; onClick?.Invoke(); };
            panel.Children.Add(b);
            return this;
        }

        /// <summary>可勾选菜单项（checked 变化时回调）。</summary>
        public MenuCheckItem AddCheck(string text, bool isChecked, Action<bool> onChanged)
        {
            var item = new MenuCheckItem(text, isChecked, onChanged, popup);
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

        /// <summary>在指定元素下方显示菜单。</summary>
        public void Show(UIElement target)
        {
            popup.PlacementTarget = target;
            popup.Placement = PlacementMode.Bottom;
            popup.HorizontalOffset = 0;
            popup.VerticalOffset = 4;
            popup.IsOpen = true;
        }

        /// <summary>最近一次实际上使用的屏幕 DIP 坐标（供自检/诊断使用）。</summary>
        public Point? LastScreenOffset { get; private set; }

        /// <summary>最近一次菜单的实际尺寸（DIP，供自检/诊断使用）。</summary>
        public Size? LastMenuSize { get; private set; }

        // ---------------------------------------------------------------
        // 定位基础设施
        //
        // 贴托盘图标的菜单最初把 Application.Current.MainWindow 当作 Popup 的
        // PlacementTarget，但"关闭到托盘"后主窗口是 Hide() 状态 —— 一个不可见的
        // 窗口没有有效的 PresentationSource，Popup 便无法正确定位（这正是
        // "右键菜单跑到别处/不在图标旁"的根因）。
        // 现在改为一个常驻的 1×1 透明宿主窗口（位于屏幕原点），它始终可见，
        // 因此 AbsolutePoint 的偏移量恒等于"屏幕 DIP 坐标"。
        // ---------------------------------------------------------------

        static Window? screenHost;

        static Window EnsureScreenHost()
        {
            if (screenHost != null) return screenHost;
            screenHost = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowInTaskbar = false,
                ShowActivated = false,
                Topmost = true,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 0, Top = 0, Width = 1, Height = 1,
                Opacity = 0.01          // 几乎不可见，但仍在渲染，可作为定位锚点
            };
            screenHost.Show();
            return screenHost;
        }

        /// <summary>取某个 Visual 所在屏幕的工作区，单位为 DIP。</summary>
        static bool WorkAreaDip(Visual v, double screenXpx, double screenYpx,
                                out double left, out double top, out double right, out double bottom)
        {
            left = 0; top = 0; right = 1920; bottom = 1080;
            try
            {
                var wa = System.Windows.Forms.Screen.FromPoint(
                    new System.Drawing.Point((int)screenXpx, (int)screenYpx)).WorkingArea;
                left = wa.Left; top = wa.Top; right = wa.Right; bottom = wa.Bottom;

                var src = PresentationSource.FromVisual(v);
                if (src?.CompositionTarget != null)
                {
                    var m = src.CompositionTarget.TransformFromDevice;
                    var tl = m.Transform(new Point(wa.Left, wa.Top));
                    var br = m.Transform(new Point(wa.Right, wa.Bottom));
                    left = tl.X; top = tl.Y; right = br.X; bottom = br.Y;
                }
                return true;
            }
            catch { return false; }
        }

        /// <summary>DIP → 设备像素（用于按屏幕查找工作区）。</summary>
        static void DipToDevice(Visual v, double dx, double dy, out double px, out double py)
        {
            px = dx; py = dy;
            try
            {
                var src = PresentationSource.FromVisual(v);
                if (src?.CompositionTarget != null)
                {
                    var p = src.CompositionTarget.TransformToDevice.Transform(new Point(dx, dy));
                    px = p.X; py = p.Y;
                }
            }
            catch { }
        }

        /// <summary>Popup 的 offset 是 DIP；把设备像素换成 DIP。</summary>
        static void DeviceToDip(Visual v, double px, double py, out double dx, out double dy)
        {
            dx = px; dy = py;
            try
            {
                var src = PresentationSource.FromVisual(v);
                if (src?.CompositionTarget != null)
                {
                    var p = src.CompositionTarget.TransformFromDevice.Transform(new Point(px, py));
                    dx = p.X; dy = p.Y;
                }
            }
            catch { }
        }

        /// <summary>测量卡片尺寸（弹窗前即可测得，用于边界翻转判断）。</summary>
        void MeasureCard(out double cw, out double ch)
        {
            card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            cw = card.DesiredSize.Width; ch = card.DesiredSize.Height;
            if (double.IsNaN(cw) || cw <= 0) cw = card.MinWidth;
            if (double.IsNaN(ch) || ch <= 0) ch = 120;
        }

        /// <summary>
        /// 在屏幕绝对坐标弹出（用于系统托盘菜单）：菜单贴着托盘图标弹出。
        ///
        /// 注意 NotifyIcon.MouseClick 的 e.Location 是**设备像素**，而 Popup 的
        /// HorizontalOffset / VerticalOffset 是 **DIP**，非 100% 缩放下相差一个系数；
        /// 托盘通常位于屏幕底部，向下弹出会越界，故优先向上翻转。
        /// </summary>
        /// <param name="screenX">设备像素 X（屏幕坐标）</param>
        /// <param name="screenY">设备像素 Y（屏幕坐标）</param>
        public void ShowAtScreen(double screenX, double screenY)
        {
            var host = EnsureScreenHost();
            MeasureCard(out double cw, out double ch);
            DeviceToDip(host, screenX, screenY, out double px, out double py);
            WorkAreaDip(host, screenX, screenY, out double L, out double T, out double R, out double B);

            // 水平：左缘对齐图标；右侧放不下则翻到图标左边
            double x = px;
            if (x + cw > R - 4) x = px - cw;
            if (x < L + 4) x = L + 4;
            if (x + cw > R - 4) x = R - cw - 4;

            // 垂直：贴着图标上方；上方空间不足则落到下方
            double y = py - ch - 8;
            if (y < T + 4) y = Math.Min(py + 8, B - ch - 4);
            if (y < T + 4) y = T + 4;

            popup.PlacementTarget = host;
            popup.Placement = PlacementMode.AbsolutePoint;
            popup.HorizontalOffset = x;
            popup.VerticalOffset = y;
            LastScreenOffset = new Point(x, y);
            LastMenuSize = new Size(cw, ch);
            popup.IsOpen = true;
        }

        /// <summary>在某个窗口（悬浮球）旁边弹出：优先右侧，放不下则左侧；垂直与球居中对齐。</summary>
        public void ShowBeside(Window anchor, double gap = 10)
        {
            var host = EnsureScreenHost();
            MeasureCard(out double cw, out double ch);

            DipToDevice(anchor, anchor.Left + anchor.Width / 2, anchor.Top + anchor.Height / 2,
                        out double apx, out double apy);
            WorkAreaDip(host, apx, apy, out double L, out double T, out double R, out double B);

            double ax = anchor.Left, ay = anchor.Top, aw = anchor.Width, ah = anchor.Height;
            double x = ax + aw + gap;
            if (x + cw > R - 4) x = ax - cw - gap;
            if (x < L + 4) x = L + 4;
            if (x + cw > R - 4) x = R - cw - 4;

            double y = ay + (ah - ch) / 2;
            if (y < T + 4) y = T + 4;
            if (y + ch > B - 4) y = B - ch - 4;
            if (y < T + 4) y = T + 4;

            popup.PlacementTarget = host;
            popup.Placement = PlacementMode.AbsolutePoint;
            popup.HorizontalOffset = x;
            popup.VerticalOffset = y;
            LastScreenOffset = new Point(x, y);
            LastMenuSize = new Size(cw, ch);
            popup.IsOpen = true;
        }

        public void Close() => popup.IsOpen = false;
    }

    /// <summary>菜单中的勾选项（自绘，保持 Win11 风格）。</summary>
    public class MenuCheckItem
    {
        public Button View { get; }
        public bool IsChecked { get; private set; }
        readonly TextBlock mark;
        readonly Action<bool> onChanged;
        readonly Popup owner;

        public MenuCheckItem(string text, bool isChecked, Action<bool> onChanged, Popup ownerPopup)
        {
            IsChecked = isChecked;
            this.onChanged = onChanged;
            owner = ownerPopup;

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
                HorizontalContentAlignment = HorizontalAlignment.Stretch
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
