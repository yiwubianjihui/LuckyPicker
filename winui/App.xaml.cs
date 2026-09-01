using Microsoft.UI.Xaml;

namespace LuckyPickerWinUI
{
    public partial class App : Application
    {
        public static Window? MainWindow;
        public static LuckyCore Core = new();
        public static bool BootMinimized;
        public static App? Current;

        public App()
        {
            Current = this;
            InitializeComponent();
        }

        public void OnAppStarted(AppContextHolder ctx)
        {
            Core = ctx.Core;
            BootMinimized = ctx.BootMinimized;
            MainWindow = new MainWindow();
            MainWindow.Activate();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            // 自定义 Main 已处理启动；这里保持空实现即可
        }
    }
}
