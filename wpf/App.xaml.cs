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
                try { System.Windows.MessageBox.Show("操作出错已忽略：\n" + ex.Exception.Message,
                    AppVersion.ProductName, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning); }
                catch { }
            };
            AppDomain.CurrentDomain.UnhandledException += (s, ex) => Log("DOMAIN-EX: " + ex.ExceptionObject);
            TaskScheduler.UnobservedTaskException += (s, ex) => { Log("TASK-EX: " + ex.Exception); ex.SetObserved(); };

            // ---------- 单实例保护：Mutex + 同名进程去重（双保险） ----------
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
            if (!first || others > 0)
            {
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
            if (!BootMinimized)
            {
                MainWin.Show();
                MainWin.Activate();
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
