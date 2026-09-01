// ================================================================
// Editor.cs — 名单管理窗口
//   程序内编辑学生名单 / 班级名称，支持导入 Excel(.xlsx) 与 CSV，
//   导入时提供预览与列匹配，并可直接保存回 students.json。
// ================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Web.Script.Serialization;

namespace LuckyPicker
{
    public class EditorForm : Form
    {
        MainForm owner;
        DataGridView studentGrid;
        DataGridView classGrid;
        CheckBox onlineChk;
        ComboBox voiceCombo;
        ComboBox sourceCombo;
        Label statusLabel;

        public EditorForm(MainForm owner, DataFile data)
        {
            this.owner = owner;
            this.Text = "名单管理 - " + AppVersion.ProductName;
            this.ClientSize = new Size(800, 640);
            this.StartPosition = FormStartPosition.CenterParent;
            this.MinimumSize = new Size(660, 500);
            this.Font = new Font("Microsoft YaHei", 9F);
            try { this.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var tabs = new TabControl { Dock = DockStyle.Fill };

            // ================= tab 1：学生名单 =================
            var page1 = new TabPage("学生名单");
            studentGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                BackgroundColor = Color.White,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = true
            };
            studentGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "姓名", Name = "name", FillWeight = 140 });
            studentGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "班级", Name = "classId", FillWeight = 80 });
            var genderCol = new DataGridViewComboBoxColumn { HeaderText = "性别", Name = "gender", FillWeight = 80, FlatStyle = FlatStyle.Flat };
            genderCol.Items.AddRange(new object[] { "男", "女", "未知" });
            studentGrid.Columns.Add(genderCol);
            LoadStudents(data);

            var topPanel = new Panel { Dock = DockStyle.Top, Height = 46 };
            var btnAdd = MakeBtn("＋ 添加学生", 8);
            var btnDel = MakeBtn("－ 删除选中", 122);
            var btnImpX = MakeBtn("导入 Excel(.xlsx)", 236);
            var btnImpC = MakeBtn("导入 CSV", 388);
            btnAdd.Click += delegate { studentGrid.Rows.Add(); studentGrid.CurrentCell = studentGrid.Rows[studentGrid.Rows.Count - 1].Cells[0]; };
            btnDel.Click += delegate
            {
                foreach (DataGridViewRow r in studentGrid.SelectedRows.Cast<DataGridViewRow>().ToList())
                    if (!r.IsNewRow) studentGrid.Rows.Remove(r);
            };
            btnImpX.Click += delegate { ImportFile("xlsx"); };
            btnImpC.Click += delegate { ImportFile("csv"); };
            topPanel.Controls.AddRange(new Control[] { btnAdd, btnDel, btnImpX, btnImpC });

            page1.Controls.Add(studentGrid);
            page1.Controls.Add(topPanel);

            // ================= tab 2：班级名称 =================
            var page2 = new TabPage("班级名称");
            classGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                BackgroundColor = Color.White,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };
            classGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "班级号", Name = "classId", FillWeight = 60 });
            classGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "显示名称", Name = "display", FillWeight = 140 });
            LoadClasses(data);
            var hint2 = new Label
            {
                Text = "班级号与显示名称的对应关系（如 19 → 示例十九班），可自由修改；新增班级会自动以“X班”命名",
                Dock = DockStyle.Top,
                Height = 32,
                ForeColor = Color.FromArgb(90, 110, 138),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(6, 0, 0, 0)
            };
            page2.Controls.Add(classGrid);
            page2.Controls.Add(hint2);

            // ================= tab 3：语音设置 =================
            var page3 = new TabPage("语音设置");
            onlineChk = new CheckBox
            {
                Text = "使用在线语音（需联网；断网/失败自动切换本地语音）",
                Location = new Point(24, 26),
                AutoSize = true,
                Checked = AppConfig.TtsOnline
            };
            onlineChk.CheckedChanged += delegate
            {
                AppConfig.TtsOnline = onlineChk.Checked;
                AppConfig.Save();
                if (owner != null) owner.SetTtsOnline(onlineChk.Checked);
            };
            var sourceLabel = new Label { Text = "在线语音源：", Location = new Point(24, 68), AutoSize = true };
            sourceCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(134, 64),
                Size = new Size(360, 26)
            };
            sourceCombo.Items.AddRange(new object[]
            {
                "自动（微软神经语音 → 百度 → 本地）",
                "微软神经语音（Azure 直连，内置）",
                "Edge 直连（WebSocket，部分网络不可用）",
                "百度翻译（免密钥）",
                "仅本地（SAPI）"
            });
            if (AppConfig.TtsSource == "azure") sourceCombo.SelectedIndex = 1;
            else if (AppConfig.TtsSource == "edge") sourceCombo.SelectedIndex = 2;
            else if (AppConfig.TtsSource == "baidu") sourceCombo.SelectedIndex = 3;
            else if (AppConfig.TtsSource == "off") sourceCombo.SelectedIndex = 4;
            else sourceCombo.SelectedIndex = 0;
            sourceCombo.SelectedIndexChanged += delegate
            {
                switch (sourceCombo.SelectedIndex)
                {
                    case 1: AppConfig.TtsSource = "azure"; break;
                    case 2: AppConfig.TtsSource = "edge"; break;
                    case 3: AppConfig.TtsSource = "baidu"; break;
                    case 4: AppConfig.TtsSource = "off"; break;
                    default: AppConfig.TtsSource = "auto"; break;
                }
                AppConfig.Save();
            };
            var voiceLabel = new Label { Text = "在线语音音色：", Location = new Point(24, 110), AutoSize = true };
            voiceCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(134, 106),
                Size = new Size(230, 26)
            };
            voiceCombo.Items.AddRange(new object[] { "晓晓（女声，推荐）", "云希（男声）", "云扬（男声·新闻）" });
            if (AppConfig.TtsVoice.IndexOf("Yunxi", StringComparison.OrdinalIgnoreCase) >= 0) voiceCombo.SelectedIndex = 1;
            else if (AppConfig.TtsVoice.IndexOf("Yunyang", StringComparison.OrdinalIgnoreCase) >= 0) voiceCombo.SelectedIndex = 2;
            else voiceCombo.SelectedIndex = 0;
            voiceCombo.SelectedIndexChanged += delegate
            {
                switch (voiceCombo.SelectedIndex)
                {
                    case 1: AppConfig.TtsVoice = "zh-CN-YunxiNeural"; break;
                    case 2: AppConfig.TtsVoice = "zh-CN-YunyangNeural"; break;
                    default: AppConfig.TtsVoice = "zh-CN-XiaoxiaoNeural"; break;
                }
                AppConfig.Save();
            };
            var note3 = new Label
            {
                Text = "语音会缓存到本机（%LOCALAPPDATA%\\LuckyPicker\\tts），听过的名字再次抽取时无需联网、立即播放。\n" +
                       "提示：微软神经语音走 Azure Speech REST 接口（内置，直连可用），与 Edge TTS 同源音色。\n" +
                       "名单保存在 ProgramData\\LuckyPicker\\students.json，保存后立即生效，无需重启。",
                Location = new Point(24, 156),
                AutoSize = true,
                ForeColor = Color.FromArgb(90, 110, 138)
            };
            page3.Controls.Add(onlineChk);
            page3.Controls.Add(voiceLabel);
            page3.Controls.Add(voiceCombo);
            page3.Controls.Add(note3);

            tabs.TabPages.Add(page1);
            tabs.TabPages.Add(page2);
            tabs.TabPages.Add(page3);

            // ================= 底部 =================
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 58 };
            var btnSave = new Button
            {
                Text = "保存到 students.json",
                Location = new Point(12, 12),
                Size = new Size(190, 34),
                BackColor = Color.FromArgb(59, 130, 246),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei", 10F, FontStyle.Bold)
            };
            var btnClose = new Button { Text = "关闭", Location = new Point(214, 12), Size = new Size(96, 34), FlatStyle = FlatStyle.Flat };
            statusLabel = new Label
            {
                Text = "修改后请点击“保存到 students.json”",
                Location = new Point(330, 22),
                AutoSize = true,
                ForeColor = Color.FromArgb(90, 110, 138)
            };
            btnSave.Click += delegate { Save(); };
            btnClose.Click += delegate { Close(); };
            bottom.Controls.AddRange(new Control[] { btnSave, btnClose, statusLabel });

            Controls.Add(tabs);
            Controls.Add(bottom);
        }

        static Button MakeBtn(string text, int x)
        {
            return new Button
            {
                Text = text,
                Location = new Point(x, 10),
                Size = new Size(106, 28),
                FlatStyle = FlatStyle.Flat
            };
        }

        void LoadStudents(DataFile data)
        {
            studentGrid.Rows.Clear();
            foreach (var s in data.students ?? new List<Student>())
            {
                string g = ImportUtil.NormalizeGender(s.gender ?? "");
                studentGrid.Rows.Add(s.name ?? "", s.classId ?? "", g.Length > 0 ? g : "未知");
            }
        }

        void LoadClasses(DataFile data)
        {
            classGrid.Rows.Clear();
            if (data.classes == null) return;
            foreach (var kv in data.classes.OrderBy(k =>
            {
                int n;
                return int.TryParse(k.Key, out n) ? n : 999;
            }))
                classGrid.Rows.Add(kv.Key, kv.Value);
        }

        string GetCell(DataGridViewRow r, string colName)
        {
            var v = r.Cells[colName].Value;
            return v == null ? "" : v.ToString();
        }

        void Save()
        {
            var students = new List<Student>();
            int rowNo = 0;
            foreach (DataGridViewRow r in studentGrid.Rows)
            {
                rowNo++;
                string name = GetCell(r, "name").Trim();
                string cls = GetCell(r, "classId").Trim();
                string g = GetCell(r, "gender");
                if (g == "未知") g = "";
                if (name.Length == 0 && cls.Length == 0 && g.Length == 0) continue;
                if (name.Length == 0)
                {
                    MessageBox.Show(this, "第 " + rowNo + " 行：姓名不能为空。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (cls.Length == 0)
                {
                    MessageBox.Show(this, "第 " + rowNo + " 行（" + name + "）：班级不能为空。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                students.Add(new Student { name = name, classId = cls, gender = g });
            }

            var classes = new Dictionary<string, string>();
            foreach (DataGridViewRow r in classGrid.Rows)
            {
                string id = GetCell(r, "classId").Trim();
                string display = GetCell(r, "display").Trim();
                if (id.Length > 0) classes[id] = display.Length > 0 ? display : (id + "班");
            }
            foreach (var id in students.Select(s => s.classId).Distinct())
                if (!classes.ContainsKey(id)) classes[id] = id + "班";

            string path = MainForm.DataPath();
            try
            {
                var ser = new JavaScriptSerializer();
                File.WriteAllText(path, ser.Serialize(new DataFile { classes = classes, students = students }));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存失败：" + ex.Message + "\n\n名单保存在 " + path + "，请确认该目录可写。",
                    "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (owner != null) owner.ReloadData();
            statusLabel.Text = "已保存 " + students.Count + " 名学生、 " + classes.Count + " 个班级 ✓";
            statusLabel.ForeColor = Color.FromArgb(6, 95, 70);
        }

        void ImportFile(string kind)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "导入名单";
                ofd.Filter = kind == "xlsx"
                    ? "Excel 工作簿 (*.xlsx)|*.xlsx|所有文件 (*.*)|*.*"
                    : "CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*";
                if (ofd.ShowDialog(this) != DialogResult.OK) return;

                List<List<string>> rows = null;
                try
                {
                    rows = kind == "xlsx" ? XlsxReader.Read(ofd.FileName) : CsvReader.Read(ofd.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this,
                        "读取文件失败：" + ex.Message + "\n\n提示：旧版 .xls 请先用 Excel 另存为 .xlsx 或 .csv。",
                        "导入失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                if (rows == null || rows.Count == 0)
                {
                    MessageBox.Show(this, "文件中没有找到数据行。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                using (var dlg = new MappingDialog(rows))
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    var students = dlg.Result;
                    if (students == null || students.Count == 0)
                    {
                        MessageBox.Show(this, "没有解析到有效学生（姓名列为空）。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    var res = MessageBox.Show(this,
                        "共解析到 " + students.Count + " 名学生。\n\n是 = 替换现有名单\n否 = 追加到现有名单末尾\n取消 = 放弃导入",
                        "导入", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                    if (res == DialogResult.Cancel) return;
                    if (res == DialogResult.Yes) studentGrid.Rows.Clear();
                    foreach (var s in students)
                        studentGrid.Rows.Add(s.name ?? "", s.classId ?? "",
                            string.IsNullOrEmpty(s.gender) ? "未知" : s.gender);
                    statusLabel.Text = "已导入 " + students.Count + " 名学生，请点击“保存到 students.json”生效";
                    statusLabel.ForeColor = Color.FromArgb(31, 43, 78);
                }
            }
        }
    }

    // ---------------- 导入预览与列匹配对话框 ----------------
    public class MappingDialog : Form
    {
        List<List<string>> rows;
        ComboBox nameCol, classCol, genderCol;
        CheckBox headerChk;
        public List<Student> Result;

        public MappingDialog(List<List<string>> rows)
        {
            this.rows = rows;
            this.Text = "导入预览与列匹配";
            this.ClientSize = new Size(780, 560);
            this.StartPosition = FormStartPosition.CenterParent;
            this.Font = new Font("Microsoft YaHei", 9F);
            try { this.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            int cols = rows.Max(r => r.Count);
            cols = Math.Max(1, Math.Min(cols, 12));
            var norm = rows.Take(50).Select(r =>
            {
                var l = r.ToList();
                while (l.Count < cols) l.Add("");
                if (l.Count > cols) l.RemoveRange(cols, l.Count - cols);
                return l;
            }).ToList();

            var preview = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                BackgroundColor = Color.White,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells
            };
            for (int c = 0; c < cols; c++) preview.Columns.Add("col" + c, "第" + (c + 1) + "列");
            foreach (var r in norm) preview.Rows.Add(r.ToArray());

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 178 };
            int y = 12;
            var l1 = new Label { Text = "姓名列：", Location = new Point(12, y + 5), AutoSize = true, Font = new Font("Microsoft YaHei", 9F, FontStyle.Bold) };
            var l2 = new Label { Text = "班级列：", Location = new Point(288, y + 5), AutoSize = true, Font = new Font("Microsoft YaHei", 9F, FontStyle.Bold) };
            var l3 = new Label { Text = "性别列：", Location = new Point(564, y + 5), AutoSize = true, Font = new Font("Microsoft YaHei", 9F, FontStyle.Bold) };
            nameCol = MakeColCombo(76, y, cols);
            classCol = MakeColCombo(352, y, cols);
            genderCol = MakeColCombo(628, y, cols);
            headerChk = new CheckBox { Text = "首行为表头（导入时跳过第一行）", Location = new Point(12, y + 52), AutoSize = true };

            int n = ImportUtil.FindColumn(rows, "姓名", "名字", "名称", "学生", "name");
            int c2 = ImportUtil.FindColumn(rows, "班级", "班", "class");
            int g2 = ImportUtil.FindColumn(rows, "性别", "gender", "sex");
            bool hasHeader = n >= 0 || c2 >= 0 || g2 >= 0;
            headerChk.Checked = hasHeader;
            nameCol.SelectedIndex = (n >= 0 ? n : 0);
            classCol.SelectedIndex = (c2 >= 0 ? c2 : (cols > 1 ? 1 : 0));
            genderCol.SelectedIndex = (g2 >= 0 ? g2 : (cols > 2 ? 2 : 0));

            var btnOk = new Button
            {
                Text = "确定导入",
                Location = new Point(12, y + 88),
                Size = new Size(130, 38),
                BackColor = Color.FromArgb(59, 130, 246),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei", 10F, FontStyle.Bold)
            };
            var btnCancel = new Button { Text = "取消", Location = new Point(152, y + 88), Size = new Size(96, 38), FlatStyle = FlatStyle.Flat };
            btnOk.Click += delegate { BuildResult(); };
            btnCancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };

            bottom.Controls.AddRange(new Control[] { l1, nameCol, l2, classCol, l3, genderCol, headerChk, btnOk, btnCancel });
            Controls.Add(preview);
            Controls.Add(bottom);
        }

        static ComboBox MakeColCombo(int x, int y, int cols)
        {
            var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(x, y), Size = new Size(196, 26) };
            for (int c = 0; c < cols; c++) cb.Items.Add("第" + (c + 1) + "列");
            return cb;
        }

        void BuildResult()
        {
            var list = new List<Student>();
            int start = headerChk.Checked ? 1 : 0;
            int nC = nameCol.SelectedIndex, cC = classCol.SelectedIndex, gC = genderCol.SelectedIndex;
            for (int i = start; i < rows.Count; i++)
            {
                string name = Get(rows[i], nC);
                if (name.Length == 0) continue;
                string cls = ImportUtil.NormalizeClass(Get(rows[i], cC));
                string g = ImportUtil.NormalizeGender(Get(rows[i], gC));
                list.Add(new Student { name = name, classId = cls, gender = g });
            }
            Result = list;
            DialogResult = DialogResult.OK;
            Close();
        }

        static string Get(List<string> r, int idx)
        {
            return (idx >= 0 && idx < r.Count) ? (r[idx] ?? "").Trim() : "";
        }
    }
}
