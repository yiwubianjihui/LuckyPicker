// ================================================================
// History.cs — 抽选记录
//   每次「抽一人 / 连抽」自动写入本机历史
//   （%LOCALAPPDATA%\LuckyPicker\history.json），
//   可在主界面「抽选记录」中查看、复制、导出 CSV 或清空。
// ================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace LuckyPicker
{
    // ---------------- 记录模型 ----------------
    public class DrawRecord
    {
        public string time { get; set; }
        public string className { get; set; }
        public string mode { get; set; }
        public List<string> names { get; set; }
        public int count { get; set; }
    }

    // ---------------- 记录存储 ----------------
    public static class HistoryStore
    {
        static readonly object sync = new object();
        static List<DrawRecord> cache;
        static bool loaded;

        public static string JsonPath
        {
            get { return Path.Combine(AppConfig.Dir, "history.json"); }
        }

        static void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;
            cache = new List<DrawRecord>();
            try
            {
                if (File.Exists(JsonPath))
                {
                    var ser = new JavaScriptSerializer();
                    var list = ser.Deserialize<List<DrawRecord>>(File.ReadAllText(JsonPath));
                    if (list != null) cache = list;
                }
            }
            catch { }
        }

        static void SaveLocked()
        {
            try
            {
                Directory.CreateDirectory(AppConfig.Dir);
                var ser = new JavaScriptSerializer();
                File.WriteAllText(JsonPath, ser.Serialize(cache));
            }
            catch { }
        }

        public static void Record(string className, string mode, List<string> names)
        {
            if (names == null || names.Count == 0) return;
            try
            {
                lock (sync)
                {
                    EnsureLoaded();
                    var rec = new DrawRecord
                    {
                        time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        className = className ?? "",
                        mode = mode ?? "",
                        names = new List<string>(names),
                        count = names.Count
                    };
                    cache.Insert(0, rec);
                    while (cache.Count > 500) cache.RemoveAt(cache.Count - 1);
                    SaveLocked();
                }
            }
            catch { }
        }

        public static List<DrawRecord> GetAll()
        {
            lock (sync)
            {
                EnsureLoaded();
                var copy = new List<DrawRecord>();
                foreach (var r in cache)
                {
                    copy.Add(new DrawRecord
                    {
                        time = r.time,
                        className = r.className,
                        mode = r.mode,
                        names = r.names == null ? new List<string>() : new List<string>(r.names),
                        count = r.count
                    });
                }
                return copy;
            }
        }

        public static void Clear()
        {
            lock (sync)
            {
                EnsureLoaded();
                cache.Clear();
                SaveLocked();
            }
        }

        public static string ExportCsv(string path)
        {
            try
            {
                var rows = GetAll();
                using (var sw = new StreamWriter(path, false, new UTF8Encoding(true)))
                {
                    sw.WriteLine("时间,班级,类型,抽中名单,人数");
                    foreach (var r in rows)
                    {
                        var names = r.names == null ? new List<string>() : r.names;
                        sw.WriteLine(Csv(r.time) + "," + Csv(r.className) + "," + Csv(r.mode) + "," +
                                     Csv(string.Join("、", names.ToArray())) + "," + r.count);
                    }
                }
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        static string Csv(string v)
        {
            if (v == null) return "";
            if (v.IndexOfAny(new char[] { ',', '"', '\r', '\n' }) < 0) return v;
            return "\"" + v.Replace("\"", "\"\"") + "\"";
        }
    }

    // ---------------- 抽选记录窗口 ----------------
    public class HistoryForm : Form
    {
        DataGridView grid;
        Label statsLabel;
        Button btnExport, btnCopy, btnClear, btnClose;

        public HistoryForm()
        {
            this.Text = "抽选记录 - " + AppVersion.ProductName;
            this.ClientSize = new Size(860, 620);
            this.StartPosition = FormStartPosition.CenterParent;
            this.MinimumSize = new Size(880, 520);
            this.BackColor = Color.FromArgb(240, 247, 255);
            this.Font = new Font("Microsoft YaHei", 9F);
            try { this.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var top = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Color.FromArgb(240, 247, 255) };
            var title = new Label
            {
                Text = "抽选记录",
                Font = new Font("Microsoft YaHei", 14F, FontStyle.Bold),
                ForeColor = Color.FromArgb(31, 43, 78),
                AutoSize = true,
                Location = new Point(24, 14)
            };
            var sub = new Label
            {
                Text = "自动记录每次抽一人 / 连抽结果（本机保存最近 500 条）",
                Font = new Font("Microsoft YaHei", 8.5F),
                ForeColor = Color.FromArgb(90, 110, 138),
                AutoSize = true,
                Location = new Point(26, 42)
            };
            top.Controls.Add(title);
            top.Controls.Add(sub);

            grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                EnableHeadersVisualStyles = false
            };
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(30, 41, 59);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei", 9F, FontStyle.Bold);
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(30, 41, 59);
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(219, 234, 254);
            grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(31, 43, 78);
            grid.DefaultCellStyle.Font = new Font("Microsoft YaHei", 9F);
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "时间", Name = "time", FillWeight = 130 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "班级", Name = "className", FillWeight = 90 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "类型", Name = "mode", FillWeight = 70 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "抽中名单", Name = "names", FillWeight = 260 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "人数", Name = "count", FillWeight = 50 });

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 68, BackColor = Color.FromArgb(240, 247, 255) };
            statsLabel = new Label
            {
                Text = "暂无记录",
                Font = new Font("Microsoft YaHei", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(71, 85, 105),
                Location = new Point(24, 24),
                AutoSize = true
            };
            btnExport = MakeBtn("导出 CSV", 440, 16, 100, 34, false);
            btnCopy = MakeBtn("复制选中", 548, 16, 100, 34, false);
            btnClear = MakeBtn("清空记录", 656, 16, 100, 34, false);
            btnClose = MakeBtn("关闭", 764, 16, 86, 34, true);
            btnExport.Click += delegate { Export(); };
            btnCopy.Click += delegate { CopySelected(); };
            btnClear.Click += delegate { ClearRecords(); };
            btnClose.Click += delegate { Close(); };
            bottom.Controls.Add(statsLabel);
            bottom.Controls.Add(btnExport);
            bottom.Controls.Add(btnCopy);
            bottom.Controls.Add(btnClear);
            bottom.Controls.Add(btnClose);

            Controls.Add(grid);
            Controls.Add(bottom);
            Controls.Add(top);

            Reload();
        }

        static Button MakeBtn(string text, int x, int y, int w, int h, bool primary)
        {
            var b = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, h),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei", 9F, primary ? FontStyle.Bold : FontStyle.Regular),
                BackColor = primary ? Color.FromArgb(59, 130, 246) : Color.White,
                ForeColor = primary ? Color.White : Color.FromArgb(31, 43, 78),
                UseVisualStyleBackColor = false,
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderColor = primary ? Color.FromArgb(59, 130, 246) : Color.FromArgb(203, 213, 225);
            return b;
        }

        void Reload()
        {
            var rows = HistoryStore.GetAll();
            grid.Rows.Clear();
            foreach (var r in rows)
            {
                var names = r.names == null ? new List<string>() : r.names;
                grid.Rows.Add(r.time ?? "", r.className ?? "", r.mode ?? "",
                    string.Join("、", names.ToArray()), names.Count);
            }
            int draws = rows.Count;
            int picks = rows.Sum(r => r.count);
            var unique = new HashSet<string>();
            foreach (var r in rows)
                if (r.names != null)
                    foreach (var n in r.names)
                        if (!string.IsNullOrEmpty(n)) unique.Add(n);
            statsLabel.Text = draws == 0
                ? "暂无记录"
                : "共 " + draws + " 次抽选 · 累计抽出 " + picks + " 人次 · 去重 " + unique.Count + " 人（仅本机记录）";
        }

        void Export()
        {
            using (var dlg = new SaveFileDialog())
            {
                dlg.Title = "导出抽选记录";
                dlg.Filter = "CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*";
                dlg.FileName = "YBH幸运摇人器-抽选记录-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                string err = HistoryStore.ExportCsv(dlg.FileName);
                if (err != null)
                {
                    MessageBox.Show(this, "导出失败：" + err, "提示", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                MessageBox.Show(this, "已导出 " + HistoryStore.GetAll().Count + " 条记录到：\n" + dlg.FileName,
                    "导出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        void CopySelected()
        {
            if (grid.SelectedRows.Count == 0)
            {
                MessageBox.Show(this, "请先选中一条记录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var r = grid.SelectedRows[0];
            var text = "[" + GetCell(r, "time") + "] " + GetCell(r, "className") + " · " +
                       GetCell(r, "mode") + "：" + GetCell(r, "names");
            try
            {
                Clipboard.SetText(text);
                statsLabel.Text = "已复制：" + text;
                statsLabel.ForeColor = Color.FromArgb(6, 95, 70);
            }
            catch
            {
                MessageBox.Show(this, "复制失败：剪贴板不可用。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void ClearRecords()
        {
            if (HistoryStore.GetAll().Count == 0) return;
            if (MessageBox.Show(this, "确定要清空全部抽选记录吗？此操作不可恢复。", "清空记录",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            HistoryStore.Clear();
            Reload();
        }

        static string GetCell(DataGridViewRow r, string name)
        {
            var v = r.Cells[name].Value;
            return v == null ? "" : v.ToString();
        }
    }
}
