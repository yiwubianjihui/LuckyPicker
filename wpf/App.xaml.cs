using System;
using System.IO;
using System.Windows;
using System.Windows.Forms;

namespace LuckyPickerWpf
{
    public partial class App : System.Windows.Application
    {
        public static LuckyCore Core = new();
        public static bool BootMinimized;
        static System.Threading.Mutex? singleMutex;
        public static MainWindow? MainWin;

        protected override void OnStartup(StartupEventArgs e)
        {
            // 单实例保护（调试：暂时禁用）

            Log("S1");
            AppConfig.Load();
            Log("S2");
            Core.Load(LuckyCore.LoadDataFile());
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
