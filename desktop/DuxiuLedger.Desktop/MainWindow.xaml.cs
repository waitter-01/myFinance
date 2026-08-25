using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Globalization;
using Microsoft.Win32;
using DuxiuLedger.Desktop.Models;
using DuxiuLedger.Desktop.Services;

namespace DuxiuLedger.Desktop;

public partial class MainWindow : Window
{
    private readonly LocalStore _store = new();
    private readonly BillImporter _importer = new();
    private readonly ObservableCollection<TransactionRecord> _records = new();
    private readonly ObservableCollection<SubscriptionSummary> _subscriptions = new();
    private readonly Dictionary<string, (Grid Page, Button Button, string Title, string Subtitle)> _navigation;

    public MainWindow()
    {
        InitializeComponent();
        _navigation = new()
        {
            ["Dashboard"] = (DashboardPage, DashboardNav, "总览", "查看本月财务情况和最近流水"),
            ["Transactions"] = (TransactionsPage, TransactionsNav, "全部流水", "搜索、核对和管理本地账单记录"),
            ["Budgets"] = (BudgetsPage, BudgetsNav, "预算计划", "规划每月支出，控制消费节奏"),
            ["Subscriptions"] = (SubscriptionsPage, SubscriptionsNav, "订阅与月卡", "看清自动续费、会员和游戏月卡的长期成本"),
            ["Categories"] = (CategoriesPage, CategoriesNav, "分类设置", "建立适合自己的收支分类体系"),
            ["Settings"] = (SettingsPage, SettingsNav, "偏好设置", "按自己的习惯调整分析标准和提醒计划"),
            ["Backup"] = (BackupPage, BackupNav, "数据备份", "复制和保护本地账本数据库")
        };
        DashboardGrid.ItemsSource = _records;
        TransactionsGrid.ItemsSource = _records;
        SubscriptionsGrid.ItemsSource = _subscriptions;
        DataPathText.Text = _store.DatabasePath;
        WeeklySummaryDayBox.ItemsSource = new[] { "星期一", "星期二", "星期三", "星期四", "星期五", "星期六", "星期日" };
        LoadSettings();
        LoadRecords();
        ShowPage("Dashboard");
    }

    private void NavigationClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string key }) ShowPage(key);
    }

    private void ShowPage(string key)
    {
        foreach (var item in _navigation.Values)
        {
            item.Page.Visibility = Visibility.Collapsed;
            item.Button.Style = (Style)FindResource("NavButton");
        }
        var selected = _navigation[key];
        selected.Page.Visibility = Visibility.Visible;
        selected.Button.Style = (Style)FindResource("SelectedNavButton");
        PageTitle.Text = selected.Title;
        PageSubtitle.Text = selected.Subtitle;
    }

    private void LoadRecords(string? search = null)
    {
        var rows = _store.List(search);
        _records.Clear();
        foreach (var row in rows) _records.Add(row);
        var month = DateTime.Now.ToString("yyyy-MM");
        var current = rows.Where(r => r.OccurredOn.ToString("yyyy-MM") == month).ToList();
        var income = current.Where(r => r.Direction == "收入").Sum(r => r.Amount);
        var expense = current.Where(r => r.Direction == "支出").Sum(r => r.Amount);
        IncomeText.Text = $"¥{income:N2}";
        ExpenseText.Text = $"¥{expense:N2}";
        BalanceText.Text = $"¥{income - expense:N2}";
        CountText.Text = $"共 {_records.Count} 条记录 · 本月 {current.Count} 条";
        LoadSubscriptionStats();
        StatusText.Text = search is null ? "数据已从本地数据库加载" : $"搜索到 {_records.Count} 条记录";
    }

    private void LoadSubscriptionStats()
    {
        var settings = _store.LoadSettings();
        var keywords = settings.SubscriptionKeywords
            .Split([',', '，', ';', '；', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var since = DateTime.Now.Date.AddMonths(-12);
        var detected = _store.List()
            .Where(r => r.Direction == "支出" && r.OccurredOn >= since)
            .Where(r => keywords.Any(keyword => $"{r.Merchant} {r.Category} {r.Note}".Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var summaries = detected
            .GroupBy(r => string.IsNullOrWhiteSpace(r.Merchant) ? "未注明交易对方" : r.Merchant.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => new SubscriptionSummary
            {
                Merchant = group.Key,
                Category = group.GroupBy(r => r.Category).OrderByDescending(g => g.Count()).First().Key,
                PaymentCount = group.Count(),
                PaidLast12Months = group.Sum(r => r.Amount),
                MonthlyAverage = group.Sum(r => r.Amount) / 12m,
                LatestPayment = group.Max(r => r.OccurredOn)
            })
            .OrderByDescending(item => item.MonthlyAverage)
            .ToList();
        _subscriptions.Clear();
        foreach (var summary in summaries) _subscriptions.Add(summary);
        var currentMonth = DateTime.Now.ToString("yyyy-MM");
        SubscriptionCurrentMonthText.Text = $"¥{detected.Where(r => r.OccurredOn.ToString("yyyy-MM") == currentMonth).Sum(r => r.Amount):N2}";
        SubscriptionMonthlyAverageText.Text = $"¥{summaries.Sum(item => item.MonthlyAverage):N2}";
        SubscriptionProviderCountText.Text = $"{summaries.Count} 项";
        SubscriptionHintText.Text = keywords.Length == 0
            ? "尚未设置订阅识别关键词，请前往偏好设置添加。"
            : $"按 {keywords.Length} 个关键词自动识别；近 12 个月总付款按 12 个月均摊。";
    }

    private void SearchClick(object sender, RoutedEventArgs e) => LoadRecords(SearchBox.Text.Trim());

    private void ImportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "账单文件|*.xlsx;*.xlsm;*.csv|Excel 文件|*.xlsx;*.xlsm|CSV 文件|*.csv", Multiselect = true };
        if (dialog.ShowDialog() != true) return;
        var imported = 0;
        try
        {
            foreach (var file in dialog.FileNames) imported += _store.Import(_importer.Read(file));
            LoadRecords();
            ShowPage("Transactions");
            StatusText.Text = $"导入完成：新增 {imported} 条，重复记录已跳过";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "导入失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void AddClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ManualEntryWindow { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is null) return;
        _store.Import([dialog.Result]);
        LoadRecords();
        ShowPage("Transactions");
        StatusText.Text = "手动流水已保存到本地数据库";
    }

    private void LoadSettings()
    {
        var settings = _store.LoadSettings();
        SmallExpenseThresholdBox.Text = settings.SmallExpenseThreshold.ToString("0.##");
        MonthlyBudgetBox.Text = settings.MonthlyBudget.ToString("0.##");
        DailyReminderCheck.IsChecked = settings.DailyReminderEnabled;
        DailyReminderTimeBox.Text = settings.DailyReminderTime;
        WeeklySummaryCheck.IsChecked = settings.WeeklySummaryEnabled;
        WeeklySummaryDayBox.SelectedIndex = settings.WeeklySummaryDay == DayOfWeek.Sunday ? 6 : (int)settings.WeeklySummaryDay - 1;
        WeeklySummaryTimeBox.Text = settings.WeeklySummaryTime;
        SubscriptionKeywordsBox.Text = settings.SubscriptionKeywords;
    }

    private void SaveSettingsClick(object sender, RoutedEventArgs e)
    {
        if (!decimal.TryParse(SmallExpenseThresholdBox.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var threshold) || threshold < 0 ||
            !decimal.TryParse(MonthlyBudgetBox.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var budget) || budget < 0)
        {
            MessageBox.Show("小额消费上限和月度预算必须是大于或等于 0 的数字。", "设置未保存", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!TimeOnly.TryParseExact(DailyReminderTimeBox.Text.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ||
            !TimeOnly.TryParseExact(WeeklySummaryTimeBox.Text.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            MessageBox.Show("提醒时间请使用 24 小时制 HH:mm，例如 21:00。", "设置未保存", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var dayIndex = Math.Max(0, WeeklySummaryDayBox.SelectedIndex);
        _store.SaveSettings(new AppSettings
        {
            SmallExpenseThreshold = threshold,
            MonthlyBudget = budget,
            DailyReminderEnabled = DailyReminderCheck.IsChecked == true,
            DailyReminderTime = DailyReminderTimeBox.Text.Trim(),
            WeeklySummaryEnabled = WeeklySummaryCheck.IsChecked == true,
            WeeklySummaryDay = dayIndex == 6 ? DayOfWeek.Sunday : (DayOfWeek)(dayIndex + 1),
            WeeklySummaryTime = WeeklySummaryTimeBox.Text.Trim(),
            SubscriptionKeywords = SubscriptionKeywordsBox.Text.Trim()
        });
        LoadSubscriptionStats();
        StatusText.Text = "偏好设置已保存，将用于消费分析和提醒计划";
    }

    private void BackupClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "SQLite 备份|*.db", FileName = $"duxiu-ledger-{DateTime.Now:yyyyMMdd-HHmm}.db" };
        if (dialog.ShowDialog() != true) return;
        File.Copy(_store.DatabasePath, dialog.FileName, true);
        StatusText.Text = $"备份完成：{dialog.FileName}";
    }

    private void OpenDataFolderClick(object sender, RoutedEventArgs e)
    {
        var folder = Path.GetDirectoryName(_store.DatabasePath)!;
        Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
    }
}
