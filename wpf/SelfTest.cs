// ================================================================
// SelfTest.cs — 应用内自检（仅当命令行带 --selftest <输出文件> 时执行）
//
// 目的：在无头/无法可靠模拟鼠标点击的环境里，仍然能对"设置项即时生效、
//       面板 toggle、子窗口随父关闭、托盘菜单坐标换算"等行为做可复现验证。
// 不影响正常启动路径（不带该参数时完全不执行）。
// ================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace LuckyPickerWpf
{
    public static class SelfTest
    {
        static readonly List<string> Lines = new();
        static void A(string name, bool ok, string detail = "")
            => Lines.Add((ok ? "PASS" : "FAIL") + "\t" + name + "\t" + detail);

        static void Info(string text) => Lines.Add("INFO\t" + text);

        // 自检会改动配置（球大小 / 固定 / 位置），结束后必须原样还原，避免污染用户设置
        static int origDiameter, origX, origY;
        static bool origPinned, origVisible;

        public static void Run(MainWindow win, string outPath)
        {
            origDiameter = AppConfig.BallDiameter;
            origPinned = AppConfig.BallPinned;
            origVisible = AppConfig.BallVisible;
            origX = AppConfig.BallX;
            origY = AppConfig.BallY;
            try
            {
                Execute(win, outPath);
            }
            catch (Exception ex)
            {
                A("自检未抛异常", false, ex.ToString().Split('\n')[0]);
            }
            finally
            {
                try
                {
                    AppConfig.BallDiameter = origDiameter;
                    AppConfig.BallPinned = origPinned;
                    AppConfig.BallVisible = origVisible;
                    AppConfig.BallX = origX;
                    AppConfig.BallY = origY;
                    AppConfig.Save();
                    win.RestoreBallForTest(origX, origY);
                    Info($"已还原用户配置：diameter={origDiameter} pinned={origPinned} visible={origVisible} pos=({origX},{origY})");
                }
                catch { }
                try { File.WriteAllLines(outPath, Lines, Encoding.UTF8); } catch { }
            }
        }

        static void Pump(int ms)
        {
            var frame = new DispatcherFrame();
            var t = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(ms) };
            t.Tick += (s, e) => { t.Stop(); frame.Continue = false; };
            t.Start();
            Dispatcher.PushFrame(frame);
        }

        static void RunCoreChecks()
        {
            var core = new LuckyCore();
            core.Load(new DataFile
            {
                students = new List<Student>
                {
                    new() { name = "甲", classId = "1", gender = "男" },
                    new() { name = "乙", classId = "1", gender = "女" },
                    new() { name = "丙", classId = "1", gender = "male" }
                }
            });
            core.SetGender("male");
            A("核心：中文与英文男性数据均可筛选", core.GetCandidates().Count == 2 && core.GetCandidates().All(s => s.gender == "male"));
            core.SetGender("female");
            A("核心：中文女性数据自动规范化", core.GetCandidates().Count == 1 && core.GetCandidates()[0].name == "乙");
            A("核心：导入性别格式统一", ImportUtil.NormalizeGender("男生") == "male" && ImportUtil.NormalizeGender("女") == "female");

            core.SetGender("all");
            var picked = core.PickMulti(3);
            A("核心：连抽不重复", picked.Count == 3 && picked.Select(s => s.name).Distinct().Count() == 3);
            A("核心：不重复池用完后自动重置", core.PickOne() != null && core.remainPool.Count == 2);

            string temp = Path.Combine(Path.GetTempPath(), "lucky-picker-selftest-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                JsonStorage.WriteAtomic(temp, "{\"value\":1}");
                JsonStorage.WriteAtomic(temp, "{\"value\":2}");
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(temp));
                A("核心：JSON 原子写入与替换", doc.RootElement.GetProperty("value").GetInt32() == 2);
            }
            catch (Exception ex) { A("核心：JSON 原子写入与替换", false, ex.Message); }
            finally { try { File.Delete(temp); } catch { } }

            A("版本：1.2+21 可提取构建号", UpdateManager.ExtractLastInt("1.2+21") == 21);
            A("版本：构建号与显示版本一致", AppVersion.Build == UpdateManager.ExtractLastInt(AppVersion.Display),
              "Build=" + AppVersion.Build + " Display=" + AppVersion.Display);
            var update = UpdateManager.ParseJson("{\"version\":\"1.2+22\",\"build\":22,\"setupUrl\":\"https://example.com/setup.exe\",\"setupSha256\":\"ABC123\"}");
            A("更新：解析安装直链与 SHA-256", update.Success && update.HasUpdate && update.SetupUrl == "https://example.com/setup.exe" && update.SetupSha256 == "abc123");
            A("更新：拒绝 HTTP 下载地址", UpdateManager.ParseJson("{\"version\":\"1.0+19\",\"url\":\"http://example.com/setup.exe\"}").DownloadUrl == "");
            A("更新：拒绝 HTTP 检查接口", !UpdateManager.Check("http://example.com/update.json").Success);
            A("语音：可取消默认输出设备静音", AudioUnmute.DefaultOutput(), AudioUnmute.LastError);
            A("语音：连抽 MP3 合并可识别帧格式并剥离 ID3", TtsEngine.Mp3ParserSelfTest(),
              "MPEG-1 44.1kHz 与 MPEG-2 24kHz 帧长/声道/标签剥离");
            A("语音：连抽 WAV 裁剪首尾静音并无缝拼接", TtsEngine.WavSequenceSelfTest(out string wavDetail), wavDetail);
        }

        static void Execute(MainWindow win, string outPath)
        {
            RunCoreChecks();
            var src = PresentationSource.FromVisual(win);
            double scale = 1.0;
            if (src?.CompositionTarget != null) scale = src.CompositionTarget.TransformToDevice.M11;
            Info("DPI 缩放系数=" + scale.ToString("F3"));

            var screenH = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Height;
            var screenW = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Width;

            // ---------- 3. 悬浮球大小：即时生效 + 持久化 ----------
            int oldD = AppConfig.BallDiameter;
            AppConfig.BallDiameter = 120;
            win.ApplyBallSettings();
            Pump(120);
            double applied = win.BallDiameterForTest;
            A("3a 球大小即时生效(120)", Math.Abs(applied - 120) < 0.5, "ballDiameter=" + applied);

            AppConfig.Save();
            Pump(80);
            int persisted = ReadConfigInt("ballDiameter");
            A("3b 球大小已持久化(120)", persisted == 120, "config.ballDiameter=" + persisted);

            AppConfig.BallDiameter = 80;
            win.ApplyBallSettings();
            Pump(120);
            A("3c 球大小即时生效(80)", Math.Abs(win.BallDiameterForTest - 80) < 0.5,
              "ballDiameter=" + win.BallDiameterForTest);

            AppConfig.BallDiameter = oldD;
            win.ApplyBallSettings();
            Pump(80);

            // ---------- 1. 悬浮球外观：浅色 + 投影留白 ----------
            A("1a 球体投影留白足够（阴影不被窗口裁掉）", win.BallShadowPadForTest >= 7.5,
              "留白=" + win.BallShadowPadForTest.ToString("F1") + " DIP（旧实现仅 3 DIP，12px 投影被裁）");
            A("1b 球体配色偏浅（低干扰）", win.BallBrushLuminanceForTest > 0.7,
              "渐变平均亮度=" + win.BallBrushLuminanceForTest.ToString("F2") + "（旧深蓝约 0.35）");

            // 1c 空闲变淡：4s 后明显变淡，任何交互都能立即恢复
            bool oldPinForIdle = AppConfig.BallPinned;
            AppConfig.BallPinned = false;
            win.ApplyBallSettings();      // 未固定才会闲时变淡
            Pump(200);
            win.BallTouchForTest();
            Pump(300);
            double awake = win.BallOpacityForTest;
            Pump(5600);
            double dimmed = win.BallOpacityForTest;
            win.BallTouchForTest();
            Pump(600);
            double restored = win.BallOpacityForTest;
            A("1c 空闲变淡生效且可被交互恢复",
              awake > 0.95 && dimmed < 0.5 && restored > 0.9,
              $"触发后={awake:F2} → 空闲5.6s={dimmed:F2} → 交互后={restored:F2}");
            AppConfig.BallPinned = oldPinForIdle;
            win.ApplyBallSettings();
            Pump(150);

            // 1d 悬浮球必须落在屏幕工作区内（DIP）。
            // 旧实现把设备像素的 WorkingArea 与 DIP 的窗口坐标混用，
            // 125% 缩放下会把球吸到屏幕外（x≈1500..1612 > 1536 DIP）；
            // 另需排除 AppConfig 的 (-1,-1) 未设置哨兵。
            {
                double bl = win.BallLeftForTest, bt = win.BallTopForTest, bw2 = win.BallWidthForTest;
                double dw = screenW / scale, dh = screenH / scale;
                bool onScreen = bl + bw2 > 24 && bl <= dw - 24 && bt + bw2 > 24 && bt <= dh - 24;
                A("1d 悬浮球位置在屏幕工作区内（DIP 未与设备像素混用）", onScreen,
                  $"ball=({bl:F0},{bt:F0},{bw2:F0}) 屏幕DIP={dw:F0}x{dh:F0}");
            }

            // 悬浮球外观截图（浅色 + 留白投影，衬灰底便于观察）
            try
            {
                string ballShot = Path.Combine(Path.GetDirectoryName(outPath) ?? ".", "ball.png");
                var ballWin = win.BallWindowForTest;
                if (ballWin != null) A("截图 悬浮球", SaveShotPadded(ballWin, ballShot, 18, Color.FromRgb(0xEE, 0xF2, 0xF7)), ballShot);
            }
            catch { }

            // ---------- 8. 固定悬浮球：抽完不贴边 + 始终可交互（但闲时仍渐隐） ----------
            bool oldPin = AppConfig.BallPinned;
            AppConfig.BallPinned = true;
            win.ApplyBallSettings();
            Pump(120);
            A("8a 固定状态已应用", win.BallIsPinnedForTest, "pinned=" + win.BallIsPinnedForTest);

            // 固定时应跳过"抽完贴边收起"（返回前不启动定时器，位置不变）
            double left0 = win.BallLeftForTest, top0 = win.BallTopForTest;
            win.BallAutoHideForTest();
            Pump(3600);   // 超过 3s 自动收起延时
            bool samePos = Math.Abs(win.BallLeftForTest - left0) < 1.5 && Math.Abs(win.BallTopForTest - top0) < 1.5;
            A("8b 固定时抽完不贴边收起", samePos, $"L {left0:F0}->{win.BallLeftForTest:F0} T {top0:F0}->{win.BallTopForTest:F0}");

            // 固定状态同样应空闲渐隐（视觉减扰），且必须保持可交互 —— 这是"固定后渐隐失效"的修复点
            win.BallHoverForTest(false);
            win.BallTouchForTest();
            Pump(400);
            double pinAwake = win.BallOpacityForTest;
            Pump(5600);
            double pinDim = win.BallOpacityForTest;
            bool pinInteractive = win.BallInteractiveForTest;
            A("8c 固定时同样空闲渐隐（不再跳过渐隐）",
              pinAwake > 0.95 && pinDim < 0.5,
              $"固定后 触发={pinAwake:F2} → 空闲5.6s={pinDim:F2}（旧实现在此恒为 1.00）");
            A("8d 渐隐期间仍保持可交互（窗口可见+可命中）", pinInteractive,
              "visible=" + pinInteractive);
            // 渐隐态外观截图（对比用）
            try
            {
                string dimShot = Path.Combine(Path.GetDirectoryName(outPath) ?? ".", "ball_dimmed.png");
                var bw3 = win.BallWindowForTest;
                if (bw3 != null) A("截图 悬浮球(渐隐态)", SaveShotPadded(bw3, dimShot, 18, Color.FromRgb(0xEE, 0xF2, 0xF7)), dimShot);
            }
            catch { }

            // 悬停在球上时不应渐隐
            win.BallHoverForTest(true);
            Pump(5200);
            double hoverOpacity = win.BallOpacityForTest;
            win.BallHoverForTest(false);
            A("8e 鼠标悬停时不渐隐（移入即保持清晰）", hoverOpacity > 0.95,
              "hover 5.2s 后 opacity=" + hoverOpacity.ToString("F2"));

            AppConfig.BallPinned = false;
            win.ApplyBallSettings();
            win.BallHoverForTest(false);
            win.BallTouchForTest();
            Pump(5600);
            A("8f 取消固定后同样闲时渐隐", win.BallOpacityForTest < 0.95,
              "opacity=" + win.BallOpacityForTest.ToString("F2"));
            AppConfig.BallPinned = oldPin;
            win.ApplyBallSettings();
            win.BallTouchForTest();
            Pump(80);

            // ---------- 9. 悬浮球动效结构 + 动画真的在跑 ----------
            A("9a 球体带缩放变换(动效载体)", win.BallHasScaleForTest, "RenderTransform=TransformGroup+ScaleTransform");
            win.BallShowPickedForTest("测试");
            Pump(110);
            double scMid = win.BallScaleForTest;      // 弹跳过程中应 >1
            Pump(500);
            double scEnd = win.BallScaleForTest;      // 动画结束后回到 1
            A("9b 抽中弹跳动画实际运行", scMid > 1.02 && Math.Abs(scEnd - 1.0) < 0.06,
              $"t=110ms scale={scMid:F2}(>1) -> t=610ms scale={scEnd:F2}(≈1)");

            // ---------- 7. 点击悬浮球 → 面板 toggle + 位置贴近悬浮球 ----------
            // 先确保球可见（否则面板会退回屏幕居中）
            AppConfig.BallVisible = true;
            win.ApplyBallSettings();
            Pump(320);
            double bx = win.BallLeftForTest, by = win.BallTopForTest, bw = win.BallWidthForTest;

            bool before = BallQuickPanel.IsOpen;
            win.BallPanelToggleForTest();     // 打开
            Pump(320);
            bool opened = BallQuickPanel.IsOpen;
            var panelPos = MainWindow.BallPanelPosForTest;
            // 面板外观截图
            try
            {
                string panelShot = Path.Combine(Path.GetDirectoryName(outPath) ?? ".", "ball_panel.png");
                var pw = BallQuickPanel.CurrentWindowForTest;
                if (pw != null) A("截图 快捷面板", SaveShotPadded(pw, panelShot, 18, Color.FromRgb(0xEE, 0xF2, 0xF7)), panelShot);
            }
            catch { }
            // 面板打开期间不应渐隐（用户正在操作）。
            //
            // 注意：快捷面板是"失焦即自动收起"的设计，而自动化环境里焦点可能被系统抢走，
            // 导致面板在下面的长泵送中提前关闭 —— 那样球体就会照常渐隐，断言偶发翻转。
            // 因此这里最多重试 3 次，且只在"整段期间面板始终开着"时才认可结果。
            bool panelHeld = false;
            double panelOpenOpacity = 1;
            for (int attempt = 0; attempt < 3 && !panelHeld; attempt++)
            {
                if (!BallQuickPanel.IsOpen) { Pump(500); win.BallPanelToggleForTest(); Pump(320); }
                if (!BallQuickPanel.IsOpen) continue;
                win.BallTouchForTest();
                Pump(5600);
                panelOpenOpacity = win.BallOpacityForTest;
                panelHeld = BallQuickPanel.IsOpen;      // 全程保持打开才算有效
            }
            A("7b 快捷面板打开期间不渐隐（保持可见）", panelHeld && panelOpenOpacity > 0.95,
              $"面板全程保持打开且空闲 5.6s 后 opacity={panelOpenOpacity.ToString("F2")}");

            // 同样先确保面板是开着的，否则"再点一次"会变成"打开"而不是"收起"。
            if (!BallQuickPanel.IsOpen) { Pump(500); win.BallPanelToggleForTest(); Pump(400); }
            bool openBeforeSecond = BallQuickPanel.IsOpen;
            win.BallPanelToggleForTest();     // 再点 → 收起
            Pump(300);
            bool closed = !BallQuickPanel.IsOpen;
            A("7 面板 toggle（开→再点收起）", !before && opened && openBeforeSecond && closed,
              $"before={before} opened={opened} openBefore2nd={openBeforeSecond} closedAfter2nd={closed}");

            // 面板应紧贴悬浮球（水平间距 ≈ 10 DIP），而不是漂到屏幕其他位置
            bool besideOk = false;
            string besideDetail = "panelPos=null";
            if (panelPos.HasValue)
            {
                double panelW = 208;   // 卡片 188 + 左右各 10 留白
                double gapRight = panelPos.Value.X - (bx + bw);
                double gapLeft = bx - (panelPos.Value.X + panelW);
                double vCenter = panelPos.Value.Y;
                bool gapOk = Math.Abs(gapRight - 10) < 4 || Math.Abs(gapLeft - 10) < 4;
                bool vOk = vCenter > by - 200 && vCenter < by + bw + 200;
                besideOk = gapOk && vOk;
                besideDetail = $"panel=({panelPos.Value.X:F0},{panelPos.Value.Y:F0}) ball=({bx:F0},{by:F0},{bw:F0}) " +
                               $"gapR={gapRight:F0} gapL={gapLeft:F0}";
            }
            A("4 快捷面板位置紧贴悬浮球（不漂移）", besideOk, besideDetail);

            // 4b 悬浮球右键菜单同样应贴在球旁
            //    ⚠️ 断言一律用 **Win32 实测的真实屏幕矩形**（ActualRectPx / BallWindowRectPx），
            //    不能用 LastScreenOffset —— 那是我们自己算出来的目标值。旧实现里目标值一直是对的，
            //    是 Popup 没有照着摆，于是自检 43/43 全过、用户看到的菜单却跑到了屏幕左上角。
            //    （教训与 Blog App 那轮一致：别断言自己假设可靠的中间信号，要断言最终产物。）
            var ballMenu = win.BallContextMenuForTest();
            Pump(260);
            var ballRect = win.BallWindowRectPxForTest;
            var bmRect = ballMenu?.ActualRectPx() ?? new Rect(0, 0, 0, 0);
            bool ballMenuOk = false;
            string bmDetail = "ballMenu=null";
            if (ballMenu != null && bmRect.Width > 0 && ballRect.Width > 0)
            {
                double gapR = bmRect.Left - ballRect.Right;
                double gapL = ballRect.Left - bmRect.Right;
                bool gapOk = Math.Abs(gapR - 10 * scale) < 8 * scale || Math.Abs(gapL - 10 * scale) < 8 * scale;
                bool vOk = bmRect.Top + bmRect.Height > ballRect.Top - 260 * scale
                        && bmRect.Top < ballRect.Bottom + 120 * scale;
                bool onScreen = bmRect.Left >= 0 && bmRect.Top >= 0
                             && bmRect.Right <= screenW && bmRect.Bottom <= screenH;
                ballMenuOk = gapOk && vOk && onScreen;
                bmDetail = $"实测菜单矩形=({bmRect.Left:F0},{bmRect.Top:F0},{bmRect.Width:F0}×{bmRect.Height:F0})px " +
                           $"球=({ballRect.Left:F0},{ballRect.Top:F0},{ballRect.Width:F0})px gapR={gapR:F0} gapL={gapL:F0}";
            }
            A("4b 悬浮球右键菜单贴在球旁（Win32 实测矩形）", ballMenuOk, bmDetail);
            try { ballMenu?.Close(); } catch { }
            Pump(150);

            // 4c 悬浮球模糊修复：球体自身绝不能挂 Effect。
            //    Effect 会把球体（渐变 + 1px 白描边 + 「摇」字）先渲染成一张中间位图再合成，
            //    而球还要被 RenderTransform 缩放、窗口还要被亚像素移动 ⇒ 位图被反复重采样 ⇒ 发虚。
            //    投影改由独立纯色层承担（它没有细节可丢）。
            A("4c 球体自身不挂 Effect，投影由独立层承担",
              !win.BallHasOwnEffectForTest && win.BallShadowHasEffectForTest && win.BallShadowSharesTransformForTest,
              $"球体Effect={win.BallHasOwnEffectForTest}(应False) 投影层Effect={win.BallShadowHasEffectForTest}(应True) " +
              $"共用变换={win.BallShadowSharesTransformForTest}(应True)");

            // 4d 窗口必须落在整数设备像素上 —— 含透明通道的窗口在非整像素位置会被 DWM 重采样，
            //    125% 缩放下 1 设备像素 = 0.8 DIP，Left 只要不是 0.8 的整数倍就中招。
            A("4d 悬浮球窗口对齐整数设备像素",
              win.BallOnWholePixelForTest,
              $"Left={win.BallLeftForTest:F2} Top={win.BallTopForTest:F2} scale={win.BallDpiScaleForTest:F2} " +
              $"⇒ Left×scale={win.BallLeftForTest * win.BallDpiScaleForTest:F3}(应为整数)");

            // ---------- 2. 托盘菜单：贴图标弹出 + 向上翻转 ----------
            // ⚠️ 全部用 **Win32 实测的真实屏幕矩形**（设备像素）断言。
            //    旧实现用 LastScreenOffset（我们自己算的目标值）断言，于是"目标对、实际错"
            //    的 bug 一路绿灯通过 —— 用户看到的正是这个：菜单跑到屏幕左上角。
            int cx = screenW - 60, cy = screenH - 24;         // 模拟托盘图标位置（右下角，设备像素）
            var trayMenu = win.ShowTrayMenuForTest(cx, cy);
            Pump(260);
            var tr = trayMenu?.ActualRectPx() ?? new Rect(0, 0, 0, 0);
            bool has = trayMenu != null && tr.Width > 0;

            // 2a 核心回归：菜单必须贴着托盘图标，而不是缩到屏幕左上角
            bool nearIcon = has && (Math.Abs(tr.Left - cx) < 8 * scale || Math.Abs(tr.Right - cx) < 8 * scale);
            A("2a 托盘菜单贴着托盘图标（Win32 实测，非左上角）", nearIcon,
              has ? $"实测菜单矩形 x∈[{tr.Left:F0},{tr.Right:F0}]px 图标x={cx}px 菜单=({tr.Left:F0},{tr.Top:F0},{tr.Width:F0}×{tr.Height:F0})"
                  : "菜单未落地");

            // 2b 底部托盘 → 菜单应向上翻转；2c 且完整落在屏幕内
            bool aboveCursor = has && tr.Bottom <= cy;
            bool insideScreen = has && tr.Left >= 0 && tr.Top >= 0 && tr.Right <= screenW && tr.Bottom <= screenH;
            A("2b 底部托盘菜单向上翻转（实测底边在图标上方）", aboveCursor && insideScreen,
              has ? $"菜单底边={tr.Bottom:F0}px ≤ 图标y={cy}px；矩形=({tr.Left:F0},{tr.Top:F0})–({tr.Right:F0},{tr.Bottom:F0}) 屏幕={screenW}×{screenH}"
                  : "菜单未落地");

            win.HideTrayMenuForTest();
            var reopened = win.ShowTrayMenuForTest(cx, cy);
            A("2c 失焦自动关闭后下一次右键可直接重开", reopened != null && reopened.IsOpen,
              "isOpen=" + (reopened?.IsOpen ?? false));
            win.CloseTrayMenuForTest();

            var cursorMenu = win.ShowTrayMenuAtCursorForTest(cx, cy);
            var cr = cursorMenu?.ActualRectPx() ?? new Rect(0, 0, 0, 0);
            bool cursorNear = cursorMenu != null && cr.Width > 0 &&
                (Math.Abs(cr.Left - cx) < 8 * scale || Math.Abs(cr.Right - cx) < 8 * scale) && cr.Bottom <= cy;
            A("2d 实际右键读取当前光标坐标（不使用失效事件坐标）", cursorNear,
              $"菜单=({cr.Left:F0},{cr.Top:F0},{cr.Width:F0}×{cr.Height:F0}) 光标=({cx},{cy})");
            win.CloseTrayMenuForTest();

            // 3b/3c 关键回归：主窗口"关闭到托盘"（Hide）后，菜单仍须正确定位 ——
            //        这正是最初用 MainWindow 当 Popup 锚点而失效的场景。
            win.HideMainForTest();
            Pump(220);
            var trayMenu2 = win.ShowTrayMenuForTest(cx, cy);
            Pump(260);
            var tr2 = trayMenu2?.ActualRectPx() ?? new Rect(0, 0, 0, 0);
            bool has2 = trayMenu2 != null && tr2.Width > 0;
            bool nearIcon2 = has2 && (Math.Abs(tr2.Left - cx) < 8 * scale || Math.Abs(tr2.Right - cx) < 8 * scale);
            bool sane2 = has2 && tr2.Top >= 0 && tr2.Left >= 0 && tr2.Right <= screenW && tr2.Bottom <= screenH
                         && tr2.Bottom <= cy;
            A("3b 主窗口隐藏时菜单仍贴在托盘图标（不再依赖隐藏的 MainWindow）", sane2 && nearIcon2,
              has2 ? $"实测矩形=({tr2.Left:F0},{tr2.Top:F0},{tr2.Width:F0}×{tr2.Height:F0})px 图标x={cx}px"
                  : "菜单未落地（旧实现此场景失效）");
            win.CloseTrayMenuForTest();
            win.ShowMainForTest();
            Pump(220);

            // ---------- 1 + 4 + 6. 设置窗口 ----------
            win.OpenSettingsForTest();
            Pump(700);
            var sw = win.SettingsWindowForTest;
            A("1a 设置窗口已打开", sw != null && sw.IsVisible, sw == null ? "null" : "visible");
            if (sw != null)
                A("1a2 配色卡片已构建", sw.ThemeCardCountForTest == ThemeManager.All.Length,
                  "cards=" + sw.ThemeCardCountForTest + " all=" + ThemeManager.All.Length);

            if (sw != null)
            {
                int pageCount = win.SettingsPageCountForTest(sw);
                A("1b 设置窗口 5 个分类页", pageCount == 5, "pages=" + pageCount);

                bool inlineRoster = win.SettingsHasInlineRosterForTestAfterShow(sw);
                A("1c 名单编辑为内嵌（DataGrid 在设置窗口内）", inlineRoster,
                  "切换到名单管理页后，rosterGrid 在设置窗口逻辑/视觉树内");

                // 主窗口截图（in-process，不依赖前台/截屏）
                string shotRoot = Path.GetDirectoryName(outPath) ?? ".";
                A("截图 主窗口", SaveShot(win, Path.Combine(shotRoot, "main_window.png")),
                  Path.Combine(shotRoot, "main_window.png"));

                var strayEditor = Application.Current.Windows.OfType<Window>()
                    .FirstOrDefault(w => w.Title != null && w.Title.Contains("名单编辑") && !ReferenceEquals(w, sw));
                A("1d 没有弹出独立名单编辑窗口", strayEditor == null,
                  strayEditor == null ? "无独立窗口" : "发现 " + strayEditor.Title);

                bool rounded = win.SettingsNavRoundedForTest(sw);
                A("4 左栏选中项圆角（自定义 TreeViewItem 模板）", rounded, "CornerRadius=10 的选中 Border");

                // 2. 悬浮球大小单选：任意时刻只能勾选一项（此前每项 GroupName 唯一 → 全选）
                sw.ShowPageForTest(0);
                Pump(280);
                win.SettingsCheckBallSizeForTest(sw, 120);
                int c120 = win.SettingsBallSizeCheckedCountForTest(sw);
                win.SettingsCheckBallSizeForTest(sw, 80);
                int c80 = win.SettingsBallSizeCheckedCountForTest(sw);
                bool sizeApplied = Math.Abs(win.BallDiameterForTest - 80) < 0.5;
                A("2d 球大小单选互斥（切到大后选小，只勾一项）", c120 == 1 && c80 == 1,
                  $"选120后勾选数={c120}，再选80后勾选数={c80}（旧实现=2，全打勾）");
                A("2e 选中球大小即时生效", sizeApplied, "ballDiameter=" + win.BallDiameterForTest);
                double prevD = win.SettingsBallPreviewDiameterForTest(sw);
                A("5 设置页预览球随尺寸同步", Math.Abs(prevD - 80) < 0.5, "预览直径=" + prevD.ToString("F0"));

                // 状态栏不应把上一页的操作反馈带到别的页面上（旧实现会显示"悬浮球大小已更新为 80"）
                string staleText = win.SettingsStatusTextForTest(sw);      // 此时是变更尺寸后的反馈
                win.SettingsSwitchPageForTest(sw, 1);                       // 切到「语音」
                Pump(80);
                string voiceText = win.SettingsStatusTextForTest(sw);
                win.SettingsSwitchPageForTest(sw, 0);                       // 切回「常规」
                Pump(80);
                string backText = win.SettingsStatusTextForTest(sw);
                A("5b 切换分类页时状态栏跟随页面（不串台）",
                  !string.IsNullOrWhiteSpace(voiceText)
                  && voiceText != staleText
                  && !voiceText.Contains("悬浮球大小")
                  && !string.IsNullOrWhiteSpace(backText),
                  $"常规页='{staleText}' → 语音页='{voiceText}' → 切回='{backText}'");

                AppConfig.BallDiameter = oldD; AppConfig.Save(); win.ApplyBallSettings();
                Pump(150);

                // 逐页渲染截图（in-process RenderTargetBitmap，不依赖点击/前台）
                string shotDir = Path.Combine(Path.GetDirectoryName(outPath) ?? ".", "settings_shots");
                int shots = 0;
                for (int p = 0; p < 5; p++)
                {
                    sw.ShowPageForTest(p);
                    Pump(420);
                    if (SaveShot(sw, Path.Combine(shotDir, $"page{p}_{PageName(p)}.png"))) shots++;
                }
                sw.ShowPageForTest(0);
                Pump(250);
                A("截图 设置窗口 5 个分类页", shots == 5, shots + "/5 张 → " + shotDir);

                // ---------- 11. 配色主题：切换即时生效 + 持久化 + 悬浮球同步 ----------
                string oldTheme = AppConfig.Theme;
                sw.ShowPageForTest(0);
                Pump(200);
                A("11a 设置页提供 3 套配色", sw.ThemeCardCountForTest == 3,
                  "cards=" + sw.ThemeCardCountForTest + " all=" + ThemeManager.All.Length);

                var mist = ThemeManager.Get("mist");
                bool pickedMist = sw.SelectThemeForTest("mist");
                Pump(1100);
                bool mistApplied = pickedMist && ThemeManager.Current.Id == "mist" && AppConfig.Theme == "mist";
                bool mistWindow = win.Background is SolidColorBrush wbMist && wbMist.Color == mist.Canvas;
                bool mistButton = win.PickBtn.Background is SolidColorBrush pbMist && pbMist.Color == mist.Accent;
                A("11b 切换「雾霭蓝」立即生效并写入配置", mistApplied && mistWindow && mistButton,
                  $"theme={ThemeManager.Current.Id} window={mistWindow} button={mistButton}");
                bool mistBall = win.BallTopColorForTest.HasValue && win.BallTopColorForTest.Value == mist.BallHi;
                A("11c 悬浮球同步换色（雾霭蓝）", mistBall,
                  "ballTop=" + (win.BallTopColorForTest?.ToString() ?? "null") + " 期望=" + mist.BallHi);
                A("截图 雾霭蓝主窗口", SaveShot(win, Path.Combine(shotRoot, "theme_mist_main.png")),
                  Path.Combine(shotRoot, "theme_mist_main.png"));
                A("截图 雾霭蓝设置页", SaveShot(sw, Path.Combine(shotRoot, "theme_mist_settings.png")),
                  Path.Combine(shotRoot, "theme_mist_settings.png"));

                var ink = ThemeManager.Get("ink");
                bool pickedInk = sw.SelectThemeForTest("ink");
                Pump(1100);
                bool inkApplied = pickedInk && ThemeManager.Current.Id == "ink" && AppConfig.Theme == "ink";
                bool inkWindow = win.Background is SolidColorBrush wbInk && wbInk.Color == ink.Canvas;
                bool inkButton = win.PickBtn.Background is SolidColorBrush pbInk && pbInk.Color == ink.Accent;
                bool inkBall = win.BallTopColorForTest.HasValue && win.BallTopColorForTest.Value == ink.BallHi;
                A("11d 切换「墨色极简」立即生效并写入配置", inkApplied && inkWindow && inkButton && inkBall,
                  $"theme={ThemeManager.Current.Id} window={inkWindow} button={inkButton} ball={inkBall}");
                A("截图 墨色极简主窗口", SaveShot(win, Path.Combine(shotRoot, "theme_ink_main.png")),
                  Path.Combine(shotRoot, "theme_ink_main.png"));
                A("截图 墨色极简设置页", SaveShot(sw, Path.Combine(shotRoot, "theme_ink_settings.png")),
                  Path.Combine(shotRoot, "theme_ink_settings.png"));

                bool pickedBack = sw.SelectThemeForTest(oldTheme);
                Pump(1100);
                string nowTheme = ThemeManager.Get(ThemeManager.Current.Id).Id;
                A("11e 恢复原配色并持久化", pickedBack && nowTheme == ThemeManager.Get(oldTheme).Id && AppConfig.Theme == nowTheme,
                  "restored=" + nowTheme + " expected=" + ThemeManager.Get(oldTheme).Id);

                win.OpenHistoryForTest();
                Pump(300);
                A("6a 抽选记录作为设置内独立导航页打开", win.SettingsCurrentPageForTest(sw) == 3,
                  "page=" + win.SettingsCurrentPageForTest(sw));
                bool historyChild = Application.Current.Windows.OfType<Window>()
                    .Any(w => w.Title != null && w.Title == "抽选记录" && w.IsVisible);
                A("6b 不再弹出重复的抽选记录子窗口", !historyChild, "childWindow=" + historyChild);
                sw.Close();
                Pump(900);
                bool settingsGone = win.SettingsWindowForTest == null;
                A("6c 设置窗口引用已释放", settingsGone, "settingsWin=" + (settingsGone ? "null" : "非空"));
            }

            // ---------- 顶部快捷入口：直达设置对应分类页 ----------
            win.RosterBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump(700);
            var swRoster = win.SettingsWindowForTest;
            A("顶部「名单管理」直达设置·名单管理页",
              swRoster != null && win.SettingsCurrentPageForTest(swRoster) == 2,
              "page=" + (swRoster == null ? -1 : win.SettingsCurrentPageForTest(swRoster)));
            swRoster?.Close();
            Pump(900);

            win.HistoryBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump(700);
            var swHistory = win.SettingsWindowForTest;
            A("顶部「抽取记录」直达设置·抽选记录页",
              swHistory != null && win.SettingsCurrentPageForTest(swHistory) == 3,
              "page=" + (swHistory == null ? -1 : win.SettingsCurrentPageForTest(swHistory)));
            swHistory?.Close();
            Pump(900);

            // ---------- 5. 按钮清晰度：模板中不再使用 Effect ----------
            bool noEffectOnBigBtn = win.BigButtonsHaveNoEffectForTest();
            A("5 大按钮不再挂 DropShadowEffect（消除位图栅格化发虚）", noEffectOnBigBtn,
              "PickBtn/MultiBtn/NumBtn Effect 均为 null");

            // ---------- 8. 开机自启动（Build 18 回归）----------
            // 8a 开启后 IsEnabled() 必须为真。
            //    早期实现把 Run 值 `"路径" /min` 直接 Trim('"',' ') 后当路径比较 ——
            //    Trim 只去首尾引号，尾部残留 `" /min` ⇒ 永远不相等 ⇒ 永远返回 false，
            //    表现为「勾选开机自启动后，重进设置页又变回未勾选」（所有设备都中）。
            bool autoWas = AutoStart.IsEnabled();
            AutoStart.SetEnabled(true);
            bool afterOn = AutoStart.IsEnabled();
            A("8a 开机自启动：开启后 IsEnabled() 为真（回归：Trim 解析导致永远 false）",
              afterOn, $"开启前={autoWas} → 开启后={afterOn}");

            // 8b 关闭后必须为假
            AutoStart.SetEnabled(false);
            bool afterOff = AutoStart.IsEnabled();
            A("8b 开机自启动：关闭后 IsEnabled() 为假", !afterOff, $"关闭后={afterOff}");

            // 8c 遗留中文值名必须被自动清理。
            //    安装器早期版本把 Run 值名写成中文「YBH幸运摇人器」，主程序用
            //    `YBHLuckyPicker` —— 两个值名并存 ⇒ 开机把程序拉起两次 ⇒
            //    两个实例互相看到对方进程、双双退出 ⇒「弹提示但程序起不来」。
            bool legacyCleaned = false;
            string legacyDetail = "无法访问 Run 键";
            try
            {
                using (var rk = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (rk != null)
                    {
                        rk.SetValue("YBH幸运摇人器", "\"C:\\nonexistent\\LuckyPicker.exe\" /min");
                        bool seeded = rk.GetValue("YBH幸运摇人器") != null;
                        AutoStart.SetEnabled(true);      // 开关一次就应当顺手清掉遗留名
                        bool gone = rk.GetValue("YBH幸运摇人器") == null;
                        legacyCleaned = seeded && gone && AutoStart.IsEnabled();
                        legacyDetail = $"塞入遗留名={seeded} 清理后仍在={!gone} 规范名生效={AutoStart.IsEnabled()}";
                    }
                }
            }
            catch (Exception ex) { legacyDetail = "异常: " + ex.Message; }
            A("8c 开机自启动：遗留中文值名被自动清理（回归：两值名并存⇒开机启动两次⇒双双退出）",
              legacyCleaned, legacyDetail);

            // 还原用户原本的自启动设置
            AutoStart.SetEnabled(autoWas);
            Info("已还原开机自启动设置：" + autoWas);
        }

        static int ReadConfigInt(string key)
        {
            try
            {
                var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(AppConfig.ConfigPath));
                if (doc.RootElement.TryGetProperty(key, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.Number)
                    return v.GetInt32();
            }
            catch { }
            return -1;
        }

        static string PageName(int i) => new[] { "general", "voice", "roster", "history", "about" }[i];

        /// <summary>把窗口当前内容渲染成 PNG（不依赖窗口在前台）。</summary>
        static bool SaveShot(Window w, string path)
        {
            try
            {
                if (w.Content is not FrameworkElement root) return false;
                root.UpdateLayout();
                double cw = root.ActualWidth + root.Margin.Left + root.Margin.Right;
                double ch = root.ActualHeight + root.Margin.Top + root.Margin.Bottom;
                if (cw <= 0 || ch <= 0) return false;
                var transform = PresentationSource.FromVisual(w)?.CompositionTarget?.TransformToDevice;
                double sx = transform?.M11 ?? 1, sy = transform?.M22 ?? 1;
                var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    (int)Math.Ceiling(cw * sx), (int)Math.Ceiling(ch * sy),
                    96 * sx, 96 * sy, PixelFormats.Pbgra32);
                rtb.Render(w);
                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var fs = File.Create(path);
                enc.Save(fs);
                return true;
            }
            catch { return false; }
        }

        /// <summary>带衬底的截图：留出 pad 像素边距，便于观察投影等溢出边界的效果。</summary>
        static bool SaveShotPadded(Window w, string path, int pad, Color bg)
        {
            try
            {
                if (w.Content is not FrameworkElement root) return false;
                root.UpdateLayout();
                double rw = root.ActualWidth, rh = root.ActualHeight;
                if (rw <= 0 || rh <= 0) return false;
                int cw = (int)Math.Ceiling(rw) + pad * 2;
                int ch = (int)Math.Ceiling(rh) + pad * 2;

                var dv = new System.Windows.Media.DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    dc.DrawRectangle(new SolidColorBrush(bg), null, new Rect(0, 0, cw, ch));
                    var vb = new VisualBrush(root) { Stretch = Stretch.Fill };
                    dc.DrawRectangle(vb, null, new Rect(pad, pad, rw, rh));
                }
                var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(cw, ch, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(dv);
                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var fs = File.Create(path);
                enc.Save(fs);
                return true;
            }
            catch { return false; }
        }
    }
}