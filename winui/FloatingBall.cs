// ================================================================
// FloatingBall.cs — 桌面悬浮球（简约单色）
//
// 一个始终置顶、独立于主窗口的小圆球：
//   · 单击     → 已选班级则立即抽一人；未选班级（如开机自启后）弹出班级选择菜单
//   · 拖动     → 移动位置（自动记忆，重启后保持）
//   · 右键     → 菜单：显示主窗口 / 抽一人 / 连抽五人 / 重置候选池 /
//                开机自启动开关 / 隐藏悬浮球 / 退出程序
//   · 抽中后   → 球面短暂显示结果：单人显示姓名，多人显示「N 人」
// 说明：悬浮球不随主窗口最小化 / 隐藏，始终置顶可见；
//       隐藏 / 显示可在主窗口「设置」菜单中切换，位置与状态均持久化。
// ================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LuckyPickerWinUI
{
    // ================================================================
    // ILuckyBallHost — 悬浮球宿主回调（由 WinUI 主窗口实现）
    // ================================================================
    public interface ILuckyBallHost
    {
        void OnBallClicked();     // 单击：未选班级弹班级小窗，否则立即抽一人
        void OnBallPickOne();     // 右键菜单「抽一人」
        void OnBallPickMulti();   // 右键菜单「连抽五人」
        void OnBallResetPool();   // 右键菜单「重置候选池」
        void OnBallHideSelf();    // 右键菜单「隐藏悬浮球」
        void OnBallQuit();        // 右键菜单「退出程序」
        void ShowMainWindow();    // 显示主窗口
    }

    public class FloatingBallForm : Form
    {
        const int SizePx = 92;
        // 简约单色主题（与主界面主色一致）
        static readonly Color BallColor = Color.FromArgb(37, 99, 235);
        static readonly Color BallColorHover = Color.FromArgb(59, 130, 246);
        static readonly Color EdgeColor = Color.FromArgb(220, 235, 255);

        readonly ILuckyBallHost host;
        bool dragging;
        Point dragStart;
        Point formStart;
        bool movedFar;
        bool hover;

        // 抽中结果短暂显示（单行）
        string flashText;
        Timer flashTimer;
        int flashTicks;

        public FloatingBallForm(ILuckyBallHost host)
        {
            this.host = host;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(SizePx, SizePx);
            DoubleBuffered = true;
            Text = "YBH幸运摇人器 · 悬浮球";

            Region = MakeCircleRegion();
            RestorePosition();

            flashTimer = new Timer { Interval = 120 };
            flashTimer.Tick += delegate
            {
                flashTicks++;
                if (flashTicks >= 25)   // 约 3 秒
                {
                    flashTimer.Stop();
                    flashText = null;
                }
                Invalidate();
            };

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("显示主窗口", null, delegate { host.ShowMainWindow(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("抽一人", null, delegate { host.OnBallPickOne(); });
            menu.Items.Add("连抽五人", null, delegate { host.OnBallPickMulti(); });
            menu.Items.Add("重置候选池", null, delegate { host.OnBallResetPool(); });
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
                    MessageBox.Show("设置开机自启动失败（注册表写入被拒绝）。", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };
            menu.Items.Add(bootItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("隐藏悬浮球", null, delegate { host.OnBallHideSelf(); });
            menu.Items.Add("退出程序", null, delegate { host.OnBallQuit(); });
            ContextMenuStrip = menu;
        }

        Region MakeCircleRegion()
        {
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(0, 0, SizePx, SizePx);
                return new Region(path);
            }
        }

        // ---------- 位置记忆 ----------
        void RestorePosition()
        {
            var wa = Screen.PrimaryScreen.WorkingArea;
            int x = AppConfig.BallX, y = AppConfig.BallY;
            if (x <= -SizePx || x >= wa.Right || y <= -SizePx || y >= wa.Bottom)
            {
                x = wa.Right - SizePx - 24;
                y = wa.Top + wa.Height / 3;
            }
            Location = new Point(x, y);
        }

        void SavePosition()
        {
            AppConfig.BallX = Location.X;
            AppConfig.BallY = Location.Y;
            AppConfig.Save();
        }

        // ---------- 抽中结果显示 ----------
        /// <summary>text 为抽中结果（连抽用「、」连接）。单人显示姓名，多人显示「N 人」。</summary>
        public void ShowPicked(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var parts = text.Split('、');
            if (parts.Length > 1) flashText = parts.Length + "人";
            else
            {
                string name = parts[0];
                flashText = name.Length > 5 ? name.Substring(0, 4) + "…" : name;
            }
            flashTicks = 0;
            flashTimer.Start();
            Invalidate();
        }

        // ---------- 交互 ----------
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                dragging = true;
                movedFar = false;
                dragStart = Cursor.Position;
                formStart = Location;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!dragging) return;
            var d = Cursor.Position;
            int dx = d.X - dragStart.X, dy = d.Y - dragStart.Y;
            if (Math.Abs(dx) + Math.Abs(dy) > 5) movedFar = true;
            if (movedFar)
            {
                var wa = Screen.PrimaryScreen.WorkingArea;
                int nx = Math.Max(wa.Left - SizePx / 2, Math.Min(formStart.X + dx, wa.Right - SizePx / 2));
                int ny = Math.Max(wa.Top, Math.Min(formStart.Y + dy, wa.Bottom - SizePx / 2));
                Location = new Point(nx, ny);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            bool wasDrag = dragging && movedFar;
            dragging = false;
            if (wasDrag) { SavePosition(); return; }
            host.OnBallClicked();   // 单击：抽一人 / 未选班级时弹班级菜单
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            hover = true;
            Cursor = Cursors.Hand;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hover = false;
            Invalidate();
        }

        // ---------- 绘制（单色简约） ----------
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var rect = new Rectangle(3, 3, SizePx - 7, SizePx - 7);
            bool flash = !string.IsNullOrEmpty(flashText);

            // 投影
            using (var shadowPath = new GraphicsPath())
            {
                shadowPath.AddEllipse(6, 8, SizePx - 10, SizePx - 10);
                using (var sb = new SolidBrush(Color.FromArgb(50, 15, 23, 42)))
                    g.FillPath(sb, shadowPath);
            }

            // 单色球体（悬停稍亮）
            using (var brush = new SolidBrush(hover ? BallColorHover : BallColor))
                g.FillEllipse(brush, rect);

            // 细边
            using (var pen = new Pen(EdgeColor, flash ? 2.5f : 1.5f))
                g.DrawEllipse(pen, rect);

            // 球面文字：抽中结果 / 默认“摇”
            var face = new Rectangle(rect.X, rect.Y, rect.Width, rect.Height);
            using (var fmt = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            })
            {
                if (flash)
                {
                    float size = flashText.Length <= 3 ? 17F : 14F;
                    using (var f = new Font("Microsoft YaHei", size, FontStyle.Bold))
                        g.DrawString(flashText, f, Brushes.White, face, fmt);
                }
                else
                {
                    using (var f = new Font("Microsoft YaHei", 22F, FontStyle.Bold))
                        g.DrawString("摇", f, Brushes.White, face, fmt);
                }
            }
        }
    }

    // ================================================================
    // ClassMiniForm — 班级快捷选择小窗（悬浮球点按弹出）
    //
    // 开机自启动后桌面只有悬浮球；单击球弹出这个小窗：
    //   · 置顶、无边框、圆角卡片，显示在悬浮球旁（自动防出屏）
    //   · 点选班级后立即抽一人，小窗自动关闭
    //   · 点击窗体外部（失焦）直接关闭，不打扰桌面
    // ================================================================
    public class ClassMiniForm : Form
    {
        const int W = 236;
        const int ItemH = 42;

        readonly Action<string> onPick;
        readonly List<KeyValuePair<string, string>> items = new List<KeyValuePair<string, string>>();
        readonly List<Rectangle> itemRects = new List<Rectangle>();
        int hoverIndex = -1;

        public ClassMiniForm(IEnumerable<string> ids, Dictionary<string, string> names, Action<string> onPick)
        {
            this.onPick = onPick;

            foreach (var id in ids)
                items.Add(new KeyValuePair<string, string>(
                    id, names != null && names.ContainsKey(id) ? names[id] : id + "班"));

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            BackColor = Color.White;
            Text = "选择班级";

            int height = 70 + items.Count * (ItemH + 8) + 12;
            Size = new Size(W, height);
            var full = new Rectangle(0, 0, W - 1, height - 1);
            Region = new Region(RoundPath(full, 16));

            Deactivate += delegate { Close(); };
        }

        /// <summary>显示在悬浮球旁边（优先左侧，空间不足自动换边、防出屏）。</summary>
        public void ShowNear(Control anchor)
        {
            var wa = Screen.PrimaryScreen.WorkingArea;
            int x, y = Math.Max(wa.Top + 8, anchor.Top - 10);
            if (anchor.Left - W - 10 >= wa.Left)
                x = anchor.Left - W - 10;                            // 球左侧
            else
                x = Math.Min(anchor.Right + 10, wa.Right - W - 8);   // 球右侧
            y = Math.Min(y, wa.Bottom - Height - 8);
            Location = new Point(x, y);
            Show();
        }

        int HitTest(Point p)
        {
            for (int i = 0; i < itemRects.Count; i++)
                if (itemRects[i].Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = HitTest(e.Location);
            if (h != hoverIndex)
            {
                hoverIndex = h;
                Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            int h = HitTest(e.Location);
            if (h < 0) return;
            var pick = onPick;
            Close();
            if (pick != null) pick(items[h].Key);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var full = new Rectangle(0, 0, W - 1, Height - 1);
            using (var b = new SolidBrush(Color.White)) g.FillRectangle(b, full);
            using (var p = new Pen(Color.FromArgb(147, 197, 253), 1f)) g.DrawPath(p, RoundPath(full, 16));

            // 标题
            using (var f = new Font("Microsoft YaHei", 11.5F, FontStyle.Bold))
            using (var br = new SolidBrush(Color.FromArgb(30, 41, 59)))
                g.DrawString("选择班级", f, br, new Rectangle(18, 12, W - 36, 24));
            using (var f = new Font("Microsoft YaHei", 8F))
            using (var br = new SolidBrush(Color.FromArgb(100, 116, 139)))
                g.DrawString("点选后立即抽取 · 点击空白处关闭", f, br, new Rectangle(18, 36, W - 36, 16));

            // 班级按钮
            itemRects.Clear();
            int y = 62;
            for (int i = 0; i < items.Count; i++)
            {
                var r = new Rectangle(14, y, W - 28, ItemH);
                itemRects.Add(r);
                bool hover = i == hoverIndex;
                using (var b = new SolidBrush(hover ? Color.FromArgb(37, 99, 235) : Color.FromArgb(241, 245, 249)))
                    g.FillPath(b, RoundPath(r, 12));
                using (var f = new Font("Microsoft YaHei", 10F, FontStyle.Bold))
                using (var br = new SolidBrush(hover ? Color.White : Color.FromArgb(30, 41, 59)))
                using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                    g.DrawString(items[i].Key + "班 · " + items[i].Value, f, br, r, fmt);
                y += ItemH + 8;
            }
        }

        static GraphicsPath RoundPath(Rectangle b, int r)
        {
            var path = new GraphicsPath();
            path.AddArc(b.X, b.Y, r, r, 180, 90);
            path.AddArc(b.Right - r, b.Y, r, r, 270, 90);
            path.AddArc(b.Right - r, b.Bottom - r, r, r, 0, 90);
            path.AddArc(b.X, b.Bottom - r, r, r, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
