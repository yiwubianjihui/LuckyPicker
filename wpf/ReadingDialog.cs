// ================================================================
// ReadingDialog.cs — 姓名读音设置对话框
//
// 自动列出姓名中的多音字，按字选择读法（同音字），实时生成
// “朗读为”文本；也可直接手动填写。读音只影响语音播报。
// ================================================================
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LuckyPickerWpf
{
    public static class ReadingDialog
    {
        public static string? Show(Window owner, string name, string currentReading, Action<string> audition)
        {
            string? result = null;
            var dlg = Build(owner, name, currentReading, audition, r => result = r);
            dlg.ShowDialog();
            return result;
        }

        internal static Window Build(Window owner, string name, string currentReading, Action<string> audition, Action<string?> complete)
        {
            var t = ThemeManager.Current;
            var dlg = new Window
            {
                Icon = owner.Icon,
                Title = "姓名读音",
                Width = 560,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false,
                Background = new SolidColorBrush(t.Surface),
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                UseLayoutRounding = true
            };

            var root = new StackPanel { Margin = new Thickness(22) };
            root.Children.Add(new TextBlock
            {
                Text = "姓名读音",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(t.Ink)
            });
            root.Children.Add(new TextBlock
            {
                Text = "语音按字面朗读。多音字可在这里选择正确读法；原名显示与抽选记录不受影响。",
                FontSize = 11.5,
                Foreground = new SolidColorBrush(t.Sub),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 14)
            });

            var nameBox = new Border
            {
                Background = new SolidColorBrush(t.AccentSoft),
                BorderBrush = new SolidColorBrush(t.AccentSoftBorder),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 14),
                Child = new TextBlock
                {
                    Text = name,
                    FontSize = 22,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(t.AccentInk)
                }
            };
            root.Children.Add(nameBox);

            var preview = new TextBox
            {
                Text = string.IsNullOrWhiteSpace(currentReading) ? name : currentReading.Trim(),
                FontSize = 15,
                Padding = new Thickness(8, 6, 8, 6),
                Background = new SolidColorBrush(t.Surface),
                Foreground = new SolidColorBrush(t.Ink),
                BorderBrush = new SolidColorBrush(t.Stroke),
                BorderThickness = new Thickness(1),
                VerticalContentAlignment = VerticalAlignment.Center
            };

            var comboByIndex = new Dictionary<int, ComboBox>();
            var rows = new StackPanel();
            for (int i = 0; i < name.Length; i++)
            {
                char ch = name[i];
                if (!NameReading.IsPolyphonic(ch)) continue;
                var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var label = new TextBlock
                {
                    Text = "第 " + (i + 1) + " 字「" + ch + "」",
                    FontSize = 12.5,
                    Foreground = new SolidColorBrush(t.Sub),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(label, 0);
                row.Children.Add(label);

                var combo = new ComboBox { FontSize = 12.5, Margin = new Thickness(0) };
                if (owner.TryFindResource("ModernCombo") is Style comboStyle) combo.Style = comboStyle;
                combo.Items.Add(new ComboBoxItem { Content = "按原名（" + ch + "）", Tag = null });
                foreach (var option in NameReading.OptionsFor(ch))
                    combo.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option });
                Grid.SetColumn(combo, 1);
                row.Children.Add(combo);
                rows.Children.Add(row);
                comboByIndex[i] = combo;

                int index = i;
                combo.SelectionChanged += (s, e) =>
                {
                    var option = (combo.SelectedItem as ComboBoxItem)?.Tag as ReadingOption;
                    string text = preview.Text;
                    if (text.Length == name.Length)
                    {
                        var chars = text.ToCharArray();
                        chars[index] = option?.Homophone ?? name[index];
                        preview.Text = new string(chars);
                    }
                    else
                    {
                        var picks = new Dictionary<int, ReadingOption>();
                        foreach (var pair in comboByIndex)
                            if ((pair.Value.SelectedItem as ComboBoxItem)?.Tag is ReadingOption picked)
                                picks[pair.Key] = picked;
                        preview.Text = NameReading.Compose(name, picks);
                    }
                };

                // 已有读音且长度一致时，回显对应选项
                string reading = preview.Text;
                if (reading.Length == name.Length)
                {
                    foreach (ComboBoxItem item in combo.Items)
                        if (item.Tag is ReadingOption o && o.Homophone == reading[index])
                        {
                            combo.SelectedItem = item;
                            break;
                        }
                }
            }

            if (rows.Children.Count > 0)
            {
                root.Children.Add(new TextBlock
                {
                    Text = "多音字读音",
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(t.InkSoft),
                    Margin = new Thickness(0, 0, 0, 8)
                });
                root.Children.Add(rows);
            }
            else
            {
                root.Children.Add(new TextBlock
                {
                    Text = "未检测到内置多音字，可直接在下方填写同音字（例如把「曾」写成「增」）。",
                    FontSize = 11.5,
                    Foreground = new SolidColorBrush(t.Sub),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 10)
                });
            }

            root.Children.Add(new TextBlock
            {
                Text = "朗读为",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(t.InkSoft),
                Margin = new Thickness(0, 2, 0, 6)
            });
            root.Children.Add(preview);
            root.Children.Add(new TextBlock
            {
                Text = "请用同音字改写（如 曾 Zēng → 增）。留空或与原姓名一致时按原名朗读。",
                FontSize = 11,
                Foreground = new SolidColorBrush(t.Muted),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 5, 0, 0)
            });

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };
            Style? ghost = owner.TryFindResource("ModernBtnGhost") as Style;
            Style? main = owner.TryFindResource("ModernBtn") as Style;
            var bTest = new Button { Content = "试听", Width = 80, Margin = new Thickness(0, 0, 8, 0), Style = ghost };
            var bReset = new Button { Content = "恢复原名", Width = 96, Margin = new Thickness(0, 0, 0, 0), Style = ghost };
            var bCancel = new Button { Content = "取消", Width = 80, Margin = new Thickness(0, 0, 8, 0), Style = ghost, IsCancel = true };
            var bOk = new Button { Content = "确定", Width = 88, Style = main, IsDefault = true };
            bTest.Click += (s, e) =>
            {
                string text = preview.Text.Trim();
                try { audition?.Invoke(text.Length == 0 ? name : text); } catch { }
            };
            bReset.Click += (s, e) =>
            {
                preview.Text = name;
                foreach (var combo in comboByIndex.Values) combo.SelectedIndex = 0;
            };
            string? result = null;
            bOk.Click += (s, e) =>
            {
                string text = preview.Text.Trim();
                result = (text.Length == 0 || text == name) ? "" : text;
                complete(result);
                dlg.Close();
            };
            buttons.Children.Add(bTest);
            buttons.Children.Add(bReset);
            buttons.Children.Add(new Border { Width = 10 });
            buttons.Children.Add(bCancel);
            buttons.Children.Add(bOk);
            root.Children.Add(buttons);

            dlg.Content = root;
            return dlg;
        }
    }
}
