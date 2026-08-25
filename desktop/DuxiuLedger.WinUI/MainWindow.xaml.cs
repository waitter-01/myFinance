using DuxiuLedger.Desktop.Services;
using DuxiuLedger.Desktop.Models;
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
        WeeklySummaryDayBox.ItemsSource = new[] { "星期一", "星期二", "星期三", "星期四", "星期五", "星期六", "星期日" };
        DataPathText.Text = _store.DatabasePath;
        LoadSettings();
        LoadDashboard();
    }

    private void LoadDashboard()
    {
        var currentMonth = DateTime.Now.ToString("yyyy-MM");
        var allRecords = _store.List().ToList();
        var records = allRecords.Where(row => row.OccurredOn.ToString("yyyy-MM") == currentMonth).ToList();
        var income = records.Where(row => row.Direction == "收入").Sum(row => row.Amount);
        var expense = records.Where(row => row.Direction == "支出").Sum(row => row.Amount);
        IncomeText.Text = $"¥{income:N2}";
        ExpenseText.Text = $"¥{expense:N2}";
        BalanceText.Text = $"¥{income - expense:N2}";
        RecentList.ItemsSource = allRecords.Take(10).ToList();
        TransactionsList.ItemsSource = allRecords;
        RecordCountText.Text = $"共 {allRecords.Count} 条记录 · 本月 {records.Count} 条";
        LoadSubscriptions(allRecords);
        StatusText.Text = $"已读取本地账本 · 本月 {records.Count} 条流水";
    }

    private void LoadSubscriptions(IReadOnlyList<TransactionRecord> allRecords)
    {
        var keywords = _store.LoadSettings().SubscriptionKeywords.Split([',', '，', ';', '；', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var since = DateTime.Now.Date.AddMonths(-12);
        var detected = allRecords.Where(row => row.Direction == "支出" && row.OccurredOn >= since).Where(row => keywords.Any(keyword => $"{row.Merchant} {row.Category} {row.Note}".Contains(keyword, StringComparison.OrdinalIgnoreCase))).ToList();
        var summaries = detected.GroupBy(row => string.IsNullOrWhiteSpace(row.Merchant) ? "未注明交易对方" : row.Merchant.Trim(), StringComparer.OrdinalIgnoreCase).Select(group => new SubscriptionSummary
        {
            Merchant = group.Key,
            Category = group.GroupBy(row => row.Category).OrderByDescending(item => item.Count()).First().Key,
            PaymentCount = group.Count(),
            PaidLast12Months = group.Sum(row => row.Amount),
            MonthlyAverage = group.Sum(row => row.Amount) / 12m,
            LatestPayment = group.Max(row => row.OccurredOn)
        }).OrderByDescending(item => item.MonthlyAverage).ToList();
        SubscriptionsList.ItemsSource = summaries;
        SubscriptionCurrentText.Text = $"¥{detected.Where(row => row.OccurredOn.ToString("yyyy-MM") == DateTime.Now.ToString("yyyy-MM")).Sum(row => row.Amount):N2}";
        SubscriptionAverageText.Text = $"¥{summaries.Sum(item => item.MonthlyAverage):N2}";
        SubscriptionCountText.Text = $"{summaries.Count} 项";
    }

    private void LoadSettings()
    {
        var settings = _store.LoadSettings();
        SmallExpenseThresholdBox.Value = (double)settings.SmallExpenseThreshold;
        MonthlyBudgetBox.Value = (double)settings.MonthlyBudget;
        DailyReminderCheck.IsChecked = settings.DailyReminderEnabled;
        DailyReminderTimePicker.Time = TimeSpan.Parse(settings.DailyReminderTime);
        WeeklySummaryCheck.IsChecked = settings.WeeklySummaryEnabled;
        WeeklySummaryDayBox.SelectedIndex = settings.WeeklySummaryDay == DayOfWeek.Sunday ? 6 : (int)settings.WeeklySummaryDay - 1;
        WeeklySummaryTimePicker.Time = TimeSpan.Parse(settings.WeeklySummaryTime);
        SubscriptionKeywordsBox.Text = settings.SubscriptionKeywords;
    }

    private void NavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is not string key || !_pages.TryGetValue(key, out var page)) return;
        PageTitle.Text = page.Title;
        PageSubtitle.Text = page.Subtitle;
        DashboardPage.Visibility = key == "Dashboard" ? Visibility.Visible : Visibility.Collapsed;
        TransactionsPage.Visibility = key == "Transactions" ? Visibility.Visible : Visibility.Collapsed;
        SubscriptionsPage.Visibility = key == "Subscriptions" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = key == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        BackupPage.Visibility = key == "Backup" ? Visibility.Visible : Visibility.Collapsed;
        PlaceholderPage.Visibility = key is "Budgets" or "Categories" ? Visibility.Visible : Visibility.Collapsed;
        PlaceholderTitle.Text = $"{page.Title}正在建设";
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

    private void SearchClick(object sender, RoutedEventArgs e)
    {
        var search = SearchBox.Text.Trim();
        var rows = _store.List(search);
        TransactionsList.ItemsSource = rows;
        StatusText.Text = $"搜索到 {rows.Count} 条记录";
    }

    private void SaveSettingsClick(object sender, RoutedEventArgs e)
    {
        var dayIndex = Math.Max(0, WeeklySummaryDayBox.SelectedIndex);
        _store.SaveSettings(new AppSettings
        {
            SmallExpenseThreshold = (decimal)Math.Max(0, SmallExpenseThresholdBox.Value),
            MonthlyBudget = (decimal)Math.Max(0, MonthlyBudgetBox.Value),
            DailyReminderEnabled = DailyReminderCheck.IsChecked == true,
            DailyReminderTime = DailyReminderTimePicker.Time.ToString(@"hh\:mm"),
            WeeklySummaryEnabled = WeeklySummaryCheck.IsChecked == true,
            WeeklySummaryDay = dayIndex == 6 ? DayOfWeek.Sunday : (DayOfWeek)(dayIndex + 1),
            WeeklySummaryTime = WeeklySummaryTimePicker.Time.ToString(@"hh\:mm"),
            SubscriptionKeywords = SubscriptionKeywordsBox.Text.Trim()
        });
        LoadDashboard();
        StatusText.Text = "偏好设置已保存";
    }

    private async void BackupClick(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileSavePicker
        {
            SuggestedFileName = $"duxiu-ledger-{DateTime.Now:yyyyMMdd-HHmm}"
        };
        picker.FileTypeChoices.Add("SQLite 数据库", [".db"]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
        File.Copy(_store.DatabasePath, file.Path, true);
        StatusText.Text = $"备份完成：{file.Path}";
    }

    private async void OpenDataFolderClick(object sender, RoutedEventArgs e)
    {
        var folderPath = Path.GetDirectoryName(_store.DatabasePath)!;
        var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(folderPath);
        await Windows.System.Launcher.LaunchFolderAsync(folder);
    }
}
