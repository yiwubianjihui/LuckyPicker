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

        public static void Run(MainWindow win, string outPath)
        {
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

            // ---------- 8. 固定悬浮球：始终活跃（不变淡、抽完不收起） ----------
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

            // 固定时不应被"闲时变淡"（>4s 后仍不透明）
            Pump(4600);
            A("8c 固定时不变淡", win.BallOpacityForTest > 0.95,
              "opacity=" + win.BallOpacityForTest.ToString("F2"));

            AppConfig.BallPinned = false;
            win.ApplyBallSettings();
            Pump(4600);
            A("8d 取消固定后恢复闲时变淡", win.BallOpacityForTest < 0.95,
              "opacity=" + win.BallOpacityForTest.ToString("F2"));
            AppConfig.BallPinned = oldPin;
            win.ApplyBallSettings();
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

            // ---------- 7. 点击悬浮球 → 面板 toggle ----------
            bool before = BallQuickPanel.IsOpen;
            win.BallPanelToggleForTest();     // 打开
            Pump(260);
            bool opened = BallQuickPanel.IsOpen;
            win.BallPanelToggleForTest();     // 再点 → 收起
            Pump(300);
            bool closed = !BallQuickPanel.IsOpen;
            A("7 面板 toggle（开→再点收起）", !before && opened && closed,
              $"before={before} opened={opened} closedAfterSecond={closed}");

            // ---------- 2. 托盘菜单：设备像素 → DIP 换算 + 向上翻转 ----------
            var screenH = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Height;
            var screenW = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Width;
            int cx = screenW - 60, cy = screenH - 24;         // 模拟托盘图标位置（右下角，设备像素）
            var trayMenu = win.ShowTrayMenuForTest(cx, cy);
            Pump(200);
            var off = trayMenu?.LastScreenOffset;
            bool has = off.HasValue;
            double expX = cx / scale, expY = cy / scale;
            // 水平：菜单右对齐鼠标；若右侧越界则左移（x <= 鼠标DIP，且不为负）
            bool notRightOfCursor = has && off!.Value.X <= expX + 1 && off.Value.X >= 0;
            bool aboveCursor = has && off!.Value.Y < expY;         // 托盘在底部 → 应向上弹
            bool insideScreen = has && off!.Value.Y >= 0 && off!.Value.X >= 0;
            double oldBuggyX = cx, oldBuggyY = cy;                  // 旧实现（像素直接当 DIP）会落在
            bool differsFromBug = has && Math.Abs(off!.Value.Y - oldBuggyY) > 10;
            A("2a 托盘菜单按 DIP 定位（非像素直用）", has && notRightOfCursor,
              has ? $"offset=({off!.Value.X:F0},{off!.Value.Y:F0}) 鼠标DIP≈({expX:F0},{expY:F0})" : "no offset");
            A("2b 底部托盘菜单向上翻转不越界", aboveCursor && insideScreen,
              has ? $"y={off!.Value.Y:F0} < cursorY={expY:F0}, 旧实现会落在 y={oldBuggyY}" : "no offset");
            A("2c 与旧(错误)行为明显不同", differsFromBug,
              has ? $"newY={off!.Value.Y:F0} vs buggyY={oldBuggyY}" : "no offset");
            try { trayMenu?.Close(); } catch { }
            Pump(120);

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
    }
}