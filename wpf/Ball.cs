// ================================================================
// Ball.cs — WPF 桌面悬浮球 + 班级快捷选择小窗 + 悬浮球快捷面板
//
// 悬浮球：无边框透明圆球、置顶、独立于主窗口；单击弹快捷面板（防误触）、
//         拖动移位记忆、右键菜单；抽中结果球面显示（单人姓名 / 多人「N 人」）。
//         固定（图钉）时保持活跃：不自动变淡、抽完不收起。
// 视觉：浅色渐变 + 留白投影（简约、低干扰）；空闲 4s 后自动变淡。
// 班级小窗：置顶小卡片，显示在球旁，点选班级立即抽取，失焦关闭。
// ================================================================
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace LuckyPickerWpf
{
    public class FloatingBallWindow : Window
    {
        // 球体外留白：窗口比球体大一圈，给投影留出绘制空间。
        // 旧实现窗口 == 球直径、球体再用 ballSize-6 做内缩，投影不足 3px 被窗口边界
        // 直接裁掉 —— 这就是"阴影被遮住"的根因。现在窗口 = 球直径 + 2*留白，
        // 球体本身仍是设定的直径，视觉大小不变。
        const double ShadowPad = 8;
        const double IdleSeconds = 4;
        const double IdleOpacity = 0.35;

        /// <summary>
        /// 渐隐目标不透明度。正常情况下是常量 IdleOpacity；
        /// 允许用环境变量 LP_IDLE_OPACITY 覆盖，仅用于诊断——
        /// 例如设成 0 可验证"渐隐是否真的作用到了屏幕合成结果"。
        /// </summary>
        static double IdleOpacityTarget
        {
            get
            {
                try
                {
                    var s = Environment.GetEnvironmentVariable("LP_IDLE_OPACITY");
                    if (!string.IsNullOrWhiteSpace(s)
                        && double.TryParse(s, System.Globalization.NumberStyles.Float,
                                           System.Globalization.CultureInfo.InvariantCulture, out var v)
                        && v >= 0 && v <= 1)
                        return v;
                }
                catch { }
                return IdleOpacity;
            }
        }

        double ballSize;
        readonly Action onClicked;          // 单击（弹出快捷面板）
        readonly Action onPickOne;          // 右键菜单：抽一人
        readonly Action onPickMulti;        // 右键菜单：连抽五人
        readonly Action onResetPool;        // 右键菜单：重置候选池
        readonly Action onHideSelf;         // 右键菜单：隐藏悬浮球
        readonly Action onQuit;             // 右键菜单：退出程序
        readonly Action showMain;           // 显示主窗口

        readonly Border ballBorder;         // 球体（浅色渐变圆）
        readonly Border ballShadow;         // 投影层（球体下方，纯色块，承担 Effect）
        readonly ScaleTransform ballScale;  // 悬停/抽中缩放
        readonly TranslateTransform ballShift; // 贴边滑动
        readonly TextBlock faceText;

        Point downPos;
        bool dragging;
        bool pinned;
        bool hovering;
        int flashTicks;
        readonly DispatcherTimer flashTimer;
        readonly DispatcherTimer idleTimer;
        bool idleDimmed;
        DateTime lastActive = DateTime.Now;

        // 浅色配色（低饱和、低干扰）
        static readonly Color BallHi = Color.FromRgb(0xF0, 0xF6, 0xFF);
        static readonly Color BallMid = Color.FromRgb(0xC6, 0xDC, 0xF9);
        static readonly Color BallEdge = Color.FromRgb(0x9E, 0xC0, 0xEE);
        static readonly Color FaceFg = Color.FromRgb(0x1D, 0x4E, 0xD8);
        static readonly Color Accent = Color.FromRgb(0x3B, 0x82, 0xF6);

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
            Width = ballSize + ShadowPad * 2; Height = ballSize + ShadowPad * 2;
            WindowStartupLocation = WindowStartupLocation.Manual;

            // ---- 球体：浅色径向渐变，干净利落 ----
            ballScale = new ScaleTransform(1, 1);
            ballShift = new TranslateTransform(0, 0);

            // 深蓝字 + 浅蓝底：对比充分且不需要文字阴影（少一点"嘈杂"）
            faceText = new TextBlock
            {
                Text = "摇", FontFamily = new FontFamily("Microsoft YaHei"),
                FontSize = Math.Max(13, ballSize * 0.30), FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(FaceFg),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var faceLayer = new Grid();
            faceLayer.Children.Add(faceText);

            double inner = ballSize;
            // 球体与投影层共用同一个变换实例 —— 悬停缩放 / 抽中弹跳 / 贴边滑动
            // 才会同步作用于投影，视觉上是一个整体。
            var ballTransform = new TransformGroup { Children = { ballScale, ballShift } };

            ballBorder = new Border
            {
                Width = inner, Height = inner,
                CornerRadius = new CornerRadius(inner / 2),
                Background = BuildBallBrush(),
                BorderBrush = new SolidColorBrush(Color.FromArgb(210, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Child = faceLayer,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                UseLayoutRounding = true,
                SnapsToDevicePixels = true,
                RenderTransform = ballTransform,
                RenderTransformOrigin = new Point(0.5, 0.5)
                // ⚠️ 这里**绝对不能**挂 Effect。
                //    Effect 会把球体（渐变 + 描边 + 「摇」字）先渲染成一张中间位图再合成，
                //    而球还要被 RenderTransform 缩放、窗口还要被亚像素移动 ⇒ 位图被反复
                //    重采样，看起来就是"糊"。这与本项目修「按钮文字模糊」时是同一个根因
                //    （见 1a47fb2）。投影改由下面的 ballShadow 承担。
            };

            // 投影层：与球体同尺寸同位置、共用同一个变换，被球体完全盖住 ⇒ 只露出模糊的晕。
            // 它是纯色块，被 Effect 光栅化没有任何细节损失，所以球体本身保持锐利。
            ballShadow = new Border
            {
                Width = inner, Height = inner,
                CornerRadius = new CornerRadius(inner / 2),
                Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x3A, 0x8A)),
                Opacity = 0.20,
                IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransform = ballTransform,
                RenderTransformOrigin = new Point(0.5, 0.5),
                Effect = new DropShadowEffect
                {
                    Color = Color.FromRgb(0x1E, 0x3A, 0x8A), Opacity = 1.0,
                    BlurRadius = 12, ShadowDepth = 1.5, Direction = 270
                }
            };

            var ballLayer = new Grid { UseLayoutRounding = true, SnapsToDevicePixels = true };
            ballLayer.Children.Add(ballShadow);
            ballLayer.Children.Add(ballBorder);
            Content = ballLayer;

            // 抽中后的轻微脉冲（浅色系，收敛为 12 帧 ≈ 1.3s）
            flashTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(110) };
            flashTimer.Tick += (s, e) =>
            {
                flashTicks++;
                if (flashTicks >= 12)
                {
                    flashTimer.Stop();
                    ballBorder.Background = BuildBallBrush();
                    SetFace("摇", Math.Max(13, ballSize * 0.30));
                    return;
                }
                ballBorder.Background = new SolidColorBrush(
                    flashTicks % 2 == 0 ? Color.FromRgb(0xB9, 0xD3, 0xF6) : BallEdge);
            };

            MouseRightButtonUp += (s, e) => { e.Handled = true; ShowContextMenu(); };
            MouseDown += OnBallMouseDown;
            MouseMove += OnBallMouseMove;
            MouseUp += OnBallMouseUp;
            MouseEnter += (s, e) => { hovering = true; Touch(); AnimateScale(1.06, 140); };
            MouseLeave += (s, e) => { hovering = false; Touch(); AnimateScale(1.0, 180); };

            // 构造阶段窗口还没有 PresentationSource，DPI 换算只能靠回退值；
            // 等 SourceInitialized 拿到真实指标后再精确校正一次位置，
            // 否则在多屏 / 非 100% 缩放下可能落在屏幕外。
            RestorePosition();
            SourceInitialized += (s, e) => RestorePosition();
            Loaded += (s, e) => RestorePosition();

            SetPinned(AppConfig.BallPinned);

            // 空闲渐隐：无操作 IdleSeconds 秒后降低不透明度，避免常驻遮挡内容。
            //
            // 用「最后活动时间 + 1s 心跳」判定，而不是一次性的单发定时器 —— 旧实现
            // 在固定(pinned)状态下直接 return，导致"固定后渐隐失效"（这既是功能缺陷，
            // 也让用户误以为渐隐坏了）。渐隐只是视觉减扰，不影响可交互性：
            // 窗口与球体始终保持命中测试，鼠标一进入立即恢复不透明。
            idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            idleTimer.Tick += (s, e) =>
            {
                // 拖动中 / 鼠标悬停 / 快捷面板或右键菜单开着 → 视为正在使用，保持清晰
                if (dragging || hovering || BallQuickPanel.IsOpen || activeCtxMenu != null)
                {
                    lastActive = DateTime.Now;
                    if (idleDimmed) WakeUp();
                    return;
                }
                bool shouldDim = (DateTime.Now - lastActive).TotalSeconds >= IdleSeconds;
                if (shouldDim && !idleDimmed) { idleDimmed = true; AnimateOpacity(IdleOpacityTarget, 520); }
                else if (!shouldDim && idleDimmed) { idleDimmed = false; AnimateOpacity(1.0, 220); }
            };
            idleTimer.Start();
        }

        Brush BuildBallBrush()
        {
            var g = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.34, 0.28),
                Center = new Point(0.42, 0.38),
                RadiusX = 0.90, RadiusY = 0.90
            };
            g.GradientStops.Add(new GradientStop(BallHi, 0.0));
            g.GradientStops.Add(new GradientStop(BallMid, 0.55));
            g.GradientStops.Add(new GradientStop(BallEdge, 1.0));
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

        /// <summary>
        /// 把 DIP 坐标吸附到**整数设备像素**。
        ///
        /// 为什么要这么做：窗口含透明通道（AllowsTransparency）时，WPF 把整个窗口
        /// 渲染成一张位图再交给 DWM 合成；窗口落在非整数设备像素上时，这张位图会被
        /// 重采样一次 —— 球体的渐变、1px 白色描边和「摇」字都会发虚。
        /// 125% 缩放下 1 设备像素 = 0.8 DIP，`Left` 只要不是 0.8 的整数倍就中招
        /// （拖动、贴边滑动、DPI 复算都可能留下小数）。
        /// </summary>
        double SnapDip(double v)
        {
            double s = DpiScaleFactor();
            return s > 0.01 ? Math.Round(v * s) / s : Math.Round(v);
        }

        /// <summary>统一的落位入口：所有 Left/Top 赋值都走这里，保证设备像素对齐。</summary>
        void MoveTo(double left, double top)
        {
            Left = SnapDip(left);
            Top = SnapDip(top);
        }

        /// <summary>平滑移动悬浮球到目标位置（内容先滑动，动画结束后落到真实 Left/Top）。</summary>
        void AnimateMoveTo(double toLeft, double toTop)
        {
            toLeft = SnapDip(toLeft); toTop = SnapDip(toTop);
            try
            {
                double dx = toLeft - Left, dy = toTop - Top;
                if (Math.Abs(dx) < 0.5 && Math.Abs(dy) < 0.5) { MoveTo(toLeft, toTop); return; }
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
                        MoveTo(toLeft, toTop);
                    }
                    catch { }
                };
                ballShift.BeginAnimation(TranslateTransform.XProperty, ax);
                ballShift.BeginAnimation(TranslateTransform.YProperty, ay);
            }
            catch { try { MoveTo(toLeft, toTop); } catch { } }
        }

        // ---------------- 状态 ----------------

        /// <summary>记录一次交互，重置空闲计时。</summary>
        void Touch()
        {
            lastActive = DateTime.Now;
            if (idleDimmed) WakeUp();
        }

        void WakeUp()
        {
            if (idleDimmed) { idleDimmed = false; AnimateOpacity(1.0, 220); }
            else { try { ballBorder.BeginAnimation(UIElement.OpacityProperty, null); ballBorder.Opacity = 1.0; } catch { } }
        }

        void SetFace(string text, double size)
        {
            faceText.Text = text;
            faceText.FontSize = size;
        }

        static ModernMenu? activeCtxMenu;   // 单例：再点球时收起

        /// <summary>右键菜单（现代菜单，跟随系统主题）：显示在悬浮球旁边。</summary>
        void ShowContextMenu()
        {
            if (activeCtxMenu != null) { try { activeCtxMenu.Close(); } catch { } activeCtxMenu = null; return; }
            Touch();
            var m = new ModernMenu(200);
            activeCtxMenu = m;
            m.AddItem("显示主窗口", () => { activeCtxMenu = null; showMain?.Invoke(); });
            m.AddSeparator();
            m.AddItem("抽一人", () => { activeCtxMenu = null; onPickOne?.Invoke(); });
            m.AddItem("连抽五人", () => { activeCtxMenu = null; onPickMulti?.Invoke(); });
            m.AddItem("重置候选池", () => { activeCtxMenu = null; onResetPool?.Invoke(); });
            m.AddSeparator();
            m.AddCheck("开机自启动", AutoStart.IsEnabled(), v => AutoStart.SetEnabled(v));
            m.AddCheck("固定悬浮球", AppConfig.BallPinned, v =>
            {
                AppConfig.BallPinned = v; AppConfig.Save();
                SetPinned(v);
                BallQuickPanel.RefreshPinState(v);
            });
            m.AddSeparator();
            m.AddItem("隐藏悬浮球", () => { activeCtxMenu = null; onHideSelf?.Invoke(); });
            m.AddItem("退出程序", () => { activeCtxMenu = null; onQuit?.Invoke(); });
            m.ShowBeside(this);            // 贴着球弹出（不再依赖相对定位）
        }

        /// <summary>自检用：弹出右键菜单并返回它。</summary>
        public ModernMenu? ShowContextMenuForTest()
        {
            activeCtxMenu = null;
            ShowContextMenu();
            return activeCtxMenu;
        }

        /// <summary>
        /// 抽取后行为：固定时保持原位且保持活跃；未固定时延时贴边收起（露出约 35%，仍可点击唤回）。
        /// </summary>
        public void AutoHideAfterPick()
        {
            Touch();
            if (AppConfig.BallPinned) { WakeUp(); return; }
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            t.Tick += (s, e) => { t.Stop(); TuckToEdge(); };
            t.Start();
        }

        /// <summary>贴边收起：平滑滑到最近的屏幕边缘并只露出一小部分（保留可点击区域）。</summary>
        public void TuckToEdge()
        {
            try
            {
                var wa = WorkAreaDip();
                bool nearRight = Left + Width / 2 > wa.Left + wa.Width / 2;
                double toTop = Math.Max(wa.Top + 8, Math.Min(Top, wa.Bottom - Height - 8));
                // 只露出球体的 35%（按球体本身而非窗口计算，窗口含 ShadowPad 留白）
                double show = ballSize * 0.35;
                double toLeft = nearRight
                    ? wa.Right - show - ShadowPad
                    : wa.Left + show - ballSize - ShadowPad;
                AnimateMoveTo(toLeft, toTop);
                WakeUp();
            }
            catch { }
        }

        /// <summary>
        /// 固定状态：固定只影响"抽完是否自动贴边收起"，不影响空闲渐隐
        /// （渐隐是纯视觉减扰，球体仍始终可点击/可拖动）。
        /// </summary>
        public void SetPinned(bool value)
        {
            try
            {
                pinned = value;
                // 固定只用一条细描边区分，不再用高对比粗黑边（降低"嘈杂"）
                ballBorder.BorderBrush = value
                    ? new SolidColorBrush(Accent)
                    : new SolidColorBrush(Color.FromArgb(210, 0xFF, 0xFF, 0xFF));
                ballBorder.BorderThickness = new Thickness(value ? 2 : 1);
                ballBorder.ToolTip = "YBH幸运摇人器"
                    + (value ? "（已固定：抽完不贴边；闲时渐隐后移入即恢复）"
                             : "（未固定：抽完贴边收起；闲时渐隐）");

                // 状态切换后给一次视觉反馈，随后重新开始空闲计时
                WakeUp();
                lastActive = DateTime.Now;
            }
            catch { }
        }

        /// <summary>
        /// 当前屏幕的工作区，单位统一为 DIP。
        ///
        /// 根因：WinForms 的 Screen.WorkingArea 是**设备像素**，而 WPF 窗口的
        /// Left/Top/Width 是 **DIP**。旧代码直接混用两者，在 125% 缩放下
        /// （屏幕 1920px = 1536 DIP）会把悬浮球甩到屏幕外（x≈1784 DIP ⇒ 2230px），
        /// 结果球完全看不见。
        /// </summary>
        Rect WorkAreaDip()
        {
            try
            {
                var wa = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
                double k = DpiScaleFactor();
                if (k > 0 && Math.Abs(k - 1.0) > 0.001)
                    return new Rect(wa.Left / k, wa.Top / k, wa.Width / k, wa.Height / k);
                return new Rect(wa.Left, wa.Top, wa.Width, wa.Height);
            }
            catch { return new Rect(0, 0, 1920, 1080); }
        }

        [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);

        /// <summary>
        /// 取屏幕缩放系数（DIP → 设备像素）。
        ///
        /// 注意：在窗口构造阶段（尚未 Show）PresentationSource 还不存在，
        /// 此时若只用 PresentationSource 会拿到 Identity(1.0) 而静默算错，
        /// 所以这里做了三级回退：自身 PresentationSource → Visual DPI → Win32 窗口 DPI。
        /// </summary>
        double DpiScaleFactor()
        {
            try
            {
                var src = PresentationSource.FromVisual(this);
                if (src?.CompositionTarget != null)
                {
                    double m = src.CompositionTarget.TransformToDevice.M11;
                    if (m > 0.01) return m;
                }
            }
            catch { }
            try
            {
                var d = VisualTreeHelper.GetDpi(this);
                if (d.DpiScaleX > 0.01) return d.DpiScaleX;
            }
            catch { }
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    uint dpi = GetDpiForWindow(hwnd);
                    if (dpi > 0) return dpi / 96.0;
                }
            }
            catch { }
            try
            {
                // 最后回退：主窗口的 PresentationSource（同进程同屏，缩放一致）
                var mw = System.Windows.Application.Current?.MainWindow;
                var src2 = mw != null ? PresentationSource.FromVisual(mw) : null;
                if (src2?.CompositionTarget != null)
                {
                    double m2 = src2.CompositionTarget.TransformToDevice.M11;
                    if (m2 > 0.01) return m2;
                }
            }
            catch { }
            return 1.0;
        }

        void RestorePosition()
        {
            var wa = WorkAreaDip();
            int x = AppConfig.BallX, y = AppConfig.BallY;

            // 坐标有效性判定，需要排除两类脏值：
            //   1) AppConfig 的"未设置"哨兵 (-1,-1)：直接当坐标用会把球甩到屏幕左上角。
            //      贴边收起时最小坐标是 -(ShadowPad + ballSize*0.35) ≤ -30，不可能恰好等于 -1，
            //      因此 (-1,-1) 可作为安全的哨兵判断。
            //   2) 旧版本单位混用（设备像素 ↔ DIP）写入的越界值，例如 125% 缩放下被吸到
            //      屏幕外右侧的 x≈1784（屏幕仅 1536 DIP 宽）。
            bool sentinel = (x == -1 && y == -1);
            bool reachable = x + Width > 24 && x <= wa.Right - 24
                             && y + Height > 24 && y <= wa.Bottom - 24;
            if (sentinel || !reachable)
            {
                x = (int)(wa.Right - Width - 24);
                y = (int)(wa.Top + wa.Height / 3);
            }
            MoveTo(x, y);
        }

        void SavePosition()
        {
            SnapToEdgeIfNeeded();
            // 拖动（DragMove）与 DPI 复算都可能把窗口丢在非整数设备像素上，
            // 这里最后再吸附一次，保证「停在哪都清晰」。
            MoveTo(Left, Top);
            AppConfig.BallX = (int)Math.Round(Left);
            AppConfig.BallY = (int)Math.Round(Top);
            AppConfig.Save();
        }

        /// <summary>靠边吸附：接近屏幕边缘时贴边并收进一部分（球体露出约 65%）。</summary>
        void SnapToEdgeIfNeeded()
        {
            try
            {
                var wa = WorkAreaDip();
                double w = Width, h = Height;
                int margin = 28;
                double hidden = ballSize * 0.35;      // 收进后隐藏的球体宽度
                double nx = Left, ny = Top;
                if (nx < wa.Left + margin) nx = wa.Left - ShadowPad - hidden;
                else if (nx + w > wa.Right - margin) nx = wa.Right - ShadowPad - (ballSize - hidden);
                if (ny < wa.Top + margin) ny = wa.Top;
                else if (ny + h > wa.Bottom - margin) ny = wa.Bottom - h;
                MoveTo(nx, ny);
            }
            catch { }
        }

        void OnBallMouseDown(object s, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                downPos = e.GetPosition(this);
                dragging = false;
                lastActive = DateTime.Now;
            }
        }

        void OnBallMouseMove(object s, MouseEventArgs e)
        {
            Touch();
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
            Touch();
            onClicked();
        }

        /// <summary>调整悬浮球大小（设置窗口调用），并按比例缩放球面文字。</summary>
        public void SetSize(double d)
        {
            ballSize = Math.Max(64, Math.Min(160, d));
            AppConfig.BallDiameter = (int)ballSize;
            Width = ballSize + ShadowPad * 2;
            Height = ballSize + ShadowPad * 2;
            ballBorder.Width = ballSize;
            ballBorder.Height = ballSize;
            ballBorder.CornerRadius = new CornerRadius(ballSize / 2);
            ballShadow.Width = ballSize;
            ballShadow.Height = ballSize;
            ballShadow.CornerRadius = new CornerRadius(ballSize / 2);
            faceText.FontSize = Math.Max(13, ballSize * 0.30);
            SavePosition();
        }

        /// <summary>球面显示抽中结果：单人姓名 / 多人「N 人」。</summary>
        public void ShowPicked(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            Touch();
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

        /// <summary>自检用：球体自身是否仍挂着 Effect（修复后应为 false —— 挂着就会发虚）。</summary>
        public bool HasOwnEffectNow => ballBorder.Effect != null;

        /// <summary>自检用：投影层是否承担了 Effect（应为 true）。</summary>
        public bool ShadowHasEffectNow => ballShadow.Effect != null;

        /// <summary>自检用：球体与投影层是否共用同一个变换实例（保证缩放/滑动同步）。</summary>
        public bool ShadowSharesTransformNow =>
            ReferenceEquals(ballBorder.RenderTransform, ballShadow.RenderTransform);

        /// <summary>自检用：当前 DPI 缩放。</summary>
        public double DpiScaleNow => DpiScaleFactor();

        /// <summary>
        /// 自检用：窗口左/上边是否落在**整数设备像素**上。
        /// 非整数会触发透明窗口位图重采样 ⇒ 球体发虚，所以要把它变成可断言的事实。
        /// </summary>
        public bool PositionOnWholePixelNow
        {
            get
            {
                double s = DpiScaleFactor();
                if (s <= 0.01) return true;
                double dx = Left * s, dy = Top * s;
                return Math.Abs(dx - Math.Round(dx)) < 0.02 && Math.Abs(dy - Math.Round(dy)) < 0.02;
            }
        }
        public bool IsPinnedNow => pinned;
        public double BallDiameterNow => ballSize;
        public double OpacityNow => ballBorder.Opacity;
        public bool HasScaleTransform => ballBorder.RenderTransform is TransformGroup tg
                                         && tg.Children.Count > 0 && tg.Children[0] is ScaleTransform;
        public double ScaleNow => ballScale.ScaleX;

        /// <summary>球外留白（= 投影可用空间），自检用。</summary>
        public double ShadowPadNow => (Width - ballBorder.Width) / 2;

        /// <summary>自检用：模拟一次交互（等价于鼠标进入/移动），用于验证空闲变淡可被打断恢复。</summary>
        public void TouchForTest() => Touch();

        /// <summary>自检用：模拟鼠标悬停在球上（悬停期间应保持不透明）。</summary>
        public void HoverForTest(bool on) { hovering = on; if (on) Touch(); else lastActive = DateTime.Now; }

        /// <summary>自检用：渐隐期间是否仍可交互（窗口可见 + 球体参与命中测试）。</summary>
        public bool InteractiveNow
        {
            get
            {
                try { return IsVisible && ballBorder.IsHitTestVisible && ballBorder.IsEnabled; }
                catch { return false; }
            }
        }

        public bool IsDimmedNow => idleDimmed;

        /// <summary>
        /// 诊断快照（--fadewatch 模式逐秒落盘用）：把决定渐隐与否的每个条件都摊开，
        /// 用于区分「定时器没跑」和「守卫条件恒真」这两类完全不同的故障。
        /// </summary>
        public string DiagNow
        {
            get
            {
                try
                {
                    return string.Format(
                        "opacity={0:F3} dimmed={1} age={2:F1}s dragging={3} hovering={4} panelOpen={5} ctxMenu={6} pinned={7}",
                        ballBorder.Opacity, idleDimmed,
                        (DateTime.Now - lastActive).TotalSeconds,
                        dragging, hovering, BallQuickPanel.IsOpen, activeCtxMenu != null, pinned);
                }
                catch (Exception ex) { return "diag-err:" + ex.Message; }
            }
        }

        /// <summary>球体渐变各色标的平均亮度（0~1），自检用：应偏浅色。</summary>
        public double BrushLuminanceNow
        {
            get
            {
                try
                {
                    if (ballBorder.Background is RadialGradientBrush g)
                    {
                        double sum = 0;
                        foreach (var st in g.GradientStops)
                            sum += (0.299 * st.Color.R + 0.587 * st.Color.G + 0.114 * st.Color.B) / 255.0;
                        return sum / Math.Max(1, g.GradientStops.Count);
                    }
                    if (ballBorder.Background is SolidColorBrush s)
                        return (0.299 * s.Color.R + 0.587 * s.Color.G + 0.114 * s.Color.B) / 255.0;
                }
                catch { }
                return -1;
            }
        }
    }

    // ---------------- 班级快捷选择小窗 ----------------
    public class ClassMiniWindow : Window
    {
        const int W = 236;
        const int PAD = 10;      // 投影留白
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
            Width = W + PAD * 2;

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
                Width = W,
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(147, 197, 253)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(14, 12, 14, 12),
                Child = panel,
                Effect = new DropShadowEffect { Color = Color.FromArgb(60, 0x1E, 0x3A, 0x8A), BlurRadius = 16, ShadowDepth = 2, Direction = 270 }
            };
            Content = new Grid { Margin = new Thickness(PAD), Children = { card } };

            int count = CountOf(ids);
            Height = 64 + count * 50 + 26 + PAD * 2;
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

        /// <summary>显示在锚点窗口（悬浮球）旁边；单位为 DIP，垂直居中对齐。</summary>
        public void ShowNear(Window anchor)
        {
            double w = Width, h = Height;
            Geometry.PlaceBesideWindow(anchor, w, h, out double x, out double y);
            Left = x; Top = y;
            try { Show(); Activate(); } catch { }
        }
    }

    // ---------------- 悬浮球快捷面板（防误触：点击球先弹面板，不直接抽取） ----------------
    public class BallQuickPanel : Window
    {
        const int W = 188;       // 卡片宽度
        const int PAD = 10;      // 投影留白
        static BallQuickPanel? current;              // 单例
        static DateTime suppressUntil = DateTime.MinValue;   // 因"点击悬浮球"而关闭后的抑制窗口

        /// <summary>最近一次面板的屏幕 DIP 位置（自检/诊断用）。</summary>
        public static Point? LastPanelPos { get; private set; }

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

        /// <summary>当前面板窗口（自检/诊断用）。</summary>
        public static Window? CurrentWindowForTest => current;

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
            Width = W + PAD * 2;

            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = "幸运摇人",
                FontSize = 13, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(30, 64, 175)),
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 2, 0, 8)
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
            panel.Children.Add(MakeButton("显示主窗口", () => { Close(); showMain?.Invoke(); }));

            // 固定悬浮球：勾选后「抽一人」不收起，便于连续抽签
            var chkPin = new CheckBox
            {
                Content = "固定悬浮球", IsChecked = pinned, FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)), Margin = new Thickness(0, 6, 0, 0)
            };
            chkPin.Checked += (s, e) => onPinChanged?.Invoke(true);
            chkPin.Unchecked += (s, e) => onPinChanged?.Invoke(false);
            panel.Children.Add(chkPin);
            _chkPin = chkPin;

            var card = new Border
            {
                Width = W,
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(191, 214, 245)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(10, 10, 10, 8),
                Child = panel,
                Effect = new DropShadowEffect { Color = Color.FromArgb(60, 0x1E, 0x3A, 0x8A), BlurRadius = 16, ShadowDepth = 2, Direction = 270 }
            };
            card.RenderTransform = new TranslateTransform(0, 8);
            card.Opacity = 0;
            Content = new Grid { Margin = new Thickness(PAD), Children = { card } };
            SizeToContent = SizeToContent.Height;   // 自适应高度，确保所有项完整可见

            // 入场动效：淡入 + 轻微上浮
            Loaded += (s, e) =>
            {
                try
                {
                    var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                    card.BeginAnimation(UIElement.OpacityProperty,
                        new DoubleAnimation(1, TimeSpan.FromMilliseconds(140)));
                    if (card.RenderTransform is TranslateTransform tt)
                        tt.BeginAnimation(TranslateTransform.YProperty,
                            new DoubleAnimation(0, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
                }
                catch { }
            };

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

        /// <summary>显示在悬浮球旁边（DIP 换算 + 屏幕边界夹取 + 面板高度实测）。</summary>
        public void ShowNear(Window anchor)
        {
            // 面板用 SizeToContent=Height，弹出前先量出真实高度，避免用 0 参与定位
            double h = ActualHeight;
            if (h <= 0 && Content is FrameworkElement fe)
            {
                fe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                h = fe.DesiredSize.Height;
            }
            if (h <= 0) h = 150;

            Geometry.PlaceBesideWindow(anchor, Width, h, out double x, out double y);
            Left = x; Top = y;
            LastPanelPos = new Point(x, y);
            try { Show(); Activate(); } catch { }
        }
    }

    // ---------------- 屏幕定位辅助 ----------------
    internal static class Geometry
    {
        /// <summary>
        /// 把一个 w×h 的窗口放到 anchor 窗口旁边（优先右侧，放不下则左侧；垂直居中对齐）。
        /// 全部使用 DIP，并用 anchor 的屏幕工作区做边界夹取 —— 旧实现把 device 像素
        /// 的 WorkingArea 和 DIP 的窗口坐标混用，导致菜单/面板位置偏移。
        /// </summary>
        public static void PlaceBesideWindow(Window anchor, double w, double h, out double x, out double y)
        {
            double ax = anchor.Left, ay = anchor.Top, aw = anchor.Width, ah = anchor.Height;
            const double gap = 10;

            double L = 0, T = 0, R = 1920, B = 1080;
            try
            {
                var src = PresentationSource.FromVisual(anchor);
                Matrix toDev = Matrix.Identity, toDip = Matrix.Identity;
                if (src?.CompositionTarget != null)
                {
                    toDev = src.CompositionTarget.TransformToDevice;
                    toDip = src.CompositionTarget.TransformFromDevice;
                }
                var c = toDev.Transform(new Point(ax + aw / 2, ay + ah / 2));
                var wa = System.Windows.Forms.Screen.FromPoint(
                    new System.Drawing.Point((int)c.X, (int)c.Y)).WorkingArea;
                var tl = toDip.Transform(new Point(wa.Left, wa.Top));
                var br = toDip.Transform(new Point(wa.Right, wa.Bottom));
                L = tl.X; T = tl.Y; R = br.X; B = br.Y;
            }
            catch { }

            x = ax + aw + gap;
            if (x + w > R - 4) x = ax - w - gap;
            if (x < L + 4) x = L + 4;
            if (x + w > R - 4) x = R - w - 4;

            y = ay + (ah - h) / 2;
            if (y < T + 4) y = T + 4;
            if (y + h > B - 4) y = B - h - 4;
            if (y < T + 4) y = T + 4;
        }
    }
}
