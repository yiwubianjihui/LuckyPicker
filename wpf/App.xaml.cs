using System;
using System.IO;
using System.Windows;
using System.Threading.Tasks;

namespace LuckyPickerWpf
{
    public partial class App : System.Windows.Application
    {
        public static LuckyCore Core = new();
        public static bool BootMinimized;
        static bool Loaded;
        static System.Threading.Mutex? singleMutex;
        public static MainWindow? MainWin;

        protected override void OnStartup(StartupEventArgs e)
        {
            // ---------- 全局异常兜底（避免误操作/资源竞争导致直接崩溃） ----------
            DispatcherUnhandledException += (s, ex) =>
            {
                Log("UI-EX: " + ex.Exception);
                ex.Handled = true;
                // 窗口关闭期间的可见性/Show/Close 竞态属无害时序问题：静默忽略，不打扰用户
                string m = ex.Exception.Message ?? "";
                bool benign = m.Contains("窗口关闭期间") || m.Contains("cannot be visible")
                           || m.Contains("Show、ShowDialog") || m.Contains("WindowInteropHelper");
                if (benign) return;
                try { System.Windows.MessageBox.Show("操作出错已忽略：\n" + ex.Exception.Message,
                    AppVersion.ProductName, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning); }
                catch { }
            };
            AppDomain.CurrentDomain.UnhandledException += (s, ex) => Log("DOMAIN-EX: " + ex.ExceptionObject);
            TaskScheduler.UnobservedTaskException += (s, ex) => { Log("TASK-EX: " + ex.Exception); ex.SetObserved(); };

            // ---------- 单实例保护：以**命名互斥体**为唯一权威判据 ----------
            //
            // ⚠️ Build 15 曾在这里叠加「同名进程去重」当双保险（`others > 0` 也判定为
            //    "已在运行"）。但那是个**竞态**：当两个实例几乎同时启动时（例如注册表
            //    Run 里存在两个自启动值名），各自都能看到对方进程 ⇒ **双双弹
            //    「已在运行」并退出，结果谁都起不来** —— 这正是用户报的
            //    「开机弹提示、但程序不启动」。Build 18 把进程数降级为**诊断日志**，
            //    不再参与判断。（互斥体随进程退出自动释放，不会像进程名那样被误判。）
            bool first;
            singleMutex = new System.Threading.Mutex(true, AppVersion.SingleInstanceMutex, out first);
            int others = 0;
            try
            {
                var cur = System.Diagnostics.Process.GetCurrentProcess();
                foreach (var p in System.Diagnostics.Process.GetProcessesByName(cur.ProcessName))
                {
                    if (p.Id != cur.Id) others++;
                }
            }
            catch { }
            if (!first)
            {
                Log("ALREADY-RUNNING (mutex held; other same-name processes=" + others + ")");
                bool notified = false;
                try
                {
                    using var ev = System.Threading.EventWaitHandle.OpenExisting(AppVersion.ShowMainEvent);
                    ev.Set();
                    notified = true;
                }
                catch { }
                System.Windows.MessageBox.Show(
                    AppVersion.ProductName + " 已在运行中。\n\n" +
                    (notified ? "已为你打开正在运行的主窗口。\n" : "可从托盘图标或桌面悬浮球唤出主窗口。\n") +
                    "（单实例运行，不会重复启动）",
                    AppVersion.ProductName, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                Shutdown();
                return;
            }
            // 抢到互斥体 ⇒ 本实例是唯一的运行实例：顺手清掉遗留的自启动值名
            // （老用户注册表里可能同时有 YBHLuckyPicker 与中文名，会把程序拉起两次）
            AutoStart.CleanupLegacy();

            Log("S1");
            AppConfig.Load();
            Log("S2");
            // 内部版：发布包内附 internal.txt 标记时，直接以包内名单为准（系统数据目录可能无写权限）
            try
            {
                string flag = Path.Combine(AppContext.BaseDirectory, "internal.txt");
                string packed = Path.Combine(AppContext.BaseDirectory, "students.json");
                if (File.Exists(flag) && File.Exists(packed))
                {
                    var df = System.Text.Json.JsonSerializer.Deserialize<DataFile>(File.ReadAllText(packed));
                    if (df != null && df.students != null && df.students.Count > 0) { Core.Load(df); Loaded = true; }
                }
            }
            catch { }
            if (!Loaded) Core.Load(LuckyCore.LoadDataFile());
            Log("S3");
            BootMinimized = e.Args != null && Array.Exists(e.Args,
                a => string.Equals(a, AutoStart.BootArg, StringComparison.OrdinalIgnoreCase));

            base.OnStartup(e);
            Log("S4");
            MainWin = new MainWindow();
            Log("S5");
            MainWindow = MainWin;

            // 应用内自检：--selftest <输出文件>（不进入正常 UI 交互流程）
            string? selfTestPath = null;
            if (e.Args != null)
            {
                int i = Array.IndexOf(e.Args, "--selftest");
                if (i >= 0 && i + 1 < e.Args.Length) selfTestPath = e.Args[i + 1];
            }

            if (!BootMinimized)
            {
                MainWin.Show();
                MainWin.Activate();
            }

            if (selfTestPath != null)
            {
                MainWin.Show();
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try { SelfTest.Run(MainWin!, selfTestPath); }
                    catch { }
                    Shutdown();
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }

            // 空转诊断：--fadewatch <输出文件>
            // 走的是**完全正常的启动路径**（不像 --selftest 那样自己驱动 dispatcher），
            // 只逐秒把悬浮球的渐隐判定条件落盘。用于排查"自检能渐隐、实际运行不渐隐"。
            string? fadeWatchPath = null;
            if (e.Args != null)
            {
                int j = Array.IndexOf(e.Args, "--fadewatch");
                if (j >= 0 && j + 1 < e.Args.Length) fadeWatchPath = e.Args[j + 1];
            }
            if (fadeWatchPath != null)
            {
                MainWin.Show();
                var watch = new System.Windows.Threading.DispatcherTimer
                { Interval = TimeSpan.FromSeconds(1) };
                int ticks = 0;
                watch.Tick += (s2, e2) =>
                {
                    try
                    {
                        var bw = MainWin!.BallWindowForTest as FloatingBallWindow;
                        File.AppendAllText(fadeWatchPath,
                            DateTime.Now.ToString("HH:mm:ss") + "  " + (bw?.DiagNow ?? "no-ball") + "\n");
                    }
                    catch { }
                    if (++ticks >= 16) { watch.Stop(); Shutdown(); }
                };
                watch.Start();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try { singleMutex?.ReleaseMutex(); } catch { }
            base.OnExit(e);
        }

        // 崩溃日志
        static string CrashLog => Path.Combine(Path.GetTempPath(), "lucky_crash.log");
        public static void Log(string tag) { try { File.AppendAllText(CrashLog, "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + tag + "\n"); } catch { } }
    }
}
