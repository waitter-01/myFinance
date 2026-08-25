using DuxiuLedger.Desktop.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace DuxiuLedger.WinUI;

public sealed partial class MainWindow : Window
{
    private readonly LocalStore _store = new();
    private readonly Dictionary<string, (string Title, string Subtitle)> _pages = new()
    {
        ["Dashboard"] = ("总览", "查看本月财务情况和最近流水"),
        ["Transactions"] = ("全部流水", "搜索、核对和管理本地账单记录"),
        ["Budgets"] = ("预算计划", "规划每月支出，控制消费节奏"),
        ["Subscriptions"] = ("订阅与月卡", "看清自动续费、会员和游戏月卡的长期成本"),
        ["Categories"] = ("分类设置", "建立适合自己的收支分类体系"),
        ["Backup"] = ("数据备份", "复制和保护本地账本数据库"),
        ["Settings"] = ("偏好设置", "按自己的习惯调整分析标准和提醒计划")
    };

    public MainWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new SizeInt32(1320, 850));
        LoadDashboard();
    }

    private void LoadDashboard()
    {
        var currentMonth = DateTime.Now.ToString("yyyy-MM");
        var records = _store.List().Where(row => row.OccurredOn.ToString("yyyy-MM") == currentMonth).ToList();
        var income = records.Where(row => row.Direction == "收入").Sum(row => row.Amount);
        var expense = records.Where(row => row.Direction == "支出").Sum(row => row.Amount);
        IncomeText.Text = $"¥{income:N2}";
        ExpenseText.Text = $"¥{expense:N2}";
        BalanceText.Text = $"¥{income - expense:N2}";
        StatusText.Text = $"已读取本地账本 · 本月 {records.Count} 条流水";
    }

    private void NavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is not string key || !_pages.TryGetValue(key, out var page)) return;
        PageTitle.Text = page.Title;
        PageSubtitle.Text = page.Subtitle;
        DashboardPage.Visibility = key == "Dashboard" ? Visibility.Visible : Visibility.Collapsed;
        PlaceholderPage.Visibility = key == "Dashboard" ? Visibility.Collapsed : Visibility.Visible;
        PlaceholderTitle.Text = $"{page.Title}正在迁移";
    }

    private void TitleBarPaneToggleRequested(TitleBar sender, object args) => NavView.IsPaneOpen = !NavView.IsPaneOpen;

    private async void ImportClick(object sender, RoutedEventArgs e) => await ShowMigrationNotice("账单导入");
    private async void AddClick(object sender, RoutedEventArgs e) => await ShowMigrationNotice("手动录入");

    private async Task ShowMigrationNotice(string feature)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = ContentHost.XamlRoot,
            Title = $"{feature}迁移中",
            Content = "当前稳定版功能仍可在 WPF 版本使用；WinUI 3 版本完成迁移后会替换发布入口。",
            CloseButtonText = "知道了"
        };
        await dialog.ShowAsync();
    }
}
