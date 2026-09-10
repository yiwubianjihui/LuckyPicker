// ================================================================
// Ball.cs — WPF 桌面悬浮球 + 班级快捷选择小窗 + 悬浮球快捷面板
//
// 悬浮球：无边框透明圆球、置顶、独立于主窗口；单击弹快捷面板（防误触）、
//         拖动移位记忆、右键菜单；抽中结果球面显示（单人姓名 / 多人「N 人」）。
//         固定（图钉）时保持活跃：不自动变淡、抽完不收起。
// 班级小窗：置顶小卡片，显示在球旁，点选班级立即抽取，失焦关闭。
// ================================================================
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace LuckyPickerWpf
{
    public class FloatingBallWindow : Window
    {
        double ballSize;
        readonly Action onClicked;          // 单击（弹出快捷面板）
        readonly Action onPickOne;          // 右键菜单：抽一人
        readonly Action onPickMulti;        // 右键菜单：连抽五人
        readonly Action onResetPool;        // 右键菜单：重置候选池
        readonly Action onHideSelf;         // 右键菜单：隐藏悬浮球
        readonly Action onQuit;             // 右键菜单：退出程序
        readonly Action showMain;           // 显示主窗口

        readonly Border ballBorder;         // 球体（渐变圆）
        readonly ScaleTransform ballScale;  // 悬停/抽中缩放
        readonly TranslateTransform ballShift; // 贴边滑动
        readonly TextBlock faceText;

        Point downPos;
        bool dragging;
        bool pinned;
        int flashTicks;
        readonly DispatcherTimer flashTimer;
        readonly DispatcherTimer idleTimer;
        bool idleDimmed;

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

            // ---- 球体：径向渐变 + 柔和高光，比纯色更立体 ----
            ballScale = new ScaleTransform(1, 1);
            ballShift = new TranslateTransform(0, 0);

            faceText = new TextBlock
            {
                Text = "摇", FontSize = Math.Max(13, ballSize * 0.30), FontWeight = FontWeights.Bold,
                Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.35, BlurRadius = 6, ShadowDepth = 1, Direction = 315 }
            };

            var faceLayer = new Grid();
            faceLayer.Children.Add(faceText);

            ballBorder = new Border
            {
                Width = ballSize - 6, Height = ballSize - 6,
                CornerRadius = new CornerRadius((ballSize - 6) / 2),
                Background = BuildBallBrush(),
                BorderBrush = new SolidColorBrush(Color.FromArgb(220, 235, 255, 255)),
                BorderThickness = new Thickness(1.5),
                Cursor = Cursors.Hand,
                Child = faceLayer,
                RenderTransform = new TransformGroup { Children = { ballScale, ballShift } },
                RenderTransformOrigin = new Point(0.5, 0.5),
                // 球体自身带柔和投影，不依赖窗口阴影（避免 AllowsTransparency 下的模糊）
                Effect = new DropShadowEffect { Color = Color.FromArgb(90, 0, 0, 0), BlurRadius = 12, ShadowDepth = 2, Direction = 270 }
            };
            Content = ballBorder;

            // 抽中后的闪烁：在几种蓝色间循环，营造"滚动"感
            flashTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(110) };
            flashTimer.Tick += (s, e) =>
            {
                flashTicks++;
                if (flashTicks >= 22)
                {
                    flashTimer.Stop();
                    ballBorder.Background = BuildBallBrush();
                    SetFace("摇", Math.Max(13, ballSize * 0.30));
                    return;
                }
                var c = (flashTicks % 2 == 0) ? Color.FromRgb(37, 99, 235) : Color.FromRgb(96, 165, 250);
                ballBorder.Background = new SolidColorBrush(c);
            };

            MouseRightButtonUp += (s, e) => { e.Handled = true; ShowContextMenu(); };
            MouseDown += OnBallMouseDown;
            MouseMove += OnBallMouseMove;
            MouseUp += OnBallMouseUp;
            MouseEnter += (s, e) => { WakeUp(); AnimateScale(1.08, 140); };
            MouseLeave += (s, e) => { AnimateScale(1.0, 180); StartIdle(); };
            RestorePosition();

            SetPinned(AppConfig.BallPinned);

            // 不活动一段时间后降低不透明度（减少视觉干扰 / 投屏常驻）；固定时不生效
            idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            idleTimer.Tick += (s, e) =>
            {
                idleTimer.Stop();
                if (dragging || pinned) return;
                idleDimmed = true;
                AnimateOpacity(0.45, 420);
            };
            idleTimer.Start();
        }

        Brush BuildBallBrush()
        {
            var g = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.32, 0.28),
                Center = new Point(0.38, 0.35),
                RadiusX = 0.85, RadiusY = 0.85
            };
            g.GradientStops.Add(new GradientStop(Color.FromRgb(96, 165, 250), 0.0));   // 高光
            g.GradientStops.Add(new GradientStop(Color.FromRgb(37, 99, 235), 0.55));
            g.GradientStops.Add(new GradientStop(Color.FromRgb(29, 78, 216), 1.0));   // 边缘
            return g;
        }

        // ---------------- 动效 ----------------

        void AnimateOpacity(double to, int ms)
        {
            try
            {
                var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                ballBorder.BeginAnimation(UIElement.OpacityProperty, a);
            }
            catch { try { ballBorder.Opacity = to; } catch { } }
        }

        void AnimateScale(double to, int ms)
        {
            try
            {
                var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                ballScale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
                ballScale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            }
            catch { }
        }

        /// <summary>抽中时的弹跳：放大 → 回缩 → 复位。</summary>
        void AnimateBounce()
        {
            try
            {
                var sb = new Storyboard();
                var kx = new DoubleAnimationUsingKeyFrames();
                kx.KeyFrames.Add(new EasingDoubleKeyFrame(1.22, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(110))) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                kx.KeyFrames.Add(new EasingDoubleKeyFrame(0.94, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(230))) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });
                kx.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(340))) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                Storyboard.SetTarget(kx, ballBorder);
                Storyboard.SetTargetProperty(kx, new PropertyPath("RenderTransform.Children[0].ScaleX"));
                var ky = kx.Clone();
                Storyboard.SetTargetProperty(ky, new PropertyPath("RenderTransform.Children[0].ScaleY"));
                sb.Children.Add(kx); sb.Children.Add(ky);
                sb.Begin();
            }
            catch { }
        }

        /// <summary>平滑移动悬浮球到目标位置（内容先滑动，动画结束后落到真实 Left/Top）。</summary>
        void AnimateMoveTo(double toLeft, double toTop)
        {
            try
            {
                double dx = toLeft - Left, dy = toTop - Top;
                if (Math.Abs(dx) < 0.5 && Math.Abs(dy) < 0.5) { Left = toLeft; Top = toTop; return; }
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                var ax = new DoubleAnimation(0, dx, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease };
                var ay = new DoubleAnimation(0, dy, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease };
                ax.Completed += (s, e) =>
                {
                    try
                    {
                        ballShift.BeginAnimation(TranslateTransform.XProperty, null);
                        ballShift.BeginAnimation(TranslateTransform.YProperty, null);
                        ballShift.X = 0; ballShift.Y = 0;
                        Left = toLeft; Top = toTop;
                    }
                    catch { }
                };
                ballShift.BeginAnimation(TranslateTransform.XProperty, ax);
                ballShift.BeginAnimation(TranslateTransform.YProperty, ay);
            }
            catch { try { Left = toLeft; Top = toTop; } catch { } }
        }

        // ---------------- 状态 ----------------

        void WakeUp()
        {
            if (idleDimmed) { idleDimmed = false; AnimateOpacity(1.0, 220); }
            else { try { ballBorder.BeginAnimation(UIElement.OpacityProperty, null); ballBorder.Opacity = 1.0; } catch { } }
            if (!pinned && idleTimer != null) { idleTimer.Stop(); idleTimer.Start(); }
        }

        void StartIdle()
        {
            if (pinned) return;
            if (idleTimer != null) { idleTimer.Stop(); idleTimer.Start(); }
        }

        void SetFace(string text, double size)
        {
            faceText.Text = text;
            faceText.FontSize = size;
        }

        static ModernMenu? activeCtxMenu;   // 单例：再点球时收起

        /// <summary>右键菜单（Win11 风格现代菜单，跟随系统主题）。再点切换。</summary>
        void ShowContextMenu()
        {
            if (activeCtxMenu != null) { try { activeCtxMenu.Close(); } catch { } activeCtxMenu = null; return; }
            var m = new ModernMenu(200);
            activeCtxMenu = m;
            m.AddItem("显示主窗口", () => { activeCtxMenu = null; showMain?.Invoke(); });
            m.AddSeparator();
            m.AddItem("抽一人", () => { activeCtxMenu = null; onPickOne?.Invoke(); });
            m.AddItem("连抽五人", () => { activeCtxMenu = null; onPickMulti?.Invoke(); });
            m.AddItem("重置候选池", () => { activeCtxMenu = null; onResetPool?.Invoke(); });
            m.AddSeparator();
            m.AddCheck("开机自启动", AutoStart.IsEnabled(), v => AutoStart.SetEnabled(v));
            m.AddCheck("固定悬浮球（连续抽签）", AppConfig.BallPinned, v =>
            {
                AppConfig.BallPinned = v; AppConfig.Save();
                SetPinned(v);
                BallQuickPanel.RefreshPinState(v);
            });
            m.AddSeparator();
            m.AddItem("隐藏悬浮球", () => { activeCtxMenu = null; onHideSelf?.Invoke(); });
            m.AddItem("退出程序", () => { activeCtxMenu = null; onQuit?.Invoke(); });
            m.Show(this);
        }

        /// <summary>
        /// 抽取后行为：固定时保持原位且保持活跃；未固定时延时贴边收起（露出约 35%，仍可点击唤回）。
        /// </summary>
        public void AutoHideAfterPick()
        {
            if (AppConfig.BallPinned) { WakeUp(); return; }
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            t.Tick += (s, e) => { t.Stop(); TuckToEdge(); };
            t.Start();
        }

        /// <summary>贴边收起：平滑滑到最近的屏幕边缘并只露出一部分（保留可点击区域）。</summary>
        public void TuckToEdge()
        {
            try
            {
                var wa = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
                bool nearRight = Left + Width / 2 > wa.Left + wa.Width / 2;
                double toTop = Math.Max(wa.Top + 8, Math.Min(Top, wa.Bottom - Height - 8));
                double toLeft = nearRight ? wa.Right - Width * 0.35 : wa.Left - Width * 0.65;
                AnimateMoveTo(toLeft, toTop);
                WakeUp();
            }
            catch { }
        }

        /// <summary>固定状态：固定时保持活跃（不变淡、抽完不收起），并更新描边视觉。</summary>
        public void SetPinned(bool value)
        {
            try
            {
                pinned = value;
                ballBorder.BorderBrush = value
                    ? new SolidColorBrush(Color.FromRgb(15, 23, 42))
                    : new SolidColorBrush(Color.FromArgb(160, 235, 255, 255));
                ballBorder.BorderThickness = new Thickness(value ? 2.5 : 1.5);
                ballBorder.ToolTip = "YBH幸运摇人器" + (value ? "（已固定：始终可交互）" : "（未固定：闲时变淡、抽完贴边）");

                if (value)
                {
                    idleTimer?.Stop();
                    idleDimmed = false;
                    AnimateOpacity(1.0, 200);
                }
                else
                {
                    StartIdle();
                }
            }
            catch { }
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
            WakeUp();
            onClicked();
        }

        /// <summary>调整悬浮球大小（设置窗口调用），并按比例缩放球面文字。</summary>
        public void SetSize(double d)
        {
            ballSize = Math.Max(64, Math.Min(160, d));
            AppConfig.BallDiameter = (int)ballSize;
            Width = ballSize; Height = ballSize;
            ballBorder.Width = ballSize - 6;
            ballBorder.Height = ballSize - 6;
            ballBorder.CornerRadius = new CornerRadius((ballSize - 6) / 2);
            faceText.FontSize = Math.Max(13, ballSize * 0.30);
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
            AnimateBounce();
            flashTicks = 0;
            flashTimer.Start();
        }

        // ---------------- 自检/诊断访问器 ----------------
        public bool IsPinnedNow => pinned;
        public double BallDiameterNow => ballSize;
        public double OpacityNow => ballBorder.Opacity;
        public bool HasScaleTransform => ballBorder.RenderTransform is TransformGroup tg
                                         && tg.Children.Count > 0 && tg.Children[0] is ScaleTransform;
        public double ScaleNow => ballScale.ScaleX;
    }

    // ---------------- 班级快捷选择小窗 ----------------
    public class ClassMiniWindow : Window
    {
        const int W = 236;
        readonly Action<string, bool> onPick;

        public ClassMiniWindow(IEnumerable<string> ids, Dictionary<string, string> names, Action<string, bool> pick)
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
            panel.Children.Add(new TextBlock { Text = "点选后立即抽取 · 点击空白处关闭", FontSize = 8, Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)), Margin = new Thickness(0, 2, 0, 6) });
            var chkDefault = new CheckBox
            {
                Content = "设为默认（以后启动不再询问）", IsChecked = true, FontSize = 9.5,
                Foreground = new SolidColorBrush(Color.FromRgb(30, 64, 175)), Margin = new Thickness(0, 0, 0, 8)
            };
            panel.Children.Add(chkDefault);
            foreach (var id in ids)
            {
                string cid = id;
                string label = id + "班 · " + (names != null && names.ContainsKey(cid) ? names[cid] : cid + "班");
                var b = new Button
                {
                    Content = label, FontSize = 10, FontWeight = FontWeights.Bold,
                    Height = 42, Margin = new Thickness(0, 4, 0, 0), Cursor = Cursors.Hand,
                    Background = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
                    Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                    BorderThickness = new Thickness(0),
                    UseLayoutRounding = true, SnapsToDevicePixels = true
                };
                b.Template = RoundTemplate(10, Color.FromRgb(219, 234, 254));
                b.Click += (s, e) =>
                {
                    // 先关小窗，等关闭完成后再执行回调（避免「窗口关闭期间 Show/Close」异常）
                    bool asDefault = chkDefault.IsChecked == true;
                    Close();
                    Dispatcher.BeginInvoke(new Action(() => onPick?.Invoke(cid, asDefault)), DispatcherPriority.Background);
                };
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
            Height = 64 + count * 50 + 26;
            Deactivated += (s, e) => { try { Close(); } catch { } };
        }

        static int CountOf(IEnumerable<string> ids) { int n = 0; foreach (var _ in ids) n++; return n; }

        /// <summary>圆角按钮模板（hover 变色）。抽出来复用，避免重复代码漏写 bd.Name。</summary>
        internal static ControlTemplate RoundTemplate(double radius, Color hover)
        {
            var tpl = new ControlTemplate(typeof(Button));
            var bd = new FrameworkElementFactory(typeof(Border));
            bd.Name = "bd";
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
            bd.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            bd.AppendChild(cp);
            tpl.VisualTree = bd;
            var trig = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            trig.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(hover), "bd"));
            tpl.Triggers.Add(trig);
            return tpl;
        }

        public void ShowNear(Window anchor)
        {
            var wa = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
            double x, y = Math.Max(wa.Top + 8, anchor.Top - 10);
            if (anchor.Left - W - 10 >= wa.Left) x = anchor.Left - W - 10;
            else x = Math.Min(anchor.Left + anchor.Width + 10, wa.Right - W - 8);
            y = Math.Min(y, wa.Bottom - Height - 8);
            Left = x; Top = y;
            try { Show(); Activate(); } catch { }
        }
    }

    // ---------------- 悬浮球快捷面板（防误触：点击球先弹面板，不直接抽取） ----------------
    public class BallQuickPanel : Window
    {
        const int W = 176;
        static BallQuickPanel? current;              // 单例
        static DateTime suppressUntil = DateTime.MinValue;   // 因"点击悬浮球"而关闭后的抑制窗口

        /// <summary>显示/收起快捷面板（toggle）。点击悬浮球时：已开→收起，未开→弹出。</summary>
        public static void ShowPanel(Action pickOne, Action showMain, bool pinned, Action<bool> onPinChanged, Window? anchor)
        {
            // 已开：收起（toggle）
            if (current != null)
            {
                try { current.Close(); } catch { }
                current = null;
                suppressUntil = DateTime.Now.AddMilliseconds(450);
                return;
            }
            // 刚刚因为"再次点击悬浮球"而收起 → 抑制这次重开，避免闪一下又出现
            if (DateTime.Now < suppressUntil) return;

            current = new BallQuickPanel(pickOne, showMain, pinned, onPinChanged);
            if (anchor != null && anchor.IsVisible)
            {
                try { current.ShowNear(anchor); return; } catch { }
            }
            current.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            try { current.Show(); current.Activate(); } catch { }
        }

        /// <summary>面板是否已开。</summary>
        public static bool IsOpen => current != null;

        /// <summary>外部命令：收起面板。</summary>
        public static void CloseIfOpen()
        {
            try { current?.Close(); } catch { }
            current = null;
        }

        public BallQuickPanel(Action pickOne, Action showMain, bool pinned, Action<bool> onPinChanged)
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
            // 主操作：抽一人（点击区域显著放大，圆角主按钮）
            var bigBtn = new Button
            {
                Content = "抽一人", FontSize = 15, FontWeight = FontWeights.SemiBold,
                Height = 56, Margin = new Thickness(0, 0, 0, 10), Cursor = Cursors.Hand,
                Background = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
                Foreground = Brushes.White, BorderThickness = new Thickness(0),
                UseLayoutRounding = true, SnapsToDevicePixels = true
            };
            bigBtn.Template = ClassMiniWindow.RoundTemplate(12, Color.FromRgb(29, 78, 216));
            bigBtn.Click += (s, e) =>
            {
                // 固定时保持面板展开，方便连续抽签；未固定时关闭面板（球面靠边）
                if (!AppConfig.BallPinned) Close();
                Dispatcher.BeginInvoke(new Action(() => pickOne?.Invoke()), DispatcherPriority.Background);
            };
            panel.Children.Add(bigBtn);
            panel.Children.Add(MakeButton("更多功能", () => { Close(); showMain?.Invoke(); }));

            // 固定悬浮窗（默认勾选）：勾选后「抽一人」不隐藏悬浮球，便于连续抽签
            var chkPin = new CheckBox
            {
                Content = "📌 固定悬浮窗（连续抽签）", IsChecked = pinned, FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)), Margin = new Thickness(0, 6, 0, 0)
            };
            chkPin.Checked += (s, e) => onPinChanged?.Invoke(true);
            chkPin.Unchecked += (s, e) => onPinChanged?.Invoke(false);
            panel.Children.Add(chkPin);
            _chkPin = chkPin;

            var card = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(147, 197, 253)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(10, 10, 10, 8),
                Child = panel,
                Effect = new DropShadowEffect { Color = Color.FromArgb(70, 0, 0, 0), BlurRadius = 14, ShadowDepth = 2, Direction = 270 }
            };
            Content = card;
            SizeToContent = SizeToContent.Height;   // 自适应高度，确保所有项完整可见

            // 失焦关闭：若是"点击悬浮球"导致失焦，则记录抑制窗口，让该次点击表现为"收起"
            Deactivated += (s, e) =>
            {
                try { Close(); } catch { }
                suppressUntil = DateTime.Now.AddMilliseconds(450);
            };
            Closed += (s, e) =>
            {
                if (ReferenceEquals(current, this)) current = null;
                if (DateTime.Now < suppressUntil) return;
                suppressUntil = DateTime.Now.AddMilliseconds(120);
            };
        }

        CheckBox? _chkPin;
        public static void RefreshPinState(bool pinned)
        {
            try { if (current != null) current._chkPin!.IsChecked = pinned; } catch { }
        }

        Button MakeButton(string text, Action onClick)
        {
            var b = new Button
            {
                Content = text, FontSize = 12.5, Height = 34,
                Margin = new Thickness(0, 0, 0, 6),
                Cursor = Cursors.Hand,
                Background = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
                Foreground = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
                BorderThickness = new Thickness(0),
                UseLayoutRounding = true, SnapsToDevicePixels = true
            };
            b.Template = ClassMiniWindow.RoundTemplate(8, Color.FromRgb(226, 232, 240));
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
            try { Show(); Activate(); } catch { }
        }
    }
}