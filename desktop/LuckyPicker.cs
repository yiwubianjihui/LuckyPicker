// ================================================================
// YBH幸运摇人器  —  Win32 桌面应用 (C# / .NET Framework WinForms)
// 班级筛选 / 性别 / 不重复模式 / 抽一人 / 连抽 / 屏蔽名单 /
// 中文语音播报 / 抽选记录 / 版本查看与检查更新
// 数据文件：与程序同目录的 students.json（可编辑），缺失时使用内置默认数据
// ================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Web.Script.Serialization;

[assembly: AssemblyTitle("YBH幸运摇人器")]
[assembly: AssemblyProduct("YBH幸运摇人器")]
[assembly: AssemblyCompany("YBH")]
[assembly: AssemblyVersion("26.11.0.0")]
[assembly: AssemblyFileVersion("26.11.0.0")]
[assembly: AssemblyInformationalVersion("26H2 Build 14")]

namespace LuckyPicker
{
    // ---------------- 数据模型 ----------------
    public class Student
    {
        public string name { get; set; }
        public string classId { get; set; }
        public string gender { get; set; }
    }

    public class DataFile
    {
        public Dictionary<string, string> classes { get; set; }
        public List<Student> students { get; set; }
    }

    // ---------------- 绘制工具 ----------------
    static class UI
    {
        public static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            if (radius <= 0) radius = 1;
            int d = radius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Rectangle r, int radius, Color c)
        {
            using (var b = new SolidBrush(c))
            using (var p = RoundedRect(r, radius))
                g.FillPath(b, p);
        }

        public static void DrawRound(Graphics g, Rectangle r, int radius, Color c, float width)
        {
            using (var p = new Pen(c, width))
            using (var path = RoundedRect(r, radius))
                g.DrawPath(p, path);
        }

        public static void FillGradient(Graphics g, Rectangle r, Color c1, Color c2, float angle)
        {
            using (var b = new LinearGradientBrush(r, c1, c2, angle))
                g.FillRectangle(b, r);
        }

        public static void DrawGradientText(Graphics g, string text, Font font, Rectangle r,
            Color c1, Color c2, StringFormat fmt)
        {
            using (var path = new GraphicsPath())
            using (var brush = new LinearGradientBrush(r, c1, c2, LinearGradientMode.Horizontal))
            {
                path.AddString(text, font.FontFamily, (int)font.Style, font.SizeInPoints, r, fmt);
                g.FillPath(brush, path);
            }
        }

        public static void DrawCentered(Graphics g, string text, Font font, Color c, Rectangle r)
        {
            using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (var b = new SolidBrush(c))
                g.DrawString(text, font, b, r, fmt);
        }
    }

    // ---------------- 屏蔽名单的标签 (chip) ----------------
    class Chip : Control
    {
        public string PersonName;
        public event Action<Chip> RemoveClicked;
        private bool hover;
        private Rectangle closeRect;

        public Chip(string name)
        {
            PersonName = name;
            this.Font = new Font("Microsoft YaHei", 10F);
            this.BackColor = Color.FromArgb(254, 249, 230);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            this.Width = TextRenderer.MeasureText(name, this.Font).Width + 52;
            this.Height = 30;
            this.Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            UI.FillRound(g, r, 15, Color.FromArgb(241, 245, 249));
            UI.DrawRound(g, r, 15, Color.FromArgb(203, 213, 225), 1f);
            using (var b = new SolidBrush(Color.FromArgb(31, 43, 78)))
                g.DrawString(PersonName, this.Font, b, 14, 6);
            closeRect = new Rectangle(Width - 28, 4, 22, 22);
            UI.FillRound(g, closeRect, 11, hover ? Color.FromArgb(239, 68, 68) : Color.FromArgb(220, 38, 38));
            UI.DrawCentered(g, "×", new Font("Microsoft YaHei", 9F, FontStyle.Bold), Color.White, closeRect);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && closeRect.Contains(e.Location))
            {
                if (RemoveClicked != null) RemoveClicked(this);
            }
        }
    }

    // ---------------- 班级选择模态框 ----------------
    class ClassModal : Form
    {
        public string SelectedClassId;
        public ClassModal(List<string> classIds, Dictionary<string, string> names)
        {
            this.Text = "选择班级 - YBH幸运摇人器";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.ClientSize = new Size(470, 380);
            this.MinimizeBox = false;
            this.MaximizeBox = false;
            this.BackColor = Color.White;
            this.Font = new Font("Microsoft YaHei", 9F);

            var lbl = new Label
            {
                Text = "请选择要抽取的班级",
                Font = new Font("Microsoft YaHei", 15F, FontStyle.Bold),
                ForeColor = Color.FromArgb(31, 43, 78),
                AutoSize = true,
                Location = new Point(24, 24)
            };
            var sub = new Label
            {
                Text = "YBH幸运摇人器 · 进入后可随时切换班级",
                Font = new Font("Microsoft YaHei", 8.5F),
                ForeColor = Color.FromArgb(90, 110, 138),
                AutoSize = true,
                Location = new Point(26, 58)
            };
            var flow = new FlowLayoutPanel
            {
                Location = new Point(24, 92),
                Size = new Size(416, 250),
                AutoScroll = true,
                WrapContents = true,
                BackColor = Color.White
            };

            foreach (var id in classIds)
            {
                string display = names.ContainsKey(id) ? names[id] : (id + "班");
                var btn = new ClassButton(id, display);
                btn.Click += delegate
                {
                    SelectedClassId = id;
                    this.DialogResult = DialogResult.OK;
                    this.Close();
                };
                flow.Controls.Add(btn);
            }

            Controls.Add(lbl);
            Controls.Add(sub);
            Controls.Add(flow);
        }
    }

    class ClassButton : Control
    {
        private string cls, display;
        private bool hover;
        public ClassButton(string cls, string display)
        {
            this.cls = cls;
            this.display = display;
            this.Size = new Size(120, 64);
            this.Cursor = Cursors.Hand;
            this.Font = new Font("Microsoft YaHei", 12F, FontStyle.Bold);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(1, 1, Width - 3, Height - 3);
            UI.FillRound(g, r, 20, hover ? Color.FromArgb(59, 130, 246) : Color.FromArgb(238, 242, 250));
            var mainColor = hover ? Color.White : Color.FromArgb(31, 43, 78);
            var subColor = hover ? Color.FromArgb(219, 234, 254) : Color.FromArgb(90, 110, 138);
            using (var b = new SolidBrush(mainColor))
                g.DrawString(cls + "班", this.Font, b, new Rectangle(4, 10, Width - 8, 26));
            using (var b = new SolidBrush(subColor))
                g.DrawString(display, new Font("Microsoft YaHei", 8.5F), b, new Rectangle(4, 38, Width - 8, 20));
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location))
                this.OnClick(EventArgs.Empty);
        }
    }

    // ---------------- 主窗体 ----------------
    public class MainForm : Form
    {
        // 布局常量（96 DPI 逻辑坐标）
        static readonly Rectangle CARD   = new Rectangle(24, 24, 852, 772);
        static readonly Rectangle FILTER = new Rectangle(60, 140, 780, 150);
        static readonly Rectangle RESULT = new Rectangle(60, 306, 780, 220);
        static readonly Rectangle BLOCK  = new Rectangle(60, 624, 780, 130);

        // 颜色
        static readonly Color C_BG1 = Color.FromArgb(240, 247, 255);
        static readonly Color C_BG2 = Color.FromArgb(226, 232, 240);
        static readonly Color C_NAVY = Color.FromArgb(30, 41, 59);
        static readonly Color C_BLUE = Color.FromArgb(59, 130, 246);
        static readonly Color C_INK = Color.FromArgb(31, 43, 78);
        static readonly Color C_GRAY = Color.FromArgb(90, 110, 138);
        static readonly Color C_FILTER_BG = Color.FromArgb(245, 248, 253);
        static readonly Color C_BLOCK_BG = Color.FromArgb(254, 249, 230);

        // 字体
        Font titleFont, subFont, labelFont, nameFont, chipFont, hintFont, btnFont;

        // 数据与状态
        DataFile data;
        List<Student> allStudents;
        public List<string> classIds;
        public Dictionary<string, string> classNames;
        string currentClassId;
        string genderFilter = "all";     // all / male / female
        bool noRepeat = true;
        HashSet<string> blocked = new HashSet<string>();
        List<Student> remainPool = new List<Student>();
        string lastPickedName = null;
        List<Student> lastMulti = new List<Student>();
        string hint = "点击下方按钮开始抽取";
        string voiceStatus = "♪ 正在加载语音引擎...";
        Random rnd = new Random();

        // 动画
        Timer animTimer;
        int animTicks = 0;
        const int animTotal = 16;
        string animName = null;
        Student pendingSingle = null;
        List<Student> pendingMulti = null;

        // 语音
        TtsEngine tts;

        // 悬浮球与启动状态
        FloatingBallForm ball;
        bool classChosen;
        /// <summary>true = 开机自启动静默启动（/min）：只显示悬浮球，不弹主窗口。</summary>
        public bool BootMinimized { get; set; }

        // 托盘与退出控制
        NotifyIcon trayIcon;
        bool reallyExit;

        // 控件
        ComboBox classCombo;
        TextBox blockInput;
        FlowLayoutPanel chipPanel;

        // 命中按钮状态
        enum BtnId { GenderAll, GenderMale, GenderFemale, Toggle, Pick, Multi, Reset, AddBlock, Settings, Replay }
        Dictionary<BtnId, Rectangle> btnRect = new Dictionary<BtnId, Rectangle>();
        HashSet<BtnId> hoverBtns = new HashSet<BtnId>();

        public MainForm()
        {
            data = LoadData();
            allStudents = data.students ?? new List<Student>();
            classNames = data.classes ?? new Dictionary<string, string>();
            classIds = BuildClassIds();
            currentClassId = classIds[0];

            InitFonts();
            InitForm();
            InitControls();
            InitButtons();
            animTimer = new Timer { Interval = 50 };
            animTimer.Tick += animTimer_Tick;
            InitSpeech();
            ResetPoolSilent();
            InitBall();
            InitTray();
            ListenShowEvent();
            CheckUpdateSilent();
        }

        // 监听「新实例请求唤起主窗口」命名事件：重复启动程序时，把已运行实例的主窗口带到前台
        void ListenShowEvent()
        {
            try
            {
                Task.Run((Action)delegate
                {
                    try
                    {
                        using (var ev = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, AppVersion.ShowMainEvent))
                        {
                            while (true)
                            {
                                ev.WaitOne();
                                if (IsDisposed) return;
                                try { BeginInvoke((Action)delegate { ShowMainFromBall(); }); }
                                catch { return; }
                            }
                        }
                    }
                    catch { }
                });
            }
            catch { }
        }

        // 后台静默检查更新：启动后执行，发现新版本时托盘气泡提醒（每日至多一次，不打扰）
        void CheckUpdateSilent()
        {
            try
            {
                Task.Run((Action)delegate
                {
                    try
                    {
                        string today = DateTime.Now.ToString("yyyy-MM-dd");
                        if (string.Equals(AppConfig.LastUpdateCheckDate, today, StringComparison.Ordinal)) return;
                        var res = UpdateManager.Check(AppConfig.UpdateUrl);
                        // 默认地址失效时回退旧地址（历史部署位置）
                        if ((res == null || !res.Success) &&
                            string.Equals(AppConfig.UpdateUrl, AppVersion.UpdateUrlDefault, StringComparison.OrdinalIgnoreCase))
                        {
                            var alt = UpdateManager.Check(AppVersion.UpdateUrlLegacy);
                            if (alt != null && alt.Success) res = alt;
                        }
                        AppConfig.LastUpdateCheckDate = today;
                        AppConfig.Save();
                        if (res != null && res.Success && res.HasUpdate && trayIcon != null && !IsDisposed)
                        {
                            try
                            {
                                trayIcon.BalloonTipTitle = AppVersion.ProductName + " 有新版本";
                                trayIcon.BalloonTipText = "发现新版本 " + res.LatestVersion +
                                    "，在「设置 → 版本 · 更新」中查看下载。";
                                trayIcon.BalloonTipIcon = ToolTipIcon.Info;
                                trayIcon.ShowBalloonTip(8000);
                            }
                            catch { }
                        }
                    }
                    catch { }
                });
            }
            catch { }
        }

        // ============ 悬浮球 ============
        void InitBall()
        {
            if (BootMinimized)
            {
                // 开机自启：完全不显示主窗口（连启动瞬间都不闪），只留悬浮球
                WindowState = FormWindowState.Minimized;
                ShowInTaskbar = false;
                Hide();
            }
            ball = new FloatingBallForm(this);
            // 独立窗体（不传 owner）：主窗口最小化 / 隐藏时悬浮球仍置顶显示
            if (AppConfig.BallVisible || BootMinimized) ball.Show();
            classChosen = false;
        }

        /// <summary>按配置显示 / 隐藏悬浮球（设置切换后调用）。</summary>
        public void ApplyBallVisibility()
        {
            if (ball == null || ball.IsDisposed) return;
            if (AppConfig.BallVisible) { if (!ball.Visible) ball.Show(); }
            else if (ball.Visible) ball.Hide();
        }

        void NotifyBall(string text)
        {
            try { if (ball != null && ball.Visible) ball.ShowPicked(text); } catch { }
        }

        // —— 供悬浮球调用的公开动作 ——
        /// <summary>单击悬浮球：已选班级则抽一人；未选班级（如开机自启后）弹班级选择菜单。</summary>
        public void OnBallClicked()
        {
            if (!classChosen) { ShowClassMenu(); return; }
            PickOne();
        }

        public void PickOneFromBall()
        {
            OnBallClicked();
        }

        public void PickMultiFromBall()
        {
            if (!classChosen) { ShowClassMenu(); return; }
            PickMultiple(5);
        }

        /// <summary>悬浮球点按弹出「选择班级」小窗（点选后立即抽取，替代全屏弹窗）。</summary>
        void ShowClassMenu()
        {
            var mini = new ClassMiniForm(classIds, classNames, delegate (string cid)
            {
                SetInitialClass(cid);
                PickOne();
            });
            if (ball != null && ball.Visible)
                mini.ShowNear(ball);
            else
            {
                mini.StartPosition = FormStartPosition.CenterScreen;
                mini.Show();
            }
        }

        public void ResetPoolFromBall()
        {
            remainPool = GetCandidates();
            lastPickedName = null;
            lastMulti = new List<Student>();
            hint = remainPool.Count == 0 ? "※ 当前无候选人" : "√ 不重复池已重置，可以开始抽取";
            Invalidate();
        }

        public void ShowMainFromBall()
        {
            EnsureClassChosen();
            ShowInTaskbar = true;
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        void EnsureClassChosen()
        {
            if (classChosen) return;
            using (var modal = new ClassModal(classIds, classNames))
            {
                if (modal.ShowDialog() == DialogResult.OK && modal.SelectedClassId != null)
                    SetInitialClass(modal.SelectedClassId);
                else if (classIds.Count > 0)
                    SetInitialClass(classIds[0]);
            }
        }

        // ============ 托盘与退出 ============
        void InitTray()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("显示主窗口", null, delegate { ShowMainFromBall(); });
            var bootItem = new ToolStripMenuItem("开机自启动")
            {
                CheckOnClick = true,
                Checked = AutoStart.IsEnabled()
            };
            bootItem.Click += delegate
            {
                bool ok = AutoStart.SetEnabled(bootItem.Checked);
                bootItem.Checked = ok && bootItem.Checked;
            };
            menu.Items.Add(bootItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { QuitApp(); });

            trayIcon = new NotifyIcon
            {
                Text = AppVersion.ProductName + " · " + AppVersion.Display,
                ContextMenuStrip = menu,
                Visible = true
            };
            try { trayIcon.Icon = this.Icon; } catch { }
            trayIcon.DoubleClick += delegate { ShowMainFromBall(); };
        }

        /// <summary>主界面「设置」下拉菜单。</summary>
        void ShowSettingsMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("名单管理 · 导入", null, delegate { OpenEditor(); });
            menu.Items.Add("抽选记录", null, delegate { OpenHistory(); });
            menu.Items.Add("版本 · 更新", null, delegate { OpenAbout(); });
            menu.Items.Add(new ToolStripSeparator());
            var bootItem = new ToolStripMenuItem("开机自启动")
            {
                CheckOnClick = true,
                Checked = AutoStart.IsEnabled()
            };
            bootItem.Click += delegate
            {
                bool ok = AutoStart.SetEnabled(bootItem.Checked);
                bootItem.Checked = ok && bootItem.Checked;
                if (!ok)
                    MessageBox.Show(this, "设置开机自启动失败（注册表写入被拒绝）。", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };
            menu.Items.Add(bootItem);
            var ballItem = new ToolStripMenuItem("桌面悬浮球")
            {
                CheckOnClick = true,
                Checked = AppConfig.BallVisible
            };
            ballItem.Click += delegate
            {
                AppConfig.BallVisible = ballItem.Checked;
                AppConfig.Save();
                ApplyBallVisibility();
            };
            menu.Items.Add(ballItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出程序", null, delegate { QuitApp(); });

            var r = btnRect[BtnId.Settings];
            menu.Show(this, new Point(r.Left, r.Bottom + 2));
        }

        /// <summary>真正退出（托盘 / 悬浮球 / 设置菜单「退出」调用）。</summary>
        public void QuitApp()
        {
            reallyExit = true;
            try { if (trayIcon != null) trayIcon.Visible = false; } catch { }
            Application.Exit();   // 触发 FormClosing；reallyExit=true 不再拦截
        }

        void InitFonts()
        {
            titleFont = new Font("Microsoft YaHei", 27F, FontStyle.Bold);
            subFont = new Font("Microsoft YaHei", 10F);
            labelFont = new Font("Microsoft YaHei", 9.5F, FontStyle.Bold);
            nameFont = new Font("Microsoft YaHei", 46F, FontStyle.Bold);
            chipFont = new Font("Microsoft YaHei", 12F, FontStyle.Bold);
            hintFont = new Font("Microsoft YaHei", 9.5F);
            btnFont = new Font("Microsoft YaHei", 14.5F, FontStyle.Bold);
        }

        void InitForm()
        {
            this.Text = AppVersion.ProductName + " · " + AppVersion.Display;
            this.ClientSize = new Size(900, 820);
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = C_BG1;
            this.Font = new Font("Microsoft YaHei", 9F);
            this.DoubleBuffered = true;
            this.KeyPreview = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            try { this.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }
        }

        void InitControls()
        {
            // 班级下拉框
            classCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei", 10F),
                Location = new Point(86, 180),
                Size = new Size(220, 34),
                BackColor = Color.White,
                ForeColor = C_INK
            };
            foreach (var id in classIds)
                classCombo.Items.Add(classNames.ContainsKey(id) ? classNames[id] : (id + "班"));
            classCombo.SelectedIndex = 0;
            classCombo.SelectedIndexChanged += delegate
            {
                int i = classCombo.SelectedIndex;
                if (i >= 0 && i < classIds.Count)
                {
                    currentClassId = classIds[i];
                    ResetResultAndPool();
                    QueueClassWarmup();
                }
            };
            Controls.Add(classCombo);

            // 屏蔽名单输入框
            blockInput = new TextBox
            {
                Font = new Font("Microsoft YaHei", 10F),
                Location = new Point(86, 664),
                Size = new Size(300, 30),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            blockInput.KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; AddBlock(); }
            };
            Controls.Add(blockInput);

            // 屏蔽标签容器
            chipPanel = new FlowLayoutPanel
            {
                Location = new Point(86, 706),
                Size = new Size(720, 40),
                BackColor = C_BLOCK_BG,
                WrapContents = true,
                AutoScroll = true
            };
            Controls.Add(chipPanel);
        }

        void InitButtons()
        {
            // 性别
            btnRect[BtnId.GenderAll] = new Rectangle(336, 180, 104, 34);
            btnRect[BtnId.GenderMale] = new Rectangle(450, 180, 84, 34);
            btnRect[BtnId.GenderFemale] = new Rectangle(544, 180, 84, 34);
            // 不重复开关
            btnRect[BtnId.Toggle] = new Rectangle(770, 180, 54, 28);
            // 动作按钮
            btnRect[BtnId.Pick] = new Rectangle(112, 546, 330, 62);
            btnRect[BtnId.Multi] = new Rectangle(458, 546, 330, 62);
            btnRect[BtnId.Reset] = new Rectangle(696, 468, 120, 34);
            btnRect[BtnId.AddBlock] = new Rectangle(396, 664, 120, 34);
            // 顶部：设置按钮（集成名单管理 / 抽选记录 / 版本更新 / 开机自启 / 悬浮球）
            btnRect[BtnId.Settings] = new Rectangle(700, 86, 140, 32);
            // 结果区“重播”小按钮
            btnRect[BtnId.Replay] = new Rectangle(342, 320, 56, 26);
        }

        // ============ 数据加载 ============
        /// <summary>名单数据目录：%ProgramData%\LuckyPicker（系统目录安装后仍可写；便携时自动回退）。</summary>
        public static string DataDir()
        {
            try
            {
                string pd = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "LuckyPicker");
                Directory.CreateDirectory(pd);
                return pd;
            }
            catch { return AppDomain.CurrentDomain.BaseDirectory; }
        }

        /// <summary>名单数据文件完整路径（编辑器保存 / 主程序读取共用）。</summary>
        public static string DataPath() { return Path.Combine(DataDir(), "students.json"); }

        DataFile LoadData()
        {
            string json = null;
            string pdPath = DataPath();
            // 1) 首选 ProgramData 数据目录
            if (File.Exists(pdPath))
            {
                try { json = File.ReadAllText(pdPath); } catch { }
            }
            // 2) 便携场景：exe 目录（读到后同步到数据目录）
            if (string.IsNullOrWhiteSpace(json))
            {
                string exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "students.json");
                if (File.Exists(exePath))
                {
                    try
                    {
                        json = File.ReadAllText(exePath);
                        try { File.WriteAllText(pdPath, json); } catch { }
                    }
                    catch { }
                }
            }
            // 3) 内嵌默认名单（写入数据目录备用）
            if (string.IsNullOrWhiteSpace(json))
            {
                var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("LuckyPicker.students.json");
                if (s != null)
                {
                    using (var r = new StreamReader(s)) json = r.ReadToEnd();
                    try { File.WriteAllText(pdPath, json); } catch { }
                }
            }
            if (string.IsNullOrWhiteSpace(json)) return new DataFile();
            try
            {
                var ser = new JavaScriptSerializer();
                return ser.Deserialize<DataFile>(json);
            }
            catch { return new DataFile(); }
        }

        // ============ 语音 ============
        void InitSpeech()
        {
            tts = new TtsEngine(this, delegate (string s)
            {
                voiceStatus = s;
                try { Invalidate(); } catch { }
            });
            voiceStatus = tts.InitStatus();
        }

        void Speak(string text)
        {
            if (tts == null || string.IsNullOrEmpty(text)) return;
            tts.Speak(text);
        }

        void RecordDraw(string mode, List<Student> picked)
        {
            if (picked == null || picked.Count == 0) return;
            try
            {
                var names = picked.Select(s => s.name).Where(n => !string.IsNullOrEmpty(n)).ToList();
                string cls = CurrentClassName();
                Task.Run(() => HistoryStore.Record(cls, mode, names));
            }
            catch { }
        }

        void ReplayLast()
        {
            if (!string.IsNullOrEmpty(lastPickedName))
            {
                Speak(lastPickedName);
                hint = "♪ 正在重播：" + lastPickedName;
            }
            else if (lastMulti != null && lastMulti.Count > 0)
            {
                var names = lastMulti.Select(s => s.name).Where(n => !string.IsNullOrEmpty(n)).ToList();
                Speak(string.Join("、", names));
                hint = "♪ 正在重播上次连抽结果";
            }
            else
            {
                hint = "※ 还没有抽选结果，先抽一次吧";
            }
            Invalidate();
        }

        // ============ 候选与池 ============
        List<string> BuildClassIds()
        {
            var ids = allStudents.Select(s => s.classId).Where(x => !string.IsNullOrEmpty(x))
                .Distinct().OrderBy(id => { int n; return int.TryParse(id, out n) ? n : 999; }).ToList();
            // 空名单安装包：没有任何学生时，仍显示 classes 中配置的班级（如 18/19/20）
            if (ids.Count == 0 && classNames != null)
                ids = classNames.Keys.OrderBy(id => { int n; return int.TryParse(id, out n) ? n : 999; }).ToList();
            if (ids.Count == 0) ids.Add("1");
            return ids;
        }

        string CurrentClassName()
        {
            return classNames.ContainsKey(currentClassId) ? classNames[currentClassId] : (currentClassId + "班");
        }

        List<Student> GetCandidates()
        {
            return allStudents.Where(s =>
                s.classId == currentClassId &&
                (genderFilter == "all" ||
                 (genderFilter == "male" ? s.gender == "男" : s.gender == "女")) &&
                !blocked.Contains(s.name)).ToList();
        }

        void ResetPoolSilent()
        {
            remainPool = GetCandidates();
        }

        void ResetResultAndPool()
        {
            lastPickedName = null;
            lastMulti = new List<Student>();
            ResetPoolSilent();
            UpdateHintAfterFilter();
            Invalidate();
        }

        void UpdateHintAfterFilter()
        {
            var cands = GetCandidates();
            if (cands.Count == 0) hint = "※ 当前无候选人，请调整筛选或屏蔽";
            else hint = "√ 条件已更新，剩余池已刷新";
        }

        // ============ 抽取逻辑 ============
        void PickOne()
        {
            var cands = GetCandidates();
            List<Student> source = noRepeat ? remainPool : cands;
            if (source.Count == 0)
            {
                if (noRepeat && cands.Count > 0)
                {
                    if (MessageBox.Show("当前不重复池已空，是否重置并继续抽取？", "提示",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    {
                        remainPool = new List<Student>(cands);
                    }
                    else return;
                }
                else
                {
                    hint = "※ 无候选人，无法抽取";
                    Invalidate();
                    return;
                }
            }
            source = noRepeat ? remainPool : cands;
            if (source.Count == 0) { hint = "※ 无候选人，无法抽取"; Invalidate(); return; }

            pendingSingle = source[rnd.Next(source.Count)];
            pendingMulti = null;
            animTicks = 0;
            animName = null;
            animTimer.Start();
        }

        void PickMultiple(int count)
        {
            var cands = GetCandidates();
            List<Student> source = noRepeat ? remainPool : cands;
            if (source.Count == 0)
            {
                if (noRepeat && cands.Count > 0)
                {
                    if (MessageBox.Show("当前不重复池已空，是否重置并继续连抽？", "提示",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                        remainPool = new List<Student>(cands);
                    else return;
                }
                else { hint = "※ 无候选人，无法连抽"; Invalidate(); return; }
            }
            source = noRepeat ? remainPool : cands;
            if (source.Count == 0) { hint = "※ 无候选人，无法连抽"; Invalidate(); return; }

            var pool = new List<Student>(source);
            var chosen = new List<Student>();
            int limit = Math.Min(count, pool.Count);
            for (int i = 0; i < limit; i++)
            {
                int idx = rnd.Next(pool.Count);
                chosen.Add(pool[idx]);
                pool.RemoveAt(idx);
            }
            pendingMulti = chosen;
            pendingSingle = null;
            animTicks = 0;
            animName = null;
            animTimer.Start();
        }

        void animTimer_Tick(object sender, EventArgs e)
        {
            animTicks++;
            var cands = GetCandidates();
            if (cands.Count > 0)
                animName = cands[rnd.Next(cands.Count)].name;
            Invalidate(RESULT);

            if (animTicks >= animTotal)
            {
                animTimer.Stop();
                if (pendingMulti != null) FinishMulti(pendingMulti);
                else if (pendingSingle != null) FinishSingle(pendingSingle);
            }
        }

        void FinishSingle(Student picked)
        {
            animName = null;
            lastPickedName = picked.name;
            lastMulti = new List<Student>();
            if (noRepeat) remainPool.RemoveAll(s => s.name == picked.name);
            if (noRepeat && remainPool.Count == 0)
                hint = "★ 抽中 " + picked.name + "！剩余池已空，下次将提示重置。";
            else
                hint = "★ 抽中 " + picked.name + "（" + CurrentClassName() + "）";
            Invalidate();
            Speak(picked.name);
            RecordDraw("抽一人", new List<Student> { picked });
            NotifyBall(picked.name);
        }

        void FinishMulti(List<Student> chosen)
        {
            animName = null;
            lastMulti = chosen;
            lastPickedName = null;
            if (noRepeat)
            {
                var names = new HashSet<string>(chosen.Select(s => s.name));
                remainPool.RemoveAll(s => names.Contains(s.name));
            }
            hint = noRepeat && remainPool.Count == 0
                ? "连抽完成，池已空，下次将提示重置"
                : "★ 连抽 " + chosen.Count + " 人完成";
            Invalidate();
            Speak(string.Join("、", chosen.Select(s => s.name)));
            RecordDraw("连抽" + chosen.Count + "人", chosen);
            NotifyBall(string.Join("、", chosen.Select(s => s.name)));
        }

        void ResetPool()
        {
            if (MessageBox.Show("确定要重置不重复池吗？这将恢复所有未抽候选人。", "提示",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            remainPool = GetCandidates();
            lastPickedName = null;
            lastMulti = new List<Student>();
            hint = remainPool.Count == 0 ? "※ 当前无候选人" : "√ 不重复池已重置，可以开始抽取";
            Invalidate();
        }

        // ============ 屏蔽名单 ============
        void AddBlock()
        {
            string name = blockInput.Text.Trim();
            if (name.Length == 0) return;
            if (!allStudents.Any(s => s.name == name)) { hint = "未找到该学生"; Invalidate(); return; }
            if (blocked.Contains(name)) { blockInput.Clear(); return; }
            blocked.Add(name);
            blockInput.Clear();
            RefreshChips();
            ResetResultAndPool();
        }

        void RefreshChips()
        {
            chipPanel.Controls.Clear();
            foreach (var name in blocked)
            {
                var chip = new Chip(name);
                chip.RemoveClicked += delegate (Chip c)
                {
                    blocked.Remove(c.PersonName);
                    RefreshChips();
                    ResetResultAndPool();
                };
                chipPanel.Controls.Add(chip);
            }
        }

        // ============ 绘制 ============
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            // 背景
            UI.FillGradient(g, ClientRectangle, C_BG1, C_BG2, 90f);

            // 卡片
            UI.FillRound(g, CARD, 40, Color.FromArgb(250, 255, 255, 255));
            UI.DrawRound(g, CARD, 40, Color.FromArgb(60, 59, 130, 246), 1f);

            // 标题
            using (var titleFmt = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Near, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                UI.DrawGradientText(g, AppVersion.ProductName, titleFont, new Rectangle(60, 38, 470, 44), C_INK, Color.FromArgb(44, 62, 109), titleFmt);

            // 副标题（含版本信息）
            using (var subFmt = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Near, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
            using (var b = new SolidBrush(C_GRAY))
                g.DrawString("智能抽人 · 不重复 · 屏蔽 · 连抽 · 语音播报 · 版本 " + AppVersion.Display, subFont, b, new Rectangle(62, 86, 330, 18), subFmt);

            // 当前班级横幅
            var bannerRect = new Rectangle(560, 38, 280, 30);
            UI.FillRound(g, bannerRect, 15, Color.FromArgb(238, 242, 250));
            using (var b = new SolidBrush(Color.FromArgb(44, 82, 130)))
                g.DrawString("● 当前班级：" + CurrentClassName(), subFont, b, bannerRect, new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });

            // 筛选面板
            UI.FillRound(g, FILTER, 28, C_FILTER_BG);
            UI.DrawRound(g, FILTER, 28, Color.FromArgb(203, 213, 225), 1f);
            using (var b = new SolidBrush(Color.FromArgb(91, 110, 140)))
            {
                g.DrawString("班级", labelFont, b, 86, 156);
                g.DrawString("性别", labelFont, b, 336, 156);
                g.DrawString("不重复模式", labelFont, b, 660, 186);
            }

            // 工具栏 + 性别 + 开关 + 动作按钮（全部命中绘制）
            DrawToolbarButtons(g);
            DrawGenderButtons(g);
            DrawToggle(g);
            DrawActionButtons(g);
            DrawResetButton(g);
            DrawAddBlockButton(g);

            // 结果面板
            UI.FillRound(g, RESULT, 40, Color.White);
            UI.DrawRound(g, RESULT, 40, Color.FromArgb(80, 59, 130, 246), 1f);

            // 语音徽章 + 重播按钮
            var voiceRect = new Rectangle(86, 320, 248, 26);
            UI.FillRound(g, voiceRect, 13, Color.FromArgb(236, 253, 245));
            using (var b = new SolidBrush(Color.FromArgb(6, 95, 70)))
            using (var vfmt = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(voiceStatus, hintFont, b, new Rectangle(94, 320, 236, 26), vfmt);
            DrawReplayButton(g);

            // 大名字 / 连抽结果
            if (lastMulti.Count > 0)
            {
                DrawMultiChips(g);
            }
            else
            {
                string shown = animName != null ? animName : (lastPickedName ?? "——");
                var nameRect = new Rectangle(60, 352, 780, 96);
                var nfmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                UI.DrawGradientText(g, shown, nameFont, nameRect, C_INK, Color.FromArgb(44, 82, 130), nfmt);
            }

            // 提示 + 候选池统计
            using (var b = new SolidBrush(C_GRAY))
                g.DrawString(hint, hintFont, b, 86, 474);
            DrawPoolStats(g);

            // 屏蔽面板
            UI.FillRound(g, BLOCK, 28, C_BLOCK_BG);
            UI.DrawRound(g, BLOCK, 28, Color.FromArgb(255, 237, 213), 1f);
            using (var b = new SolidBrush(C_INK))
                g.DrawString("屏蔽名单（被屏蔽学生不参与抽取）", new Font("Microsoft YaHei", 10F, FontStyle.Bold), b, 86, 636);
        }

        void DrawGenderButtons(Graphics g)
        {
            DrawPill(g, BtnId.GenderAll, "都抽", genderFilter == "all");
            DrawPill(g, BtnId.GenderMale, "男生", genderFilter == "male");
            DrawPill(g, BtnId.GenderFemale, "女生", genderFilter == "female");
        }

        void DrawPill(Graphics g, BtnId id, string text, bool checked_)
        {
            Rectangle r = btnRect[id];
            bool hover = hoverBtns.Contains(id);
            Color bg = checked_ ? C_BLUE : (hover ? Color.FromArgb(219, 234, 254) : Color.White);
            Color fg = checked_ ? Color.White : C_INK;
            UI.FillRound(g, r, 17, bg);
            if (!checked_) UI.DrawRound(g, r, 17, Color.FromArgb(203, 213, 225), 1f);
            using (var f = new Font("Microsoft YaHei", 9.5F))
            using (var b = new SolidBrush(fg))
                g.DrawString(text, f, b, r, new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
        }

        void DrawToggle(Graphics g)
        {
            Rectangle r = btnRect[BtnId.Toggle];
            bool on = noRepeat;
            bool hover = hoverBtns.Contains(BtnId.Toggle);
            Color track = on ? C_BLUE : Color.FromArgb(203, 213, 225);
            UI.FillRound(g, r, 14, track);
            int knob = on ? r.Right - 24 : r.Left + 2;
            var kr = new Rectangle(knob, r.Top + 2, 24, 24);
            using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, kr);
        }

        void DrawActionButtons(Graphics g)
        {
            DrawBigButton(g, BtnId.Pick, "抽一人", C_NAVY, Color.FromArgb(15, 23, 42));
            DrawBigButton(g, BtnId.Multi, "连抽五人", Color.FromArgb(45, 62, 110), Color.FromArgb(30, 43, 79));
        }

        void DrawBigButton(Graphics g, BtnId id, string text, Color bg, Color bgHover)
        {
            Rectangle r = btnRect[id];
            bool hover = hoverBtns.Contains(id);
            UI.FillRound(g, r, 31, hover ? bgHover : bg);
            using (var b = new SolidBrush(Color.White))
                g.DrawString(text, btnFont, b, r, new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
        }

        void DrawResetButton(Graphics g)
        {
            Rectangle r = btnRect[BtnId.Reset];
            bool hover = hoverBtns.Contains(BtnId.Reset);
            UI.FillRound(g, r, 17, hover ? Color.FromArgb(55, 65, 81) : Color.FromArgb(75, 85, 99));
            using (var f = new Font("Microsoft YaHei", 9F))
            using (var b = new SolidBrush(Color.White))
                g.DrawString("重置不重复池", f, b, r, new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
        }

        void DrawAddBlockButton(Graphics g)
        {
            Rectangle r = btnRect[BtnId.AddBlock];
            bool hover = hoverBtns.Contains(BtnId.AddBlock);
            UI.FillRound(g, r, 17, hover ? Color.FromArgb(55, 65, 81) : Color.FromArgb(71, 85, 105));
            using (var f = new Font("Microsoft YaHei", 9F))
            using (var b = new SolidBrush(Color.White))
                g.DrawString("屏蔽此人", f, b, r, new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
        }

        void DrawToolbarButtons(Graphics g)
        {
            DrawToolButton(g, BtnId.Settings, "设置 ▼");
        }

        void DrawToolButton(Graphics g, BtnId id, string text)
        {
            Rectangle r = btnRect[id];
            bool hover = hoverBtns.Contains(id);
            UI.FillRound(g, r, 16, hover ? Color.FromArgb(219, 234, 254) : Color.White);
            UI.DrawRound(g, r, 16, Color.FromArgb(59, 130, 246), 1f);
            using (var f = new Font("Microsoft YaHei", 9.5F))
            using (var b = new SolidBrush(Color.FromArgb(37, 99, 235)))
            using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(text, f, b, r, fmt);
        }

        void DrawReplayButton(Graphics g)
        {
            Rectangle r = btnRect[BtnId.Replay];
            bool hover = hoverBtns.Contains(BtnId.Replay);
            UI.FillRound(g, r, 13, hover ? Color.FromArgb(37, 99, 235) : Color.FromArgb(219, 234, 254));
            using (var f = new Font("Microsoft YaHei", 8F))
            using (var b = new SolidBrush(hover ? Color.White : Color.FromArgb(37, 99, 235)))
            using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString("重播", f, b, r, fmt);
        }

        void DrawPoolStats(Graphics g)
        {
            var cands = GetCandidates();
            int total = cands.Count;
            int remain = noRepeat ? remainPool.Count : total;
            var r = new Rectangle(430, 468, 250, 24);
            using (var f = new Font("Microsoft YaHei", 8.5F))
            using (var b = new SolidBrush(Color.FromArgb(71, 85, 105)))
            using (var fmt = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center })
                g.DrawString("候选 " + total + " 人 · 剩余 " + remain + " 人", f, b, r, fmt);
        }

        void DrawMultiChips(Graphics g)
        {
            int pad = 14;
            var sizes = new List<Size>();
            int total = 0;
            foreach (var s in lastMulti)
            {
                SizeF sz = g.MeasureString(s.name, chipFont);
                int w = (int)sz.Width + pad * 2;
                sizes.Add(new Size(w, 38));
                total += w + 12;
            }
            total -= (lastMulti.Count > 0 ? 12 : 0);
            int x = 60 + (780 - total) / 2;
            int y = 360;
            for (int i = 0; i < lastMulti.Count; i++)
            {
                var r = new Rectangle(x, y, sizes[i].Width, 38);
                UI.FillRound(g, r, 19, Color.FromArgb(230, 240, 255));
                UI.DrawRound(g, r, 19, Color.FromArgb(147, 197, 253), 1f);
                using (var b = new SolidBrush(C_INK))
                    g.DrawString(lastMulti[i].name, chipFont, b, r, new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
                x += sizes[i].Width + 12;
            }
        }

        // ============ 鼠标交互 ============
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool overAny = false;
            var newHover = new HashSet<BtnId>();
            foreach (var kv in btnRect)
            {
                if (kv.Value.Contains(e.Location))
                {
                    newHover.Add(kv.Key);
                    overAny = true;
                }
            }
            bool changed = !newHover.SetEquals(hoverBtns);
            hoverBtns = newHover;
            this.Cursor = overAny ? Cursors.Hand : Cursors.Default;
            if (changed) Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hoverBtns.Clear();
            this.Cursor = Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            foreach (var kv in btnRect)
            {
                if (!kv.Value.Contains(e.Location)) continue;
                switch (kv.Key)
                {
                    case BtnId.GenderAll: genderFilter = "all"; break;
                    case BtnId.GenderMale: genderFilter = "male"; break;
                    case BtnId.GenderFemale: genderFilter = "female"; break;
                    case BtnId.Toggle: noRepeat = !noRepeat; break;
                    case BtnId.Pick: PickOne(); return;
                    case BtnId.Multi: PickMultiple(5); return;
                    case BtnId.Reset: ResetPool(); return;
                    case BtnId.AddBlock: AddBlock(); return;
                    case BtnId.Settings: ShowSettingsMenu(); return;
                    case BtnId.Replay: ReplayLast(); return;
                }
                // 性别/开关变化后刷新池
                if (kv.Key == BtnId.GenderAll || kv.Key == BtnId.GenderMale ||
                    kv.Key == BtnId.GenderFemale || kv.Key == BtnId.Toggle)
                {
                    hint = noRepeat ? "√ 条件已更新，剩余池已刷新" : "已切换至可重复模式";
                    ResetResultAndPool();
                }
                Invalidate();
                return;
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            // 输入框内的编辑快捷键 / 回车交给输入框自身，避免误触发全局功能
            if (blockInput != null && blockInput.Focused)
            {
                if (e.Control && (e.KeyCode == Keys.A || e.KeyCode == Keys.C ||
                                  e.KeyCode == Keys.V || e.KeyCode == Keys.X || e.KeyCode == Keys.Z))
                    return;
                if (e.KeyCode == Keys.Enter) return;
            }

            if (e.Control)
            {
                if (e.KeyCode == Keys.M) { e.SuppressKeyPress = true; PickMultiple(5); }
                else if (e.KeyCode == Keys.R) { e.SuppressKeyPress = true; ResetPool(); }
                else if (e.KeyCode == Keys.E) { e.SuppressKeyPress = true; OpenEditor(); }
                else if (e.KeyCode == Keys.H) { e.SuppressKeyPress = true; OpenHistory(); }
                else if (e.KeyCode == Keys.U) { e.SuppressKeyPress = true; OpenAbout(); }
                else if (e.KeyCode == Keys.V) { e.SuppressKeyPress = true; ReplayLast(); }
            }
            else if (e.KeyCode == Keys.Space && (blockInput == null || !blockInput.Focused))
            {
                e.SuppressKeyPress = true;
                PickOne();
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // 设置输入框提示文字
            try { SendMessage(blockInput.Handle, EM_SETCUEBANNER, IntPtr.Zero, "输入学生姓名"); } catch { }
            // 语音预热：后台合成“开始摇人”与当前班级名单，抽取时自然语音秒开
            if (tts != null)
            {
                tts.QueueWarmup(new[] { "开始摇人" });
                QueueClassWarmup();
            }
        }

        void QueueClassWarmup()
        {
            if (tts == null) return;
            var names = allStudents.Where(s => s.classId == currentClassId).Select(s => s.name).ToArray();
            tts.QueueWarmup(names);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // 点击关闭按钮 → 最小化到托盘（悬浮球与托盘图标保持可见），不退出
            if (!reallyExit && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                WindowState = FormWindowState.Minimized;
                Hide();
                ShowInTaskbar = false;
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            try { if (ball != null) ball.Dispose(); } catch { }
            try { if (trayIcon != null) { trayIcon.Visible = false; trayIcon.Dispose(); } } catch { }
            if (tts != null) { try { tts.Dispose(); } catch { } }
        }

        public void SetInitialClass(string classId)
        {
            int idx = classIds.IndexOf(classId);
            if (idx >= 0) classCombo.SelectedIndex = idx;
            else if (classIds.Count > 0) classCombo.SelectedIndex = 0;
            ResetResultAndPool();
            classChosen = true;
        }

        public void SetTtsOnline(bool on)
        {
            if (tts != null) tts.SetOnline(on);
        }

        void OpenEditor()
        {
            try
            {
                using (var ed = new EditorForm(this, data)) ed.ShowDialog(this);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "打开名单管理失败：" + ex.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void OpenHistory()
        {
            try
            {
                using (var hf = new HistoryForm()) hf.ShowDialog(this);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "打开抽选记录失败：" + ex.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void OpenAbout()
        {
            try
            {
                using (var af = new AboutForm()) af.ShowDialog(this);
                ApplyBallVisibility();   // 偏好设置里可能切换了悬浮球
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "打开版本信息失败：" + ex.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // 名单被编辑器修改后重新加载
        public void ReloadData()
        {
            data = LoadData();
            allStudents = data.students ?? new List<Student>();
            classNames = data.classes ?? new Dictionary<string, string>();
            classIds = BuildClassIds();
            if (!classIds.Contains(currentClassId)) currentClassId = classIds[0];
            classCombo.Items.Clear();
            foreach (var id in classIds) classCombo.Items.Add(classNames.ContainsKey(id) ? classNames[id] : (id + "班"));
            classCombo.SelectedIndex = classIds.IndexOf(currentClassId);
            ResetResultAndPool();
            Invalidate();
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, string lParam);
        const int EM_SETCUEBANNER = 0x1501;
    }

    // ---------------- 入口 ----------------
    static class Program
    {
        static System.Threading.Mutex singleMutex;

        [STAThread]
        static void Main(string[] args)
        {
            // ---- 单实例保护：已有实例在运行时，提示并唤起其主窗口，新实例退出 ----
            bool firstInstance;
            singleMutex = new System.Threading.Mutex(true, AppVersion.SingleInstanceMutex, out firstInstance);
            if (!firstInstance)
            {
                bool notified = false;
                try
                {
                    using (var ev = System.Threading.EventWaitHandle.OpenExisting(AppVersion.ShowMainEvent))
                    {
                        ev.Set();
                        notified = true;
                    }
                }
                catch { }
                MessageBox.Show(
                    "YBH幸运摇人器 已在运行中。\n\n" +
                    (notified ? "已为你打开正在运行的主窗口。\n" : "可从桌面托盘图标或悬浮球唤出主窗口。\n") +
                    "（单实例运行，不会重复启动）",
                    AppVersion.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                AppConfig.Load();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                var main = new MainForm();

                // 开机自启动（/min）：跳过班级选择弹窗，仅显示悬浮球；
                // 用户点击悬浮球「显示主窗口」时再补选班级。
                bool bootMinimized = args != null && Array.Exists(args,
                    a => string.Equals(a, AutoStart.BootArg, StringComparison.OrdinalIgnoreCase));
                if (bootMinimized)
                {
                    main.BootMinimized = true;
                    Application.Run(main);
                    return;
                }

                using (var modal = new ClassModal(main.classIds, main.classNames))
                {
                    if (modal.ShowDialog() == DialogResult.OK && modal.SelectedClassId != null)
                        main.SetInitialClass(modal.SelectedClassId);
                    else
                        main.SetInitialClass(main.classIds.Count > 0 ? main.classIds[0] : "1");
                }

                Application.Run(main);
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log"), ex.ToString()); }
                catch { }
                MessageBox.Show("程序启动失败：" + ex.Message + "\n\n详情已写入 error.log", AppVersion.ProductName,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                try { if (singleMutex != null) singleMutex.ReleaseMutex(); } catch { }
            }
        }
    }
}
