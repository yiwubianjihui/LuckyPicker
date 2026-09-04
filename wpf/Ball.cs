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
        double ballSize;
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
            ballSize = Math.Max(64, Math.Min(160, AppConfig.BallDiameter));
            Width = ballSize; Height = ballSize;
            WindowStartupLocation = WindowStartupLocation.Manual;

            ballBorder = new Border
            {
                Width = ballSize - 6, Height = ballSize - 6,
                CornerRadius = new CornerRadius((ballSize - 6) / 2),
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
            MouseEnter += (s, e) => WakeUp();
            MouseLeave += (s, e) => StartIdle();
            RestorePosition();

            // 不活动一段时间后降低不透明度（减少视觉干扰 / 投屏常驻）
            idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            idleTimer.Tick += (s, e) =>
            {
                idleTimer.Stop();
                if (!dragging) ballBorder.Opacity = 0.45;
            };
            idleTimer.Start();
        }

        DispatcherTimer idleTimer;

        void WakeUp()
        {
            ballBorder.Opacity = 1.0;
            if (idleTimer != null) { idleTimer.Stop(); idleTimer.Start(); }
        }

        void StartIdle()
        {
            if (idleTimer != null) { idleTimer.Stop(); idleTimer.Start(); }
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
            if (x <= -ballSize || x >= wa.Right || y <= -ballSize || y >= wa.Bottom)
            {
                x = (int)(wa.Right - ballSize - 24);
                y = wa.Top + wa.Height / 3;
            }
            Left = x; Top = y;
        }

        void SavePosition()
        {
            SnapToEdgeIfNeeded();
            AppConfig.BallX = (int)Left;
            AppConfig.BallY = (int)Top;
            AppConfig.Save();
        }

        /// <summary>靠边吸附：接近屏幕边缘时贴边并收进边框一部分（露出约 65%）。</summary>
        void SnapToEdgeIfNeeded()
        {
            try
            {
                var wa = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
                double w = Width, h = Height;
                int margin = 28;
                if (Left < wa.Left + margin) Left = wa.Left - w * 0.35;
                else if (Left + w > wa.Right - margin) Left = wa.Right - w * 0.65;
                if (Top < wa.Top + margin) Top = wa.Top;
                else if (Top + h > wa.Bottom - margin) Top = wa.Bottom - h;
            }
            catch { }
        }

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
                // DragMove 阻塞至鼠标释放（期间不再触发 MouseUp），返回后立即记忆新位置
                try { DragMove(); } catch { }
                dragging = false;
                SavePosition();
            }
        }

        void OnBallMouseUp(object s, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            if (dragging) { dragging = false; SavePosition(); return; }
            onClicked();
        }

        /// <summary>调整悬浮球大小（设置菜单调用），并按比例缩放球面文字。</summary>
        public void SetSize(double d)
        {
            ballSize = Math.Max(64, Math.Min(160, d));
            AppConfig.BallDiameter = (int)ballSize;
            Width = ballSize; Height = ballSize;
            ballBorder.Width = ballSize - 6;
            ballBorder.Height = ballSize - 6;
            ballBorder.CornerRadius = new CornerRadius((ballSize - 6) / 2);
            if (ballBorder.Child is TextBlock tb)
                tb.FontSize = Math.Max(13, ballSize * 0.24);
            SavePosition();
        }

        /// <summary>球面显示抽中结果：单人姓名 / 多人「N 人」。</summary>
        public void ShowPicked(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var parts = text.Split('、');
            string show = parts.Length > 1 ? parts.Length + "人" : (parts[0].Length > 5 ? parts[0].Substring(0, 4) + "…" : parts[0]);
            double k = ballSize / 96.0;
            double size = (show.Length <= 3 ? 17 : 14) * k;
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


    // ---------------- 悬浮球快捷面板（防误触：点击球先弹面板，不直接抽取） ----------------
    public class BallQuickPanel : Window
    {
        const int W = 176;
        static BallQuickPanel? current;   // 单例：避免连续开关导致的「关闭期间 Show」异常

        /// <summary>显示快捷面板（若已打开则先关闭旧面板）。</summary>
        public static void ShowPanel(Action pickOne, Action showMain, Action options, Window? anchor)
        {
            try { current?.Close(); } catch { }
            current = new BallQuickPanel(pickOne, showMain, options);
            if (anchor != null && anchor.IsVisible)
            {
                try { current.ShowNear(anchor); return; } catch { }
            }
            current.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            current.Show();
            current.Activate();
        }

        public BallQuickPanel(Action pickOne, Action showMain, Action options)
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            Width = W;

            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = "YBH\n幸运摇人",
                FontSize = 12.5, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(30, 64, 175)),
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 2, 0, 8),
                LineHeight = 17
            });
            panel.Children.Add(MakeButton("抽一人", () => { Close(); pickOne?.Invoke(); }));
            panel.Children.Add(MakeButton("显示窗口", () => { Close(); showMain?.Invoke(); }));
            panel.Children.Add(MakeButton("选项", () => { Close(); options?.Invoke(); }));

            var card = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(147, 197, 253)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(10, 10, 10, 4),
                Child = panel
            };
            Content = card;
            Height = 40 * 3 + 64;
            Deactivated += (s, e) => Close();
        }

        Button MakeButton(string text, Action onClick)
        {
            var b = new Button
            {
                Content = text, FontSize = 12.5, Height = 34,
                Margin = new Thickness(0, 0, 0, 6),
                Cursor = Cursors.Hand,
                Background = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
                BorderThickness = new Thickness(0)
            };
            b.Click += (s, e) =>
            {
                // 先关面板，等关闭完成后再执行回调（避免「窗口关闭期间 Show」异常）
                Close();
                Dispatcher.BeginInvoke(new Action(() => onClick?.Invoke()), DispatcherPriority.Background);
            };
            return b;
        }

        public void ShowNear(Window anchor)
        {
            var wa = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
            double x, y = Math.Max(wa.Top + 8, anchor.Top - 10);
            if (anchor.Left - W - 10 >= wa.Left) x = anchor.Left - W - 10;
            else x = Math.Min(anchor.Left + anchor.Width + 10, wa.Right - W - 8);
            Left = x;
            Top = Math.Min(y, wa.Bottom - Height - 8);
            Show();
            Activate();
        }
    }
