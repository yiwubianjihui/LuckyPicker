// ================================================================
// MainWindow.xaml.cs — WPF 版主窗口（逻辑层与 WinForms/WinUI 版共享）
// ================================================================
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
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
            App.Log("W-xaml");
            InitCore();
            App.Log("W-core");
            InitTray();
            ListenShowEvent();
            CheckUpdateSilent();
            classChosen = !App.BootMinimized;
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
            InitSpeech();
        }

        void InitSpeech()
        {
            try
            {
                tts = new TtsEngine(s => Dispatcher.Invoke(() => { try { VoiceStatusText.Text = s; } catch { } }));
                string st = tts.InitStatus();   // 初始化本地 SAPI 兜底（在线失败时保证能出声）
                VoiceStatusText.Text = "♪ 语音：" + st;
                try { tts.QueueWarmup(GetClassNames()); } catch { }
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
            var s = core.PickOne();
            if (s == null) { HintText.Text = "※ 当前无候选人（请检查名单 / 屏蔽 / 筛选）"; return; }
            await AnimatePickAsync(s.name);
            Speak(s.name);
            AddHistory(new HistoryEntry { time = Now(), text = s.name, classId = core.currentClassId });
            RefreshPoolStats();
        }

        async void OnMulti(object sender, RoutedEventArgs e)
        {
            var list = core.PickMulti(5);
            if (list.Count == 0) { HintText.Text = "※ 当前无候选人"; return; }
            var names = list.Select(x => x.name).ToList();
            await ShowMultiAsync(names);
            // 逐姓名依次播报：每个姓名各自命中缓存（拼接整句会导致缓存永不命中）
            tts?.SpeakSequence(names);
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
            var tcs = new TaskCompletionSource<bool>();
            Dispatcher.BeginInvoke(() =>
            {
                MultiPanel.Children.Clear();
                foreach (var n in names)
                {
                    MultiPanel.Children.Add(new Border
                    {
                        Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(230, 240, 255)),
                        CornerRadius = new CornerRadius(19),
                        Padding = new Thickness(14, 8, 14, 8),
                        Margin = new Thickness(0, 0, 8, 8),
                        Child = new TextBlock { Text = n, FontSize = 14, FontWeight = FontWeights.Bold }
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

        void Speak(string text)
        {
            if (tts == null) return;
            Task.Run(() => { try { tts.Speak(text); } catch { } });
        }

        void AddHistory(HistoryEntry entry)
        {
            try { HistoryStore.Add(entry); } catch { }
            HintText.Text = entry.text;
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
            BlockChips.Children.Clear();
            foreach (var n in core.blockNames)
            {
                string name = n;
                var chip = new Button
                {
                    Content = n + " ✕", FontSize = 12, Padding = new Thickness(10, 3, 10, 3),
                    Margin = new Thickness(0, 0, 6, 6), Cursor = Cursors.Hand,
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 237, 213)),
                    BorderThickness = new Thickness(0)
                };
                chip.Click += (s, e) => { core.RemoveBlock(name); RenderBlockChips(); core.ResetPoolKeepLast(); RefreshPoolStats(); };
                BlockChips.Children.Add(chip);
            }
        }

        // ---------- 设置菜单 ----------
        // 设置按钮：左键点击弹出菜单（WPF ContextMenu 默认仅右键触发）
        void OnSettingsClick(object sender, RoutedEventArgs e)
        {
            var ctx = SettingsBtn.ContextMenu;
            if (ctx == null) return;
            ctx.PlacementTarget = SettingsBtn;
            ctx.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            ctx.IsOpen = true;
        }

        void OnMenuVoice(object sender, RoutedEventArgs e) => ShowVoiceDialog();

        // ---------------- 语音设置（独立窗口） ----------------
        void ShowVoiceDialog()
        {
            var dlg = new Window
            {
                Title = "语音设置", Width = 440, Height = 420,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this
            };
            var panel = new StackPanel { Margin = new Thickness(22) };

            // 播报来源
            panel.Children.Add(new TextBlock { Text = "播报引擎", FontSize = 12.5, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) });
            var srcCombo = new ComboBox { Margin = new Thickness(0, 0, 0, 14) };
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
            var voiceCombo = new ComboBox { Margin = new Thickness(0, 0, 0, 14) };
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
                FontSize = 11, Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 116, 139)),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12)
            };
            panel.Children.Add(hint);

            var btnTest = new Button { Content = "试听语音", Height = 36, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 0, 8) };
            var btnClose = new Button { Content = "关闭", Height = 36, Cursor = Cursors.Hand };
            btnTest.Click += (s, e) =>
            {
                ApplyVoice(srcCombo, voiceCombo);
                Speak("欢迎使用 YBH 幸运摇人器");
            };
            btnClose.Click += (s, e) => dlg.Close();
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(btnTest); btnTest.Margin = new Thickness(0, 0, 10, 0);
            row.Children.Add(btnClose);
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
            VoiceStatusText.Text = "♪ 语音：" + status;
            try { tts.QueueWarmup(GetClassNames()); } catch { }
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
                if (!string.IsNullOrEmpty(last) && last != "——") Speak(last);
            }
        }

        void OnMenuEditor(object sender, RoutedEventArgs e) => ShowEditorDialog();
        void OnMenuHistory(object sender, RoutedEventArgs e) => ShowHistoryDialog();
        void OnMenuAbout(object sender, RoutedEventArgs e) => ShowAboutDialog();

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

        void OnBallClicked()
        {
            if (!classChosen) { ShowClassMini(); return; }
            DoBallPickOne();
        }

        void DoBallPickOne()
        {
            var picked = core.PickOne();
            if (picked == null) { HintText.Text = "※ 当前无候选人"; return; }
            ball?.ShowPicked(picked.name);
            Speak(picked.name);
            AddHistory(new HistoryEntry { time = Now(), text = picked.name, classId = core.currentClassId });
            RefreshPoolStats();
        }

        void DoBallPickMulti()
        {
            var list = core.PickMulti(5);
            if (list.Count == 0) { HintText.Text = "※ 当前无候选人"; return; }
            ball?.ShowPicked(list.Count + "人");
            Speak(string.Join("、", list.Select(x => x.name)));
            AddHistory(new HistoryEntry { time = Now(), text = "连抽： " + string.Join("、", list.Select(x => x.name)), classId = core.currentClassId });
            RefreshPoolStats();
        }

        void ShowClassMini()
        {
            var mini = new ClassMiniWindow(core.classIds, core.classNames, cid =>
            {
                core.SetClass(cid);
                classChosen = true;
                RefreshBadge();
                RefreshPoolStats();
                DoBallPickOne();
            });
            if (ball != null && ball.IsVisible) mini.ShowNear(ball);
            else { mini.WindowStartupLocation = WindowStartupLocation.CenterScreen; mini.Show(); }
        }

        void ShowMainWindow()
        {
            if (!IsVisible) Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
        }

        // ---------- 托盘 ----------
        void InitTray()
        {
            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("显示主窗口", null, (s, e) => Dispatcher.Invoke(ShowMainWindow));
            var boot = new System.Windows.Forms.ToolStripMenuItem("开机自启动") { CheckOnClick = true, Checked = AutoStart.IsEnabled() };
            boot.Click += (s, e) => Dispatcher.Invoke(() => { MenuAutoStart.IsChecked = AutoStart.IsEnabled(); });
            menu.Items.Add(boot);
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("退出", null, (s, e) => Dispatcher.Invoke(QuitApp));
            tray = new System.Windows.Forms.NotifyIcon
            {
                Text = AppVersion.ProductName + " · " + AppVersion.Display,
                ContextMenuStrip = menu,
                Visible = true
            };
            try
            {
                var icoPath = Path.Combine(AppContext.BaseDirectory, "icon.ico");
                if (File.Exists(icoPath)) tray.Icon = new System.Drawing.Icon(icoPath);
                else tray.Icon = System.Drawing.SystemIcons.Application;
            }
            catch { tray.Icon = System.Drawing.SystemIcons.Application; }
            tray.DoubleClick += (s, e) => Dispatcher.Invoke(ShowMainWindow);
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
        void ShowRosterEditor()
        {
            var dlg = new Window
            {
                Title = "名单编辑", Width = 760, Height = 580,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this
            };
            var root = new Grid { Margin = new Thickness(18) };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var rows = new ObservableCollection<Student>(
                core.allStudents.Select(x => new Student { name = x.name, classId = x.classId, gender = x.gender }));

            var grid = new DataGrid
            {
                AutoGenerateColumns = false, CanUserAddRows = true, ItemsSource = rows,
                HeadersVisibility = DataGridHeadersVisibility.Column, RowHeaderWidth = 0,
                Margin = new Thickness(0, 0, 0, 10), CanUserDeleteRows = true
            };
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
            Grid.SetRow(grid, 0);
            root.Children.Add(grid);

            var tip = new TextBlock
            {
                Text = "双击单元格直接修改；最后一行为新增行；选中行后按 Delete 或点「删除选中行」移除。",
                FontSize = 11.5, TextWrapping = TextWrapping.Wrap,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 116, 139)),
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(tip, 1);
            root.Children.Add(tip);

            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var bDel = new Button { Content = "删除选中行", Width = 110, Height = 32, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 10, 0) };
            var bSave = new Button { Content = "保存", Width = 90, Height = 32, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 10, 0) };
            var bCancel = new Button { Content = "取消", Width = 90, Height = 32, Cursor = Cursors.Hand };
            bDel.Click += (s, e) => { if (grid.SelectedItem is Student st) rows.Remove(st); };
            bSave.Click += (s, e) => { SaveRoster(rows); dlg.Close(); };
            bCancel.Click += (s, e) => dlg.Close();
            btns.Children.Add(bDel); btns.Children.Add(bSave); btns.Children.Add(bCancel);
            Grid.SetRow(btns, 2);
            root.Children.Add(btns);

            dlg.Content = root;
            dlg.ShowDialog();
        }

        void SaveRoster(IEnumerable<Student> rows)
        {
            try
            {
                var list = rows.Where(x => !string.IsNullOrWhiteSpace(x.name)).ToList();
                var classes = new Dictionary<string, string>(core.classNames);
                foreach (var s in list)
                    if (!classes.ContainsKey(s.classId)) classes[s.classId] = s.classId + "班";
                Directory.CreateDirectory(LuckyCore.DataDir());
                File.WriteAllText(LuckyCore.DataPath(),
                    System.Text.Json.JsonSerializer.Serialize(new DataFile { classes = classes, students = list }));
                core.Reload();
                InitCore();
                HintText.Text = "√ 名单已保存（" + list.Count + " 名学生）";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存失败：" + ex.Message, "名单编辑", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        void ShowEditorDialog()
        {
            var dlg = new Window
            {
                Title = "名单管理", Width = 460, Height = 380,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this
            };
            var panel = new StackPanel { Margin = new Thickness(20) };
            panel.Children.Add(new TextBlock
            {
                Text = "名单文件：" + LuckyCore.DataPath() + "\n保存在本机（%ProgramData%\\LuckyPicker），换机需手动备份。",
                FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14)
            });
            var btnEdit = new Button { Content = "编辑名单（表格直接增删改）", Height = 36, Margin = new Thickness(0, 0, 0, 8), Cursor = Cursors.Hand };
            var btnImport = new Button { Content = "导入 Excel / CSV / JSON 名单", Height = 36, Margin = new Thickness(0, 0, 0, 8), Cursor = Cursors.Hand };
            var btnExport = new Button { Content = "导出备份（复制名单文件到桌面）", Height = 36, Margin = new Thickness(0, 0, 0, 8), Cursor = Cursors.Hand };
            var btnOpenDir = new Button { Content = "打开名单所在文件夹", Height = 36, Margin = new Thickness(0, 0, 0, 8), Cursor = Cursors.Hand };
            var status = new TextBlock { FontSize = 12, Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(6, 95, 70)), TextWrapping = TextWrapping.Wrap };

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
                    Directory.CreateDirectory(LuckyCore.DataDir());
                    File.WriteAllText(LuckyCore.DataPath(),
                        System.Text.Json.JsonSerializer.Serialize(new DataFile { classes = data.classes, students = data.students }));
                    core.Reload();
                    InitCore();
                    status.Text = "已导入 " + data.students.Count + " 名学生并保存 ✓";
                    // 询问：批量预生成语音缓存（此后抽取即读，无需联网等待）
                    var askPrefetch = MessageBox.Show(this,
                        "已导入 " + data.students.Count + " 名学生。\n\n是否立即预生成全部姓名的语音缓存？\n（生成后抽取播报秒开，不依赖网络；生成中可随时取消）",
                        "预生成语音缓存", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (askPrefetch == MessageBoxResult.Yes)
                    {
                        var names = data.students.Select(x => x.name).Distinct().ToList();
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
            dlg.ShowDialog();
        }

        // ---------------- 批量预生成语音缓存（进度 + 取消） ----------------
        void ShowPrefetchDialog(List<string> names)
        {
            if (names == null || names.Count == 0 || tts == null) return;
            var dlg = new Window
            {
                Title = "预生成语音缓存", Width = 480, Height = 210,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this,
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
            var txt = new TextBlock { Text = "准备中…", FontSize = 12, Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 116, 139)) };
            panel.Children.Add(txt);
            var btnCancel = new Button
            {
                Content = "取消生成", Height = 32, Width = 110, Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0)
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
            dlg.ShowDialog();
        }

        void ShowHistoryDialog()
        {
            var dlg = new Window
            {
                Title = "抽选记录", Width = 460, Height = 420,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this
            };
            var panel = new StackPanel { Margin = new Thickness(16) };
            var list = new ListBox { Height = 320, FontSize = 12 };
            var items = HistoryStore.Load().Select(h => h.time + "  " + h.text).ToList();
            if (items.Count == 0) items.Add("（暂无记录）");
            list.ItemsSource = items;
            var btnClear = new Button { Content = "清空记录", Height = 32, Margin = new Thickness(0, 10, 0, 0), Cursor = Cursors.Hand };
            btnClear.Click += (s, e) => { HistoryStore.Clear(); items.Clear(); items.Add("（已清空）"); list.ItemsSource = items.ToList(); };
            panel.Children.Add(list);
            panel.Children.Add(btnClear);
            dlg.Content = panel;
            dlg.ShowDialog();
        }

        void ShowAboutDialog()
        {
            var dlg = new Window
            {
                Title = "版本与更新", Width = 460, Height = 400,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this
            };
            var panel = new StackPanel { Margin = new Thickness(20) };
            panel.Children.Add(new TextBlock
            {
                Text = "当前版本：26H2 Build 14（内部构建 14）\n更新通道：26H2\n产品：YBH幸运摇人器",
                FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12)
            });
            var resultBox = new TextBlock
            {
                Text = "更新地址：" + AppConfig.UpdateUrl, FontSize = 12, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            };
            var btnCheck = new Button { Content = "检查更新", Height = 36, Cursor = Cursors.Hand };
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
                resultBox.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 65, 85));
                if (res.HasUpdate && !string.IsNullOrEmpty(res.DownloadUrl))
                {
                    var ask = MessageBox.Show(this, "发现新版本 " + res.LatestVersion + "\n是否打开官网下载页？",
                        "发现新版本", MessageBoxButton.YesNo, MessageBoxImage.Information);
                    if (ask == MessageBoxResult.Yes)
                    {
                        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(res.DownloadUrl) { UseShellExecute = true }); } catch { }
                    }
                }
            };
            panel.Children.Add(resultBox);
            panel.Children.Add(btnCheck);
            dlg.Content = panel;
            dlg.ShowDialog();
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
    }
}
