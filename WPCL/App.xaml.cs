using System.Configuration;
using System.Data;
using System.IO;
using System.Windows;

namespace WPCL
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>

    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 1. UI 线程
            DispatcherUnhandledException += (s, args) =>
            {
                Log("Dispatcher", args.Exception);
                args.Handled = true;
            };

            // 2. 非 UI 线程（AppDomain 层面，无法阻止退出，但能记录）
            AppDomain.CurrentDomain.UnhandledException += static (s, args) =>
            {
                Log("AppDomain", args.ExceptionObject as Exception);
            };

            // 3. Task 未观察异常
            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                Log("TaskScheduler", args.Exception);
                args.SetObserved();
            };
        }

        private static void Log(string source, Exception? ex)
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(AppContext.BaseDirectory, "crash.log"),
                    $"[{DateTime.Now}] [{source}]\n{ex}\n\n");
            }
            catch { }
        }
    }

}
