// ================================================================
// MainWindow.xaml.cs — WPF 版主窗口（逻辑层与 WinForms/WinUI 版共享）
// ================================================================
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using System.Threading.Tasks;

namespace LuckyPickerWpf
{
    public partial class MainWindow : Window
    {
        LuckyCore core => App.Core;
        TtsEngine tts;
        bool animating;
        FloatingBallWindow? ball;
        bool classChosen;
        System.Windows.Forms.NotifyIcon? tray;
        bool reallyExit;

        public MainWindow()
        {
            App.Log("W-ctor");
            InitializeComponent();
            SubText.Text = "班级随机摇人 · " + AppVersion.Display;
            try   // 窗口/任务栏图标（exe 目录随 Content 拷贝的 icon.ico）
            {
                string ico = System.IO.Path.Combine(AppContext.BaseDirectory, "icon.ico");
                if (System.IO.File.Exists(ico))
                    Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri(ico));
            }
            catch { }
            App.Log("W-xaml");
            InitCore();
            InitSpeech();
            App.Log("W-core");
            InitTray();
            ListenShowEvent();
            CheckUpdateSilent();
            classChosen = false;
            Loaded += (s, e) => Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (!string.IsNullOrEmpty(AppConfig.DefaultClassId) && core.classIds.Contains(AppConfig.DefaultClassId))
                    {
                        core.SetClass(AppConfig.DefaultClassId);
                        classChosen = true;
                        RefreshComboSelection();
                        RefreshBadge();
                        RefreshPoolStats();
                        return;
                    }
                    ShowClassMini();
                }
                catch (Exception ex) { App.Log("MINI-EX: " + ex); }
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            InitBall();
            App.Log("W-ball");
        }

        void InitCore()
        {
            ClassCombo.Items.Clear();
            foreach (var id in core.classIds) ClassCombo.Items.Add(core.ClassName(id));
            int idx = core.classIds.IndexOf(core.currentClassId);
            ClassCombo.SelectedIndex = idx >= 0 ? idx : 0;
            RefreshBadge();
            RefreshPoolStats();
            RenderBlockChips();
        }

        void InitSpeech()
        {
            try
            {
                tts = new TtsEngine(s => Dispatcher.Invoke(() => { try { VoiceStatusText.Text = s; } catch { } }));
                string st = tts.InitStatus();   // 初始化本地 SAPI 兜底（在线失败时保证能出声）
                VoiceStatusText.Text = st;
                try { tts.QueueWarmup(GetClassSpeechNames()); } catch { }
            }
            catch { }
        }

        // ---------- 班级 / 筛选 ----------
        void OnClassChanged(object sender, SelectionChangedEventArgs e)
        {
            int i = ClassCombo.SelectedIndex;
            if (i >= 0 && i < core.classIds.Count)
            {
                core.SetClass(core.classIds[i]);
                if (ClassDefaultChk != null && ClassDefaultChk.IsChecked == true)
                { AppConfig.DefaultClassId = core.classIds[i]; AppConfig.Save(); }
                RefreshBadge(); RefreshPoolStats(); ResetResult();
            }
        }

        void RefreshBadge() => ClassBadgeText.Text = "● 当前班级：" + core.ClassName(core.currentClassId);

        void OnGenderClick(object sender, RoutedEventArgs e)
        {
            var tb = (ToggleButton)sender;
            if (tb == GenderAll) { GenderAll.IsChecked = true; GenderMale.IsChecked = false; GenderFemale.IsChecked = false; core.SetGender("all"); }
            else if (tb == GenderMale)
            {
                if (tb.IsChecked != true) { GenderAll.IsChecked = true; GenderMale.IsChecked = false; return; }
                GenderAll.IsChecked = false; GenderFemale.IsChecked = false; core.SetGender("male");
            }
            else
            {
                if (tb.IsChecked != true) { GenderAll.IsChecked = true; GenderFemale.IsChecked = false; return; }
                GenderAll.IsChecked = false; GenderMale.IsChecked = false; core.SetGender("female");
            }
            RefreshPoolStats(); ResetResult();
        }

        // ---------- 抽取 ----------
        async void OnPick(object sender, RoutedEventArgs e)
        {
            AudioUnmute.DefaultOutput();
            var s = core.PickOne();
            if (s == null) { HintText.Text = "※ 当前无候选人（请检查名单 / 屏蔽 / 筛选）"; return; }
            await AnimatePickAsync(s.name);
            Speak(NameReading.Spoken(s));
            AddHistory(new HistoryEntry { time = Now(), text = s.name, classId = core.currentClassId });
            RefreshPoolStats();
        }

        async void OnMulti(object sender, RoutedEventArgs e)
        {
            AudioUnmute.DefaultOutput();
            var list = core.PickMulti(5);
            if (list.Count == 0) { HintText.Text = "※ 当前无候选人"; return; }
            var names = list.Select(x => x.name).ToList();
            await ShowMultiAsync(names);
            tts?.SpeakSequence(list.Select(NameReading.Spoken).ToList());
            AddHistory(new HistoryEntry { time = Now(), text = "连抽： " + string.Join("、", names), classId = core.currentClassId });
            RefreshPoolStats();
        }

        void OnReset(object sender, RoutedEventArgs e)
        {
            core.ResetPool();
            ResetResult();
            HintText.Text = "√ 已重置，可以重新抽取";
        }

        void ResetResult()
        {
            animating = false;
            PickedName.Text = "——";
            PickedName.Opacity = 0.16;
            MultiPanel.Visibility = Visibility.Collapsed;
            PickedName.Visibility = Visibility.Visible;
            HintText.Text = "点击「抽一人」开始抽取";
            RefreshPoolStats();
        }

        Task AnimatePickAsync(string name)
        {
            var tcs = new TaskCompletionSource<bool>();
            var names = GetClassNames();
            Dispatcher.BeginInvoke(async () =>
            {
                animating = true;
                PickedName.Opacity = 1;
                for (int i = 0; animating && i < 8; i++)
                {
                    PickedName.Text = names[Random.Shared.Next(names.Count)];
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
            var t = ThemeManager.Current;
            var tcs = new TaskCompletionSource<bool>();
            Dispatcher.BeginInvoke(() =>
            {
                MultiPanel.Children.Clear();
                foreach (var n in names)
                {
                    MultiPanel.Children.Add(new Border
                    {
                        Background = new SolidColorBrush(t.AccentSoft),
                        BorderBrush = new SolidColorBrush(t.AccentSoftBorder),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(12),
                        Padding = new Thickness(16, 10, 16, 10),
                        Margin = new Thickness(0, 0, 10, 10),
                        Child = new TextBlock
                        {
                            Text = n, FontSize = 16, FontWeight = FontWeights.SemiBold,
                            Foreground = new SolidColorBrush(t.AccentInk)
                        }
                    });
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

        List<string> GetClassSpeechNames()
        {
            return core.allStudents.Where(s => s.classId == core.currentClassId)
                .Select(NameReading.Spoken).Where(n => n.Length > 0).Distinct().ToList();
        }

        void Speak(string text)
        {
            if (tts == null) return;
            Task.Run(() => { try { tts.Speak(text); } catch { } });
        }

        void AddHistory(HistoryEntry entry)
        {
            bool saved = HistoryStore.Add(entry);
            HintText.Text = saved ? entry.text : entry.text + "（记录保存失败：" + HistoryStore.LastError + "）";
        }

        static string Now() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        void RefreshPoolStats()
        {
            int total = core.GetCandidates().Count;
            int remain = core.noRepeat ? core.remainPool.Count : total;
            if (PoolStatsText != null) PoolStatsText.Text = "候选 " + total + " 人 · 剩余 " + remain + " 人";
        }

        // ---------- 屏蔽 ----------
        void OnAddBlock(object sender, RoutedEventArgs e) => AddBlock();
        void OnBlockKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { e.Handled = true; AddBlock(); }
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
            var t = ThemeManager.Current;
            BlockChips.Children.Clear();
            foreach (var n in core.blockNames)
            {
                string name = n;
                var chip = new Button
                {
                    Content = n + " ✕", FontSize = 12, Padding = new Thickness(11, 4, 11, 4),
                    Margin = new Thickness(0, 0, 6, 6), Cursor = Cursors.Hand,
                    Background = new SolidColorBrush(t.BlockChipBg),
                    Foreground = new SolidColorBrush(t.BlockChipInk),
                    BorderBrush = new SolidColorBrush(t.BlockChipBorder),
                    BorderThickness = new Thickness(1),
                    Style = (Style)FindResource("GhostBtn")
                };
                chip.Click += (s, e) => { core.RemoveBlock(name); RenderBlockChips(); core.ResetPoolKeepLast(); RefreshPoolStats(); };
                BlockChips.Children.Add(chip);
            }
        }

        // ---------- 设置菜单 ----------
        // 设置按钮：左键点击弹出菜单（WPF ContextMenu 默认仅右键触发）

        void OnMenuVoice(object sender, RoutedEventArgs e) => ShowVoiceDialog();

        // ---------------- 语音设置（独立窗口） ----------------
        void ShowVoiceDialog()
        {
            var dlg = new Window
            {
                Icon = Icon,
                Title = "语音设置", Width = 440, Height = 420,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this,
                Background = new SolidColorBrush(ThemeManager.Current.Surface)
            };
            var panel = new StackPanel { Margin = new Thickness(22) };

            // 播报来源
            panel.Children.Add(new TextBlock { Text = "播报引擎", FontSize = 12.5, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) });
            var srcCombo = new ComboBox { Margin = new Thickness(0, 0, 0, 14), Style = (Style)FindResource("ModernCombo") };
            srcCombo.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = "自动（微软神经语音 → 备用）", Tag = "auto" });
            srcCombo.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = "微软神经语音（网络）", Tag = "azure" });
            srcCombo.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = "Edge 直连语音", Tag = "edge" });
            srcCombo.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = "百度在线语音", Tag = "baidu" });
            srcCombo.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = "仅本地语音", Tag = "off" });
            foreach (var it in srcCombo.Items)
                if (it is System.Windows.Controls.ComboBoxItem ci && (string)ci.Tag == AppConfig.TtsSource) srcCombo.SelectedItem = ci;
            if (srcCombo.SelectedIndex < 0) srcCombo.SelectedIndex = 0;
            panel.Children.Add(srcCombo);

            // 微软音色
            panel.Children.Add(new TextBlock { Text = "微软音色（微软神经语音 / Edge 直连）", FontSize = 12.5, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) });
            var voiceCombo = new ComboBox { Margin = new Thickness(0, 0, 0, 14), Style = (Style)FindResource("ModernCombo") };
            var voices = new (string Name, string Id)[]
            {
                ("晓晓（女 · 温柔）", "zh-CN-XiaoxiaoNeural"),
                ("晓伊（女）", "zh-CN-XiaoyiNeural"),
                ("云希（男 · 阳光）", "zh-CN-YunxiNeural"),
                ("云扬（男 · 新闻）", "zh-CN-YunyangNeural"),
                ("云健（男 · 情感）", "zh-CN-YunjianNeural"),
                ("晓辰（女 · 儿童）", "zh-CN-XiaochenNeural"),
            };
            foreach (var (nm, id) in voices)
                voiceCombo.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = nm, Tag = id });
            foreach (var it in voiceCombo.Items)
                if (it is System.Windows.Controls.ComboBoxItem ci && (string)ci.Tag == AppConfig.TtsVoice) voiceCombo.SelectedItem = it;
            if (voiceCombo.SelectedIndex < 0) voiceCombo.SelectedIndex = 0;
            panel.Children.Add(voiceCombo);

            var hint = new TextBlock
            {
                Text = "选择后立即生效。网络引擎需要联网；无网络时自动使用本地语音。",
                FontSize = 11, Foreground = new SolidColorBrush(ThemeManager.Current.Sub),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12)
            };
            panel.Children.Add(hint);

            var btnTest = new Button { Content = "试听", Height = 38, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 10, 0), Style = (Style)FindResource("ModernBtn") };
            var btnApply = new Button { Content = "保存", Height = 38, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 10, 0), Style = (Style)FindResource("ModernBtn") };
            var btnCancel = new Button { Content = "取消", Height = 38, Cursor = Cursors.Hand, Style = (Style)FindResource("ModernBtnGhost") };
            btnTest.Click += (s, e) =>
            {
                ApplyVoice(srcCombo, voiceCombo);
                Speak("欢迎使用 YBH 幸运摇人器");
            };
            btnApply.Click += (s, e) => { ApplyVoice(srcCombo, voiceCombo); dlg.Close(); };
            btnCancel.Click += (s, e) => dlg.Close();
            // 对话框意外关闭（X / Alt+F4）→ 不保存，避免误操作
            dlg.Closing += (s, e) => { try { if (!btnApply.IsEnabled) return; } catch { } };
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            row.Children.Add(btnTest); row.Children.Add(btnApply); row.Children.Add(btnCancel);
            panel.Children.Add(row);

            dlg.Content = panel;
            dlg.ShowDialog();
        }

        void ApplyVoice(ComboBox srcCombo, ComboBox voiceCombo)
        {
            if (srcCombo.SelectedItem is System.Windows.Controls.ComboBoxItem s && s.Tag is string st) AppConfig.TtsSource = st;
            if (voiceCombo.SelectedItem is System.Windows.Controls.ComboBoxItem v && v.Tag is string vt) AppConfig.TtsVoice = vt;
            AppConfig.Save();
            try { tts?.Dispose(); } catch { }
            tts = new TtsEngine(msg => Dispatcher.Invoke(() => { try { VoiceStatusText.Text = msg; } catch { } }));
            string status = tts.InitStatus();
            VoiceStatusText.Text = status;
            try { tts.QueueWarmup(GetClassSpeechNames()); } catch { }
        }

        // ---------------- 快捷键（与经典版对齐） ----------------
        void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            bool editing = e.OriginalSource is System.Windows.Controls.TextBox
                        || e.OriginalSource is System.Windows.Controls.ComboBox;
            var mod = Keyboard.Modifiers;
            if (e.Key == Key.Space && !editing)
            {
                e.Handled = true;
                OnPick(this, new RoutedEventArgs());
            }
            else if (e.Key == Key.M && mod == ModifierKeys.Control)
            {
                e.Handled = true;
                OnMulti(this, new RoutedEventArgs());
            }
            else if (e.Key == Key.R && mod == ModifierKeys.Control)
            {
                e.Handled = true;
                OnReset(this, new RoutedEventArgs());
            }
            else if (e.Key == Key.E && mod == ModifierKeys.Control)
            {
                e.Handled = true;
                ShowEditorDialog();
            }
            else if (e.Key == Key.H && mod == ModifierKeys.Control)
            {
                e.Handled = true;
                ShowHistoryDialog();
            }
            else if (e.Key == Key.U && mod == ModifierKeys.Control)
            {
                e.Handled = true;
                ShowAboutDialog();
            }
            else if (e.Key == Key.V && mod == ModifierKeys.Control)
            {
                e.Handled = true;
                string last = PickedName.Text;
                if (!string.IsNullOrEmpty(last) && last != "——")
                {
                    var student = core.allStudents.FirstOrDefault(x => x.name == last && x.classId == core.currentClassId);
                    Speak(student == null ? last : NameReading.Spoken(student));
                }
            }
        }

        void OnMenuEditor(object sender, RoutedEventArgs e) => ShowEditorDialog();
        void OnMenuHistory(object sender, RoutedEventArgs e) => ShowHistoryDialog();
        void OnMenuAbout(object sender, RoutedEventArgs e) => ShowAboutDialog();


        void OnMenuQuit(object sender, RoutedEventArgs e) => QuitApp();

        public void QuitApp()
        {
            reallyExit = true;
            try { ball?.Close(); } catch { }
            try { tray?.Dispose(); } catch { }
            Close();   // reallyExit=true 时 OnClosing 不再拦截
        }

        // ---------- 悬浮球 ----------
        void InitBall()
        {
            ball = new FloatingBallWindow(
                clicked: OnBallClicked,
                pickOne: DoBallPickOne,
                pickMulti: DoBallPickMulti,
                resetPool: () => { core.ResetPool(); HintText.Text = "√ 已重置，可以重新抽取"; RefreshPoolStats(); },
                hideSelf: () => { AppConfig.BallVisible = false; AppConfig.Save(); ApplyBallVisibility(); },
                quit: QuitApp,
                showMainWin: ShowMainWindow);
            if (AppConfig.BallVisible || App.BootMinimized) ball.Show();
        }

        void ApplyBallVisibility()
        {
            if (ball == null) return;
            if (AppConfig.BallVisible) { if (!ball.IsVisible) ball.Show(); }
            else if (ball.IsVisible) ball.Hide();
        }

        // 防误触：点击悬浮球先弹出小面板（抽一人 / 显示窗口 / 选项），不直接抽取
        void OnBallClicked() => ShowBallPanel();

        void ShowBallPanel()
        {
            BallQuickPanel.ShowPanel(
                pickOne: () =>
                {
                    if (!classChosen) { ShowClassMini(); return; }
                    DoBallPickOne();
                },
                showMain: () => ShowMainWindow(),
                pinned: AppConfig.BallPinned,
                onPinChanged: v => { AppConfig.BallPinned = v; AppConfig.Save(); ball?.SetPinned(v); },
                ball);
        }

        void DoBallPickOne()
        {
            AudioUnmute.DefaultOutput();
            var picked = core.PickOne();
            if (picked == null) { HintText.Text = "※ 当前无候选人"; return; }
            ball?.ShowPicked(picked.name);
            ball?.AutoHideAfterPick();
            Speak(NameReading.Spoken(picked));
            AddHistory(new HistoryEntry { time = Now(), text = picked.name, classId = core.currentClassId });
            RefreshPoolStats();
        }

        void DoBallPickMulti()
        {
            AudioUnmute.DefaultOutput();
            var list = core.PickMulti(5);
            if (list.Count == 0) { HintText.Text = "※ 当前无候选人"; return; }
            var names = list.Select(x => x.name).ToList();
            ball?.ShowPicked(list.Count + "人");
            ball?.AutoHideAfterPick();
            tts?.SpeakSequence(list.Select(NameReading.Spoken).ToList());
            AddHistory(new HistoryEntry { time = Now(), text = "连抽： " + string.Join("、", names), classId = core.currentClassId });
            RefreshPoolStats();
        }

        ClassMiniWindow? mini;

        void ShowClassMini()
        {
            if (mini != null && mini.IsLoaded) { mini.Activate(); return; }   // 防重入
            mini = new ClassMiniWindow(core.classIds, core.classNames, (cid, asDefault) =>
            {
                core.SetClass(cid);
                classChosen = true;
                if (asDefault) { AppConfig.DefaultClassId = cid; AppConfig.Save(); }
                RefreshBadge();
                RefreshComboSelection();
                RefreshPoolStats();
                DoBallPickOne();
            });
            if (ball != null && ball.IsVisible) mini.ShowNear(ball);
            else { mini.WindowStartupLocation = WindowStartupLocation.CenterScreen; mini.Show(); }
        }

        // ---------- 设置窗口（统一设置） ----------
        SettingsWindow? settingsWin;
        HistoryWindow? historyWin;
        DataGrid? rosterEditorGrid;
        Button? rosterEditorReadingButton;
        void OnSettingsClick(object sender, RoutedEventArgs e) => OpenSettings();
        void OnRosterClick(object sender, RoutedEventArgs e) => ShowEditorDialog();
        void OnHistoryClick(object sender, RoutedEventArgs e) => ShowHistoryDialog();

        void ShowHistoryDialog()
        {
            if (historyWin != null && historyWin.IsLoaded)
            {
                historyWin.ReloadHistory();
                historyWin.Activate();
                return;
            }
            historyWin = new HistoryWindow(this);
            historyWin.Closed += (s, e) => historyWin = null;
            historyWin.Show();
        }

        void OpenSettings()
        {
            if (settingsWin != null && settingsWin.IsLoaded)
            {
                settingsWin.Activate();
                return;
            }
            settingsWin = new SettingsWindow(this,
                applyVoice: ApplyVoicePublic,
                ballChanged: ApplyBallSettings,
                showUpdateDownload: res => ShowUpdateDownload(res, settingsWin, modal: false),
                speak: text => { try { Speak(text); } catch { } },
                changeDefaultClass: () => { AppConfig.DefaultClassId = ""; AppConfig.Save(); classChosen = false; ShowClassMini(); },
                onQuit: QuitApp,
                themeChanged: ApplyTheme);
            settingsWin.Closed += (s, e) => settingsWin = null;
            settingsWin.Show();
        }

        /// <summary>把悬浮球相关的配置一次性应用到球上（设置窗口调用，立即生效 + 已随 AppConfig.Save 持久化）。</summary>
        public void ApplyBallSettings()
        {
            if (ball == null) return;
            ball.SetSize(AppConfig.BallDiameter);        // 立即应用尺寸
            ball.SetPinned(AppConfig.BallPinned);        // 固定状态（含"始终活跃"）
            ApplyBallVisibility();                       // 显示 / 隐藏
        }

        /// <summary>配色切换后刷新主窗口里代码创建的部分（XAML 侧由 DynamicResource 自动更新）。</summary>
        public void ApplyTheme()
        {
            try { RenderBlockChips(); } catch { }
            try
            {
                var t = ThemeManager.Current;
                foreach (var b in MultiPanel.Children.OfType<Border>())
                {
                    b.Background = new SolidColorBrush(t.AccentSoft);
                    b.BorderBrush = new SolidColorBrush(t.AccentSoftBorder);
                    if (b.Child is TextBlock tb) tb.Foreground = new SolidColorBrush(t.AccentInk);
                }
            }
            catch { }
            try { ball?.ApplyTheme(); } catch { }
        }

        /// <summary>设置窗口语音保存：重建 TTS 引擎使新引擎/音色立即生效。</summary>
        public void ApplyVoicePublic()
        {
            try { tts?.Dispose(); } catch { }
            tts = new TtsEngine(msg => Dispatcher.Invoke(() => { try { VoiceStatusText.Text = msg; } catch { } }));
            string status = tts.InitStatus();
            VoiceStatusText.Text = status;
            try { tts.QueueWarmup(GetClassSpeechNames()); } catch { }
        }

        // ---------- 默认班级 ----------
        void RefreshComboSelection()
        {
            int i = core.classIds.IndexOf(core.currentClassId);
            if (i >= 0) ClassCombo.SelectedIndex = i;
        }

        void OnClassDefaultChk(object sender, RoutedEventArgs e)
        {
            if (ClassDefaultChk.IsChecked == true && ClassCombo.SelectedIndex >= 0)
            {
                AppConfig.DefaultClassId = core.currentClassId;
                AppConfig.Save();
            }
        }

        void OnMenuChangeDefaultClass(object sender, RoutedEventArgs e)
        {
            AppConfig.DefaultClassId = "";
            AppConfig.Save();
            classChosen = false;
            ShowClassMini();
        }

        // ---------- 随机编号生成器 ----------
        static readonly Random rndShared = new();
        readonly List<int> numPool = new();
        string numPoolKey = "";

        int DrawNumber(int lo, int hi, bool noRepeat)
        {
            string key = lo + "-" + hi;
            if (numPoolKey != key) { numPoolKey = key; numPool.Clear(); }
            if (!noRepeat) return rndShared.Next(lo, hi + 1);
            if (numPool.Count == 0)
                for (int i = lo; i <= hi; i++) numPool.Add(i);
            int idx = rndShared.Next(numPool.Count);
            int v = numPool[idx];
            numPool.RemoveAt(idx);
            return v;
        }

        void ShowNumberResult(int n, int lo, int hi)
        {
            string text = n.ToString();
            PickedName.Text = text;
            PickedName.Opacity = 1;
            MultiPanel.Visibility = Visibility.Collapsed;
            HintText.Text = "√ 已生成编号：" + text + "（范围 " + lo + " ~ " + hi + "）";
            ball?.ShowPicked(text);
            Speak(text);
            AddHistory(new HistoryEntry { time = Now(), text = "编号：" + text + "（范围 " + lo + "~" + hi + "）", classId = core.currentClassId });
        }

        void OnRandomNumber(object sender, RoutedEventArgs e)
        {
            var t = ThemeManager.Current;
            var dlg = new Window
            {
                Title = "随机编号", Width = 372, Height = 258,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this,
                ResizeMode = ResizeMode.NoResize,
                Background = new SolidColorBrush(t.Surface)
            };
            var root = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };
            root.Children.Add(new TextBlock
            {
                Text = "随机编号（未导入名单时抽取编号）",
                FontSize = 13.5, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(t.Ink),
                Margin = new Thickness(0, 0, 0, 16)
            });

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            row.Children.Add(new TextBlock { Text = "编号范围", FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
            var tbMin = new TextBox { Text = "1", Width = 74, Height = 36, FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(10, 4, 10, 4), Background = new SolidColorBrush(t.SurfaceMuted), Foreground = new SolidColorBrush(t.Ink), CaretBrush = new SolidColorBrush(t.Accent), BorderBrush = new SolidColorBrush(t.Stroke), BorderThickness = new Thickness(1) };
            row.Children.Add(tbMin);
            row.Children.Add(new TextBlock { Text = " ~ ", FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0) });
            var tbMax = new TextBox { Text = "50", Width = 74, Height = 36, FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(10, 4, 10, 4), Background = new SolidColorBrush(t.SurfaceMuted), Foreground = new SolidColorBrush(t.Ink), CaretBrush = new SolidColorBrush(t.Accent), BorderBrush = new SolidColorBrush(t.Stroke), BorderThickness = new Thickness(1) };
            row.Children.Add(tbMax);
            root.Children.Add(row);

            var chkNoRepeat = new CheckBox
            {
                Content = "不重复（抽过的编号不再出现）", IsChecked = true, FontSize = 12,
                Foreground = new SolidColorBrush(t.InkSoft),
                Margin = new Thickness(0, 10, 0, 0)
            };
            root.Children.Add(chkNoRepeat);

            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
            var bGo = new Button { Content = "生成", Width = 100, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 10, 0), Style = (Style)FindResource("ModernBtn") };
            var bCancel = new Button { Content = "取消", Width = 82, Cursor = Cursors.Hand, Style = (Style)FindResource("ModernBtnGhost") };
            bCancel.Click += (s, e) => dlg.Close();
            btns.Children.Add(bGo); btns.Children.Add(bCancel);
            root.Children.Add(btns);

            bGo.Click += (s, e) =>
            {
                if (!int.TryParse(tbMin.Text.Trim(), out int lo) || !int.TryParse(tbMax.Text.Trim(), out int hi))
                { MessageBox.Show(this, "请输入有效的数字范围。", "随机编号", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
                if (lo > hi) { int t = lo; lo = hi; hi = t; }
                int n = DrawNumber(lo, hi, chkNoRepeat.IsChecked == true);
                if (n < 0)
                {
                    MessageBox.Show(this, "该范围内编号已抽完，请调整范围或关闭「不重复」。", "随机编号", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                ShowNumberResult(n, lo, hi);
                dlg.Close();
            };

            dlg.Content = root;
            dlg.ShowDialog();
        }

        // ---------- 悬浮球大小 ----------


        void ShowMainWindow()
        {
            if (!IsVisible) Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
        }

        // ---------- 托盘 ----------
        ModernMenu? trayMenu;   // 单例 WPF 风格托盘菜单（替代 WinForms ContextMenuStrip）
        void InitTray()
        {
            tray = new System.Windows.Forms.NotifyIcon
            {
                Text = AppVersion.ProductName + " · " + AppVersion.Display,
                Visible = true
            };
            try
            {
                var icoPath = Path.Combine(AppContext.BaseDirectory, "icon.ico");
                if (File.Exists(icoPath)) tray.Icon = new System.Drawing.Icon(icoPath);
                else tray.Icon = System.Drawing.SystemIcons.Application;
            }
            catch { tray.Icon = System.Drawing.SystemIcons.Application; }

            // 左键双击显示主窗口；右键单击弹出 WPF 风格菜单（自带圆角/阴影/主题色）
            tray.MouseDoubleClick += (s, e) => { if (e.Button == System.Windows.Forms.MouseButtons.Left) Dispatcher.Invoke(ShowMainWindow); };
            tray.MouseClick += (s, e) =>
            {
                if (e.Button != System.Windows.Forms.MouseButtons.Right) return;
                Dispatcher.BeginInvoke(() => ShowTrayMenu(System.Windows.Forms.Cursor.Position));
            };
        }

        void ShowTrayMenu(System.Drawing.Point screenPt)
        {
            // 自动失焦关闭后，旧引用不应吞掉下一次右键。
            if (trayMenu != null)
            {
                bool wasOpen = trayMenu.IsOpen;
                try { trayMenu.Close(); } catch { }
                trayMenu = null;
                if (wasOpen) return;
            }
            var menu = new ModernMenu(210);
            trayMenu = menu;
            menu.AddItem("显示主窗口", () => { trayMenu = null; Dispatcher.Invoke(ShowMainWindow); });
            menu.AddItem("打开设置…", () => { trayMenu = null; Dispatcher.BeginInvoke(OpenSettings); });
            menu.AddSeparator();
            menu.AddItem("退出程序", () => { trayMenu = null; Dispatcher.Invoke(QuitApp); });

            // 设备像素 → DIP 的换算在 ModernMenu.ShowAtScreen 内部完成（含边界翻转）
            menu.ShowAtScreen(screenPt.X, screenPt.Y);
        }

        // ---------- 单实例唤起 ----------
        void ListenShowEvent()
        {
            Task.Run(() =>
            {
                try
                {
                    using var ev = new System.Threading.EventWaitHandle(false,
                        System.Threading.EventResetMode.AutoReset, AppVersion.ShowMainEvent);
                    while (true)
                    {
                        ev.WaitOne();
                        Dispatcher.Invoke(ShowMainWindow);
                    }
                }
                catch { }
            });
        }

        // ---------- 静默更新检查 ----------
        void CheckUpdateSilent()
        {
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
                        Dispatcher.Invoke(() =>
                        {
                            var ask = MessageBox.Show(this,
                                "发现新版本 " + res.LatestVersion + "\n\n是否打开官网下载？",
                                AppVersion.ProductName + " 有新版本",
                                MessageBoxButton.YesNo, MessageBoxImage.Information);
                            if (ask == MessageBoxResult.Yes && !string.IsNullOrEmpty(res.DownloadUrl))
                            {
                                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(res.DownloadUrl) { UseShellExecute = true }); } catch { }
                            }
                        });
                    }
                }
                catch { }
            });
        }

        // ---------- 名单管理对话框 ----------
        // ---------------- 名单编辑（表格直接增删改） ----------------
        void ShowRosterEditor() => BuildRosterEditor().ShowDialog();

        Window BuildRosterEditor()
        {
            var t = ThemeManager.Current;
            var dlg = new Window
            {
                Icon = Icon,
                Title = "名单编辑", Width = 760, Height = 580,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this
            };
            var root = new Grid { Margin = new Thickness(18) };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var rows = new ObservableCollection<Student>(
                core.allStudents.Select(x => new Student { name = x.name, classId = x.classId, gender = x.gender, reading = x.reading }));

            var grid = new DataGrid
            {
                AutoGenerateColumns = false, CanUserAddRows = true, ItemsSource = rows,
                HeadersVisibility = DataGridHeadersVisibility.Column, RowHeaderWidth = 0,
                Margin = new Thickness(0, 0, 0, 10), CanUserDeleteRows = true,
                RowHeight = 34, FontSize = 13,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                HorizontalGridLinesBrush = new SolidColorBrush(t.Canvas),
                Background = new SolidColorBrush(t.Surface), BorderBrush = new SolidColorBrush(t.Stroke),
                BorderThickness = new Thickness(1), RowBackground = new SolidColorBrush(t.Surface),
                AlternatingRowBackground = new SolidColorBrush(t.SurfaceMuted),
                UseLayoutRounding = true, SnapsToDevicePixels = true
            };
            rosterEditorGrid = grid;
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "姓名",
                Binding = new System.Windows.Data.Binding("name"),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star)
            });
            var classCol = new DataGridComboBoxColumn
            {
                Header = "班级",
                SelectedValueBinding = new System.Windows.Data.Binding("classId"),
                SelectedValuePath = "Key", DisplayMemberPath = "Value",
                Width = new DataGridLength(150)
            };
            classCol.ItemsSource = core.classIds
                .Select(id => new KeyValuePair<string, string>(id, core.ClassName(id))).ToList();
            grid.Columns.Add(classCol);
            var genderCol = new DataGridComboBoxColumn
            {
                Header = "性别",
                SelectedValueBinding = new System.Windows.Data.Binding("gender"),
                SelectedValuePath = "Key", DisplayMemberPath = "Value",
                Width = new DataGridLength(90)
            };
            genderCol.ItemsSource = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("", "不限"),
                new KeyValuePair<string, string>("male", "男"),
                new KeyValuePair<string, string>("female", "女")
            };
            grid.Columns.Add(genderCol);
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "读音",
                Binding = new System.Windows.Data.Binding("reading"),
                Width = new DataGridLength(130)
            });
            Grid.SetRow(grid, 0);
            root.Children.Add(grid);

            var tip = new TextBlock
            {
                Text = "双击单元格直接修改；「读音」列填同音字可纠正多音字朗读（如 曾→增），选中行可点「设置读音」选择。",
                FontSize = 11.5, TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(t.Sub),
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(tip, 1);
            root.Children.Add(tip);

            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var modernGhost2 = (Style)FindResource("ModernBtnGhost");
            var modernMain2 = (Style)FindResource("ModernBtn");
            var bReading = new Button { Content = "设置读音…", Width = 104, Height = 36, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 10, 0), Style = modernGhost2 };
            rosterEditorReadingButton = bReading;
            var bDel = new Button { Content = "删除选中行", Width = 110, Height = 36, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 10, 0), Style = modernGhost2 };
            var bSave = new Button { Content = "保存", Width = 90, Height = 36, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 10, 0), Style = modernMain2 };
            var bCancel = new Button { Content = "取消", Width = 90, Height = 36, Cursor = Cursors.Hand, Style = modernGhost2 };
            bReading.Click += (s, e) =>
            {
                if (grid.SelectedItem is not Student st) return;
                string? reading = ReadingDialog.Show(dlg, st.name ?? "", st.reading ?? "", text => Speak(text));
                if (reading == null) return;
                st.reading = reading;
                grid.Items.Refresh();
            };
            bDel.Click += (s, e) => { if (grid.SelectedItem is Student st) rows.Remove(st); };
            bSave.Click += (s, e) => { SaveRoster(rows); dlg.Close(); };
            bCancel.Click += (s, e) => dlg.Close();
            btns.Children.Add(bReading); btns.Children.Add(bDel); btns.Children.Add(bSave); btns.Children.Add(bCancel);
            Grid.SetRow(btns, 2);
            root.Children.Add(btns);

            dlg.Content = root;
            return dlg;
        }

        void SaveRoster(IEnumerable<Student> rows)
        {
            try
            {
                var list = rows.Where(x => !string.IsNullOrWhiteSpace(x.name)).ToList();
                var classes = new Dictionary<string, string>(core.classNames);
                foreach (var s in list)
                    if (!classes.ContainsKey(s.classId)) classes[s.classId] = s.classId + "班";
                LuckyCore.SaveDataFile(new DataFile { classes = classes, students = list });
                core.Reload();
                InitCore();
                HintText.Text = "√ 名单已保存（" + list.Count + " 名学生）";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存失败：" + ex.Message, "名单编辑", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        Window? editorManagerWin;
        void ShowEditorDialog()
        {
            if (editorManagerWin != null && editorManagerWin.IsLoaded)
            {
                editorManagerWin.Activate();
                return;
            }
            var t = ThemeManager.Current;
            var dlg = new Window
            {
                Icon = Icon,
                Title = "名单管理", Width = 460, Height = 380,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this
            };
            var panel = new StackPanel { Margin = new Thickness(20) };
            panel.Children.Add(new TextBlock
            {
                Text = "名单文件：" + LuckyCore.DataPath() + "\n保存在本机（%ProgramData%\\LuckyPicker），换机需手动备份。",
                FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14)
            });
            var modern = (Style)FindResource("ModernBtn");
            var modernGhost = (Style)FindResource("ModernBtnGhost");
            var btnEdit = new Button { Content = "编辑名单（表格直接增删改）", Height = 38, Margin = new Thickness(0, 0, 0, 8), Cursor = Cursors.Hand, Style = modern };
            var btnImport = new Button { Content = "导入 Excel / CSV / JSON 名单", Height = 38, Margin = new Thickness(0, 0, 0, 8), Cursor = Cursors.Hand, Style = modern };
            var btnExport = new Button { Content = "导出备份（复制名单文件到桌面）", Height = 38, Margin = new Thickness(0, 0, 0, 8), Cursor = Cursors.Hand, Style = modernGhost };
            var btnOpenDir = new Button { Content = "打开名单所在文件夹", Height = 38, Margin = new Thickness(0, 0, 0, 8), Cursor = Cursors.Hand, Style = modernGhost };
            var status = new TextBlock { FontSize = 12, Foreground = new SolidColorBrush(t.AccentInk), TextWrapping = TextWrapping.Wrap };

            btnImport.Click += (s, e) =>
            {
                var ofd = new Microsoft.Win32.OpenFileDialog { Filter = "名单文件|*.xlsx;*.csv;*.json|Excel|*.xlsx|CSV|*.csv|JSON 备份|*.json" };
                if (ofd.ShowDialog(this) != true) return;
                try
                {
                    var data = ParseRoster(ofd.FileName);
                    if (data == null || data.students.Count == 0)
                    {
                        status.Text = "未解析出学生，请检查文件格式。";
                        status.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 38, 38));
                        return;
                    }
                    LuckyCore.SaveDataFile(new DataFile { classes = data.classes, students = data.students });
                    core.Reload();
                    InitCore();
                    status.Text = "已导入 " + data.students.Count + " 名学生并保存 ✓";
                    // 询问：批量预生成语音缓存（此后抽取即读，无需联网等待）
                    var askPrefetch = MessageBox.Show(this,
                        "已导入 " + data.students.Count + " 名学生。\n\n是否立即预生成全部姓名的语音缓存？\n（生成后抽取播报秒开，不依赖网络；生成中可随时取消）",
                        "预生成语音缓存", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (askPrefetch == MessageBoxResult.Yes)
                    {
                        var names = data.students.Select(NameReading.Spoken).Where(n => n.Length > 0).Distinct().ToList();
                        Dispatcher.BeginInvoke(() => ShowPrefetchDialog(names));
                    }
                }
                catch (Exception ex) { status.Text = "导入失败：" + ex.Message; status.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 38, 38)); }
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
                try { Directory.CreateDirectory(LuckyCore.DataDir()); System.Diagnostics.Process.Start("explorer.exe", LuckyCore.DataDir()); } catch { }
            };
            btnEdit.Click += (s, e) => { dlg.Close(); ShowRosterEditor(); };
            panel.Children.Add(btnEdit);
            panel.Children.Add(btnImport);
            panel.Children.Add(btnExport);
            panel.Children.Add(btnOpenDir);
            panel.Children.Add(status);
            dlg.Content = panel;
            editorManagerWin = dlg;
            dlg.Closed += (s, e) => editorManagerWin = null;
            dlg.Show();
        }

        // ---------------- 批量预生成语音缓存（进度 + 取消） ----------------
        void ShowPrefetchDialog(List<string> names, Window? owner = null, bool modal = true)
        {
            if (names == null || names.Count == 0 || tts == null) return;
            var dlg = new Window
            {
                Icon = Icon,
                Title = "预生成语音缓存", Width = 480, Height = 210,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = owner ?? (Window)this,
                ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false
            };
            var panel = new StackPanel { Margin = new Thickness(22) };
            panel.Children.Add(new TextBlock
            {
                Text = "正在为 " + names.Count + " 个姓名生成语音缓存（抽取播报秒开）…",
                FontSize = 13, TextWrapping = TextWrapping.Wrap
            });
            var prog = new ProgressBar { Height = 16, Maximum = names.Count, Minimum = 0, Margin = new Thickness(0, 14, 0, 10) };
            panel.Children.Add(prog);
            var txt = new TextBlock { Text = "准备中…", FontSize = 12, Foreground = new SolidColorBrush(ThemeManager.Current.Sub) };
            panel.Children.Add(txt);
            var btnCancel = new Button
            {
                Content = "取消生成", Width = 110, Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0),
                Style = (Style)FindResource("ModernBtnGhost")
            };
            panel.Children.Add(btnCancel);
            dlg.Content = panel;

            bool userClosed = false;
            btnCancel.Click += (s, e) =>
            {
                userClosed = true;
                btnCancel.IsEnabled = false;
                txt.Text = "正在取消…（已生成的会保留）";
                tts?.CancelPrefetch();
            };
            dlg.Closed += (s, e) => { userClosed = true; tts?.CancelPrefetch(); };

            var ui = Dispatcher;
            tts.PrefetchBatch(
                names,
                onProgress: (done, total) => ui.BeginInvoke(() =>
                {
                    if (!dlg.IsVisible) return;
                    prog.Value = done;
                    txt.Text = "正在生成 " + done + " / " + total + "…";
                }),
                onDone: () => ui.BeginInvoke(() =>
                {
                    if (!dlg.IsVisible) return;
                    if (!userClosed)
                    {
                        txt.Text = "✓ 全部生成完成，抽取播报已就绪";
                        btnCancel.IsEnabled = false;
                    }
                }));
            if (modal) dlg.ShowDialog(); else dlg.Show();
        }

        void ShowAboutDialog()
        {
            var dlg = new Window
            {
                Icon = Icon,
                Title = "版本与更新", Width = 460, Height = 400,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this
            };
            var panel = new StackPanel { Margin = new Thickness(20) };
            panel.Children.Add(new TextBlock
            {
                Text = "当前版本：" + AppVersion.Display + "\n更新通道：" + AppVersion.Channel + "\n产品：" + AppVersion.ProductName,
                FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12)
            });
            var resultBox = new TextBlock
            {
                Text = "更新地址：" + AppConfig.UpdateUrl, FontSize = 12, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            };
            var btnCheck = new Button { Content = "检查更新", Cursor = Cursors.Hand, Style = (Style)FindResource("ModernBtn") };
            btnCheck.Click += async (s, e) =>
            {
                btnCheck.IsEnabled = false;
                resultBox.Text = "正在检查更新...";
                var res = await Task.Run(() => UpdateManager.Check(AppConfig.UpdateUrl));
                if ((res == null || !res.Success) &&
                    string.Equals(AppConfig.UpdateUrl, AppVersion.UpdateUrlDefault, StringComparison.OrdinalIgnoreCase))
                {
                    var alt = await Task.Run(() => UpdateManager.Check(AppVersion.UpdateUrlLegacy));
                    if (alt != null && alt.Success) res = alt;
                }
                btnCheck.IsEnabled = true;
                if (res == null || !res.Success) { resultBox.Text = "检查失败：" + (res?.ErrorMessage ?? "未知错误"); resultBox.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 38, 38)); return; }
                string txt = "远程版本：" + res.LatestVersion;
                if (!string.IsNullOrEmpty(res.ReleaseDate)) txt += "\n发布日期：" + res.ReleaseDate;
                if (!string.IsNullOrEmpty(res.Notes)) txt += "\n更新说明：\n" + res.Notes;
                txt += "\n\n" + (res.HasUpdate ? "★ 发现新版本！可在官网下载：\n" + res.DownloadUrl : "✓ 当前已是最新版本");
                resultBox.Text = txt;
                resultBox.Foreground = new SolidColorBrush(ThemeManager.Current.InkSoft);
                if (res.HasUpdate)
                {
                    var ask = MessageBox.Show(this,
                        "发现新版本 " + res.LatestVersion + "\n\n是否现在下载安装程序？（后台下载，完成后可手动安装）",
                        "发现新版本", MessageBoxButton.YesNo, MessageBoxImage.Information);
                    if (ask == MessageBoxResult.Yes) ShowUpdateDownload(res);
                }
            };
            panel.Children.Add(resultBox);
            panel.Children.Add(btnCheck);
            dlg.Content = panel;
            dlg.ShowDialog();
        }

        // ---------- 更新：后台下载安装器 ----------
        void ShowUpdateDownload(UpdateResult res, Window? owner = null, bool modal = true)
        {
            var t = ThemeManager.Current;
            string url = !string.IsNullOrEmpty(res.SetupUrl) ? res.SetupUrl : res.DownloadUrl;
            if (string.IsNullOrEmpty(url))
            {
                MessageBox.Show(owner ?? (Window)this, "未获取到安装程序下载地址，请到官网下载。", "更新", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!Uri.TryCreate(url, UriKind.Absolute, out var downloadUri) || downloadUri.Scheme != Uri.UriSchemeHttps)
            {
                MessageBox.Show(owner ?? (Window)this, "安装程序必须使用 HTTPS 下载，已取消本次下载。", "更新", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (res.SetupSha256.Length > 0 && (res.SetupSha256.Length != 64 || res.SetupSha256.Any(c => !Uri.IsHexDigit(c))))
            {
                MessageBox.Show(owner ?? (Window)this, "更新信息中的 SHA-256 校验值无效，已取消本次下载。", "更新", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LuckyPicker", "updates");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "LuckyPicker-Setup-" + (res.LatestBuild > 0 ? res.LatestBuild.ToString() : "latest") + ".exe");

            var dlg = new Window
            {
                Title = "下载更新 " + res.LatestVersion, Width = 420, Height = 210,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = owner ?? (Window)this,
                ResizeMode = ResizeMode.NoResize,
                Background = new SolidColorBrush(t.Surface)
            };
            var root = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };
            root.Children.Add(new TextBlock
            {
                Text = "正在后台下载安装程序…", FontSize = 13.5, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(t.Ink), Margin = new Thickness(0, 0, 0, 14)
            });
            var bar = new ProgressBar { Height = 12, Minimum = 0, Maximum = 100, Value = 0 };
            root.Children.Add(bar);
            var status = new TextBlock
            {
                Text = "准备中…", FontSize = 12, Margin = new Thickness(0, 10, 0, 0),
                Foreground = new SolidColorBrush(t.Sub)
            };
            root.Children.Add(status);

            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            var bOpen = new Button { Content = "打开安装程序", Width = 130, IsEnabled = false, Margin = new Thickness(0, 0, 10, 0), Cursor = Cursors.Hand, Style = (Style)FindResource("ModernBtn") };
            var bRetry = new Button { Content = "重试", Width = 90, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 10, 0), Style = (Style)FindResource("ModernBtnGhost") };
            var bCancel = new Button { Content = "取消", Width = 90, Cursor = Cursors.Hand, Style = (Style)FindResource("ModernBtnGhost") };
            bCancel.Click += (s, e) => dlg.Close();
            bOpen.Click += (s, e) =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file) { UseShellExecute = true }); }
                catch { }
                dlg.Close();
            };
            btns.Children.Add(bOpen); btns.Children.Add(bRetry); btns.Children.Add(bCancel);
            root.Children.Add(btns);

            var cts = new System.Threading.CancellationTokenSource();
            dlg.Closed += (s, e) => cts.Cancel();
            bRetry.Click += async (s, e) => { await RunDownload(); };
            bRetry.IsEnabled = false;

            async System.Threading.Tasks.Task RunDownload()
            {
                bRetry.IsEnabled = false;
                bOpen.IsEnabled = false;
                bar.Value = 0;
                status.Text = "正在下载…";
                status.Foreground = new SolidColorBrush(t.Sub);
                cts = new System.Threading.CancellationTokenSource();
                try
                {
                    using var http = new System.Net.Http.HttpClient();
                    http.Timeout = TimeSpan.FromMinutes(20);
                    using var resp = await http.GetAsync(url, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, cts.Token);
                    resp.EnsureSuccessStatusCode();
                    long? total = resp.Content.Headers.ContentLength;
                    using var src = await resp.Content.ReadAsStreamAsync(cts.Token);
                    var buf = new byte[81920];
                    long read = 0;
                    int n;
                    using (var fs = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        while ((n = await src.ReadAsync(buf, 0, buf.Length, cts.Token)) > 0)
                        {
                            await fs.WriteAsync(buf, 0, n, cts.Token);
                            read += n;
                            if (total.HasValue && total.Value > 0)
                            {
                                double pct = read * 100.0 / total.Value;
                                bar.Value = pct;
                                status.Text = string.Format("已下载 {0:F1} / {1:F1} MB（{2:F0}%）",
                                    read / 1048576.0, total.Value / 1048576.0, pct);
                            }
                            else
                            {
                                status.Text = string.Format("已下载 {0:F1} MB", read / 1048576.0);
                            }
                        }
                    }
                    if (res.SetupSha256.Length > 0)
                    {
                        status.Text = "正在校验安装程序…";
                        using var downloaded = File.OpenRead(file);
                        string actual = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(downloaded)).ToLowerInvariant();
                        if (!string.Equals(actual, res.SetupSha256, StringComparison.Ordinal))
                            throw new InvalidDataException("SHA-256 校验失败，文件可能已损坏或被篡改。");
                    }
                    bar.Value = 100;
                    status.Text = "下载完成，可点击「打开安装程序」安装。";
                    status.Foreground = new SolidColorBrush(t.AccentInk);
                    bOpen.IsEnabled = true;
                    bRetry.IsEnabled = true;
                }
                catch (Exception ex)
                {
                    status.Text = "下载失败：" + ex.Message + "\n可点击「重试」继续。";
                    status.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                    bRetry.IsEnabled = true;
                }
            }

            dlg.Content = root;
            dlg.Loaded += async (s, e) => await RunDownload();
            if (modal) dlg.ShowDialog(); else dlg.Show();
        }

        // ---------- 解析名单文件 ----------
        static DataFile? ParseRoster(string path)
        {
            List<List<string>> rows;
            if (path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) rows = XlsxReader.Read(path);
            else if (path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) rows = CsvReader.Read(path);
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
            int readingCol = ImportUtil.FindColumn(rows, "reading", "读音", "朗读");
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
                string reading = readingCol >= 0 && readingCol < r.Count ? r[readingCol].Trim() : "";
                students.Add(new Student { name = name, classId = cid, gender = g, reading = reading });
                if (!classes.ContainsKey(cid)) classes[cid] = cid + "班";
            }
            return new DataFile { classes = classes, students = students };
        }

        // ---------- 关闭 → 托盘 ----------
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            base.OnClosing(e);
            if (!reallyExit)
            {
                e.Cancel = true;
                Hide();
                App.Log("W-hidden");
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            try { ball?.Close(); } catch { }
            try { tray?.Dispose(); } catch { }
        }

        // ================================================================
        // 自检钩子（仅供 SelfTest 使用，不影响正常逻辑）
        // ================================================================
        internal double BallDiameterForTest => ball?.BallDiameterNow ?? -1;
        internal bool BallIsPinnedForTest => ball?.IsPinnedNow ?? false;
        internal double BallLeftForTest => ball?.Left ?? double.NaN;
        internal double BallTopForTest => ball?.Top ?? double.NaN;
        internal double BallOpacityForTest => ball?.OpacityNow ?? -1;
        internal bool BallHasScaleForTest => ball?.HasScaleTransform ?? false;
        internal double BallScaleForTest => ball?.ScaleNow ?? -1;
        internal void BallShowPickedForTest(string text) => ball?.ShowPicked(text);
        internal void BallAutoHideForTest() => ball?.AutoHideAfterPick();
        internal void BallPanelToggleForTest() { ShowBallPanel(); PumpFrames(220); }
        internal double BallShadowPadForTest => ball?.ShadowPadNow ?? -1;
        internal double BallBrushLuminanceForTest => ball?.BrushLuminanceNow ?? -1;
        internal System.Windows.Media.Color? BallTopColorForTest => ball?.BallTopColorNow;
        internal void BallTouchForTest() => ball?.TouchForTest();
        internal void BallHoverForTest(bool on) => ball?.HoverForTest(on);
        internal bool BallInteractiveForTest => ball?.InteractiveNow ?? false;
        internal bool BallDimmedForTest => ball?.IsDimmedNow ?? false;

        // ---- Build 17：悬浮球模糊 & 菜单定位的观测点 ----
        /** 球体自身挂 Effect ⇒ 会被光栅化后重采样 ⇒ 发虚。修好后应为 false。 */
        internal bool BallHasOwnEffectForTest => ball?.HasOwnEffectNow ?? true;
        internal bool BallShadowHasEffectForTest => ball?.ShadowHasEffectNow ?? false;
        internal bool BallShadowSharesTransformForTest => ball?.ShadowSharesTransformNow ?? false;
        internal bool BallOnWholePixelForTest => ball?.PositionOnWholePixelNow ?? false;
        internal double BallDpiScaleForTest => ball?.DpiScaleNow ?? 1.0;
        /** 悬浮球窗口的真实屏幕矩形（设备像素，Win32 口径）。 */
        internal System.Windows.Rect BallWindowRectPxForTest =>
            ball == null ? new System.Windows.Rect(0, 0, 0, 0) : ModernMenu.WindowRectPx(ball);

        /// <summary>自检用：还原悬浮球位置（避免自检把位置写脏）。直接赋值，不触发吸附与保存。</summary>
        internal void RestoreBallForTest(int x, int y)
        {
            try
            {
                if (ball == null) return;
                ball.Left = x; ball.Top = y;
            }
            catch { }
        }
        internal Window? BallWindowForTest => ball;

        /// <summary>自检用：弹出悬浮球右键菜单。</summary>
        internal ModernMenu? BallContextMenuForTest()
        {
            var m = ball?.ShowContextMenuForTest();
            PumpFrames(200);
            return m;
        }
        internal static System.Windows.Point? BallPanelPosForTest => BallQuickPanel.LastPanelPos;
        internal double BallWidthForTest => ball?.Width ?? -1;

        /// <summary>自检用：把主窗口隐藏（模拟"关闭到托盘"），验证托盘菜单仍能正确定位。</summary>
        internal void HideMainForTest() { Hide(); PumpFrames(250); }
        internal void ShowMainForTest() { Show(); Activate(); PumpFrames(250); }

        internal ModernMenu? ShowTrayMenuForTest(int x, int y)
        {
            ShowTrayMenu(new System.Drawing.Point(x, y));
            PumpFrames(150);
            return trayMenu;
        }

        internal ModernMenu? ShowTrayMenuAtCursorForTest(int x, int y)
        {
            var previous = System.Windows.Forms.Cursor.Position;
            try
            {
                System.Windows.Forms.Cursor.Position = new System.Drawing.Point(x, y);
                ShowTrayMenu(System.Windows.Forms.Cursor.Position);
                PumpFrames(150);
                return trayMenu;
            }
            finally { System.Windows.Forms.Cursor.Position = previous; }
        }

        internal void HideTrayMenuForTest()
        {
            try { trayMenu?.Close(); } catch { }
            PumpFrames(150);
        }

        /// <summary>自检用：关闭并清空托盘菜单引用（模拟"菜单已收起"，以便再次弹出）。</summary>
        internal void CloseTrayMenuForTest()
        {
            try { trayMenu?.Close(); } catch { }
            trayMenu = null;
            PumpFrames(150);
        }

        internal SettingsWindow? SettingsWindowForTest => settingsWin;
        internal void OpenSettingsForTest()
        {
            OpenSettings();
            PumpFrames(400);
        }
        internal int SettingsPageCountForTest(SettingsWindow sw) => sw.PageCountForTest;
        internal bool SettingsHasNavTitleForTest(SettingsWindow sw, string title) => sw.HasNavTitleForTest(title);
        internal bool SettingsNavRoundedForTest(SettingsWindow sw) => sw.NavRoundedForTest;
        internal void OpenHistoryForTest() { ShowHistoryDialog(); PumpFrames(300); }
        internal HistoryWindow? HistoryWindowForTest => historyWin;
        internal Window? EditorManagerForTest => editorManagerWin;
        internal Window BuildRosterEditorForTest() => BuildRosterEditor();
        internal bool RosterEditorHasReadingUiForTest =>
            rosterEditorGrid != null
            && rosterEditorGrid.Columns.Any(c => string.Equals(c.Header as string, "读音"))
            && rosterEditorReadingButton != null;
        internal int SettingsBallSizeCheckedCountForTest(SettingsWindow sw) => sw.BallSizeCheckedCountForTest;
        internal double SettingsBallPreviewDiameterForTest(SettingsWindow sw) => sw.BallPreviewDiameterForTest;
        internal string SettingsStatusTextForTest(SettingsWindow sw) => sw.StatusTextForTest;
        internal void SettingsSwitchPageForTest(SettingsWindow sw, int idx) => sw.SwitchPageForTest(idx);
        internal bool SettingsCheckBallSizeForTest(SettingsWindow sw, int d)
        {
            bool ok = sw.CheckBallSizeForTest(d);
            PumpFrames(200);
            return ok;
        }

        internal bool BigButtonsHaveNoEffectForTest()
            => PickBtn.Effect == null && MultiBtn.Effect == null && NumBtn.Effect == null;

        internal static void PumpFrames(int ms)
        {
            var frame = new DispatcherFrame();
            var t = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(ms) };
            t.Tick += (s, e) => { t.Stop(); frame.Continue = false; };
            t.Start();
            Dispatcher.PushFrame(frame);
        }
    }
}
