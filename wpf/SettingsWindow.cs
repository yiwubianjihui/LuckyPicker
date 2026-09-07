// ================================================================
// SettingsWindow.cs — 统一设置窗口
//
// 左侧分类导航（树状）+ 右侧内容区，类似 Windows 11 设置面板。
// 替代原来的 ModernMenu 弹出菜单 + 多个子对话框（语音/名单/版本）。
// 所有开关/单选/操作都在一个窗口内完成。
// ================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace LuckyPickerWpf
{
    public class SettingsWindow : Window
    {
        // -------- 引用主窗口回调（避免 SettingsWindow 直接依赖太多） --------
        readonly Action onApplyVoice;             // (ComboBox src, ComboBox voice) => apply + save
        readonly Func<List<string>> getClassNames;
        readonly Action refreshBall;
        readonly Action showHistory;
        readonly Action onChangeDefaultClass;     // 清空 DefaultClassId + 弹出 ClassMini
        readonly Action onQuit;
        readonly Action<UpdateResult> showUpdateDownload;
        readonly Func<string, Task<UpdateResult?>> checkUpdateAsync;
        readonly Window mainWin;
        readonly ComboBox srcCombo;   // 用于"语音"标签里的引擎下拉
        readonly ComboBox voiceCombo; // 用于"语音"标签里的音色下拉

        // -------- 控件引用 --------
        TreeView treeNav;
        TextBlock statusText;
        ComboBox defaultClassCombo;
        List<MenuCheckItem> sizeItems = new();
        CheckBox chkAutoStart, chkBallVisible, chkBallPinned;
        StackPanel[] pages = new StackPanel[5];   // 5 个分类页
        const int IDX_GENERAL = 0, IDX_VOICE = 1, IDX_ROSTER = 2, IDX_HISTORY = 3, IDX_ABOUT = 4;
        TextBlock voiceStatus, historyEmpty;
        ListBox historyList;
        TextBlock updateResultText;

        public SettingsWindow(Window owner,
            Action applyVoice, Func<List<string>> classNames,
            Action refreshBall, Action showHistory, Action changeDefaultClass,
            Action onQuit, Action<UpdateResult> showUpdateDownload,
            Func<string, Task<UpdateResult?>> checkUpdateAsync)
        {
            onApplyVoice = applyVoice;
            getClassNames = classNames;
            this.refreshBall = refreshBall;
            this.showHistory = showHistory;
            onChangeDefaultClass = changeDefaultClass;
            this.onQuit = onQuit;
            this.showUpdateDownload = showUpdateDownload;
            this.checkUpdateAsync = checkUpdateAsync;
            mainWin = owner;

            Icon = owner.Icon;
            Title = "设置 · " + AppVersion.ProductName;
            Width = 720; Height = 520;
            MinWidth = 600; MinHeight = 420;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Owner = owner;
            Background = new SolidColorBrush(Color.FromRgb(248, 250, 252));

            BuildLayout();
        }

        void BuildLayout()
        {
            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(178) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // 左侧导航
            var navBg = new SolidColorBrush(Color.FromRgb(241, 245, 249));
            var navBorder = new Border { Background = navBg, BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)), BorderThickness = new Thickness(0, 0, 1, 0) };
            navBorder.Child = BuildNavPanel();
            Grid.SetColumn(navBorder, 0);
            root.Children.Add(navBorder);

            // 右侧内容
            var content = new Grid();
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            BuildAllPages(content);
            // 底部状态栏 + 关闭按钮
            var bottom = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)), BorderThickness = new Thickness(0, 1, 0, 0), Background = Brushes.White, Padding = new Thickness(20, 12, 20, 12) };
            var bottomRow = new Grid();
            bottomRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottomRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            statusText = new TextBlock { Text = "就绪", FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(statusText, 0);
            bottomRow.Children.Add(statusText);
            var btnRow = new StackPanel { Orientation = Orientation.Horizontal };
            var btnOk = new Button { Content = "关闭", Width = 100, Style = (Style)FindResource("ModernBtn") };
            btnOk.Click += (s, e) => Close();
            btnRow.Children.Add(btnOk);
            Grid.SetColumn(bottomRow, 1);
            bottomRow.Children.Add(btnRow);
            bottom.Child = bottomRow;
            Grid.SetRow(bottom, 1);
            content.Children.Add(bottom);

            Grid.SetColumn(content, 1);
            root.Children.Add(content);

            Content = root;
        }

        StackPanel BuildNavPanel()
        {
            var panel = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
            panel.Children.Add(MakeNavTitle("YBH 幸运摇人器"));
            panel.Children.Add(MakeNavSubtitle(AppVersion.Display));

            treeNav = new TreeView { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Margin = new Thickness(8, 12, 8, 0) };
            // TreeView 在 Win11 风格下重设 ItemContainerStyle
            var itemStyle = new Style(typeof(TreeViewItem));
            itemStyle.Setters.Add(new Setter(TreeViewItem.PaddingProperty, new Thickness(14, 10, 14, 10)));
            itemStyle.Setters.Add(new Setter(TreeViewItem.FontSizeProperty, 13.5));
            itemStyle.Setters.Add(new Setter(TreeViewItem.CursorProperty, Cursors.Hand));
            itemStyle.Setters.Add(new Setter(TreeViewItem.ForegroundProperty, new SolidColorBrush(Color.FromRgb(30, 41, 59))));
            // 选中样式
            var selTrig = new Trigger { Property = TreeViewItem.IsSelectedProperty, Value = true };
            selTrig.Setters.Add(new Setter(TreeViewItem.BackgroundProperty, new SolidColorBrush(Color.FromArgb(40, 37, 99, 235))));
            selTrig.Setters.Add(new Setter(TreeViewItem.ForegroundProperty, new SolidColorBrush(Color.FromRgb(29, 78, 216))));
            selTrig.Setters.Add(new Setter(TreeViewItem.FontWeightProperty, FontWeights.SemiBold));
            itemStyle.Triggers.Add(selTrig);
            treeNav.ItemContainerStyle = itemStyle;

            string[] titles = { "常规", "语音", "名单管理", "抽选记录", "关于 · 更新" };
            for (int i = 0; i < titles.Length; i++)
            {
                var item = new TreeViewItem { Header = titles[i], Tag = i };
                int idx = i;
                item.Selected += (s, e) => { if (item.IsSelected) ShowPage(idx); };
                treeNav.Items.Add(item);
            }
            treeNav.SelectedItemChanged += (s, e) =>
            {
                if (treeNav.SelectedItem is TreeViewItem t && t.Tag is int idx) ShowPage(idx);
            };
            panel.Children.Add(treeNav);

            return panel;
        }

        TextBlock MakeNavTitle(string text) => new TextBlock
        {
            Text = text, FontSize = 14, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(30, 64, 175)),
            Margin = new Thickness(20, 0, 20, 2)
        };
        TextBlock MakeNavSubtitle(string text) => new TextBlock
        {
            Text = text, FontSize = 11.5,
            Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
            Margin = new Thickness(20, 0, 20, 0)
        };

        void BuildAllPages(Grid parent)
        {
            // 用一个 Grid 装 5 个页面（每个占满），切换时只显示一个
            var host = new Grid();
            for (int i = 0; i < pages.Length; i++)
            {
                pages[i] = new StackPanel { Margin = new Thickness(28, 24, 28, 16), Visibility = Visibility.Collapsed };
                host.Children.Add(pages[i]);
            }
            // 注入各页内容
            FillGeneralPage(pages[IDX_GENERAL]);
            FillVoicePage(pages[IDX_VOICE]);
            FillRosterPage(pages[IDX_ROSTER]);
            FillHistoryPage(pages[IDX_HISTORY]);
            FillAboutPage(pages[IDX_ABOUT]);

            Grid.SetRow(host, 0);
            parent.Children.Add(host);

            ShowPage(0);
            // 默认选中第一项
            if (treeNav.Items.Count > 0) ((TreeViewItem)treeNav.Items[0]).IsSelected = true;
        }

        void ShowPage(int idx)
        {
            for (int i = 0; i < pages.Length; i++) pages[i].Visibility = (i == idx) ? Visibility.Visible : Visibility.Collapsed;
        }

        // ==================== 各分类页 ====================

        void FillGeneralPage(StackPanel sp)
        {
            sp.Children.Add(MakeSectionTitle("常规"));
            sp.Children.Add(MakeHint("基础行为、悬浮球与默认班级。"));

            // 开机自启动
            chkAutoStart = new CheckBox
            {
                Content = "开机自启动", IsChecked = AutoStart.IsEnabled(),
                FontSize = 13.5, Margin = new Thickness(0, 12, 0, 6), Cursor = Cursors.Hand,
                Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59))
            };
            chkAutoStart.Checked += (s, e) => AutoStart.SetEnabled(true);
            chkAutoStart.Unchecked += (s, e) => AutoStart.SetEnabled(false);
            sp.Children.Add(chkAutoStart);

            // 桌面悬浮球
            chkBallVisible = new CheckBox
            {
                Content = "桌面悬浮球", IsChecked = AppConfig.BallVisible,
                FontSize = 13.5, Margin = new Thickness(0, 6, 0, 6), Cursor = Cursors.Hand,
                Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59))
            };
            chkBallVisible.Checked += (s, e) => { AppConfig.BallVisible = true; AppConfig.Save(); refreshBall(); };
            chkBallVisible.Unchecked += (s, e) => { AppConfig.BallVisible = false; AppConfig.Save(); refreshBall(); };
            sp.Children.Add(chkBallVisible);

            // 固定悬浮球
            chkBallPinned = new CheckBox
            {
                Content = "固定悬浮球（连续抽签时不被收起）",
                IsChecked = AppConfig.BallPinned,
                FontSize = 13.5, Margin = new Thickness(0, 6, 0, 14), Cursor = Cursors.Hand,
                Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59))
            };
            chkBallPinned.Checked += (s, e) => { AppConfig.BallPinned = true; AppConfig.Save(); };
            chkBallPinned.Unchecked += (s, e) => { AppConfig.BallPinned = false; AppConfig.Save(); };
            sp.Children.Add(chkBallPinned);

            // 悬浮球大小
            sp.Children.Add(MakeSubTitle("悬浮球大小"));
            int cur = AppConfig.BallDiameter;
            var sizeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 14) };
            foreach (var kv in new[] { new[] { "小（80）", "80" }, new[] { "中（96）", "96" }, new[] { "大（120）", "120" } })
            {
                string label = kv[0]; int d = int.Parse(kv[1]);
                var b = new RadioButton
                {
                    Content = label, GroupName = "BallSize",
                    IsChecked = Math.Abs(cur - d) < 9,
                    FontSize = 13, Margin = new Thickness(0, 0, 16, 0), Cursor = Cursors.Hand,
                    Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59))
                };
                int captured = d;
                b.Checked += (s, e) => { if (b.IsChecked == true) { AppConfig.BallDiameter = captured; AppConfig.Save(); refreshBall(); } };
                sizeRow.Children.Add(b);
            }
            sp.Children.Add(sizeRow);

            // 默认班级
            sp.Children.Add(MakeSubTitle("默认班级"));
            sp.Children.Add(MakeHint("下次启动时若勾选「设为默认」即直接使用该班级。"));
            var defRow = new Grid { Margin = new Thickness(0, 4, 0, 8) };
            defRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            defRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            defaultClassCombo = new ComboBox
            {
                MinWidth = 180, FontSize = 13,
                Style = (Style)FindResource("ModernCombo")
            };
            try
            {
                var classIds = App.Core.classIds;
                var classNames = App.Core.classNames;
                foreach (var id in classIds)
                {
                    string label = id + "班 · " + (classNames.ContainsKey(id) ? classNames[id] : id + "班");
                    defaultClassCombo.Items.Add(new ComboBoxItem { Content = label, Tag = id });
                }
                int sel = classIds.IndexOf(AppConfig.DefaultClassId);
                if (sel >= 0) defaultClassCombo.SelectedIndex = sel;
                else if (defaultClassCombo.Items.Count > 0) defaultClassCombo.SelectedIndex = 0;
                defaultClassCombo.SelectionChanged += (s, e) =>
                {
                    if (defaultClassCombo.SelectedItem is ComboBoxItem ci && ci.Tag is string cid)
                    {
                        AppConfig.DefaultClassId = cid; AppConfig.Save();
                        statusText.Text = "已设置默认班级：" + cid;
                    }
                };
            }
            catch { }
            Grid.SetColumn(defaultClassCombo, 0);
            defRow.Children.Add(defaultClassCombo);
            var btnChange = new Button { Content = "重新选择默认班级…", Margin = new Thickness(10, 0, 0, 0), Style = (Style)FindResource("ModernBtnGhost") };
            btnChange.Click += (s, e) => { onChangeDefaultClass(); Close(); };
            Grid.SetColumn(btnChange, 1);
            defRow.Children.Add(btnChange);
            sp.Children.Add(defRow);

            // 退出
            sp.Children.Add(new Border { Height = 12 });
            var btnQuit = new Button { Content = "退出程序", Width = 120, Style = (Style)FindResource("ModernBtnGhost"), HorizontalAlignment = HorizontalAlignment.Left };
            btnQuit.Click += (s, e) => { onQuit(); Close(); };
            sp.Children.Add(btnQuit);
        }

        void FillVoicePage(StackPanel sp)
        {
            sp.Children.Add(MakeSectionTitle("语音"));
            sp.Children.Add(MakeHint("播报引擎与音色，选择后点「保存」立即生效。"));

            sp.Children.Add(MakeSubTitle("播报引擎"));
            var srcLocal = new ComboBox { Margin = new Thickness(0, 4, 0, 12), Style = (Style)FindResource("ModernCombo") };
            void reloadSrc()
            {
                srcLocal.Items.Clear();
                srcLocal.Items.Add(new ComboBoxItem { Content = "自动（微软神经语音 → 备用）", Tag = "auto" });
                srcLocal.Items.Add(new ComboBoxItem { Content = "微软神经语音（网络）", Tag = "azure" });
                srcLocal.Items.Add(new ComboBoxItem { Content = "Edge 直连语音", Tag = "edge" });
                srcLocal.Items.Add(new ComboBoxItem { Content = "百度在线语音", Tag = "baidu" });
                srcLocal.Items.Add(new ComboBoxItem { Content = "仅本地语音", Tag = "off" });
                foreach (var it in srcLocal.Items)
                    if (it is ComboBoxItem ci && (string)ci.Tag == AppConfig.TtsSource) srcLocal.SelectedItem = ci;
                if (srcLocal.SelectedIndex < 0) srcLocal.SelectedIndex = 0;
            }
            reloadSrc();
            sp.Children.Add(srcLocal);

            sp.Children.Add(MakeSubTitle("微软音色（微软神经语音 / Edge 直连）"));
            var voiceLocal = new ComboBox { Margin = new Thickness(0, 4, 0, 8), Style = (Style)FindResource("ModernCombo") };
            var voices = new (string Name, string Id)[]
            {
                ("晓晓（女 · 温柔）", "zh-CN-XiaoxiaoNeural"),
                ("晓伊（女）", "zh-CN-XiaoyiNeural"),
                ("云希（男 · 阳光）", "zh-CN-YunxiNeural"),
                ("云扬（男 · 新闻）", "zh-CN-YunyangNeural"),
                ("云健（男 · 情感）", "zh-CN-YunjianNeural"),
                ("晓辰（女 · 儿童）", "zh-CN-XiaochenNeural"),
            };
            foreach (var (nm, id) in voices) voiceLocal.Items.Add(new ComboBoxItem { Content = nm, Tag = id });
            foreach (var it in voiceLocal.Items)
                if (it is ComboBoxItem ci && (string)ci.Tag == AppConfig.TtsVoice) voiceLocal.SelectedItem = it;
            if (voiceLocal.SelectedIndex < 0) voiceLocal.SelectedIndex = 0;
            sp.Children.Add(voiceLocal);

            voiceStatus = new TextBlock
            {
                FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                Margin = new Thickness(0, 6, 0, 14), TextWrapping = TextWrapping.Wrap
            };
            sp.Children.Add(voiceStatus);

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal };
            var btnTest = new Button { Content = "试听", Width = 90, Margin = new Thickness(0, 0, 10, 0), Style = (Style)FindResource("ModernBtn") };
            var btnApply = new Button { Content = "保存", Width = 90, Margin = new Thickness(0, 0, 10, 0), Style = (Style)FindResource("ModernBtn") };
            btnTest.Click += (s, e) => { onApplyVoice?.Invoke(); statusText.Text = "已试听：" + (voiceLocal.SelectedItem is ComboBoxItem vci ? vci.Content : ""); };
            btnApply.Click += (s, e) => { onApplyVoice?.Invoke(); statusText.Text = "✓ 语音设置已保存并立即生效"; };
            btnRow.Children.Add(btnTest); btnRow.Children.Add(btnApply);
            sp.Children.Add(btnRow);

            sp.Children.Add(new TextBlock
            {
                Text = "提示：网络引擎需要联网；无网络时自动使用本地语音。",
                FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                Margin = new Thickness(0, 14, 0, 0), TextWrapping = TextWrapping.Wrap
            });
        }

        void FillRosterPage(StackPanel sp)
        {
            sp.Children.Add(MakeSectionTitle("名单管理"));
            sp.Children.Add(MakeHint("所有学生数据保存在本机（%ProgramData%\\LuckyPicker）。"));
            var pathText = new TextBlock
            {
                Text = "数据文件：" + LuckyCore.DataPath(),
                FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
                Margin = new Thickness(0, 6, 0, 16), TextWrapping = TextWrapping.Wrap
            };
            sp.Children.Add(pathText);

            string statusTxt = "";
            var statusLabel = new TextBlock
            {
                FontSize = 12.5, Foreground = new SolidColorBrush(Color.FromRgb(6, 95, 70)),
                Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap
            };
            void SetStatus(string msg, bool err = false)
            {
                statusTxt = msg;
                statusLabel.Text = msg;
                statusLabel.Foreground = new SolidColorBrush(err ? Color.FromRgb(220, 38, 38) : Color.FromRgb(6, 95, 70));
            }

            void AddBtn(string text, Action onClick, bool primary = true)
            {
                var b = new Button { Content = text, Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Stretch, Style = (Style)FindResource(primary ? "ModernBtn" : "ModernBtnGhost") };
                b.Click += (s, e) => onClick();
                sp.Children.Add(b);
            }

            // 直接走原 dialog 方法（编辑表格 / 导入 / 导出 / 打开目录）—— 把状态写回 statusLabel
            AddBtn("编辑名单（表格直接增删改）", () => { Close(); mainWin.Dispatcher.BeginInvoke(() => mainWin.GetType().GetMethod("ShowEditorDialog", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(mainWin, null)); }, true);
            AddBtn("导入 Excel / CSV / JSON 名单", () => { Close(); mainWin.Dispatcher.BeginInvoke(() => mainWin.GetType().GetMethod("ShowEditorDialog", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(mainWin, null)); });
            AddBtn("导出备份（复制名单文件到桌面）", () =>
            {
                try
                {
                    string dst = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                        "LuckyPicker名单备份_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".json");
                    Directory.CreateDirectory(LuckyCore.DataDir());
                    File.Copy(LuckyCore.DataPath(), dst, true);
                    SetStatus("已导出到：" + dst);
                }
                catch (Exception ex) { SetStatus("导出失败：" + ex.Message, true); }
            }, false);
            AddBtn("打开名单所在文件夹", () =>
            {
                try { Directory.CreateDirectory(LuckyCore.DataDir()); System.Diagnostics.Process.Start("explorer.exe", LuckyCore.DataDir()); }
                catch { }
            }, false);

            sp.Children.Add(statusLabel);
        }

        void FillHistoryPage(StackPanel sp)
        {
            sp.Children.Add(MakeSectionTitle("抽选记录"));
            sp.Children.Add(MakeHint("最近 200 条抽取记录，含时间与结果。"));
            historyList = new ListBox { FontSize = 12, Height = 260, Margin = new Thickness(0, 10, 0, 12), BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)), BorderThickness = new Thickness(1) };
            ReloadHistory();
            sp.Children.Add(historyList);

            historyEmpty = new TextBlock { FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)), Margin = new Thickness(0, 0, 0, 8) };
            sp.Children.Add(historyEmpty);

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var btnReload = new Button { Content = "刷新", Width = 90, Margin = new Thickness(0, 0, 10, 0), Style = (Style)FindResource("ModernBtnGhost") };
            btnReload.Click += (s, e) => ReloadHistory();
            var btnClear = new Button { Content = "清空记录", Width = 110, Margin = new Thickness(0, 0, 10, 0), Style = (Style)FindResource("ModernBtnGhost") };
            btnClear.Click += (s, e) => { HistoryStore.Clear(); ReloadHistory(); statusText.Text = "✓ 已清空抽选记录"; };
            var btnOpen = new Button { Content = "完整窗口…", Width = 110, Style = (Style)FindResource("ModernBtn") };
            btnOpen.Click += (s, e) => { Close(); showHistory(); };
            row.Children.Add(btnReload); row.Children.Add(btnClear); row.Children.Add(btnOpen);
            sp.Children.Add(row);
        }

        void ReloadHistory()
        {
            var items = HistoryStore.Load().Select(h => h.time + "  " + h.text).ToList();
            if (items.Count == 0)
            {
                historyList.ItemsSource = new List<string> { "（暂无记录）" };
                if (historyEmpty != null) historyEmpty.Text = "共 0 条记录";
            }
            else
            {
                historyList.ItemsSource = items;
                if (historyEmpty != null) historyEmpty.Text = "共 " + items.Count + " 条记录（仅展示最近 200 条）";
            }
        }

        void FillAboutPage(StackPanel sp)
        {
            sp.Children.Add(MakeSectionTitle("关于 · 更新"));
            sp.Children.Add(MakeHint("版本信息与检查更新。"));

            var info = new TextBlock
            {
                FontSize = 13, TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                LineHeight = 22, Margin = new Thickness(0, 6, 0, 14)
            };
            info.Inlines.Add(new System.Windows.Documents.Run("当前版本：") { FontWeight = FontWeights.SemiBold });
            info.Inlines.Add(new System.Windows.Documents.Run(AppVersion.Display + "\n"));
            info.Inlines.Add(new System.Windows.Documents.Run("更新通道：") { FontWeight = FontWeights.SemiBold });
            info.Inlines.Add(new System.Windows.Documents.Run(AppVersion.Channel + "\n"));
            info.Inlines.Add(new System.Windows.Documents.Run("产品名称：") { FontWeight = FontWeights.SemiBold });
            info.Inlines.Add(new System.Windows.Documents.Run(AppVersion.ProductName + "\n"));
            info.Inlines.Add(new System.Windows.Documents.Run("更新地址：") { FontWeight = FontWeights.SemiBold });
            info.Inlines.Add(new System.Windows.Documents.Run(AppConfig.UpdateUrl));
            sp.Children.Add(info);

            updateResultText = new TextBlock
            {
                FontSize = 12.5, TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
                Margin = new Thickness(0, 4, 0, 14), MinHeight = 50
            };
            sp.Children.Add(updateResultText);

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var btnCheck = new Button { Content = "检查更新", Width = 110, Margin = new Thickness(0, 0, 10, 0), Style = (Style)FindResource("ModernBtn") };
            btnCheck.Click += async (s, e) =>
            {
                btnCheck.IsEnabled = false;
                updateResultText.Text = "正在检查更新…";
                updateResultText.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
                try
                {
                    var res = await checkUpdateAsync?.Invoke(AppConfig.UpdateUrl);
                    if (res == null || !res.Success)
                    {
                        updateResultText.Text = "检查失败：" + (res?.ErrorMessage ?? "未知错误");
                        updateResultText.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                        return;
                    }
                    string txt = "远程版本：" + res.LatestVersion;
                    if (!string.IsNullOrEmpty(res.ReleaseDate)) txt += "\n发布日期：" + res.ReleaseDate;
                    if (!string.IsNullOrEmpty(res.Notes)) txt += "\n更新说明：\n" + res.Notes;
                    txt += "\n\n" + (res.HasUpdate ? "★ 发现新版本！点击下方按钮下载。" : "✓ 当前已是最新版本");
                    updateResultText.Text = txt;
                    updateResultText.Foreground = new SolidColorBrush(Color.FromRgb(51, 65, 85));
                    if (res.HasUpdate)
                    {
                        var ask = MessageBox.Show(this, "发现新版本 " + res.LatestVersion + "\n\n是否现在下载安装程序？",
                            "发现新版本", MessageBoxButton.YesNo, MessageBoxImage.Information);
                        if (ask == MessageBoxResult.Yes) showUpdateDownload?.Invoke(res);
                    }
                }
                catch (Exception ex)
                {
                    updateResultText.Text = "检查出错：" + ex.Message;
                    updateResultText.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                }
                finally { btnCheck.IsEnabled = true; }
            };
            row.Children.Add(btnCheck);
            sp.Children.Add(row);
        }

        // ==================== 辅助 ====================

        TextBlock MakeSectionTitle(string text) => new TextBlock
        {
            Text = text, FontSize = 17, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
            Margin = new Thickness(0, 0, 0, 4)
        };
        TextBlock MakeSubTitle(string text) => new TextBlock
        {
            Text = text, FontSize = 13, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
            Margin = new Thickness(0, 14, 0, 4)
        };
        TextBlock MakeHint(string text) => new TextBlock
        {
            Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
            Margin = new Thickness(0, 0, 0, 4)
        };
    }
}