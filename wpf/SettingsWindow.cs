// ================================================================
// SettingsWindow.cs — 统一设置窗口
//
// 左侧分类导航（圆角选中态）+ 右侧内容区，类似 Windows 11 设置面板。
// 名单管理与抽选记录由主界面顶部按钮独立打开，设置内不再保留重复入口。
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
        readonly Action<UpdateResult> showUpdateDownload;  // 下载安装器（Owner = 本窗口）
        readonly Action<string> speak;                     // 试听（用主窗 TTS 引擎）
        readonly Action onChangeDefaultClass;
        readonly Action onQuit;
        readonly Action themeChanged;                      // 配色切换 → 主窗刷新代码创建的控件

        // -------- 导航 / 通用 --------
        TreeView treeNav;
        Grid? pageHost;
        TextBlock statusText;
        ComboBox defaultClassCombo;
        CheckBox chkAutoStart, chkBallVisible, chkBallPinned;
        bool loading = true;
        int currentPage;
        const int IDX_GENERAL = 0, IDX_VOICE = 1, IDX_ABOUT = 2;
        readonly List<StackPanel> pages = new();

        // -------- 常规页：悬浮球大小单选组 / 配色卡片 --------
        readonly List<RadioButton> sizeRadios = new();
        readonly List<RadioButton> themeCards = new();
        Border? ballPreview;

        // -------- 关于 --------
        TextBlock updateResultText;

        public SettingsWindow(Window owner,
            Action applyVoice, Action ballChanged,
            Action<UpdateResult> showUpdateDownload, Action<string> speak,
            Action changeDefaultClass, Action onQuit, Action themeChanged)
        {
            this.themeChanged = themeChanged;
            this.applyVoice = applyVoice;
            this.ballChanged = ballChanged;
            this.showUpdateDownload = showUpdateDownload;
            this.speak = speak;
            onChangeDefaultClass = changeDefaultClass;
            this.onQuit = onQuit;

            Icon = owner.Icon;
            Title = "设置 · " + AppVersion.ProductName;
            Width = 820; Height = 620;
            MinWidth = 700; MinHeight = 500;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Owner = owner;
            Background = new SolidColorBrush(ThemeManager.Current.Canvas);
            FontFamily = new FontFamily("Microsoft YaHei UI");
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            BuildLayout();
            loading = false;
        }

        // ==================== 布局 ====================

        void BuildLayout()
        {
            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(198) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var navBorder = new Border
            {
                Background = new SolidColorBrush(ThemeManager.Current.SurfaceMuted),
                BorderBrush = new SolidColorBrush(ThemeManager.Current.Stroke),
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
                BorderBrush = new SolidColorBrush(ThemeManager.Current.Stroke),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Background = new SolidColorBrush(ThemeManager.Current.Surface),
                Padding = new Thickness(20, 10, 20, 10)
            };
            var bottomRow = new Grid();
            bottomRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottomRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            statusText = new TextBlock
            {
                Text = "就绪", FontSize = 12,
                Foreground = new SolidColorBrush(ThemeManager.Current.Sub),
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
            var panel = new StackPanel { Margin = new Thickness(0, 20, 0, 0) };
            panel.Children.Add(new TextBlock
            {
                Text = "设置", FontSize = 22, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(ThemeManager.Current.Ink),
                Margin = new Thickness(22, 0, 20, 3)
            });
            panel.Children.Add(new TextBlock
            {
                Text = AppVersion.Display, FontSize = 11.5,
                Foreground = new SolidColorBrush(ThemeManager.Current.Sub),
                Margin = new Thickness(22, 0, 20, 0)
            });

            treeNav = new TreeView
            {
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Margin = new Thickness(12, 18, 12, 0),
                ItemContainerStyle = BuildNavItemStyle()
            };

            string[] titles = { "常规", "语音", "关于 · 更新" };
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
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            bd.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(TreeViewItem.BackgroundProperty));
            bd.SetValue(Border.PaddingProperty, new Thickness(14, 11, 14, 11));
            bd.SetValue(Border.MarginProperty, new Thickness(0, 2, 0, 2));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.ContentSourceProperty, "Header");
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            bd.AppendChild(cp);
            tpl.VisualTree = bd;

            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(ThemeManager.Current.Canvas), "Bd"));
            tpl.Triggers.Add(hover);

            var selected = new Trigger { Property = TreeViewItem.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(ThemeManager.Current.AccentSoft), "Bd"));
            selected.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(ThemeManager.Current.AccentInk)));
            selected.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
            tpl.Triggers.Add(selected);

            style.Setters.Add(new Setter(TreeViewItem.TemplateProperty, tpl));
            style.Setters.Add(new Setter(TreeViewItem.FontSizeProperty, 13.5));
            style.Setters.Add(new Setter(TreeViewItem.CursorProperty, Cursors.Hand));
            style.Setters.Add(new Setter(TreeViewItem.ForegroundProperty, new SolidColorBrush(ThemeManager.Current.Ink)));
            style.Setters.Add(new Setter(TreeViewItem.FocusVisualStyleProperty, null));
            style.Setters.Add(new Setter(TreeViewItem.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            return style;
        }

        void BuildAllPages(Grid parent)
        {
            pageHost = new Grid();
            for (int i = 0; i < 3; i++)
            {
                var page = new StackPanel { Margin = new Thickness(30, 26, 30, 18) };
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
            FillAboutPage(pages[IDX_ABOUT]);

            Grid.SetRow(pageHost, 0);
            parent.Children.Add(pageHost);

            ShowPage(0);
            if (treeNav.Items.Count > 0) ((TreeViewItem)treeNav.Items[0]).IsSelected = true;
        }

        // 各分类页的默认底部提示。切换页面时刷新状态栏，避免把上一页的
        // "✓ 悬浮球大小已更新为 80（已保存）"之类操作反馈带到别的页面上——
        // 那看起来像串台，也会让人误以为当前页面刚发生了什么。
        static readonly string[] PageHints =
        {
            "设置改动即时生效并已保存",
            "点「试听」预览音色，点「保存」立即生效",
            "检查更新不会改动当前设置",
        };

        void ShowPage(int idx)
        {
            if (pageHost == null) return;
            currentPage = idx;
            for (int i = 0; i < pageHost.Children.Count; i++)
                pageHost.Children[i].Visibility = (i == idx) ? Visibility.Visible : Visibility.Collapsed;
            if (statusText != null && idx >= 0 && idx < PageHints.Length)
                statusText.Text = PageHints[idx];
        }

        internal void ShowPageAt(int idx) => ShowPageForTest(idx);

        // ==================== 常规 ====================

        void FillGeneralPage(StackPanel sp)
        {
            sp.Children.Add(MakeSectionTitle("常规"));
            sp.Children.Add(MakeHint("基础行为、悬浮球与默认班级。"));

            // 配色主题：三张卡片，点选立即生效
            sp.Children.Add(MakeSubTitle("配色"));
            sp.Children.Add(MakeHint("切换后立即生效，并记住你的选择。"));
            var themeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
            themeCards.Clear();
            foreach (var p in ThemeManager.All) themeRow.Children.Add(BuildThemeCard(p));
            sp.Children.Add(themeRow);

            chkAutoStart = new CheckBox
            {
                Content = "开机自启动", IsChecked = AutoStart.IsEnabled(),
                FontSize = 13.5, Margin = new Thickness(0, 12, 0, 6), Cursor = Cursors.Hand,
                Foreground = new SolidColorBrush(ThemeManager.Current.Ink)
            };
            chkAutoStart.Checked += (s, e) => { if (!loading) { AutoStart.SetEnabled(true); statusText.Text = "✓ 已设为开机自启动"; } };
            chkAutoStart.Unchecked += (s, e) => { if (!loading) { AutoStart.SetEnabled(false); statusText.Text = "已取消开机自启动"; } };
            sp.Children.Add(chkAutoStart);

            chkBallVisible = new CheckBox
            {
                Content = "桌面悬浮球", IsChecked = AppConfig.BallVisible,
                FontSize = 13.5, Margin = new Thickness(0, 6, 0, 6), Cursor = Cursors.Hand,
                Foreground = new SolidColorBrush(ThemeManager.Current.Ink)
            };
            chkBallVisible.Checked += (s, e) => { if (loading) return; AppConfig.BallVisible = true; AppConfig.Save(); ballChanged?.Invoke(); statusText.Text = "✓ 已显示桌面悬浮球"; };
            chkBallVisible.Unchecked += (s, e) => { if (loading) return; AppConfig.BallVisible = false; AppConfig.Save(); ballChanged?.Invoke(); statusText.Text = "已隐藏桌面悬浮球"; };
            sp.Children.Add(chkBallVisible);

            chkBallPinned = new CheckBox
            {
                Content = "固定悬浮球",
                IsChecked = AppConfig.BallPinned,
                FontSize = 13.5, Margin = new Thickness(0, 6, 0, 4), Cursor = Cursors.Hand,
                Foreground = new SolidColorBrush(ThemeManager.Current.Ink)
            };
            chkBallPinned.Checked += (s, e) => { if (loading) return; AppConfig.BallPinned = true; AppConfig.Save(); ballChanged?.Invoke(); BallQuickPanel.RefreshPinState(true); UpdateBallPreview(); statusText.Text = "✓ 悬浮球已固定"; };
            chkBallPinned.Unchecked += (s, e) => { if (loading) return; AppConfig.BallPinned = false; AppConfig.Save(); ballChanged?.Invoke(); BallQuickPanel.RefreshPinState(false); UpdateBallPreview(); statusText.Text = "已取消固定悬浮球"; };
            sp.Children.Add(chkBallPinned);
            sp.Children.Add(new TextBlock
            {
                Text = "4 秒无操作后自动半透明以免遮挡内容；固定只影响抽完是否贴边收起，不影响渐隐。",
                FontSize = 11, Foreground = new SolidColorBrush(ThemeManager.Current.Muted),
                Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap
            });

            // 悬浮球大小 —— 选中即时应用到球并持久化
            sp.Children.Add(MakeSubTitle("悬浮球大小"));
            int cur = AppConfig.BallDiameter;
            var sizeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
            sizeRadios.Clear();
            foreach (var kv in new[] { new[] { "小（80）", "80" }, new[] { "中（96）", "96" }, new[] { "大（120）", "120" } })
            {
                string label = kv[0];
                int d = int.Parse(kv[1]);
                var b = new RadioButton
                {
                    Content = label,
                    // 关键：三项必须共用同一个 GroupName 才能互斥。
                    // 之前每项各用 Guid 生成的唯一组名 → 互不相干 → 点一个不会取消另一个
                    //（表现就是"全都打勾"）。
                    GroupName = "BallSizeGroup",
                    Tag = d,
                    IsChecked = Math.Abs(cur - d) < 9,
                    FontSize = 13, Margin = new Thickness(0, 0, 18, 0), Cursor = Cursors.Hand,
                    Foreground = new SolidColorBrush(ThemeManager.Current.Ink)
                };
                int captured = d;
                b.Checked += (s, e) =>
                {
                    if (loading || b.IsChecked != true) return;
                    AppConfig.BallDiameter = captured;
                    AppConfig.Save();            // 持久化
                    ballChanged?.Invoke();       // 立即应用到悬浮球
                    UpdateBallPreview();
                    statusText.Text = "✓ 悬浮球大小已更新为 " + captured + "（已保存）";
                };
                sizeRadios.Add(b);
                sizeRow.Children.Add(b);
            }
            sp.Children.Add(sizeRow);
            sp.Children.Add(new TextBlock
            {
                Text = "调整后立即生效，并写入配置文件，重启后保持。",
                FontSize = 11, Foreground = new SolidColorBrush(ThemeManager.Current.Muted),
                Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap
            });

            // 实时预览：随大小 / 固定状态同步，所见即所得
            sp.Children.Add(MakeSubTitle("预览"));
            var previewHost = new Border
            {
                Height = 156, CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(ThemeManager.Current.Canvas),
                BorderBrush = new SolidColorBrush(ThemeManager.Current.Stroke),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 2, 0, 6)
            };
            ballPreview = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Background = BuildPreviewBrush(),
                BorderBrush = new SolidColorBrush(Color.FromArgb(210, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = ThemeManager.Current.BallShadow, Opacity = 0.20,
                    BlurRadius = 12, ShadowDepth = 1.5, Direction = 270
                },
                Child = new TextBlock
                {
                    Text = "摇", FontSize = 15, FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(ThemeManager.Current.BallFace),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            previewHost.Child = new Grid { Children = { ballPreview } };
            sp.Children.Add(previewHost);
            UpdateBallPreview();

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

        /// <summary>预览球体渐变（与悬浮球实际配色一致）。</summary>
        static Brush BuildPreviewBrush()
        {
            var g = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.34, 0.28),
                Center = new Point(0.42, 0.38),
                RadiusX = 0.90, RadiusY = 0.90
            };
            g.GradientStops.Add(new GradientStop(ThemeManager.Current.BallHi, 0.0));
            g.GradientStops.Add(new GradientStop(ThemeManager.Current.BallMid, 0.55));
            g.GradientStops.Add(new GradientStop(ThemeManager.Current.BallEdge, 1.0));
            return g;
        }

        /// <summary>按当前配置刷新预览球的尺寸 / 固定描边。</summary>
        void UpdateBallPreview()
        {
            if (ballPreview == null) return;
            try
            {
                double d = Math.Max(64, Math.Min(160, AppConfig.BallDiameter));
                ballPreview.Width = d;
                ballPreview.Height = d;
                ballPreview.CornerRadius = new CornerRadius(d / 2);
                ballPreview.BorderBrush = AppConfig.BallPinned
                    ? new SolidColorBrush(ThemeManager.Current.Accent)
                    : new SolidColorBrush(Color.FromArgb(210, 0xFF, 0xFF, 0xFF));
                ballPreview.BorderThickness = new Thickness(AppConfig.BallPinned ? 2 : 1);
                if (ballPreview.Child is TextBlock tb) tb.FontSize = Math.Max(13, d * 0.30);
            }
            catch { }
        }

        /// <summary>单张配色卡片：色板预览 + 名称 + 选中标记。</summary>
        RadioButton BuildThemeCard(ThemePalette p)
        {
            bool selected = string.Equals(ThemeManager.Current.Id, p.Id, StringComparison.OrdinalIgnoreCase);
            var t = ThemeManager.Current;

            var swatch = new Border
            {
                Width = 42, Height = 42, CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(p.Canvas),
                BorderBrush = new SolidColorBrush(p.Stroke), BorderThickness = new Thickness(1)
            };
            var dots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            dots.Children.Add(new Border { Width = 15, Height = 15, CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(p.Accent), Margin = new Thickness(0, 0, 3, 0) });
            dots.Children.Add(new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(5), Background = new SolidColorBrush(p.AccentSoftBorder), VerticalAlignment = VerticalAlignment.Center });
            swatch.Child = dots;

            var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 6, 0) };
            labels.Children.Add(new TextBlock { Text = p.Name, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(t.Ink) });
            labels.Children.Add(new TextBlock { Text = p.Description, FontSize = 11, Foreground = new SolidColorBrush(t.Sub), Margin = new Thickness(0, 2, 0, 0) });

            var check = new TextBlock
            {
                Text = "✓", FontSize = 13, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(p.Accent),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = selected ? Visibility.Visible : Visibility.Collapsed
            };

            var content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(swatch, 0);
            Grid.SetColumn(labels, 1);
            Grid.SetColumn(check, 2);
            content.Children.Add(swatch);
            content.Children.Add(labels);
            content.Children.Add(check);

            var rb = new RadioButton
            {
                GroupName = "ThemeGroup", Tag = p.Id, IsChecked = selected,
                Content = content, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 10, 0),
                VerticalContentAlignment = VerticalAlignment.Center
            };

            var tpl = new ControlTemplate(typeof(RadioButton));
            var bd = new FrameworkElementFactory(typeof(Border));
            bd.Name = "Bd";
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
            bd.SetValue(Border.BackgroundProperty, new SolidColorBrush(t.Surface));
            bd.SetValue(Border.BorderBrushProperty, new SolidColorBrush(t.Stroke));
            bd.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            bd.SetValue(Border.PaddingProperty, new Thickness(10, 9, 12, 9));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            bd.AppendChild(cp);
            tpl.VisualTree = bd;

            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(t.AccentSoftHover), "Bd"));
            hover.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(t.StrokeHover), "Bd"));
            tpl.Triggers.Add(hover);

            var isChecked = new Trigger { Property = System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, Value = true };
            isChecked.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(t.AccentSoft), "Bd"));
            isChecked.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(p.Accent), "Bd"));
            isChecked.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(1.5), "Bd"));
            tpl.Triggers.Add(isChecked);
            rb.Template = tpl;

            rb.Checked += (s, e) =>
            {
                if (loading || rb.IsChecked != true) return;
                // 重建会移除触发点击的卡片，推迟到本次事件之后再执行
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    ThemeManager.Apply(p.Id, persist: true);
                    themeChanged?.Invoke();
                    ReloadForTheme();
                    statusText.Text = "✓ 已切换为「" + p.Name + "」配色（已保存）";
                }), System.Windows.Threading.DispatcherPriority.Background);
            };
            themeCards.Add(rb);
            return rb;
        }

        /// <summary>换色后重建界面：代码里创建的颜色才会全部换成新主题。</summary>
        void ReloadForTheme()
        {
            ThemeReloadCountForTest++;
            int page = currentPage;
            loading = true;
            pages.Clear();
            sizeRadios.Clear();
            themeCards.Clear();
            ballPreview = null;
            updateResultText = null;
            defaultClassCombo = null;
            chkAutoStart = null;
            chkBallVisible = null;
            chkBallPinned = null;
            Background = new SolidColorBrush(ThemeManager.Current.Canvas);
            BuildLayout();
            loading = false;
            ShowPageForTest(page);
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
                FontSize = 11, Foreground = new SolidColorBrush(ThemeManager.Current.Sub),
                Margin = new Thickness(0, 14, 0, 0), TextWrapping = TextWrapping.Wrap
            });
        }

        // ==================== 关于 · 更新 ====================

        void FillAboutPage(StackPanel sp)
        {
            sp.Children.Add(MakeSectionTitle("关于 · 更新"));
            sp.Children.Add(MakeHint("版本信息与检查更新。"));

            var info = new TextBlock
            {
                FontSize = 13, TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(ThemeManager.Current.Ink),
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
                Foreground = new SolidColorBrush(ThemeManager.Current.InkSoft),
                Margin = new Thickness(0, 4, 0, 14), MinHeight = 54
            };
            sp.Children.Add(updateResultText);

            var btnCheck = new Button { Content = "检查更新", Width = 120, HorizontalAlignment = HorizontalAlignment.Left, Style = (Style)FindResource("ModernBtn") };
            btnCheck.Click += async (s, e) =>
            {
                btnCheck.IsEnabled = false;
                updateResultText.Text = "正在检查更新…";
                updateResultText.Foreground = new SolidColorBrush(ThemeManager.Current.Sub);
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
                    updateResultText.Foreground = new SolidColorBrush(ThemeManager.Current.InkSoft);
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
        internal void ShowPageForTest(int idx)
        {
            // 同步左侧导航选中态，保证截图/断言与实际点击一致
            try { if (idx >= 0 && idx < treeNav.Items.Count) ((TreeViewItem)treeNav.Items[idx]).IsSelected = true; } catch { }
            ShowPage(idx);
        }
        internal bool HasNavTitleForTest(string title)
            => treeNav != null && treeNav.Items.OfType<TreeViewItem>().Any(t => string.Equals(t.Header as string, title));

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

        internal int CurrentPageForTest => currentPage;

        /// <summary>自检用：当前被勾选的"悬浮球大小"单选项数量（应为 1）。</summary>
        internal int BallSizeCheckedCountForTest => sizeRadios.Count(r => r.IsChecked == true);

        /// <summary>自检用：模拟点选某个尺寸，返回是否找到该选项。</summary>
        internal bool CheckBallSizeForTest(int diameter)
        {
            var r = sizeRadios.FirstOrDefault(x => x.Tag is int d && d == diameter);
            if (r == null) return false;
            r.IsChecked = true;
            return true;
        }

        /// <summary>自检用：预览球直径是否已随配置同步。</summary>
        internal double BallPreviewDiameterForTest => ballPreview?.Width ?? -1;

        /// <summary>自检用：底部状态栏当前文案。</summary>
        internal string StatusTextForTest => statusText?.Text ?? "";

        /// <summary>自检用：切到某个分类页。</summary>
        internal void SwitchPageForTest(int idx) => ShowPage(idx);

        /// <summary>自检用：配色卡片数量（应为 3）。</summary>
        internal int ThemeCardCountForTest => themeCards.Count;

        /// <summary>自检用：主题重建次数。</summary>
        internal int ThemeReloadCountForTest { get; private set; }

        /// <summary>自检用：点选某个配色。</summary>
        internal bool SelectThemeForTest(string id)
        {
            var card = themeCards.FirstOrDefault(c => string.Equals(c.Tag as string, id, StringComparison.OrdinalIgnoreCase));
            if (card == null) return false;
            card.IsChecked = true;
            return true;
        }

        /// <summary>自检用：当前设置页读到的主题。</summary>
        internal string CurrentThemeIdForTest => ThemeManager.Current.Id;

        // ==================== 辅助 ====================

        static TextBlock MakeSectionTitle(string text) => new TextBlock
        {
            Text = text, FontSize = 20, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ThemeManager.Current.Ink),
            Margin = new Thickness(0, 0, 0, 5)
        };
        static TextBlock MakeSubTitle(string text) => new TextBlock
        {
            Text = text, FontSize = 13, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ThemeManager.Current.InkSoft),
            Margin = new Thickness(0, 14, 0, 4)
        };
        static TextBlock MakeHint(string text) => new TextBlock
        {
            Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(ThemeManager.Current.Sub),
            Margin = new Thickness(0, 0, 0, 4)
        };
    }
}