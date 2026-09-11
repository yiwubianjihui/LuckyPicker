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

        static void Execute(MainWindow win, string outPath)
        {
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
            win.BallTouchForTest();
            Pump(400);
            double pinAwake = win.BallOpacityForTest;
            Pump(5200);
            double pinDim = win.BallOpacityForTest;
            bool pinInteractive = win.BallInteractiveForTest;
            A("8c 固定时同样空闲渐隐（不再跳过渐隐）",
              pinAwake > 0.95 && pinDim < 0.5,
              $"固定后 触发={pinAwake:F2} → 空闲5.2s={pinDim:F2}（旧实现在此恒为 1.00）");
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
            win.BallTouchForTest();
            Pump(5200);
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
            var ballMenu = win.BallContextMenuForTest();
            var bmPos = ballMenu?.LastScreenOffset;
            var bmSize = ballMenu?.LastMenuSize;
            bool ballMenuOk = false;
            string bmDetail = "ballMenuPos=null";
            if (bmPos.HasValue && bmSize.HasValue)
            {
                double gapR = bmPos.Value.X - (bx + bw);
                double gapL = bx - (bmPos.Value.X + bmSize.Value.Width);
                bool gapOk = Math.Abs(gapR - 10) < 8 || Math.Abs(gapL - 10) < 8;
                bool vOk = bmPos.Value.Y > by - 260 && bmPos.Value.Y < by + bw + 100;
                bool onScreen = bmPos.Value.X >= 0 && bmPos.Value.Y >= 0;
                ballMenuOk = gapOk && vOk && onScreen;
                bmDetail = $"menu=({bmPos.Value.X:F0},{bmPos.Value.Y:F0}) w={bmSize.Value.Width:F0} " +
                           $"ball=({bx:F0},{by:F0},{bw:F0}) gapR={gapR:F0} gapL={gapL:F0}";
            }
            A("4b 悬浮球右键菜单贴在球旁", ballMenuOk, bmDetail);
            try { ballMenu?.Close(); } catch { }
            Pump(150);

            // ---------- 2. 托盘菜单：设备像素 → DIP 换算 + 向上翻转 ----------
            int cx = screenW - 60, cy = screenH - 24;         // 模拟托盘图标位置（右下角，设备像素）
            var trayMenu = win.ShowTrayMenuForTest(cx, cy);
            Pump(200);
            var off = trayMenu?.LastScreenOffset;
            var msz = trayMenu?.LastMenuSize;
            bool has = off.HasValue;
            double expX = cx / scale, expY = cy / scale;
            // 水平：菜单右对齐鼠标；若右侧越界则左移（x <= 鼠标DIP，且不为负）
            bool notRightOfCursor = has && off!.Value.X <= expX + 1 && off.Value.X >= 0;
            bool aboveCursor = has && off!.Value.Y < expY;         // 托盘在底部 → 应向上弹
            bool insideScreen = has && off!.Value.Y >= 0 && off.Value.X >= 0;
            double oldBuggyX = cx, oldBuggyY = cy;                  // 旧实现（像素直接当 DIP）会落在
            bool differsFromBug = has && Math.Abs(off!.Value.Y - oldBuggyY) > 10;
            A("2a 托盘菜单按 DIP 定位（非像素直用）", has && notRightOfCursor,
              has ? $"offset=({off!.Value.X:F0},{off!.Value.Y:F0}) 鼠标DIP≈({expX:F0},{expY:F0})" : "no offset");
            A("2b 底部托盘菜单向上翻转不越界", aboveCursor && insideScreen,
              has ? $"y={off!.Value.Y:F0} < cursorY={expY:F0}, 旧实现会落在 y={oldBuggyY}" : "no offset");
            A("2c 与旧(错误)行为明显不同", differsFromBug,
              has ? $"newY={off!.Value.Y:F0} vs buggyY={oldBuggyY}" : "no offset");
            // 菜单边缘应贴着托盘图标（横向贴合，间距 < 8 DIP）
            bool nearIcon = has && msz.HasValue &&
                            (Math.Abs(off!.Value.X - expX) < 8 || Math.Abs(off.Value.X + msz!.Value.Width - expX) < 8);
            A("3a 菜单贴着托盘图标弹出（主窗口可见）", nearIcon,
              has && msz.HasValue ? $"菜单 x∈[{off!.Value.X:F0},{off.Value.X + msz.Value.Width:F0}] 图标x={expX:F0}" : "no offset");
            try { trayMenu?.Close(); } catch { }
            win.CloseTrayMenuForTest();

            // 3b/3c 关键回归：主窗口"关闭到托盘"（Hide）后，菜单仍须正确定位
            win.HideMainForTest();
            Pump(200);
            var trayMenu2 = win.ShowTrayMenuForTest(cx, cy);
            Pump(220);
            var off2 = trayMenu2?.LastScreenOffset;
            var msz2 = trayMenu2?.LastMenuSize;
            bool has2 = off2.HasValue;
            bool nearIcon2 = has2 && msz2.HasValue &&
                             (Math.Abs(off2!.Value.X - expX) < 8 || Math.Abs(off2.Value.X + msz2!.Value.Width - expX) < 8);
            bool sane2 = has2 && off2!.Value.Y >= 0 && off2.Value.X >= 0 && off2.Value.Y < expY;
            A("3b 主窗口隐藏时菜单仍有有效定位（不再依赖隐藏的 MainWindow）", sane2,
              has2 ? $"offset=({off2!.Value.X:F0},{off2!.Value.Y:F0})" : "no offset（旧实现此场景失效）");
            A("3c 主窗口隐藏时菜单依旧贴着托盘图标", nearIcon2,
              has2 && msz2.HasValue ? $"菜单 x∈[{off2!.Value.X:F0},{off2.Value.X + msz2.Value.Width:F0}] 图标x={expX:F0}" : "no offset");
            win.CloseTrayMenuForTest();
            win.ShowMainForTest();
            Pump(200);

            // ---------- 1 + 4 + 6. 设置窗口 ----------
            win.OpenSettingsForTest();
            Pump(700);
            var sw = win.SettingsWindowForTest;
            A("1a 设置窗口已打开", sw != null && sw.IsVisible, sw == null ? "null" : "visible");

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
                A("4 左栏选中项圆角（自定义 TreeViewItem 模板）", rounded, "CornerRadius=9 的选中 Border");

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

                // 6. 子窗口随父窗口关闭
                win.SettingsOpenChildForTest(sw);
                Pump(900);
                var child = Application.Current.Windows.OfType<Window>()
                    .FirstOrDefault(w => w.Title != null && w.Title.Contains("抽选记录"));
                A("6a 设置窗口可打开子窗口（非模态）", child != null, child?.Title ?? "null");
                sw.Close();
                Pump(900);
                bool childGone = !Application.Current.Windows.OfType<Window>()
                    .Any(w => w.Title != null && w.Title.Contains("抽选记录") && w.IsVisible);
                bool settingsGone = win.SettingsWindowForTest == null;
                A("6b 关闭设置窗口时子窗口一并关闭", childGone, "childGone=" + childGone);
                A("6c 设置窗口引用已释放", settingsGone, "settingsWin=" + (settingsGone ? "null" : "非空"));
            }

            // ---------- 5. 按钮清晰度：模板中不再使用 Effect ----------
            bool noEffectOnBigBtn = win.BigButtonsHaveNoEffectForTest();
            A("5 大按钮不再挂 DropShadowEffect（消除位图栅格化发虚）", noEffectOnBigBtn,
              "PickBtn/MultiBtn/NumBtn Effect 均为 null");
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
                int cw = (int)Math.Ceiling(root.ActualWidth);
                int ch = (int)Math.Ceiling(root.ActualHeight);
                if (cw <= 0 || ch <= 0) return false;
                var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    cw, ch, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(root);
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