// ================================================================
// SettingsWindow.cs — 统一设置窗口
//
// 左侧分类导航（圆角选中态）+ 右侧内容区，类似 Windows 11 设置面板。
// 名单管理为**内嵌**编辑器（不弹独立窗口）；关闭窗口时一并关闭其子窗口。
// ================================================================
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace LuckyPickerWpf
{
    public class SettingsWindow : Window
    {
        // -------- 宿主回调 --------
        readonly Action applyVoice;                        // 语音引擎/音色变化 → 重建 TTS
        readonly Action ballChanged;                       // 悬浮球相关配置变化 → 立即应用
        readonly Action rosterSaved;                       // 名单写入后 → 主窗刷新
        readonly Func<string, DataFile?> parseRoster;      // 解析导入文件
        readonly Action<List<string>> askPrefetch;         // 询问/执行语音缓存预生成
        readonly Action showHistory;                       // 打开抽选记录（Owner = 本窗口）
        readonly Action<UpdateResult> showUpdateDownload;  // 下载安装器（Owner = 本窗口）
        readonly Action<string> speak;                     // 试听（用主窗 TTS 引擎）
        readonly Action onChangeDefaultClass;
        readonly Action onQuit;

        // -------- 导航 / 通用 --------
        TreeView treeNav;
        Grid? pageHost;
        TextBlock statusText;
        ComboBox defaultClassCombo;
        CheckBox chkAutoStart, chkBallVisible, chkBallPinned;
        bool loading = true;
        const int IDX_GENERAL = 0, IDX_VOICE = 1, IDX_ROSTER = 2, IDX_HISTORY = 3, IDX_ABOUT = 4;
        readonly List<StackPanel> pages = new();

        // -------- 名单页 --------
        ObservableCollection<Student> rosterRows = new();
        DataGrid rosterGrid;
        TextBlock rosterStatus;

        // -------- 记录 / 关于 --------
        ListBox historyList;
        TextBlock historyEmpty;
        TextBlock updateResultText;

        public SettingsWindow(Window owner,
            Action applyVoice, Action ballChanged, Action rosterSaved,
            Func<string, DataFile?> parseRoster, Action<List<string>> askPrefetch,
            Action showHistory, Action<UpdateResult> showUpdateDownload, Action<string> speak,
            Action changeDefaultClass, Action onQuit)
        {
            this.applyVoice = applyVoice;
            this.ballChanged = ballChanged;
            this.rosterSaved = rosterSaved;
            this.parseRoster = parseRoster;
            this.askPrefetch = askPrefetch;
            this.showHistory = showHistory;
            this.showUpdateDownload = showUpdateDownload;
            this.speak = speak;
            onChangeDefaultClass = changeDefaultClass;
            this.onQuit = onQuit;

            Icon = owner.Icon;
            Title = "设置 · " + AppVersion.ProductName;
            Width = 760; Height = 580;
            MinWidth = 640; MinHeight = 460;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Owner = owner;
            Background = new SolidColorBrush(Color.FromRgb(248, 250, 252));
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            BuildLayout();
            loading = false;
        }

        // ==================== 布局 ====================

        void BuildLayout()
        {
            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(186) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var navBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(0, 0, 1, 0),
                Child = BuildNavPanel()
            };
            Grid.SetColumn(navBorder, 0);
            root.Children.Add(navBorder);

            var content = new Grid();
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            BuildAllPages(content);

            // 底部状态栏 + 关闭
            var bottom = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Background = Brushes.White,
                Padding = new Thickness(20, 10, 20, 10)
            };
            var bottomRow = new Grid();
            bottomRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottomRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            statusText = new TextBlock
            {
                Text = "就绪", FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(statusText, 0);
            bottomRow.Children.Add(statusText);
            var btnClose = new Button { Content = "关闭", Width = 100, Style = (Style)FindResource("ModernBtn") };
            btnClose.Click += (s, e) => Close();
            Grid.SetColumn(btnClose, 1);
            bottomRow.Children.Add(btnClose);
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
            panel.Children.Add(new TextBlock
            {
                Text = "YBH 幸运摇人器", FontSize = 14, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(30, 64, 175)),
                Margin = new Thickness(22, 0, 20, 2)
            });
            panel.Children.Add(new TextBlock
            {
                Text = AppVersion.Display, FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                Margin = new Thickness(22, 0, 20, 0)
            });

            treeNav = new TreeView
            {
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Margin = new Thickness(12, 12, 12, 0),
                ItemContainerStyle = BuildNavItemStyle()
            };

            string[] titles = { "常规", "语音", "名单管理", "抽选记录", "关于 · 更新" };
            for (int i = 0; i < titles.Length; i++)
            {
                var item = new TreeViewItem { Header = titles[i], Tag = i };
                treeNav.Items.Add(item);
            }
            treeNav.SelectedItemChanged += (s, e) =>
            {
                if (treeNav.SelectedItem is TreeViewItem t && t.Tag is int idx) ShowPage(idx);
            };
            panel.Children.Add(treeNav);
            return panel;
        }

        /// <summary>
        /// 左栏导航项样式：自定义模板 + 圆角 Border（默认 TreeViewItem 模板为直角，
        /// 并且选中高亮来自系统主题色，无法直接改成圆角 —— 这是"选中项没有圆角"的根因）。
        /// </summary>
        static Style BuildNavItemStyle()
        {
            var style = new Style(typeof(TreeViewItem));

            var tpl = new ControlTemplate(typeof(TreeViewItem));
            var bd = new FrameworkElementFactory(typeof(Border));
            bd.Name = "Bd";
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
            bd.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(TreeViewItem.BackgroundProperty));
            bd.SetValue(Border.PaddingProperty, new Thickness(14, 9, 14, 9));
            bd.SetValue(Border.MarginProperty, new Thickness(0, 2, 0, 2));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.ContentSourceProperty, "Header");
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            bd.AppendChild(cp);
            tpl.VisualTree = bd;

            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(226, 232, 240)), "Bd"));
            tpl.Triggers.Add(hover);

            var selected = new Trigger { Property = TreeViewItem.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(38, 37, 99, 235)), "Bd"));
            selected.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(29, 78, 216))));
            selected.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
            tpl.Triggers.Add(selected);

            style.Setters.Add(new Setter(TreeViewItem.TemplateProperty, tpl));
            style.Setters.Add(new Setter(TreeViewItem.FontSizeProperty, 13.5));
            style.Setters.Add(new Setter(TreeViewItem.CursorProperty, Cursors.Hand));
            style.Setters.Add(new Setter(TreeViewItem.ForegroundProperty, new SolidColorBrush(Color.FromRgb(30, 41, 59))));
            style.Setters.Add(new Setter(TreeViewItem.FocusVisualStyleProperty, null));
            style.Setters.Add(new Setter(TreeViewItem.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            return style;
        }

        void BuildAllPages(Grid parent)
        {
            pageHost = new Grid();
            for (int i = 0; i < 5; i++)
            {
                var page = new StackPanel { Margin = new Thickness(26, 22, 26, 16) };
                pages.Add(page);
                pageHost.Children.Add(new ScrollViewer
                {
                    Content = page,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Visibility = Visibility.Collapsed,
                    Padding = new Thickness(0)
                });
            }
            FillGeneralPage(pages[IDX_GENERAL]);
            FillVoicePage(pages[IDX_VOICE]);
            FillRosterPage(pages[IDX_ROSTER]);
            FillHistoryPage(pages[IDX_HISTORY]);
            FillAboutPage(pages[IDX_ABOUT]);

            Grid.SetRow(pageHost, 0);
            parent.Children.Add(pageHost);

            ShowPage(0);
            if (treeNav.Items.Count > 0) ((TreeViewItem)treeNav.Items[0]).IsSelected = true;
        }

        void ShowPage(int idx)
        {
            if (pageHost == null) return;
            for (int i = 0; i < pageHost.Children.Count; i++)
                pageHost.Children[i].Visibility = (i == idx) ? Visibility.Visible : Visibility.Collapsed;
        }

        // ==================== 常规 ====================

        void FillGeneralPage(StackPanel sp)
        {
            sp.Children.Add(MakeSectionTitle("常规"));
            sp.Children.Add(MakeHint("基础行为、悬浮球与默认班级。"));

            chkAutoStart = new CheckBox
            {
                Content = "开机自启动", IsChecked = AutoStart.IsEnabled(),
                FontSize = 13.5, Margin = new Thickness(0, 12, 0, 6), Cursor = Cursors.Hand,
                Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59))
            };
            chkAutoStart.Checked += (s, e) => { if (!loading) { AutoStart.SetEnabled(true); statusText.Text = "✓ 已设为开机自启动"; } };
            chkAutoStart.Unchecked += (s, e) => { if (!loading) { AutoStart.SetEnabled(false); statusText.Text = "已取消开机自启动"; } };
            sp.Children.Add(chkAutoStart);

            chkBallVisible = new CheckBox
            {
                Content = "桌面悬浮球", IsChecked = AppConfig.BallVisible,
                FontSize = 13.5, Margin = new Thickness(0, 6, 0, 6), Cursor = Cursors.Hand,
                Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59))
            };
            chkBallVisible.Checked += (s, e) => { if (loading) return; AppConfig.BallVisible = true; AppConfig.Save(); ballChanged?.Invoke(); statusText.Text = "✓ 已显示桌面悬浮球"; };
            chkBallVisible.Unchecked += (s, e) => { if (loading) return; AppConfig.BallVisible = false; AppConfig.Save(); ballChanged?.Invoke(); statusText.Text = "已隐藏桌面悬浮球"; };
            sp.Children.Add(chkBallVisible);

            chkBallPinned = new CheckBox
            {
                Content = "固定悬浮球（始终活跃，连续抽签不收起）",
                IsChecked = AppConfig.BallPinned,
                FontSize = 13.5, Margin = new Thickness(0, 6, 0, 14), Cursor = Cursors.Hand,
                Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59))
            };
            chkBallPinned.Checked += (s, e) => { if (loading) return; AppConfig.BallPinned = true; AppConfig.Save(); ballChanged?.Invoke(); BallQuickPanel.RefreshPinState(true); statusText.Text = "✓ 悬浮球已固定（保持活跃）"; };
            chkBallPinned.Unchecked += (s, e) => { if (loading) return; AppConfig.BallPinned = false; AppConfig.Save(); ballChanged?.Invoke(); BallQuickPanel.RefreshPinState(false); statusText.Text = "已取消固定悬浮球"; };
            sp.Children.Add(chkBallPinned);

            // 悬浮球大小 —— 选中即时应用到球并持久化
            sp.Children.Add(MakeSubTitle("悬浮球大小"));
            int cur = AppConfig.BallDiameter;
            var sizeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
            foreach (var kv in new[] { new[] { "小（80）", "80" }, new[] { "中（96）", "96" }, new[] { "大（120）", "120" } })
            {
                string label = kv[0];
                int d = int.Parse(kv[1]);
                var b = new RadioButton
                {
                    Content = label, GroupName = "BallSize_" + Guid.NewGuid().ToString("N"),
                    IsChecked = Math.Abs(cur - d) < 9,
                    FontSize = 13, Margin = new Thickness(0, 0, 18, 0), Cursor = Cursors.Hand,
                    Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59))
                };
                int captured = d;
                b.Checked += (s, e) =>
                {
                    if (loading || b.IsChecked != true) return;
                    AppConfig.BallDiameter = captured;
                    AppConfig.Save();            // 持久化
                    ballChanged?.Invoke();       // 立即应用到悬浮球
                    statusText.Text = "✓ 悬浮球大小已更新为 " + captured + "（已保存）";
                };
                sizeRow.Children.Add(b);
            }
            sp.Children.Add(sizeRow);
            sp.Children.Add(new TextBlock
            {
                Text = "调整后立即生效，并写入配置文件，重启后保持。",
                FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap
            });

            // 默认班级
            sp.Children.Add(MakeSubTitle("默认班级"));
            sp.Children.Add(MakeHint("选定后启动时不再询问班级。"));
            var defRow = new Grid { Margin = new Thickness(0, 4, 0, 10) };
            defRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            defRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            defaultClassCombo = new ComboBox { MinWidth = 180, FontSize = 13, Style = (Style)FindResource("ModernCombo") };
            try
            {
                foreach (var id in App.Core.classIds)
                {
                    string label = App.Core.ClassName(id);
                    defaultClassCombo.Items.Add(new ComboBoxItem { Content = label, Tag = id });
                }
                int sel = App.Core.classIds.IndexOf(AppConfig.DefaultClassId);
                if (sel >= 0) defaultClassCombo.SelectedIndex = sel;
                else if (defaultClassCombo.Items.Count > 0) defaultClassCombo.SelectedIndex = 0;
                defaultClassCombo.SelectionChanged += (s, e) =>
                {
                    if (loading) return;
                    if (defaultClassCombo.SelectedItem is ComboBoxItem ci && ci.Tag is string cid)
                    {
                        AppConfig.DefaultClassId = cid; AppConfig.Save();
                        statusText.Text = "✓ 默认班级已设为 " + App.Core.ClassName(cid);
                    }
                };
            }
            catch { }
            Grid.SetColumn(defaultClassCombo, 0);
            defRow.Children.Add(defaultClassCombo);
            var btnChange = new Button { Content = "重新选择…", Margin = new Thickness(10, 0, 0, 0), Style = (Style)FindResource("ModernBtnGhost") };
            btnChange.Click += (s, e) => { onChangeDefaultClass(); Close(); };
            Grid.SetColumn(btnChange, 1);
            defRow.Children.Add(btnChange);
            sp.Children.Add(defRow);

            sp.Children.Add(MakeSubTitle("其他"));
            var btnQuit = new Button { Content = "退出程序", Width = 130, Style = (Style)FindResource("ModernBtnGhost"), HorizontalAlignment = HorizontalAlignment.Left };
            btnQuit.Click += (s, e) => { onQuit(); Close(); };
            sp.Children.Add(btnQuit);
        }

        // ==================== 语音 ====================

        void FillVoicePage(StackPanel sp)
        {
            sp.Children.Add(MakeSectionTitle("语音"));
            sp.Children.Add(MakeHint("播报引擎与音色；切换后点「保存」立即生效。"));

            sp.Children.Add(MakeSubTitle("播报引擎"));
            var srcCombo = new ComboBox { Margin = new Thickness(0, 4, 0, 10), Style = (Style)FindResource("ModernCombo") };
            srcCombo.Items.Add(new ComboBoxItem { Content = "自动（微软神经语音 → 备用）", Tag = "auto" });
            srcCombo.Items.Add(new ComboBoxItem { Content = "微软神经语音（网络）", Tag = "azure" });
            srcCombo.Items.Add(new ComboBoxItem { Content = "Edge 直连语音", Tag = "edge" });
            srcCombo.Items.Add(new ComboBoxItem { Content = "百度在线语音", Tag = "baidu" });
            srcCombo.Items.Add(new ComboBoxItem { Content = "仅本地语音", Tag = "off" });
            foreach (var it in srcCombo.Items)
                if (it is ComboBoxItem ci && (string)ci.Tag == AppConfig.TtsSource) srcCombo.SelectedItem = ci;
            if (srcCombo.SelectedIndex < 0) srcCombo.SelectedIndex = 0;
            sp.Children.Add(srcCombo);

            sp.Children.Add(MakeSubTitle("微软音色（微软神经语音 / Edge 直连）"));
            var voiceCombo = new ComboBox { Margin = new Thickness(0, 4, 0, 10), Style = (Style)FindResource("ModernCombo") };
            var voices = new (string Name, string Id)[]
            {
                ("晓晓（女 · 温柔）", "zh-CN-XiaoxiaoNeural"),
                ("晓伊（女）", "zh-CN-XiaoyiNeural"),
                ("云希（男 · 阳光）", "zh-CN-YunxiNeural"),
                ("云扬（男 · 新闻）", "zh-CN-YunyangNeural"),
                ("云健（男 · 情感）", "zh-CN-YunjianNeural"),
                ("晓辰（女 · 儿童）", "zh-CN-XiaochenNeural"),
            };
            foreach (var (nm, id) in voices) voiceCombo.Items.Add(new ComboBoxItem { Content = nm, Tag = id });
            foreach (var it in voiceCombo.Items)
                if (it is ComboBoxItem ci && (string)ci.Tag == AppConfig.TtsVoice) voiceCombo.SelectedItem = it;
            if (voiceCombo.SelectedIndex < 0) voiceCombo.SelectedIndex = 0;
            sp.Children.Add(voiceCombo);

            void Commit()
            {
                if (srcCombo.SelectedItem is ComboBoxItem s && s.Tag is string st) AppConfig.TtsSource = st;
                if (voiceCombo.SelectedItem is ComboBoxItem v && v.Tag is string vt) AppConfig.TtsVoice = vt;
                AppConfig.Save();
                applyVoice?.Invoke();
            }

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            var btnTest = new Button { Content = "试听", Width = 96, Margin = new Thickness(0, 0, 10, 0), Style = (Style)FindResource("ModernBtn") };
            var btnSave = new Button { Content = "保存", Width = 96, Margin = new Thickness(0, 0, 10, 0), Style = (Style)FindResource("ModernBtn") };
            btnTest.Click += (s, e) =>
            {
                Commit();
                speak?.Invoke("欢迎使用 YBH 幸运摇人器");
                statusText.Text = "已按当前音色试听：" + (voiceCombo.SelectedItem is ComboBoxItem vci ? vci.Content : "");
            };
            btnSave.Click += (s, e) => { Commit(); statusText.Text = "✓ 语音设置已保存并立即生效"; };
            row.Children.Add(btnTest); row.Children.Add(btnSave);
            sp.Children.Add(row);

            sp.Children.Add(new TextBlock
            {
                Text = "提示：网络引擎需要联网；无网络时自动使用本地语音。",
                FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                Margin = new Thickness(0, 14, 0, 0), TextWrapping = TextWrapping.Wrap
            });
        }

        // ==================== 名单（内嵌编辑器） ====================

        void FillRosterPage(StackPanel sp)
        {
            sp.Children.Add(MakeSectionTitle("名单管理"));
            sp.Children.Add(MakeHint("直接在下表中增删改，点「保存名单」写入本机。双击单元格编辑；最后一行可新增。"));

            rosterRows = new ObservableCollection<Student>(
                App.Core.allStudents.Select(x => new Student { name = x.name, classId = x.classId, gender = x.gender }));

            rosterGrid = new DataGrid
            {
                AutoGenerateColumns = false, CanUserAddRows = true, CanUserDeleteRows = true,
                ItemsSource = rosterRows,
                HeadersVisibility = DataGridHeadersVisibility.Column, RowHeaderWidth = 0,
                Height = 250, FontSize = 13, RowHeight = 32,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                HorizontalGridLinesBrush = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
                Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(1),
                RowBackground = Brushes.White,
                AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(250, 251, 253)),
                UseLayoutRounding = true,
                Margin = new Thickness(0, 10, 0, 8)
            };
            rosterGrid.Columns.Add(new DataGridTextColumn
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
                Width = new DataGridLength(170)
            };
            classCol.ItemsSource = App.Core.classIds
                .Select(id => new KeyValuePair<string, string>(id, App.Core.ClassName(id))).ToList();
            rosterGrid.Columns.Add(classCol);
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
            rosterGrid.Columns.Add(genderCol);
            sp.Children.Add(rosterGrid);

            var editRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var btnAdd = new Button { Content = "添加一行", Width = 104, Margin = new Thickness(0, 0, 8, 0), Style = (Style)FindResource("ModernBtnGhost") };
            btnAdd.Click += (s, e) =>
            {
                rosterRows.Add(new Student { name = "", classId = App.Core.currentClassId, gender = "" });
                rosterGrid.SelectedIndex = rosterRows.Count - 1;
                rosterGrid.ScrollIntoView(rosterGrid.SelectedItem);
            };
            var btnDel = new Button { Content = "删除选中行", Width = 110, Margin = new Thickness(0, 0, 8, 0), Style = (Style)FindResource("ModernBtnGhost") };
            btnDel.Click += (s, e) =>
            {
                if (rosterGrid.SelectedItem is Student st) rosterRows.Remove(st);
                else SetRosterStatus("请先选中一行再删除。", true);
            };
            var btnReload = new Button { Content = "放弃修改并重载", Width = 140, Style = (Style)FindResource("ModernBtnGhost") };
            btnReload.Click += (s, e) => { ReloadRosterRows(); SetRosterStatus("已从磁盘重新载入名单。"); };
            editRow.Children.Add(btnAdd); editRow.Children.Add(btnDel); editRow.Children.Add(btnReload);
            sp.Children.Add(editRow);

            var fileRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var btnSave = new Button { Content = "保存名单", Width = 110, Margin = new Thickness(0, 0, 8, 0), Style = (Style)FindResource("ModernBtn") };
            var btnImport = new Button { Content = "导入 Excel / CSV / JSON", Width = 180, Margin = new Thickness(0, 0, 8, 0), Style = (Style)FindResource("ModernBtnGhost") };
            var btnExport = new Button { Content = "导出备份", Width = 104, Margin = new Thickness(0, 0, 8, 0), Style = (Style)FindResource("ModernBtnGhost") };
            var btnDir = new Button { Content = "打开文件夹", Width = 110, Style = (Style)FindResource("ModernBtnGhost") };
            btnSave.Click += (s, e) => SaveRosterInline();
            btnImport.Click += (s, e) => ImportRosterInline();
            btnExport.Click += (s, e) => ExportRosterInline();
            btnDir.Click += (s, e) =>
            {
                try { Directory.CreateDirectory(LuckyCore.DataDir()); System.Diagnostics.Process.Start("explorer.exe", LuckyCore.DataDir()); }
                catch { }
            };
            fileRow.Children.Add(btnSave); fileRow.Children.Add(btnImport);
            fileRow.Children.Add(btnExport); fileRow.Children.Add(btnDir);
            sp.Children.Add(fileRow);

            rosterStatus = new TextBlock
            {
                FontSize = 12, TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(6, 95, 70)),
                Margin = new Thickness(0, 2, 0, 0)
            };
            sp.Children.Add(rosterStatus);
            SetRosterStatus("数据文件：" + LuckyCore.DataPath() + "（共 " + rosterRows.Count + " 名学生）");
        }

        void SetRosterStatus(string msg, bool err = false)
        {
            if (rosterStatus == null) return;
            rosterStatus.Text = msg;
            rosterStatus.Foreground = new SolidColorBrush(err ? Color.FromRgb(220, 38, 38) : Color.FromRgb(6, 95, 70));
        }

        void ReloadRosterRows()
        {
            rosterRows.Clear();
            foreach (var s in App.Core.allStudents)
                rosterRows.Add(new Student { name = s.name, classId = s.classId, gender = s.gender });
        }

        void SaveRosterInline()
        {
            try
            {
                var list = rosterRows.Where(x => !string.IsNullOrWhiteSpace(x.name)).ToList();
                var classes = new Dictionary<string, string>(App.Core.classNames);
                foreach (var s in list)
                    if (!classes.ContainsKey(s.classId)) classes[s.classId] = s.classId + "班";
                Directory.CreateDirectory(LuckyCore.DataDir());
                File.WriteAllText(LuckyCore.DataPath(),
                    JsonSerializer.Serialize(new DataFile { classes = classes, students = list }));
                rosterSaved?.Invoke();
                SetRosterStatus("✓ 已保存 " + list.Count + " 名学生到 " + LuckyCore.DataPath());
                statusText.Text = "✓ 名单已保存（" + list.Count + " 名学生）";
            }
            catch (Exception ex) { SetRosterStatus("保存失败：" + ex.Message, true); }
        }

        void ImportRosterInline()
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "名单文件|*.xlsx;*.csv;*.json|Excel|*.xlsx|CSV|*.csv|JSON 备份|*.json"
            };
            if (ofd.ShowDialog(this) != true) return;
            try
            {
                var data = parseRoster?.Invoke(ofd.FileName);
                if (data == null || data.students.Count == 0)
                {
                    SetRosterStatus("未解析出学生，请检查文件格式（需含「姓名」列）。", true);
                    return;
                }
                Directory.CreateDirectory(LuckyCore.DataDir());
                File.WriteAllText(LuckyCore.DataPath(),
                    JsonSerializer.Serialize(new DataFile { classes = data.classes, students = data.students }));
                rosterSaved?.Invoke();
                ReloadRosterRows();
                SetRosterStatus("✓ 已导入 " + data.students.Count + " 名学生并保存。");
                statusText.Text = "✓ 已导入 " + data.students.Count + " 名学生";
                askPrefetch?.Invoke(data.students.Select(x => x.name).Distinct().ToList());
            }
            catch (Exception ex) { SetRosterStatus("导入失败：" + ex.Message, true); }
        }

        void ExportRosterInline()
        {
            try
            {
                string dst = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    "LuckyPicker名单备份_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".json");
                Directory.CreateDirectory(LuckyCore.DataDir());
                File.Copy(LuckyCore.DataPath(), dst, true);
                SetRosterStatus("已导出到：" + dst);
            }
            catch (Exception ex) { SetRosterStatus("导出失败：" + ex.Message, true); }
        }

        // ==================== 抽选记录 ====================

        void FillHistoryPage(StackPanel sp)
        {
            sp.Children.Add(MakeSectionTitle("抽选记录"));
            sp.Children.Add(MakeHint("最近 200 条抽取记录（含时间与结果）。"));

            historyList = new ListBox
            {
                FontSize = 12, Height = 300, Margin = new Thickness(0, 10, 0, 10),
                BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(1),
                UseLayoutRounding = true
            };
            ReloadHistory();
            sp.Children.Add(historyList);

            historyEmpty = new TextBlock
            {
                FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                Margin = new Thickness(0, 0, 0, 10)
            };
            sp.Children.Add(historyEmpty);

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var btnReload = new Button { Content = "刷新", Width = 96, Margin = new Thickness(0, 0, 10, 0), Style = (Style)FindResource("ModernBtnGhost") };
            btnReload.Click += (s, e) => { ReloadHistory(); statusText.Text = "记录已刷新"; };
            var btnClear = new Button { Content = "清空记录", Width = 110, Margin = new Thickness(0, 0, 10, 0), Style = (Style)FindResource("ModernBtnGhost") };
            btnClear.Click += (s, e) => { HistoryStore.Clear(); ReloadHistory(); statusText.Text = "✓ 已清空抽选记录"; };
            var btnOpen = new Button { Content = "在独立窗口查看…", Width = 160, Style = (Style)FindResource("ModernBtn") };
            btnOpen.Click += (s, e) => showHistory?.Invoke();
            row.Children.Add(btnReload); row.Children.Add(btnClear); row.Children.Add(btnOpen);
            sp.Children.Add(row);
        }

        void ReloadHistory()
        {
            if (historyList == null) return;
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

        // ==================== 关于 · 更新 ====================

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
                Margin = new Thickness(0, 4, 0, 14), MinHeight = 54
            };
            sp.Children.Add(updateResultText);

            var btnCheck = new Button { Content = "检查更新", Width = 120, HorizontalAlignment = HorizontalAlignment.Left, Style = (Style)FindResource("ModernBtn") };
            btnCheck.Click += async (s, e) =>
            {
                btnCheck.IsEnabled = false;
                updateResultText.Text = "正在检查更新…";
                updateResultText.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
                try
                {
                    var res = await Task.Run(() => UpdateManager.Check(AppConfig.UpdateUrl));
                    if ((res == null || !res.Success) &&
                        string.Equals(AppConfig.UpdateUrl, AppVersion.UpdateUrlDefault, StringComparison.OrdinalIgnoreCase))
                    {
                        var alt = await Task.Run(() => UpdateManager.Check(AppVersion.UpdateUrlLegacy));
                        if (alt != null && alt.Success) res = alt;
                    }
                    if (res == null || !res.Success)
                    {
                        updateResultText.Text = "检查失败：" + (res?.ErrorMessage ?? "未知错误");
                        updateResultText.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                        return;
                    }
                    string txt = "远程版本：" + res.LatestVersion;
                    if (!string.IsNullOrEmpty(res.ReleaseDate)) txt += "\n发布日期：" + res.ReleaseDate;
                    if (!string.IsNullOrEmpty(res.Notes)) txt += "\n更新说明：\n" + res.Notes;
                    txt += "\n\n" + (res.HasUpdate ? "★ 发现新版本！" : "✓ 当前已是最新版本");
                    updateResultText.Text = txt;
                    updateResultText.Foreground = new SolidColorBrush(Color.FromRgb(51, 65, 85));
                    if (res.HasUpdate)
                    {
                        var ask = MessageBox.Show(this,
                            "发现新版本 " + res.LatestVersion + "\n\n是否现在下载安装程序？",
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
            sp.Children.Add(btnCheck);
        }

        // ==================== 关闭时一并关闭子窗口 ====================

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            try
            {
                foreach (var w in System.Windows.Application.Current.Windows.OfType<Window>().ToList())
                {
                    if (ReferenceEquals(w, this)) continue;
                    if (ReferenceEquals(w.Owner, this))
                    {
                        try { w.Close(); } catch { }
                    }
                }
            }
            catch { }
        }

        // ==================== 自检钩子 ====================

        internal int PageCountForTest => pages.Count;
        internal void ShowPageForTest(int idx) => ShowPage(idx);
        internal bool HasInlineRosterForTest
        {
            get
            {
                if (rosterGrid == null) return false;
                DependencyObject? d = rosterGrid;
                while (d != null)
                {
                    if (ReferenceEquals(d, this)) return true;
                    d = LogicalTreeHelper.GetParent(d) ?? VisualTreeHelper.GetParent(d);
                }
                return false;
            }
        }

        /// <summary>真实运行时断言：左栏第一项模板里的 "Bd" Border 是否带圆角。</summary>
        internal bool NavRoundedForTest
        {
            get
            {
                try
                {
                    if (treeNav == null || treeNav.Items.Count == 0) return false;
                    if (treeNav.ItemContainerGenerator.ContainerFromIndex(0) is not TreeViewItem item) return false;
                    item.ApplyTemplate();
                    if (item.Template?.FindName("Bd", item) is not Border bd) return false;
                    return bd.CornerRadius.TopLeft > 0;
                }
                catch { return false; }
            }
        }

        /// <summary>自检用：打开一个子窗口（非模态，Owner=本窗口）。</summary>
        internal void OpenChildForTest() => showHistory?.Invoke();

        // ==================== 辅助 ====================

        static TextBlock MakeSectionTitle(string text) => new TextBlock
        {
            Text = text, FontSize = 17, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
            Margin = new Thickness(0, 0, 0, 4)
        };
        static TextBlock MakeSubTitle(string text) => new TextBlock
        {
            Text = text, FontSize = 13, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
            Margin = new Thickness(0, 14, 0, 4)
        };
        static TextBlock MakeHint(string text) => new TextBlock
        {
            Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
            Margin = new Thickness(0, 0, 0, 4)
        };
    }
}