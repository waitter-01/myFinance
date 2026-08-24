using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace DuxiuLedger.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => LogAndNotify(args.ExceptionObject as Exception ?? new Exception("未知启动错误"));
        try { new MainWindow().Show(); }
        catch (Exception ex) { LogAndNotify(ex); Shutdown(1); }
        base.OnStartup(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        LogAndNotify(e.Exception);
    }

    private static void LogAndNotify(Exception exception)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DuxiuLedger", "logs");
        Directory.CreateDirectory(directory);
        var logFile = Path.Combine(directory, "startup-error.log");
        File.AppendAllText(logFile, $"[{DateTime.Now:O}]{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        MessageBox.Show($"程序启动时发生错误。详细信息已写入：{logFile}", "独秀账本", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
