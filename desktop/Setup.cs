// ================================================================
// YBH幸运摇人器 · 安装程序 (Setup)
// 将 LuckyPicker.exe + students.json 安装到用户目录，
// 创建桌面 / 开始菜单快捷方式，并注册卸载项。
// 用法：
//   Setup.exe                  -> 图形安装界面
//   Setup.exe /silent <目录> <桌面快捷方式0/1> <开始菜单0/1> <预装名单0/1> <开机自启0/1>
//   Uninstall.exe /uninstall   -> 静默卸载
// ================================================================
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("YBH幸运摇人器 安装程序")]
[assembly: AssemblyProduct("YBH幸运摇人器 安装程序")]
[assembly: AssemblyCompany("YBH")]
[assembly: AssemblyVersion("26.12.0.0")]
[assembly: AssemblyFileVersion("26.12.0.0")]
[assembly: AssemblyInformationalVersion("26H2 Build 13")]

namespace LuckyPickerSetup
{
    static class Installer
    {
        public const string AppExe = "LuckyPicker.exe";
        public const string DataFile = "students.json";
        public const string UpdateSample = "update.sample.json";
        public const string UninstallExe = "Uninstall.exe";

        // 不勾选“预装名单”时写入的空名单（仅保留示例班级名称，等待用户导入）
        public const string EmptyStudentsJson =
            "{\r\n" +
            "  \"classes\": {\r\n" +
            "    \"1\": \"示例一班\",\r\n" +
            "    \"2\": \"示例二班\",\r\n" +
            "    \"3\": \"示例三班\"\r\n" +
            "  },\r\n" +
            "  \"students\": []\r\n" +
            "}";
        public const string ShortcutName = "YBH幸运摇人器";
        public const string LegacyShortcutName = "幸运摇人器";
        public const string UninstallShortcutName = "卸载 YBH幸运摇人器";
        public const string RegKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\LuckyPicker";
        public const string Version = "26H2 Build 13";
        // 与主程序 AutoStart 保持一致的 Run 注册表项
        public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string RunValueName = "YBHLuckyPicker";
        // 本机数据目录（配置 / 语音缓存 / 抽选记录），卸载时一并清理
        public static string LocalDataDir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "LuckyPicker");
            }
        }
        // 共享数据目录（名单文件），安装到系统目录后写入此处，卸载时清理
        public static string ProgramDataDir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "LuckyPicker");
            }
        }

        public static string DefaultInstallDir
        {
            get
            {
                // 系统目录：便于统一管理（Setup.exe 带 requireAdministrator 清单）
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "LuckyPicker");
            }
        }

        public static void ExtractResource(string name, string dest)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (s == null) throw new Exception("缺少内嵌资源：" + name);
                using (var fs = File.Create(dest))
                {
                    s.CopyTo(fs);
                }
            }
        }

        public static void CreateShortcut(string shortcutPath, string targetPath, string workDir, string description)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;
            dynamic shell = Activator.CreateInstance(shellType);
            dynamic lnk = shell.CreateShortcut(shortcutPath);
            lnk.TargetPath = targetPath;
            lnk.WorkingDirectory = workDir;
            lnk.Description = description;
            lnk.IconLocation = targetPath + ",0";
            lnk.Save();
        }

        public static string StartMenuPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), ShortcutName + ".lnk");
        }

        public static string StartMenuUninstallPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), UninstallShortcutName + ".lnk");
        }

        public static string DesktopPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ShortcutName + ".lnk");
        }

        // 更名前的旧快捷方式（安装新版时清理，避免开始菜单出现两个图标）
        public static void DeleteLegacyShortcuts()
        {
            TryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), LegacyShortcutName + ".lnk"));
            TryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), LegacyShortcutName + ".lnk"));
        }

        public static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        // 执行安装，返回错误信息（null 表示成功）
        public static string DoInstall(string installDir, bool desktop, bool startMenu, bool preloadStudents, bool autoStart)
        {
            try
            {
                Directory.CreateDirectory(installDir);
                string exePath = Path.Combine(installDir, AppExe);
                string dataPath = Path.Combine(installDir, DataFile);
                string uninstPath = Path.Combine(installDir, UninstallExe);

                ExtractResource(AppExe, exePath);
                if (preloadStudents)
                    ExtractResource(DataFile, dataPath);
                else
                    File.WriteAllText(dataPath, EmptyStudentsJson, new UTF8Encoding(false));
                ExtractResource(UpdateSample, Path.Combine(installDir, UpdateSample));

                // 名单同时写入共享数据目录 %ProgramData%\LuckyPicker（系统目录下主程序可写）
                try
                {
                    Directory.CreateDirectory(ProgramDataDir);
                    File.Copy(dataPath, Path.Combine(ProgramDataDir, DataFile), true);
                }
                catch { }

                // 复制自身作为卸载程序
                File.Copy(Application.ExecutablePath, uninstPath, true);

                DeleteLegacyShortcuts();
                if (startMenu)
                {
                    CreateShortcut(StartMenuPath(), exePath, installDir, "YBH幸运摇人器 - 班级随机摇人工具");
                    CreateShortcut(StartMenuUninstallPath(), uninstPath, installDir, "卸载 YBH幸运摇人器（清理程序与本地数据）");
                }
                if (desktop) CreateShortcut(DesktopPath(), exePath, installDir, "YBH幸运摇人器 - 班级随机摇人工具");

                // 开机自启动（当前用户 Run 项，指向安装后的主程序）
                SetAutoStart(autoStart, exePath);

                // 注册卸载信息（系统目录安装 → 写入 HKLM）
                var key = Registry.LocalMachine.CreateSubKey(RegKey);
                if (key != null)
                {
                    key.SetValue("DisplayName", "YBH幸运摇人器");
                    key.SetValue("DisplayVersion", Version);
                    key.SetValue("Publisher", "YBH");
                    key.SetValue("InstallLocation", installDir);
                    key.SetValue("UninstallString", "\"" + uninstPath + "\" /uninstall");
                    key.SetValue("DisplayIcon", exePath + ",0");
                    key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    key.Close();
                }
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        public static extern bool MoveFileEx(string existing, string newName, int flags);

        /// <summary>写入 / 清除开机自启动注册表项（与主程序内 AutoStart 逻辑一致）。</summary>
        public static void SetAutoStart(bool enabled, string targetExe)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (key == null) return;
                    if (enabled)
                        key.SetValue(RunValueName, "\"" + targetExe + "\" /min", RegistryValueKind.String);
                    else if (key.GetValue(RunValueName) != null)
                        key.DeleteValue(RunValueName, false);
                }
            }
            catch { }
        }

        public static void RemoveAutoStart()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key != null && key.GetValue(RunValueName) != null)
                        key.DeleteValue(RunValueName, false);
                }
            }
            catch { }
        }

        /// <summary>执行卸载。confirmUI=true 时先弹确认框（从「应用和功能」/ 卸载快捷方式触发）。</summary>
        public static void RunUninstall(bool confirmUI)
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;

            if (confirmUI)
            {
                var ask = MessageBox.Show(
                    "确定要卸载 YBH幸运摇人器 吗？\n\n" +
                    "将删除：\n" +
                    "  · 程序文件与快捷方式\n" +
                    "  · 开机自启动设置\n" +
                    "  · 本机数据（配置、语音缓存、抽选记录）\n\n" +
                    "已安装目录中的名单文件 students.json 也会一并删除，请提前备份。",
                    "卸载 YBH幸运摇人器",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (ask != DialogResult.Yes) Environment.Exit(0);
            }

            TryDelete(StartMenuPath());
            TryDelete(StartMenuUninstallPath());
            TryDelete(DesktopPath());
            DeleteLegacyShortcuts();
            RemoveAutoStart();
            // 卸载项清理（HKLM 新装 / HKCU 旧装都兼容）
            try { Registry.LocalMachine.DeleteSubKeyTree(RegKey, false); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(RegKey, false); } catch { }

            // 清理本机数据目录（配置 / 语音缓存 / 抽选记录）
            try
            {
                if (Directory.Exists(LocalDataDir))
                {
                    foreach (var f in Directory.GetFiles(LocalDataDir)) TryDelete(f);
                    try { Directory.Delete(LocalDataDir, true); } catch { }
                }
            }
            catch { }

            // 清理共享数据目录（名单文件）
            try
            {
                if (Directory.Exists(ProgramDataDir))
                {
                    try { Directory.Delete(ProgramDataDir, true); } catch { }
                }
            }
            catch { }

            // 删除目录内除 Uninstall.exe 外的所有文件
            try
            {
                if (Directory.Exists(dir))
                {
                    foreach (var f in Directory.GetFiles(dir))
                    {
                        if (!string.Equals(Path.GetFileName(f), UninstallExe, StringComparison.OrdinalIgnoreCase))
                            try { File.Delete(f); } catch { }
                    }
                }
            }
            catch { }

            // 复制自身到临时目录，由其负责删除安装目录（含 Uninstall.exe 自身）
            try
            {
                string tmp = Path.Combine(Path.GetTempPath(), "LuckyPicker_uninst_" + Guid.NewGuid().ToString("N") + ".exe");
                File.Copy(Application.ExecutablePath, tmp, true);
                var psi = new ProcessStartInfo(tmp, "/finalize \"" + dir.TrimEnd('\\') + "\"");
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.WorkingDirectory = Path.GetTempPath();
                Process.Start(psi);
            }
            catch { }
            Environment.Exit(0);
        }
    }

    class SetupForm : Form
    {
        TextBox dirBox;
        CheckBox desktopChk, startMenuChk, preloadChk, bootChk;
        Button installBtn, browseBtn;
        Label statusLabel;

        public SetupForm()
        {
            this.Text = "YBH幸运摇人器 安装程序";
            this.ClientSize = new Size(560, 512);
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.White;
            this.Font = new Font("Microsoft YaHei", 9F);
            try { this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var title = new Label
            {
                Text = "YBH幸运摇人器 · 安装",
                Font = new Font("Microsoft YaHei", 16F, FontStyle.Bold),
                ForeColor = Color.FromArgb(31, 43, 78),
                AutoSize = true,
                Location = new Point(24, 22)
            };
            var sub = new Label
            {
                Text = "班级随机摇人工具 —— 筛选/不重复/连抽/屏蔽/语音播报/抽选记录/版本更新 · " + Installer.Version,
                Font = new Font("Microsoft YaHei", 8.5F),
                ForeColor = Color.FromArgb(90, 110, 138),
                AutoSize = true,
                Location = new Point(26, 62)
            };

            var dirLabel = new Label
            {
                Text = "安装目录",
                Font = new Font("Microsoft YaHei", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(31, 43, 78),
                AutoSize = true,
                Location = new Point(26, 100)
            };
            dirBox = new TextBox
            {
                Text = Installer.DefaultInstallDir,
                Location = new Point(26, 124),
                Size = new Size(400, 28),
                Font = new Font("Microsoft YaHei", 9F)
            };
            browseBtn = new Button
            {
                Text = "浏览...",
                Location = new Point(434, 122),
                Size = new Size(90, 30),
                FlatStyle = FlatStyle.Flat
            };
            browseBtn.Click += delegate
            {
                using (var dlg = new FolderBrowserDialog { Description = "选择安装目录", SelectedPath = dirBox.Text })
                {
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                        dirBox.Text = Path.Combine(dlg.SelectedPath, "LuckyPicker");
                }
            };

            desktopChk = new CheckBox
            {
                Text = "创建桌面快捷方式",
                Checked = true,
                Location = new Point(28, 170),
                AutoSize = true
            };
            startMenuChk = new CheckBox
            {
                Text = "创建开始菜单快捷方式",
                Checked = true,
                Location = new Point(28, 202),
                AutoSize = true
            };
            preloadChk = new CheckBox
            {
                Text = "添加预装名单（内置示例名单，可在程序内替换）",
                Checked = true,
                Location = new Point(28, 234),
                AutoSize = true
            };
            bootChk = new CheckBox
            {
                Text = "开机自启动（登录后仅显示悬浮球）",
                Checked = true,
                Location = new Point(28, 266),
                AutoSize = true
            };

            installBtn = new Button
            {
                Text = "开始安装",
                Location = new Point(26, 306),
                Size = new Size(150, 42),
                Font = new Font("Microsoft YaHei", 11F, FontStyle.Bold),
                BackColor = Color.FromArgb(59, 130, 246),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            installBtn.Click += delegate { DoInstall(); };

            statusLabel = new Label
            {
                Text = "点击“开始安装”以继续。",
                Location = new Point(26, 366),
                Size = new Size(500, 100),
                ForeColor = Color.FromArgb(90, 110, 138),
                Font = new Font("Microsoft YaHei", 9F)
            };

            Controls.Add(title);
            Controls.Add(sub);
            Controls.Add(dirLabel);
            Controls.Add(dirBox);
            Controls.Add(browseBtn);
            Controls.Add(desktopChk);
            Controls.Add(startMenuChk);
            Controls.Add(preloadChk);
            Controls.Add(bootChk);
            Controls.Add(installBtn);
            Controls.Add(statusLabel);
        }

        void DoInstall()
        {
            string dir = dirBox.Text.Trim();
            if (dir.Length == 0) { MessageBox.Show(this, "请输入安装目录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            installBtn.Enabled = false;
            browseBtn.Enabled = false;
            statusLabel.Text = "正在安装到：" + dir + " ...";
            statusLabel.ForeColor = Color.FromArgb(31, 43, 78);
            Application.DoEvents();

            string err = Installer.DoInstall(dir, desktopChk.Checked, startMenuChk.Checked, preloadChk.Checked, bootChk.Checked);
            if (err != null)
            {
                statusLabel.Text = "安装失败：" + err;
                statusLabel.ForeColor = Color.FromArgb(220, 38, 38);
                installBtn.Enabled = true;
                browseBtn.Enabled = true;
                return;
            }

            statusLabel.Text = "安装完成！\n\n安装目录：" + dir +
                "\n名单：" + (preloadChk.Checked ? "已添加预装名单" : "空名单（可稍后在程序内导入）") +
                "\n开机自启动：" + (bootChk.Checked ? "已开启（登录后仅显示悬浮球）" : "未开启（可稍后在程序内设置）") +
                "\n可通过“开始菜单 → YBH幸运摇人器”或桌面快捷方式启动；\n卸载请用“开始菜单 → 卸载 YBH幸运摇人器”（会一并清理本机数据）。";
            statusLabel.ForeColor = Color.FromArgb(6, 95, 70);
            installBtn.Text = "完成";
            installBtn.Enabled = true;

            var run = MessageBox.Show(this, "安装完成！是否立即运行YBH幸运摇人器？", "安装完成",
                MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (run == DialogResult.Yes)
            {
                try { Process.Start(Path.Combine(dir, Installer.AppExe)); } catch { }
                this.Close();
            }
        }
    }

    static class Program
    {
        static bool HasArg(string[] args, string name)
        {
            foreach (var a in args)
                if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static int IndexOfArg(string[] args, string name)
        {
            for (int i = 0; i < args.Length; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        [STAThread]
        static void Main(string[] args)
        {
            int fi = IndexOfArg(args, "/finalize");
            if (fi >= 0 && args.Length > fi + 1)
            {
                string dir = args[fi + 1];
                try { Directory.SetCurrentDirectory(Path.GetTempPath()); } catch { }
                System.Threading.Thread.Sleep(900);
                for (int t = 0; t < 3; t++)
                {
                    try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
                    if (!Directory.Exists(dir)) break;
                    System.Threading.Thread.Sleep(800);
                }
                try { Installer.MoveFileEx(Application.ExecutablePath, null, 0x4); } catch { }
                Environment.Exit(0);
            }

            if (HasArg(args, "/uninstall") || HasArg(args, "-uninstall"))
            {
                // /uninstall /silent = 静默卸载（不弹确认）；否则显示确认框
                bool silent = HasArg(args, "/silent") || HasArg(args, "-silent");
                Installer.RunUninstall(!silent);
                return;
            }

            int si = IndexOfArg(args, "/silent");
            if (si < 0) si = IndexOfArg(args, "-silent");
            if (si >= 0)
            {
                string dir = (args.Length > si + 1 && args[si + 1].Length > 0) ? args[si + 1] : Installer.DefaultInstallDir;
                bool desktop = args.Length > si + 2 ? args[si + 2] == "1" : true;
                bool startMenu = args.Length > si + 3 ? args[si + 3] == "1" : true;
                bool preload = args.Length > si + 4 ? args[si + 4] == "1" : true;
                bool boot = args.Length > si + 5 ? args[si + 5] == "1" : true;
                string err = Installer.DoInstall(dir, desktop, startMenu, preload, boot);
                Environment.Exit(err == null ? 0 : 1);
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm());
        }
    }
}
