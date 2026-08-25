using Microsoft.UI.Xaml;

namespace DuxiuLedger.WinUI;

public partial class App : Application
{
    private Window? _window;
    private readonly NotificationService _notifications = new();

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, args) =>
        {
            WriteStartupError(args.Exception);
            args.Handled = true;
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _notifications.Register();
            var commandLine = Environment.GetCommandLineArgs();
            if (commandLine.Contains("--daily-reminder"))
            {
                _notifications.Show("今天的账记了吗？", "花一分钟补全今天的消费，月底分析才不会遗漏小额支出。");
                Exit();
                return;
            }
            if (commandLine.Contains("--weekly-summary"))
            {
                var rows = new DuxiuLedger.Desktop.Services.LocalStore().List().Where(row => row.Direction == "支出" && row.OccurredOn >= DateTime.Today.AddDays(-7)).ToList();
                _notifications.Show("独秀账本每周总结", $"最近 7 天共记录 {rows.Count} 笔支出，合计 ¥{rows.Sum(row => row.Amount):N2}。打开应用查看详细去向。");
                Exit();
                return;
            }
            _window = new MainWindow();
            _window.Closed += (_, _) => _notifications.Unregister();
            _window.Activate();
        }
        catch (Exception ex)
        {
            WriteStartupError(ex);
            throw;
        }
    }

    private static void WriteStartupError(Exception exception)
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DuxiuLedger", "logs");
        Directory.CreateDirectory(folder);
        File.AppendAllText(Path.Combine(folder, "startup-error.log"), $"[{DateTime.Now:O}] WinUI 3\n{exception}\n\n");
    }
}
