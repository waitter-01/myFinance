using Microsoft.UI.Xaml;

namespace DuxiuLedger.WinUI;

public partial class App : Application
{
    private Window? _window;

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
            _window = new MainWindow();
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
