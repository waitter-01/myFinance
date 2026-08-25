using DuxiuLedger.Desktop.Services;
using DuxiuLedger.Desktop.Models;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using System.Reflection;

namespace DuxiuLedger.WinUI;

public sealed partial class MainWindow : Window
{
    private readonly LocalStore _store = new();
    private readonly BillImporter _importer = new();
    private readonly Dictionary<string, (string Title, string Subtitle)> _pages = new()
    {
        ["Dashboard"] = ("总览", "查看本月财务情况和最近流水"),
        ["Transactions"] = ("全部流水", "搜索、核对和管理本地账单记录"),
        ["Budgets"] = ("预算计划", "规划每月支出，控制消费节奏"),
        ["Subscriptions"] = ("订阅与月卡", "看清自动续费、会员和游戏月卡的长期成本"),
        ["Accounts"] = ("账户管理", "管理现金、银行卡、电子钱包和信用账户"),
        ["Categories"] = ("分类设置", "建立适合自己的收支分类体系"),
        ["Backup"] = ("数据备份", "复制和保护本地账本数据库"),
        ["Settings"] = ("偏好设置", "按自己的习惯调整分析标准和提醒计划")
    };

    public MainWindow()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.2.0";
        AppTitleBar.Subtitle = $"个人财务中心 · v{version}";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
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
        var expense = records.Where(row => row.Direction == "支出").Sum(row => row.Amount)
            - records.Where(row => row.Direction is "退款" or "报销").Sum(row => row.Amount);
        IncomeText.Text = $"¥{income:N2}";
        ExpenseText.Text = $"¥{expense:N2}";
        BalanceText.Text = $"¥{income - expense:N2}";
        RecentList.ItemsSource = allRecords.Take(10).ToList();
        TransactionsList.ItemsSource = allRecords;
        RecordCountText.Text = $"共 {allRecords.Count} 条记录 · 本月 {records.Count} 条";
        LoadSubscriptions(allRecords);
        LoadAccounts();
        StatusText.Text = $"已读取本地账本 · 本月 {records.Count} 条流水";
    }

    private void LoadAccounts() => AccountsList.ItemsSource = _store.ListAccounts();

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
        AccountsPage.Visibility = key == "Accounts" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = key == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        BackupPage.Visibility = key == "Backup" ? Visibility.Visible : Visibility.Collapsed;
        PlaceholderPage.Visibility = key is "Budgets" or "Categories" ? Visibility.Visible : Visibility.Collapsed;
        PlaceholderTitle.Text = $"{page.Title}正在建设";
    }

    private void TitleBarPaneToggleRequested(TitleBar sender, object args) => NavView.IsPaneOpen = !NavView.IsPaneOpen;

    private async void ImportClick(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.FileTypeFilter.Add(".xlsx");
        picker.FileTypeFilter.Add(".xlsm");
        picker.FileTypeFilter.Add(".csv");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var files = await picker.PickMultipleFilesAsync();
        if (files.Count == 0) return;
        var previews = new List<ImportPreviewResult>();
        foreach (var file in files)
        {
            try { previews.Add(_importer.Preview(file.Path)); }
            catch (Exception ex)
            {
                previews.Add(new ImportPreviewResult
                {
                    Source = file.Name,
                    Issues = [new ImportIssue { Source = file.Name, RowNumber = 0, Reason = "文件无法解析", RawValue = ex.Message }]
                });
            }
        }
        var previewDialog = new ImportPreviewDialog(previews, _store.ListAccounts(), _store.ExistingFingerprints()) { XamlRoot = ContentHost.XamlRoot };
        if (await previewDialog.ShowAsync() != ContentDialogResult.Primary) return;
        var imported = _store.Import(previewDialog.RowsToImport);
        LoadDashboard();
        SelectNavigation("Transactions");
        StatusText.Text = $"导入完成：新增 {imported} 条，重复 {previewDialog.DuplicateCount} 条，问题行 {previewDialog.IssueCount} 条";
    }

    private async void AddClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ManualEntryDialog(_store.ListAccounts()) { XamlRoot = ContentHost.XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || dialog.Result is null) return;
        _store.Import([dialog.Result]);
        LoadDashboard();
        SelectNavigation("Transactions");
        StatusText.Text = "手动流水已保存到本地数据库";
    }

    private async void EditTransactionClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TransactionRecord record }) return;
        var dialog = new ManualEntryDialog(record, _store.ListAccounts()) { XamlRoot = ContentHost.XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || dialog.Result is null) return;
        if (!_store.Update(dialog.Result))
        {
            await ShowMessage("保存失败", "没有找到要编辑的流水，它可能已经被删除。 ");
            return;
        }
        LoadDashboard();
        SelectNavigation("Transactions");
        StatusText.Text = "流水修改已保存";
    }

    private async void DeleteTransactionClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TransactionRecord record }) return;
        var confirm = new ContentDialog
        {
            XamlRoot = ContentHost.XamlRoot,
            Title = "删除这条流水？",
            Content = $"{record.DateDisplay} · {record.Merchant}\n{record.Direction} {record.AmountDisplay}\n\n删除后只能通过数据库备份恢复。",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
        if (!_store.Delete(record.Id))
        {
            await ShowMessage("删除失败", "没有找到这条流水，它可能已经被删除。 ");
            return;
        }
        LoadDashboard();
        SelectNavigation("Transactions");
        StatusText.Text = "流水已删除";
    }

    private async void AddAccountClick(object sender, RoutedEventArgs e)
    {
        var dialog = new AccountDialog { XamlRoot = ContentHost.XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || dialog.Result is null) return;
        try
        {
            _store.SaveAccount(dialog.Result);
            LoadDashboard();
            SelectNavigation("Accounts");
            StatusText.Text = "账户已添加";
        }
        catch (Exception ex) { await ShowMessage("账户保存失败", ex.Message); }
    }

    private async void EditAccountClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AccountRecord account }) return;
        var dialog = new AccountDialog(account) { XamlRoot = ContentHost.XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || dialog.Result is null) return;
        try
        {
            _store.SaveAccount(dialog.Result);
            LoadDashboard();
            SelectNavigation("Accounts");
            StatusText.Text = "账户修改已保存";
        }
        catch (Exception ex) { await ShowMessage("账户保存失败", ex.Message); }
    }

    private async void DeleteAccountClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AccountRecord account }) return;
        var confirm = new ContentDialog
        {
            XamlRoot = ContentHost.XamlRoot,
            Title = "删除这个账户？",
            Content = $"{account.Name} · {account.Type}\n当前余额 {account.CurrentBalanceDisplay}",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            _store.DeleteAccount(account.Id);
            LoadDashboard();
            SelectNavigation("Accounts");
            StatusText.Text = "账户已删除";
        }
        catch (Exception ex) { await ShowMessage("账户不能删除", ex.Message); }
    }

    private void SelectNavigation(string key)
    {
        var item = NavView.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(menuItem => menuItem.Tag?.ToString() == key);
        if (item is not null) NavView.SelectedItem = item;
    }

    private async Task ShowMessage(string title, string message)
    {
        var dialog = new ContentDialog { XamlRoot = ContentHost.XamlRoot, Title = title, Content = message, CloseButtonText = "确定" };
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
