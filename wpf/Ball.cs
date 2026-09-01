// ================================================================
// Ball.cs — WPF 桌面悬浮球 + 班级快捷选择小窗
//
// 悬浮球：无边框透明圆球、置顶、独立于主窗口；单击抽一人（未选班级弹小窗）、
//         拖动移位记忆、右键菜单；抽中结果球面显示（单人姓名 / 多人「N 人」）。
// 班级小窗：置顶小卡片，显示在球旁，点选班级立即抽取，失焦关闭。
// ================================================================
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace LuckyPickerWpf
{
    public class FloatingBallWindow : Window
    {
        const int SizePx = 92;
        readonly Action onClicked;          // 单击（未选班级时由宿主弹小窗）
        readonly Action onPickOne;          // 右键菜单：抽一人
        readonly Action onPickMulti;        // 右键菜单：连抽五人
        readonly Action onResetPool;        // 右键菜单：重置候选池
        readonly Action onHideSelf;         // 右键菜单：隐藏悬浮球
        readonly Action onQuit;             // 右键菜单：退出程序
        readonly Action showMain;           // 显示主窗口

        readonly Border ballBorder;
        Point downPos;
        bool dragging;
        int flashTicks;
        DispatcherTimer flashTimer;

        public FloatingBallWindow(Action clicked, Action pickOne, Action pickMulti,
                                  Action resetPool, Action hideSelf, Action quit, Action showMainWin)
        {
            onClicked = clicked; onPickOne = pickOne; onPickMulti = pickMulti;
            onResetPool = resetPool; onHideSelf = hideSelf; onQuit = quit; showMain = showMainWin;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            Width = SizePx; Height = SizePx;
            WindowStartupLocation = WindowStartupLocation.Manual;

            ballBorder = new Border
            {
                Width = SizePx - 6, Height = SizePx - 6,
                CornerRadius = new CornerRadius((SizePx - 6) / 2),
                Background = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(220, 235, 255, 255)),
                BorderThickness = new Thickness(1.5),
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = "摇", FontSize = 22, FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            Content = ballBorder;

            flashTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            flashTimer.Tick += (s, e) =>
            {
                flashTicks++;
                if (flashTicks >= 25) { flashTimer.Stop(); SetFace("摇", 22); }
            };

            ContextMenu = BuildMenu();
            MouseDown += OnBallMouseDown;
            MouseMove += OnBallMouseMove;
            MouseUp += OnBallMouseUp;
            RestorePosition();
        }

        void SetFace(string text, double size)
        {
            if (ballBorder.Child is TextBlock tb) { tb.Text = text; tb.FontSize = size; }
        }

        ContextMenu BuildMenu()
        {
            var m = new ContextMenu();
            MenuItem item;
            item = new MenuItem { Header = "显示主窗口" }; item.Click += (s, e) => showMain(); m.Items.Add(item);
            m.Items.Add(new Separator());
            item = new MenuItem { Header = "抽一人" }; item.Click += (s, e) => onPickOne(); m.Items.Add(item);
            item = new MenuItem { Header = "连抽五人" }; item.Click += (s, e) => onPickMulti(); m.Items.Add(item);
            item = new MenuItem { Header = "重置候选池" }; item.Click += (s, e) => onResetPool(); m.Items.Add(item);
            m.Items.Add(new Separator());
            var boot = new MenuItem { Header = "开机自启动", IsCheckable = true, IsChecked = AutoStart.IsEnabled() };
            boot.Click += (s, e) => { bool ok = AutoStart.SetEnabled(boot.IsChecked); boot.IsChecked = ok && boot.IsChecked; };
            m.Items.Add(boot);
            m.Items.Add(new Separator());
            item = new MenuItem { Header = "隐藏悬浮球" }; item.Click += (s, e) => onHideSelf(); m.Items.Add(item);
            item = new MenuItem { Header = "退出程序" }; item.Click += (s, e) => onQuit(); m.Items.Add(item);
            return m;
        }

        void RestorePosition()
        {
            var wa = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
            int x = AppConfig.BallX, y = AppConfig.BallY;
            if (x <= -SizePx || x >= wa.Right || y <= -SizePx || y >= wa.Bottom)
            {
                x = wa.Right - SizePx - 24;
                y = wa.Top + wa.Height / 3;
            }
            Left = x; Top = y;
        }

        void SavePosition() { AppConfig.BallX = (int)Left; AppConfig.BallY = (int)Top; AppConfig.Save(); }

        void OnBallMouseDown(object s, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                downPos = e.GetPosition(this);
                dragging = false;
            }
        }

        void OnBallMouseMove(object s, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(this);
            if (!dragging && (Math.Abs(p.X - downPos.X) + Math.Abs(p.Y - downPos.Y)) > 5)
            {
                dragging = true;
                DragMove();
            }
        }

        void OnBallMouseUp(object s, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            if (dragging) { dragging = false; SavePosition(); return; }
            onClicked();
        }

        /// <summary>球面显示抽中结果：单人姓名 / 多人「N 人」。</summary>
        public void ShowPicked(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var parts = text.Split('、');
            string show = parts.Length > 1 ? parts.Length + "人" : (parts[0].Length > 5 ? parts[0].Substring(0, 4) + "…" : parts[0]);
            double size = show.Length <= 3 ? 17 : 14;
            SetFace(show, size);
            flashTicks = 0;
            flashTimer.Start();
        }
    }

    // ---------------- 班级快捷选择小窗 ----------------
    public class ClassMiniWindow : Window
    {
        const int W = 236;
        readonly Action<string> onPick;

        public ClassMiniWindow(IEnumerable<string> ids, Dictionary<string, string> names, Action<string> pick)
        {
            onPick = pick;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            Width = W;

            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = "选择班级", FontSize = 11.5, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59)) });
            panel.Children.Add(new TextBlock { Text = "点选后立即抽取 · 点击空白处关闭", FontSize = 8, Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)), Margin = new Thickness(0, 2, 0, 8) });
            foreach (var id in ids)
            {
                string cid = id;
                string label = id + "班 · " + (names != null && names.ContainsKey(cid) ? names[cid] : cid + "班");
                var b = new Button
                {
                    Content = label, FontSize = 10, FontWeight = FontWeights.Bold,
                    Height = 42, Margin = new Thickness(0, 4, 0, 0), Cursor = Cursors.Hand,
                    Background = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
                    BorderThickness = new Thickness(0)
                };
                b.Click += (s, e) => { var p = onPick; Close(); p?.Invoke(cid); };
                panel.Children.Add(b);
            }

            var card = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(147, 197, 253)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(14, 12, 14, 12),
                Child = panel
            };
            Content = card;

            int count = CountOf(ids);
            Height = 64 + count * 50;
            Deactivated += (s, e) => Close();
        }

        static int CountOf(IEnumerable<string> ids) { int n = 0; foreach (var _ in ids) n++; return n; }

        public void ShowNear(Window anchor)
        {
            var wa = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
            double x, y = Math.Max(wa.Top + 8, anchor.Top - 10);
            if (anchor.Left - W - 10 >= wa.Left) x = anchor.Left - W - 10;
            else x = Math.Min(anchor.Left + anchor.Width + 10, wa.Right - W - 8);
            y = Math.Min(y, wa.Bottom - Height - 8);
            Left = x; Top = y;
            Show();
            Activate();
        }
    }
}
