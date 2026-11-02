// ================================================================
// RosterImportWindow.cs — 名单导入预览与列匹配
//   文件选择 → 自动识别表头/列 → 预览 → 替换或追加 → 导入
// ================================================================
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace LuckyPickerWpf
{
    public static class RosterImportWindow
    {
        public static RosterImportSelection? Show(Window owner, string path, DataFile current)
        {
            var dlg = new RosterImportDialog(owner, path, current);
            return dlg.ShowDialog() == true ? dlg.Selection : null;
        }
    }

    public sealed class RosterImportDialog : Window
    {
        sealed class ColumnOption
        {
            public int Index;
            public string Label = "";
            public override string ToString() => Label;
        }

        readonly DataFile current;
        readonly ThemePalette t;

        string path = "";
        List<List<string>> rows = new();
        int detectedHeader = -1;
        int headerRow = -1;
        bool loading;
        RosterImportBuild? lastBuild;

        ComboBox nameCombo = null!, classCombo = null!, genderCombo = null!, readingCombo = null!;
        CheckBox headerCheck = null!;
        DataGrid previewGrid = null!;
        TextBlock fileText = null!, summaryText = null!, warningText = null!;
        RadioButton replaceRadio = null!, appendRadio = null!;
        Button importButton = null!;

        public RosterImportSelection? Selection { get; private set; }

        public RosterImportDialog(Window owner, string path, DataFile current)
        {
            this.current = current;
            t = ThemeManager.Current;

            Title = "导入名单";
            Width = 920;
            Height = 740;
            MinWidth = 820;
            MinHeight = 620;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Owner = owner;
            ShowInTaskbar = false;
            Background = new SolidColorBrush(t.Canvas);
            FontFamily = new FontFamily("Microsoft YaHei UI");
            UseLayoutRounding = true;

            Content = BuildRoot();
            LoadFile(path);
        }

        FrameworkElement BuildRoot()
        {
            var root = new Grid { Margin = new Thickness(22) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // ---- 标题 + 模板下载 ----
            var head = new DockPanel();
            var titleStack = new StackPanel();
            titleStack.Children.Add(new TextBlock
            {
                Text = "导入名单",
                FontSize = 19,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(t.Ink)
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = "先预览并确认列对应关系，再选择替换或追加。导入前会自动备份当前名单。",
                FontSize = 11.5,
                Foreground = new SolidColorBrush(t.Sub),
                Margin = new Thickness(0, 4, 0, 0)
            });
            var templateBtn = new Button
            {
                Content = "下载导入模板…",
                Width = 132,
                Height = 34,
                Cursor = System.Windows.Input.Cursors.Hand,
                Style = (Style)FindResource("ModernBtnGhost"),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top
            };
            templateBtn.Click += (s, e) => SaveTemplate();
            DockPanel.SetDock(templateBtn, Dock.Right);
            head.Children.Add(templateBtn);
            head.Children.Add(titleStack);
            Grid.SetRow(head, 0);
            root.Children.Add(head);

            // ---- 文件信息 ----
            var fileCard = new Border
            {
                Background = new SolidColorBrush(t.Surface),
                BorderBrush = new SolidColorBrush(t.Stroke),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 14, 0, 0)
            };
            var fileRow = new DockPanel();
            fileText = new TextBlock
            {
                Text = "正在读取…",
                FontSize = 12.5,
                Foreground = new SolidColorBrush(t.InkSoft),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var changeBtn = new Button
            {
                Content = "更换文件…",
                Width = 100,
                Height = 30,
                Cursor = System.Windows.Input.Cursors.Hand,
                Style = (Style)FindResource("ModernBtnGhost")
            };
            changeBtn.Click += (s, e) => ChooseAnotherFile();
            DockPanel.SetDock(changeBtn, Dock.Right);
            fileRow.Children.Add(changeBtn);
            fileRow.Children.Add(fileText);
            fileCard.Child = fileRow;
            Grid.SetRow(fileCard, 1);
            root.Children.Add(fileCard);

            // ---- 列匹配 ----
            var mapCard = new Border
            {
                Background = new SolidColorBrush(t.Surface),
                BorderBrush = new SolidColorBrush(t.Stroke),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 12, 14, 12),
                Margin = new Thickness(0, 10, 0, 0)
            };
            var mapStack = new StackPanel();
            mapStack.Children.Add(new TextBlock
            {
                Text = "列匹配",
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(t.InkSoft),
                Margin = new Thickness(0, 0, 0, 8)
            });

            var mapGrid = new Grid();
            for (int i = 0; i < 4; i++) mapGrid.ColumnDefinitions.Add(new ColumnDefinition());
            var comboStyle = (Style)FindResource("ModernCombo");
            nameCombo = NewColumnCombo(comboStyle);
            classCombo = NewColumnCombo(comboStyle);
            genderCombo = NewColumnCombo(comboStyle);
            readingCombo = NewColumnCombo(comboStyle);
            AddMapping(mapGrid, 0, "姓名列（必填）", nameCombo);
            AddMapping(mapGrid, 1, "班级列", classCombo);
            AddMapping(mapGrid, 2, "性别列", genderCombo);
            AddMapping(mapGrid, 3, "读音列", readingCombo);
            mapStack.Children.Add(mapGrid);

            headerCheck = new CheckBox
            {
                Content = "首行为表头（跳过该行）",
                FontSize = 12,
                Foreground = new SolidColorBrush(t.Sub),
                Margin = new Thickness(0, 10, 0, 0)
            };
            headerCheck.Checked += (s, e) => OnHeaderToggle();
            headerCheck.Unchecked += (s, e) => OnHeaderToggle();
            mapStack.Children.Add(headerCheck);
            mapStack.Children.Add(new TextBlock
            {
                Text = "表头可自动识别；标题行、空行会被跳过。列对不上时在这里手动改。",
                FontSize = 11,
                Foreground = new SolidColorBrush(t.Muted),
                Margin = new Thickness(0, 4, 0, 0)
            });
            mapCard.Child = mapStack;
            Grid.SetRow(mapCard, 2);
            root.Children.Add(mapCard);

            // ---- 预览标题 ----
            var previewHead = new DockPanel { Margin = new Thickness(2, 12, 2, 6) };
            var previewTitle = new TextBlock
            {
                Text = "数据预览",
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(t.InkSoft)
            };
            var previewHint = new TextBlock
            {
                Text = "表头行高亮 · 仅显示前 200 行",
                FontSize = 11,
                Foreground = new SolidColorBrush(t.Muted),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            DockPanel.SetDock(previewHint, Dock.Right);
            previewHead.Children.Add(previewHint);
            previewHead.Children.Add(previewTitle);
            Grid.SetRow(previewHead, 3);
            root.Children.Add(previewHead);

            previewGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                IsReadOnly = true,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                RowHeaderWidth = 0,
                RowHeight = 27,
                FontSize = 12,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                HorizontalGridLinesBrush = new SolidColorBrush(t.Canvas),
                Background = new SolidColorBrush(t.Surface),
                BorderBrush = new SolidColorBrush(t.Stroke),
                BorderThickness = new Thickness(1),
                RowBackground = new SolidColorBrush(t.Surface),
                AlternatingRowBackground = new SolidColorBrush(t.SurfaceMuted),
                UseLayoutRounding = true,
                SnapsToDevicePixels = true
            };
            previewGrid.LoadingRow += (s, e) =>
            {
                bool isHeader = headerRow >= 0 && e.Row.GetIndex() == headerRow;
                e.Row.Background = new SolidColorBrush(isHeader ? t.AccentSoft : (e.Row.GetIndex() % 2 == 0 ? t.Surface : t.SurfaceMuted));
                e.Row.FontWeight = isHeader ? FontWeights.SemiBold : FontWeights.Normal;
                e.Row.Foreground = new SolidColorBrush(isHeader ? t.AccentInk : t.Ink);
            };
            Grid.SetRow(previewGrid, 4);
            root.Children.Add(previewGrid);

            // ---- 统计与提示 ----
            var infoStack = new StackPanel { Margin = new Thickness(2, 10, 2, 0) };
            summaryText = new TextBlock
            {
                Text = "",
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(t.AccentInk)
            };
            warningText = new TextBlock
            {
                Text = "",
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(180, 83, 9)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            };
            infoStack.Children.Add(summaryText);
            infoStack.Children.Add(warningText);
            Grid.SetRow(infoStack, 5);
            root.Children.Add(infoStack);

            // ---- 底部：替换/追加 + 按钮 ----
            var footer = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
            var modes = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            replaceRadio = new RadioButton
            {
                Content = "替换现有名单",
                FontSize = 12,
                Foreground = new SolidColorBrush(t.InkSoft),
                GroupName = "importMode",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 18, 0)
            };
            appendRadio = new RadioButton
            {
                Content = "追加到现有名单（自动跳过重复）",
                FontSize = 12,
                Foreground = new SolidColorBrush(t.InkSoft),
                IsChecked = true,
                GroupName = "importMode",
                VerticalAlignment = VerticalAlignment.Center
            };
            replaceRadio.Checked += (s, e) => UpdateSummary();
            appendRadio.Checked += (s, e) => UpdateSummary();
            modes.Children.Add(replaceRadio);
            modes.Children.Add(appendRadio);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = new Button
            {
                Content = "取消",
                Width = 84,
                Height = 36,
                IsCancel = true,
                Cursor = System.Windows.Input.Cursors.Hand,
                Style = (Style)FindResource("ModernBtnGhost"),
                Margin = new Thickness(0, 0, 10, 0)
            };
            importButton = new Button
            {
                Content = "导入",
                Width = 100,
                Height = 36,
                IsDefault = true,
                Cursor = System.Windows.Input.Cursors.Hand,
                Style = (Style)FindResource("ModernBtn")
            };
            importButton.Click += (s, e) => ConfirmImport();
            buttons.Children.Add(cancel);
            buttons.Children.Add(importButton);
            DockPanel.SetDock(buttons, Dock.Right);
            footer.Children.Add(buttons);
            footer.Children.Add(modes);
            Grid.SetRow(footer, 6);
            root.Children.Add(footer);

            return root;
        }

        ComboBox NewColumnCombo(Style style) => new()
        {
            Height = 34,
            Style = style,
            Margin = new Thickness(0)
        };

        void AddMapping(Grid grid, int column, string label, ComboBox combo)
        {
            var stack = new StackPanel
            {
                Margin = new Thickness(column == 0 ? 0 : 10, 0, column == 3 ? 0 : 4, 0)
            };
            stack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11.5,
                Foreground = new SolidColorBrush(t.Sub),
                Margin = new Thickness(2, 0, 0, 5)
            });
            combo.SelectionChanged += (s, e) => { if (!loading) { UpdateSummary(); UpdatePreviewHeaders(); } };
            stack.Children.Add(combo);
            Grid.SetColumn(stack, column);
            grid.Children.Add(stack);
        }

        // ---------- 文件加载 ----------
        void ChooseAnotherFile()
        {
            var ofd = new OpenFileDialog
            {
                Title = "选择名单文件",
                Filter = "名单文件|*.xlsx;*.csv|Excel|*.xlsx|CSV|*.csv"
            };
            if (ofd.ShowDialog(this) == true) LoadFile(ofd.FileName);
        }

        void LoadFile(string file)
        {
            path = file;
            try { rows = RosterImport.ReadTable(file) ?? new List<List<string>>(); }
            catch (Exception ex)
            {
                rows = new List<List<string>>();
                App.Log("ROSTER-IMPORT-READ-EX: " + ex);
            }

            if (rows.Count == 0)
            {
                detectedHeader = -1;
                headerRow = -1;
                fileText.Text = "文件：" + Path.GetFileName(file) + " · 未能读取到数据";
                previewGrid.ItemsSource = null;
                previewGrid.Columns.Clear();
                summaryText.Text = "没有可导入的数据，请检查文件是否为 .xlsx / .csv，或更换文件。";
                summaryText.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                warningText.Text = "";
                importButton.IsEnabled = false;
                return;
            }

            detectedHeader = RosterImport.DetectHeaderRow(rows);
            headerRow = detectedHeader;
            int cols = RosterImport.ColumnCount(rows);
            fileText.Text = "文件：" + Path.GetFileName(file) + " · " + rows.Count + " 行 × " + cols + " 列"
                + (detectedHeader >= 0 ? " · 表头在第 " + (detectedHeader + 1) + " 行" : " · 未检测到表头");

            loading = true;
            try
            {
                FillColumnCombos();
                headerCheck.IsChecked = detectedHeader >= 0;
                headerCheck.Content = detectedHeader >= 0
                    ? "首行为表头（跳过第 " + (detectedHeader + 1) + " 行）"
                    : "首行为表头（跳过第一行）";
            }
            finally { loading = false; }

            RebuildPreview();
            UpdateSummary();
        }

        void FillColumnCombos()
        {
            var suggested = RosterImport.Suggest(rows, detectedHeader);
            int cols = RosterImport.ColumnCount(rows);
            var header = detectedHeader >= 0 && detectedHeader < rows.Count
                ? rows[detectedHeader]
                : (rows.Count > 0 ? rows[0] : new List<string>());

            var all = new List<ColumnOption>();
            for (int c = 0; c < cols; c++)
            {
                string hint = c < header.Count ? RosterImport.CleanCell(header[c]) : "";
                if (hint.Length > 8) hint = hint.Substring(0, 8) + "…";
                all.Add(new ColumnOption
                {
                    Index = c,
                    Label = "第" + (c + 1) + "列" + (hint.Length > 0 ? " · " + hint : "")
                });
            }
            var optional = new List<ColumnOption> { new() { Index = -1, Label = "（不导入）" } };
            optional.AddRange(all);

            nameCombo.ItemsSource = all;
            classCombo.ItemsSource = optional;
            genderCombo.ItemsSource = optional;
            readingCombo.ItemsSource = optional;

            nameCombo.SelectedIndex = Math.Max(0, suggested.NameColumn);
            classCombo.SelectedIndex = suggested.ClassColumn >= 0 ? suggested.ClassColumn + 1 : 0;
            genderCombo.SelectedIndex = suggested.GenderColumn >= 0 ? suggested.GenderColumn + 1 : 0;
            readingCombo.SelectedIndex = suggested.ReadingColumn >= 0 ? suggested.ReadingColumn + 1 : 0;
        }

        void OnHeaderToggle()
        {
            if (loading) return;
            if (headerCheck.IsChecked == true)
                headerRow = detectedHeader >= 0 ? detectedHeader : 0;
            else
                headerRow = -1;
            RebuildPreview();
            UpdateSummary();
        }

        // ---------- 预览 ----------
        void RebuildPreview()
        {
            if (rows.Count == 0) return;
            int cols = Math.Min(RosterImport.ColumnCount(rows), RosterImport.MaxPreviewColumns);
            var table = new DataTable();
            table.Columns.Add("R", typeof(int));
            for (int c = 0; c < cols; c++) table.Columns.Add("C" + c, typeof(string));

            int limit = Math.Min(rows.Count, 200);
            for (int r = 0; r < limit; r++)
            {
                var row = table.NewRow();
                row["R"] = r + 1;
                for (int c = 0; c < cols; c++)
                    row["C" + c] = c < rows[r].Count ? rows[r][c] ?? "" : "";
                table.Rows.Add(row);
            }

            previewGrid.ItemsSource = table.DefaultView;
            previewGrid.Columns.Clear();
            previewGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "行",
                Binding = new System.Windows.Data.Binding("[R]"),
                Width = new DataGridLength(46)
            });
            for (int c = 0; c < cols; c++)
            {
                previewGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = PreviewHeader(c),
                    Binding = new System.Windows.Data.Binding("[C" + c + "]"),
                    Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                    MaxWidth = 220
                });
            }
        }

        string PreviewHeader(int column)
        {
            string header = "第" + (column + 1) + "列";
            if (column == SelectedColumn(nameCombo)) return header + " · 姓名";
            if (column == SelectedColumn(classCombo)) return header + " · 班级";
            if (column == SelectedColumn(genderCombo)) return header + " · 性别";
            if (column == SelectedColumn(readingCombo)) return header + " · 读音";
            return header;
        }

        void UpdatePreviewHeaders()
        {
            if (previewGrid.Columns.Count < 2) return;
            int cols = Math.Min(RosterImport.ColumnCount(rows), RosterImport.MaxPreviewColumns);
            for (int c = 0; c < cols && c + 1 < previewGrid.Columns.Count; c++)
                previewGrid.Columns[c + 1].Header = PreviewHeader(c);
        }

        // ---------- 统计 ----------
        RosterColumnMap CurrentMap()
        {
            int name = SelectedColumn(nameCombo);
            return new RosterColumnMap
            {
                HeaderRow = headerRow,
                NameColumn = name >= 0 ? name : 0,
                ClassColumn = SelectedColumn(classCombo),
                GenderColumn = SelectedColumn(genderCombo),
                ReadingColumn = SelectedColumn(readingCombo),
                DefaultClassId = "1"
            };
        }

        static int SelectedColumn(ComboBox combo) =>
            combo.SelectedItem is ColumnOption option ? option.Index : -1;

        void UpdateSummary()
        {
            if (rows.Count == 0)
            {
                lastBuild = null;
                importButton.IsEnabled = false;
                return;
            }
            bool append = appendRadio.IsChecked == true;
            var build = RosterImport.Build(rows, CurrentMap(), append ? current.students : null);
            lastBuild = build;

            int currentCount = append ? (current.students?.Count ?? 0) : 0;
            int total = build.Accepted.Count + currentCount;
            var classIds = new HashSet<string>(StringComparer.Ordinal);
            if (append)
                foreach (var s in current.students ?? new List<Student>()) classIds.Add(s.classId);
            foreach (var s in build.Accepted) classIds.Add(s.classId);

            string text = "有效 " + build.Accepted.Count + " 人 · 导入后共 " + total + " 人（" + classIds.Count + " 个班）";
            if (build.Skipped > 0) text += " · 跳过 " + build.Skipped + " 行";
            if (append && build.DuplicateExisting > 0) text += " · 已存在 " + build.DuplicateExisting + " 人";
            summaryText.Text = text;
            summaryText.Foreground = new SolidColorBrush(build.Accepted.Count == 0
                ? Color.FromRgb(220, 38, 38) : t.AccentInk);

            if (build.Accepted.Count == 0)
            {
                warningText.Text = "没有可导入的数据。请检查姓名列、表头设置，或更换文件。";
            }
            else if (build.Warnings.Count > 0)
            {
                warningText.Text = string.Join("\n", build.Warnings.Take(3));
                if (build.Warnings.Count > 3) warningText.Text += "\n…另有 " + (build.Warnings.Count - 3) + " 条提示";
            }
            else
            {
                warningText.Text = "";
            }
            importButton.IsEnabled = build.Accepted.Count > 0;
        }

        // ---------- 模板 / 导入 ----------
        void SaveTemplate() => RosterTemplate.SaveInteractive(this);

        void ConfirmImport()
        {
            if (lastBuild == null || lastBuild.Accepted.Count == 0) return;
            bool replace = replaceRadio.IsChecked == true;
            int existing = current.students?.Count ?? 0;
            if (replace && existing > 0)
            {
                var ask = MessageBox.Show(this,
                    "将用 " + lastBuild.Accepted.Count + " 名学生替换现有 " + existing + " 名学生。\n\n" +
                    "导入前会自动备份当前名单。确定继续？",
                    "替换名单", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (ask != MessageBoxResult.Yes) return;
            }

            try
            {
                var data = RosterImport.Merge(current, lastBuild, replace);
                Selection = new RosterImportSelection
                {
                    Data = data,
                    Replace = replace,
                    ImportedCount = lastBuild.Accepted.Count,
                    TotalCount = data.students.Count,
                    Build = lastBuild
                };
                DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "导入失败：" + ex.Message, "导入名单",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 自检钩子
        internal string SummaryTextForTest => summaryText.Text;
        internal bool ImportEnabledForTest => importButton.IsEnabled;
        internal int PreviewRowsForTest => previewGrid.Items.Count;
        internal void ChooseMappingForTest(int name, int cls, int gender, int reading)
        {
            nameCombo.SelectedIndex = name;
            classCombo.SelectedIndex = cls + 1;
            genderCombo.SelectedIndex = gender + 1;
            readingCombo.SelectedIndex = reading + 1;
        }
    }
}
