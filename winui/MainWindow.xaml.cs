// ================================================================
// MainWindow.xaml.cs — WinUI 3 主窗口（WinForms 版功能移植）
// ================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;

namespace LuckyPickerWinUI
{
    public sealed partial class MainWindow : Window, ILuckyBallHost
    {
        LuckyCore core => App.Core;
        TtsEngine tts;
        bool animating;
        string lastResultText = "";
        FloatingBallForm? ball;
        bool classChosen;

        public MainWindow()
        {
            try { System.IO.File.AppendAllText(@"C://Users//a1894//lucky_crash.log", "W1-InitComponent\n"); } catch { }
            InitializeComponent();
            try { System.IO.File.AppendAllText(@"C://Users//a1894//lucky_crash.log", "W2-AfterInitXaml\n"); } catch { }
            AppConfig.Load();
            InitCore();
            InitTray();
            ListenShowEvent();
            CheckUpdateSilent();
            classChosen = !App.BootMinimized;
            try { System.IO.File.AppendAllText(@"C://Users//a1894//lucky_crash.log", "W3-BeforeInitBall\n"); } catch { }
            InitBall();
            try { System.IO.File.AppendAllText(@"C://Users//a1894//lucky_crash.log", "W4-AfterInitBall\n"); } catch { }
            if (App.BootMinimized)
            {
                HideWindow();
            }
            else if (core.classIds.Count > 1)
            {
                _ = ShowClassDialogAsync();
            }
        }

        // ---------- 悬浮球 ----------
        void InitBall()
        {
            ball = new FloatingBallForm(this);
            if (AppConfig.BallVisible || App.BootMinimized) ball.Show();
        }

        void ApplyBallVisibility()
        {
            if (ball == null || ball.IsDisposed) return;
            if (AppConfig.BallVisible) { if (!ball.Visible) ball.Show(); }
            else if (ball.Visible) ball.Hide();
        }

        // —— ILuckyBallHost 实现 ——
        public void OnBallClicked()
        {
            if (!classChosen) { ShowClassMini(); return; }
            DoBallPickOne();
        }

        public void OnBallPickOne() => DoBallPickOne();

        public void OnBallPickMulti()
        {
            if (!classChosen) { ShowClassMini(); return; }
            DoBallPickMulti();
        }

        public void OnBallResetPool()
        {
            core.ResetPool();
            HintText.Text = "√ 不重复池已重置";
            RefreshPoolStats();
        }

        public void OnBallHideSelf()
        {
            AppConfig.BallVisible = false;
            AppConfig.Save();
            ApplyBallVisibility();
        }

        public void OnBallQuit() => QuitApp();

        public void ShowMainWindow() => ShowWindow();

        void DoBallPickOne()
        {
            var picked = core.PickOne();
            if (picked == null)
            {
                HintText.Text = "※ 当前无候选人";
                return;
            }
            ball?.ShowPicked(picked.name);
            Speak(picked.name);
            AddHistory(new HistoryEntry { time = Now(), text = picked.name, classId = core.currentClassId });
            RefreshPoolStats();
        }

        void DoBallPickMulti()
        {
            var list = core.PickMulti(5);
            if (list.Count == 0)
            {
                HintText.Text = "※ 当前无候选人";
                return;
            }
            ball?.ShowPicked(list.Count + "人");
            Speak(string.Join("、", list.Select(x => x.name)));
            AddHistory(new HistoryEntry { time = Now(), text = "连抽： " + string.Join("、", list.Select(x => x.name)), classId = core.currentClassId });
            RefreshPoolStats();
        }

        // 班级小窗（WinForms 复用：置顶圆角卡片，失焦关闭，点选立即抽取）
        void ShowClassMini()
        {
            var mini = new ClassMiniForm(core.classIds, core.classNames, cid =>
            {
                core.SetClass(cid);
                classChosen = true;
                RefreshBadge();
                RefreshPoolStats();
                DoBallPickOne();
            });
            if (ball != null && ball.Visible) mini.ShowNear(ball);
            else
            {
                mini.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
                mini.Show();
            }
        }

        void InitCore()
        {
            // 班级下拉
            ClassCombo.Items.Clear();
            foreach (var id in core.classIds)
                ClassCombo.Items.Add(core.ClassName(id));
            int idx = core.classIds.IndexOf(core.currentClassId);
            ClassCombo.SelectedIndex = idx >= 0 ? idx : 0;
            RefreshBadge();
            RefreshPoolStats();
            RenderBlockChips();
            InitSpeech();
        }

        void InitSpeech()
        {
            tts = new TtsEngine(s => DispatcherQueue.TryEnqueue(() => { try { VoiceStatusText.Text = s; } catch { } }));
            try { tts.QueueWarmup(GetClassNames()); } catch { }
            VoiceStatusText.Text = "♪ 语音：在线神经语音优先 · 本地语音备用";
        }

        // ---------- 窗口显隐（托盘化） ----------
        AppWindow? _appWindow;
        public AppWindow AppWin
        {
            get
            {
                if (_appWindow == null) _appWindow = this.AppWindow;
                return _appWindow;
            }
        }

        public void ShowWindow()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                AppWin.Show();
                try { WinRT.Interop.WindowNative.GetWindowHandle(this); } catch { }
            });
        }

        public void HideWindow()
        {
            try { AppWin.Hide(); } catch { }
        }

        // ---------- 班级 ----------
        void OnClassChanged(object sender, SelectionChangedEventArgs e)
        {
            int i = ClassCombo.SelectedIndex;
            if (i >= 0 && i < core.classIds.Count)
            {
                core.SetClass(core.classIds[i]);
                RefreshBadge();
                RefreshPoolStats();
                ResetResult();
            }
        }

        void RefreshBadge() => ClassBadgeText.Text = "● 当前班级：" + core.ClassName(core.currentClassId);

        // ---------- 性别 / 不重复 ----------
        void OnGenderClick(object sender, RoutedEventArgs e)
        {
            var tb = (ToggleButton)sender;
            bool isOn = tb.IsChecked == true;
            if (tb == GenderAll) { GenderAll.IsChecked = true; GenderMale.IsChecked = false; GenderFemale.IsChecked = false; core.SetGender("all"); }
            else if (tb == GenderMale) { if (!isOn) { GenderAll.IsChecked = true; GenderMale.IsChecked = false; return; } GenderAll.IsChecked = false; GenderFemale.IsChecked = false; core.SetGender("male"); }
            else { if (!isOn) { GenderAll.IsChecked = true; GenderFemale.IsChecked = false; return; } GenderAll.IsChecked = false; GenderMale.IsChecked = false; core.SetGender("female"); }
            RefreshPoolStats();
            ResetResult();
        }

        void OnNoRepeatToggled(object sender, RoutedEventArgs e)
        {
            core.noRepeat = NoRepeatSwitch.IsOn;
            core.ResetPoolKeepLast();
            RefreshPoolStats();
        }

        // ---------- 抽取 ----------
        async void OnPick(object sender, RoutedEventArgs e)
        {
            var s = core.PickOne();
            if (s == null)
            {
                HintText.Text = "※ 当前无候选人（请检查名单 / 屏蔽 / 筛选）";
                return;
            }
            await AnimatePickAsync(s.name);
            Speak(s.name);
            AddHistory(new HistoryEntry { time = Now(), text = s.name, classId = core.currentClassId });
        }

        async void OnMulti(object sender, RoutedEventArgs e)
        {
            var list = core.PickMulti(5);
            if (list.Count == 0)
            {
                HintText.Text = "※ 当前无候选人";
                return;
            }
            var names = list.Select(x => x.name).ToList();
            await ShowMultiAsync(names);
            Speak(string.Join("、", names));
            AddHistory(new HistoryEntry { time = Now(), text = "连抽： " + string.Join("、", names), classId = core.currentClassId });
            RefreshPoolStats();
        }

        void OnReset(object sender, RoutedEventArgs e)
        {
            core.ResetPool();
            ResetResult();
            HintText.Text = "√ 不重复池已重置，可以开始抽取";
        }

        void ResetResult()
        {
            animating = false;
            PickedName.Text = "——";
            MultiPanel.Visibility = Visibility.Collapsed;
            PickedName.Visibility = Visibility.Visible;
            HintText.Text = "点击「抽一人」开始抽取";
            RefreshPoolStats();
        }

        Task AnimatePickAsync(string name)
        {
            var tcs = new TaskCompletionSource<bool>();
            DispatcherQueue.TryEnqueue(async () =>
            {
                // 简单滚动动画：快速换字后定格
                var names = GetClassNames();
                animating = true;
                int ticks = 0;
                while (animating && ticks < 8)
                {
                    PickedName.Text = names[Random.Shared.Next(names.Count)];
                    ticks++;
                    await Task.Delay(45);
                }
                animating = false;
                PickedName.Text = name;
                MultiPanel.Visibility = Visibility.Collapsed;
                PickedName.Visibility = Visibility.Visible;
                tcs.TrySetResult(true);
            });
            return tcs.Task;
        }

        Task ShowMultiAsync(List<string> names)
        {
            var tcs = new TaskCompletionSource<bool>();
            DispatcherQueue.TryEnqueue(() =>
            {
                MultiPanel.Children.Clear();
                foreach (var n in names)
                {
                    var chip = new Border
                    {
                        Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 230, 240, 255)),
                        BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 147, 197, 253)),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(19),
                        Padding = new Thickness(14, 8, 14, 8),
                        Child = new TextBlock { Text = n, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.Bold }
                    };
                    MultiPanel.Children.Add(chip);
                }
                MultiPanel.Visibility = Visibility.Visible;
                PickedName.Visibility = Visibility.Collapsed;
                tcs.TrySetResult(true);
            });
            return tcs.Task;
        }

        List<string> GetClassNames()
        {
            return core.allStudents.Where(s => s.classId == core.currentClassId).Select(s => s.name).ToList();
        }

        void Speak(string text)
        {
            if (tts == null) return;
            Task.Run(() => { try { tts.Speak(text); } catch { } });
        }

        void AddHistory(HistoryEntry entry)
        {
            try { HistoryStore.Add(entry); } catch { }
            HintText.Text = lastResultText;
        }

        static string Now() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        void RefreshPoolStats()
        {
            int total = core.GetCandidates().Count;
            int remain = core.noRepeat ? core.remainPool.Count : total;
            PoolStatsText.Text = "候选 " + total + " 人 · 剩余 " + remain + " 人";
        }

        // ---------- 屏蔽 ----------
        void OnAddBlock(object sender, RoutedEventArgs e) => AddBlock();

        void OnBlockKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                AddBlock();
            }
        }

        void AddBlock()
        {
            string name = BlockInput.Text.Trim();
            if (name.Length == 0) return;
            core.AddBlock(name);
            BlockInput.Text = "";
            RenderBlockChips();
            core.ResetPoolKeepLast();
            RefreshPoolStats();
            HintText.Text = "已屏蔽 " + name;
        }

        void RenderBlockChips()
        {
            BlockChips.Children.Clear();
            foreach (var n in core.blockNames)
            {
                var chip = new Button
                {
                    Content = n + " ✕",
                    FontSize = 12,
                    Padding = new Thickness(10, 3, 10, 3),
                    CornerRadius = new CornerRadius(10),
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 237, 213)),
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 120, 53, 15)),
                };
                string name = n;
                chip.Click += (s, e) => { core.RemoveBlock(name); RenderBlockChips(); core.ResetPoolKeepLast(); RefreshPoolStats(); };
                BlockChips.Children.Add(chip);
            }
        }

        // ---------- 设置菜单 ----------
        void OnMenuEditor(object sender, RoutedEventArgs e) => _ = ShowEditorDialogAsync();
        void OnMenuHistory(object sender, RoutedEventArgs e) => _ = ShowHistoryDialogAsync();
        void OnMenuAbout(object sender, RoutedEventArgs e) => _ = ShowAboutDialogAsync();

        void OnMenuAutoStart(object sender, RoutedEventArgs e)
        {
            bool ok = AutoStart.SetEnabled(MenuAutoStart.IsChecked);
            if (!ok) MenuAutoStart.IsChecked = AutoStart.IsEnabled();
        }

        void OnMenuBall(object sender, RoutedEventArgs e)
        {
            AppConfig.BallVisible = MenuBall.IsChecked;
            AppConfig.Save();
            ApplyBallVisibility();
        }

        void OnMenuQuit(object sender, RoutedEventArgs e) => QuitApp();

        public void QuitApp()
        {
            try { ball?.Dispose(); } catch { }
            try { tray?.Dispose(); } catch { }
            Environment.Exit(0);
        }

        // ---------- 班级选择对话框（ContentDialog） ----------
        async Task ShowClassDialogAsync()
        {
            var dlg = new ContentDialog
            {
                Title = "选择班级",
                PrimaryButtonText = "确定",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot
            };
            var combo = new ComboBox { MinWidth = 200 };
            foreach (var id in core.classIds)
                combo.Items.Add(core.ClassName(id));
            combo.SelectedIndex = core.classIds.IndexOf(core.currentClassId) >= 0 ? core.classIds.IndexOf(core.currentClassId) : 0;
            dlg.Content = combo;
            var result = await dlg.ShowAsync();
            if (result == ContentDialogResult.Primary && combo.SelectedIndex >= 0)
            {
                core.SetClass(core.classIds[combo.SelectedIndex]);
                RefreshBadge();
                RefreshPoolStats();
                ResetResult();
            }
        }

        // ---------- 名单管理对话框 ----------
        async Task ShowEditorDialogAsync()
        {
            var dlg = new ContentDialog
            {
                Title = "名单管理",
                CloseButtonText = "关闭",
                XamlRoot = Content.XamlRoot
            };
            var panel = new StackPanel { Spacing = 12, MinWidth = 360 };
            var info = new TextBlock
            {
                Text = "名单文件：" + LuckyCore.DataPath() + "\n保存在本机（%ProgramData%\\LuckyPicker），换机需手动备份。",
                FontSize = 12, TextWrapping = TextWrapping.Wrap
            };
            var btnImport = new Button { Content = "导入 Excel / CSV 名单", Padding = new Thickness(16, 8, 16, 8) };
            var btnExport = new Button { Content = "导出备份（复制名单文件）", Padding = new Thickness(16, 8, 16, 8) };
            var btnOpenDir = new Button { Content = "打开名单所在文件夹", Padding = new Thickness(16, 8, 16, 8) };
            var status = new TextBlock { FontSize = 12, Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 6, 95, 70)) };

            btnImport.Click += async (s, e) =>
            {
                var picker = new FileOpenPicker();
                InitializeWithWindow(picker, Hwnd());
                picker.FileTypeFilter.Add(".xlsx");
                picker.FileTypeFilter.Add(".csv");
                picker.FileTypeFilter.Add(".json");
                var file = await picker.PickSingleFileAsync();
                if (file == null) return;
                try
                {
                    var data = ParseRoster(file.Path);
                    if (data == null || data.students.Count == 0)
                    {
                        status.Text = "未解析出学生，请检查文件格式。";
                        status.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 220, 38, 38));
                        return;
                    }
                    // 覆盖保存
                    Directory.CreateDirectory(LuckyCore.DataDir());
                    File.WriteAllText(LuckyCore.DataPath(),
                        System.Text.Json.JsonSerializer.Serialize(new DataFile { classes = data.classes, students = data.students }));
                    core.Reload();
                    InitCore();
                    status.Text = "已导入 " + data.students.Count + " 名学生并保存 ✓";
                    status.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 6, 95, 70));
                }
                catch (Exception ex)
                {
                    status.Text = "导入失败：" + ex.Message;
                    status.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 220, 38, 38));
                }
            };

            btnExport.Click += (s, e) =>
            {
                try
                {
                    string dst = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                        "LuckyPicker名单备份_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".json");
                    Directory.CreateDirectory(LuckyCore.DataDir());
                    File.Copy(LuckyCore.DataPath(), dst, true);
                    status.Text = "已导出到：" + dst;
                }
                catch (Exception ex) { status.Text = "导出失败：" + ex.Message; }
            };

            btnOpenDir.Click += (s, e) =>
            {
                try
                {
                    Directory.CreateDirectory(LuckyCore.DataDir());
                    System.Diagnostics.Process.Start("explorer.exe", LuckyCore.DataDir());
                }
                catch { }
            };

            panel.Children.Add(info);
            panel.Children.Add(btnImport);
            panel.Children.Add(btnExport);
            panel.Children.Add(btnOpenDir);
            panel.Children.Add(status);
            dlg.Content = panel;
            await dlg.ShowAsync();
        }

        // ---------- 抽选记录对话框 ----------
        async Task ShowHistoryDialogAsync()
        {
            var dlg = new ContentDialog
            {
                Title = "抽选记录",
                CloseButtonText = "关闭",
                XamlRoot = Content.XamlRoot
            };
            var list = new ListView { MinHeight = 300, MinWidth = 420 };
            var items = HistoryStore.Load().Select(h => h.time + "  " + h.text).ToList();
            if (items.Count == 0) items.Add("（暂无记录）");
            list.ItemsSource = items;
            var panel = new StackPanel { Spacing = 8 };
            var btnClear = new Button { Content = "清空记录" };
            btnClear.Click += (s, e) => { HistoryStore.Clear(); items.Clear(); items.Add("（已清空）"); list.ItemsSource = items.ToList(); };
            panel.Children.Add(list);
            panel.Children.Add(btnClear);
            dlg.Content = panel;
            await dlg.ShowAsync();
        }

        // ---------- 版本与更新对话框 ----------
        async Task ShowAboutDialogAsync()
        {
            var dlg = new ContentDialog
            {
                Title = "版本与更新",
                CloseButtonText = "关闭",
                PrimaryButtonText = "检查更新",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot
            };
            var panel = new StackPanel { Spacing = 10, MinWidth = 380 };
            panel.Children.Add(new TextBlock
            {
                Text = "当前版本：26H2 Build 13（内部构建 13）\n更新通道：26H2\n产品：YBH幸运摇人器",
                FontSize = 13, TextWrapping = TextWrapping.Wrap
            });
            var resultBox = new TextBlock
            {
                Text = "点击「检查更新」获取最新版本信息。\n更新地址：" + AppConfig.UpdateUrl,
                FontSize = 12, TextWrapping = TextWrapping.Wrap
            };
            panel.Children.Add(resultBox);
            dlg.Content = panel;
            dlg.PrimaryButtonClick += async (s, e) =>
            {
                e.Cancel = true;   // 手动控制关闭
                resultBox.Text = "正在检查更新...";
                var res = await Task.Run(() => UpdateManager.Check(AppConfig.UpdateUrl));
                if ((res == null || !res.Success) &&
                    string.Equals(AppConfig.UpdateUrl, AppVersion.UpdateUrlDefault, StringComparison.OrdinalIgnoreCase))
                {
                    var alt = await Task.Run(() => UpdateManager.Check(AppVersion.UpdateUrlLegacy));
                    if (alt != null && alt.Success) res = alt;
                }
                if (res == null || !res.Success)
                {
                    resultBox.Text = "检查失败：" + (res?.ErrorMessage ?? "未知错误");
                    resultBox.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 220, 38, 38));
                    return;
                }
                resultBox.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 51, 65, 85));
                string txt = "远程版本：" + res.LatestVersion;
                if (!string.IsNullOrEmpty(res.ReleaseDate)) txt += "\n发布日期：" + res.ReleaseDate;
                if (!string.IsNullOrEmpty(res.Notes)) txt += "\n更新说明：\n" + res.Notes;
                txt += "\n\n" + (res.HasUpdate ? "★ 发现新版本！可在官网下载：\n" + res.DownloadUrl : "✓ 当前已是最新版本");
                resultBox.Text = txt;
                if (res.HasUpdate && !string.IsNullOrEmpty(res.DownloadUrl))
                {
                    var ask = new ContentDialog
                    {
                        Title = "发现新版本 " + res.LatestVersion,
                        Content = "是否打开官网下载页？",
                        PrimaryButtonText = "打开",
                        CloseButtonText = "稍后",
                        XamlRoot = Content.XamlRoot
                    };
                    if (await ask.ShowAsync() == ContentDialogResult.Primary)
                    {
                        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(res.DownloadUrl) { UseShellExecute = true }); } catch { }
                    }
                }
            };
            await dlg.ShowAsync();
        }

        // ---------- 托盘（WinForms 互操作，独立线程消息循环） ----------
        System.Windows.Forms.NotifyIcon? tray;

        void InitTray()
        {
            var uiThread = DispatcherQueue;
            var thread = new System.Threading.Thread(() =>
            {
                tray = new System.Windows.Forms.NotifyIcon
                {
                    Text = AppVersion.ProductName + " · " + AppVersion.Display,
                    Visible = true
                };
                try
                {
                    var icoPath = System.IO.Path.Combine(AppContext.BaseDirectory, "icon.ico");
                    if (File.Exists(icoPath)) tray.Icon = new System.Drawing.Icon(icoPath);
                    else tray.Icon = System.Drawing.SystemIcons.Application;
                }
                catch { tray.Icon = System.Drawing.SystemIcons.Application; }

                var menu = new System.Windows.Forms.ContextMenuStrip();
                menu.Items.Add("显示主窗口", null, (s, e) => uiThread.TryEnqueue(ShowWindow));
                var boot = new System.Windows.Forms.ToolStripMenuItem("开机自启动") { CheckOnClick = true, Checked = AutoStart.IsEnabled() };
                boot.Click += (s, e) => uiThread.TryEnqueue(() => { MenuAutoStart.IsChecked = AutoStart.IsEnabled(); });
                menu.Items.Add(boot);
                menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
                menu.Items.Add("退出", null, (s, e) => uiThread.TryEnqueue(QuitApp));
                tray.ContextMenuStrip = menu;
                tray.DoubleClick += (s, e) => uiThread.TryEnqueue(ShowWindow);

                System.Windows.Forms.Application.Run();
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
        }

        // ---------- 单实例唤起监听 ----------
        void ListenShowEvent()
        {
            var uiThread = DispatcherQueue;
            Task.Run(() =>
            {
                try
                {
                    using var ev = new System.Threading.EventWaitHandle(false,
                        System.Threading.EventResetMode.AutoReset, AppVersion.ShowMainEvent);
                    while (true)
                    {
                        ev.WaitOne();
                        uiThread.TryEnqueue(ShowWindow);
                    }
                }
                catch { }
            });
        }

        // ---------- 静默更新检查 ----------
        void CheckUpdateSilent()
        {
            var uiThread = DispatcherQueue;
            Task.Run(() =>
            {
                try
                {
                    string today = DateTime.Now.ToString("yyyy-MM-dd");
                    if (string.Equals(AppConfig.LastUpdateCheckDate, today, StringComparison.Ordinal)) return;
                    var res = UpdateManager.Check(AppConfig.UpdateUrl);
                    if ((res == null || !res.Success) &&
                        string.Equals(AppConfig.UpdateUrl, AppVersion.UpdateUrlDefault, StringComparison.OrdinalIgnoreCase))
                    {
                        var alt = UpdateManager.Check(AppVersion.UpdateUrlLegacy);
                        if (alt != null && alt.Success) res = alt;
                    }
                    AppConfig.LastUpdateCheckDate = today;
                    AppConfig.Save();
                    if (res != null && res.Success && res.HasUpdate)
                    {
                        uiThread.TryEnqueue(() =>
                        {
                            var dlg = new ContentDialog
                            {
                                Title = AppVersion.ProductName + " 有新版本",
                                Content = "发现新版本 " + res.LatestVersion + "，是否打开官网下载？",
                                PrimaryButtonText = "打开下载",
                                CloseButtonText = "知道了",
                                XamlRoot = Content.XamlRoot
                            };
                            _ = dlg.ShowAsync().AsTask().ContinueWith(t =>
                            {
                                if (t.Result == ContentDialogResult.Primary && !string.IsNullOrEmpty(res.DownloadUrl))
                                {
                                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(res.DownloadUrl) { UseShellExecute = true }); } catch { }
                                }
                            }, TaskScheduler.FromCurrentSynchronizationContext());
                        });
                    }
                }
                catch { }
            });
        }

        // ---------- 工具 ----------
        IntPtr Hwnd() => WinRT.Interop.WindowNative.GetWindowHandle(this);

        // 解析名单文件（xlsx / csv）→ DataFile
        static DataFile? ParseRoster(string path)
        {
            List<List<string>> rows;
            if (path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                rows = XlsxReader.Read(path);
            else if (path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                rows = CsvReader.Read(path);
            else if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                try { return System.Text.Json.JsonSerializer.Deserialize<DataFile>(File.ReadAllText(path)); }
                catch { return null; }
            }
            else return null;
            if (rows == null || rows.Count < 2) return null;
            int nameCol = ImportUtil.FindColumn(rows, "name", "姓名", "名字", "学生");
            int classCol = ImportUtil.FindColumn(rows, "classId", "class", "班级", "班");
            int genderCol = ImportUtil.FindColumn(rows, "gender", "性别");
            if (nameCol < 0) return null;
            var students = new List<Student>();
            var classes = new Dictionary<string, string>();
            for (int i = 1; i < rows.Count; i++)
            {
                var r = rows[i];
                string name = (nameCol < r.Count ? r[nameCol] : "").Trim();
                if (name.Length == 0) continue;
                string cid = classCol >= 0 && classCol < r.Count ? ImportUtil.NormalizeClass(r[classCol]) : "1";
                if (cid.Length == 0) cid = "1";
                string g = genderCol >= 0 && genderCol < r.Count ? ImportUtil.NormalizeGender(r[genderCol]) : "";
                students.Add(new Student { name = name, classId = cid, gender = g });
                if (!classes.ContainsKey(cid)) classes[cid] = cid + "班";
            }
            return new DataFile { classes = classes, students = students };
        }

        static void InitializeWithWindow(FileOpenPicker picker, IntPtr hwnd)
        {
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }
    }
}
