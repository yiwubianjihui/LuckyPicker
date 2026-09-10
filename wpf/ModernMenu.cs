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
            popup.IsOpen = true;
        }

        /// <summary>最近一次 ShowAtScreen 实际使用的 DIP 坐标（供自检/诊断使用）。</summary>
        public Point? LastScreenOffset { get; private set; }

        /// <summary>
        /// 在屏幕绝对坐标弹出（用于系统托盘菜单）。
        ///
        /// 根因说明：NotifyIcon.MouseClick 的 e.Location 是**设备像素**，而 WPF 的
        /// Popup.HorizontalOffset / VerticalOffset 是**设备无关单位（DIP）**。
        /// 在非 100% 缩放（如 125% / 150%）下两者相差一个缩放系数，直接赋值就会错位。
        /// 另外托盘在屏幕底部，向下弹出会超出工作区，需自动翻转到上方。
        /// </summary>
        /// <param name="screenX">设备像素 X（屏幕坐标）</param>
        /// <param name="screenY">设备像素 Y（屏幕坐标）</param>
        public void ShowAtScreen(double screenX, double screenY)
        {
            var w = System.Windows.Application.Current?.MainWindow;
            if (w == null) return;

            // 目标屏幕的工作区（设备像素）
            var wa = System.Windows.Forms.Screen.FromPoint(
                new System.Drawing.Point((int)screenX, (int)screenY)).WorkingArea;

            // 设备像素 → DIP
            var src = PresentationSource.FromVisual(w);
            Matrix toDip = Matrix.Identity;
            if (src?.CompositionTarget != null) toDip = src.CompositionTarget.TransformFromDevice;

            double px = screenX, py = screenY;
            double workLeft = wa.Left, workTop = wa.Top, workRight = wa.Right, workBottom = wa.Bottom;
            if (!toDip.IsIdentity)
            {
                var p = toDip.Transform(new Point(screenX, screenY));
                px = p.X; py = p.Y;
                var tl = toDip.Transform(new Point(wa.Left, wa.Top));
                var br = toDip.Transform(new Point(wa.Right, wa.Bottom));
                workLeft = tl.X; workTop = tl.Y; workRight = br.X; workBottom = br.Y;
            }

            // 先测量实际尺寸，再决定向上还是向下、是否需要左移
            card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double cw = card.DesiredSize.Width, ch = card.DesiredSize.Height;
            if (double.IsNaN(cw) || cw <= 0) cw = card.MinWidth;
            if (double.IsNaN(ch) || ch <= 0) ch = 120;

            // 水平：右对齐鼠标，超出右边界则左移
            double x = px;
            if (x + cw > workRight - 4) x = workRight - cw - 4;
            if (x < workLeft + 4) x = workLeft + 4;

            // 垂直：托盘一般在底部 → 优先向上弹出
            double y = py - ch - 8;
            if (y < workTop + 4) y = Math.Min(py + 8, workBottom - ch - 4);
            if (y < workTop + 4) y = workTop + 4;

            popup.PlacementTarget = w;
            popup.Placement = PlacementMode.AbsolutePoint;
            popup.HorizontalOffset = x;
            popup.VerticalOffset = y;
            LastScreenOffset = new Point(x, y);
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
