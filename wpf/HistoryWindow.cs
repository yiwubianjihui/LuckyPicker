// ================================================================
// HistoryWindow.cs — 独立的抽选记录窗口
//
// 主界面顶部「抽取记录」与托盘菜单共用；设置窗口不再保留重复入口。
// ================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace LuckyPickerWpf
{
    public class HistoryWindow : Window
    {
        ListBox? list;
        TextBlock? info;

        public HistoryWindow(Window owner)
        {
            Icon = owner.Icon;
            Title = "抽选记录";
            Width = 580; Height = 560;
            MinWidth = 440; MinHeight = 360;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Owner = owner;
            Background = new SolidColorBrush(ThemeManager.Current.Canvas);
            FontFamily = new FontFamily("Microsoft YaHei UI");
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            Build();
        }

        void Build()
        {
            var t = ThemeManager.Current;
            var root = new Grid { Margin = new Thickness(20) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var head = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            head.Children.Add(new TextBlock
            {
                Text = "抽选记录", FontSize = 20, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(t.Ink)
            });
            head.Children.Add(new TextBlock
            {
                Text = "最近 200 条抽取记录（含时间与结果），仅保存在本机。",
                FontSize = 12, Foreground = new SolidColorBrush(t.Sub),
                Margin = new Thickness(0, 3, 0, 0)
            });
            Grid.SetRow(head, 0);
            root.Children.Add(head);

            list = new ListBox
            {
                FontSize = 12.5,
                BorderBrush = new SolidColorBrush(t.Stroke),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(t.Surface),
                Padding = new Thickness(6),
                UseLayoutRounding = true
            };
            list.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            Grid.SetRow(list, 1);
            root.Children.Add(list);

            info = new TextBlock
            {
                FontSize = 11.5, Foreground = new SolidColorBrush(t.Sub),
                Margin = new Thickness(0, 8, 0, 10)
            };
            Grid.SetRow(info, 2);
            root.Children.Add(info);

            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ghost = (Style)FindResource("ModernBtnGhost");
            var btnReload = new Button { Content = "刷新", Width = 96, Height = 36, Margin = new Thickness(0, 0, 10, 0), Cursor = Cursors.Hand, Style = ghost };
            var btnClear = new Button { Content = "清空记录", Width = 110, Height = 36, Margin = new Thickness(0, 0, 10, 0), Cursor = Cursors.Hand, Style = ghost };
            var btnClose = new Button { Content = "关闭", Width = 90, Height = 36, Cursor = Cursors.Hand, Style = ghost };
            btnReload.Click += (s, e) => { ReloadHistory(); if (info != null) info.Text = "记录已刷新"; };
            btnClear.Click += (s, e) =>
            {
                HistoryStore.Clear();
                ReloadHistory();
                if (info != null) info.Text = "✓ 已清空抽选记录";
            };
            btnClose.Click += (s, e) => Close();
            btns.Children.Add(btnReload);
            btns.Children.Add(btnClear);
            btns.Children.Add(btnClose);
            Grid.SetRow(btns, 3);
            root.Children.Add(btns);

            Content = root;
            ReloadHistory();
        }

        internal void ReloadHistory()
        {
            if (list == null) return;
            var items = HistoryStore.Load().Select(h => h.time + "  " + h.text).ToList();
            list.ItemsSource = items.Count == 0 ? new List<string> { "（暂无记录）" } : items;
            if (info != null)
                info.Text = items.Count == 0 ? "共 0 条记录" : "共 " + items.Count + " 条记录（仅展示最近 200 条）";
        }
    }
}
