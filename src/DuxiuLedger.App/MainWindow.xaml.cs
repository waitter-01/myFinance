using DuxiuLedger.Desktop.Services;
using DuxiuLedger.Desktop.Models;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;
using System.Reflection;

namespace DuxiuLedger.WinUI;

public sealed partial class MainWindow : Window
{
    private static readonly HashSet<string> ScreenshotExtensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff" };
    private readonly LocalStore _store = new();
    private readonly BillImporter _importer = new();
    private readonly ScreenshotBillImporter _screenshotImporter = new();
    private readonly S3SyncService _syncService;
    private readonly FinancialAnalysisService _analysisService = new();
    private AnalysisPeriodKind _analysisPeriod = AnalysisPeriodKind.Month;
    private DateTime _analysisAnchor = DateTime.Today;
    private TransactionQuery _transactionQuery = new();
    private readonly CollectionViewSource _transactionGroupsSource = new() { IsSourceGrouped = true };
    private readonly List<TransactionFilterOption> _transactionCategoryOptions = [];
    private readonly List<TransactionFilterOption> _transactionAccountOptions = [];
    private readonly List<TransactionFilterOption> _transactionSourceOptions = [];
    private CancellationTokenSource? _transactionSearchDebounce;
    private bool _transactionFiltersReady;
    private bool _isInitialized;
    private readonly Dictionary<string, (string Title, string Subtitle)> _pages = new()
    {
        ["Dashboard"] = ("总览", "查看本月财务情况和最近流水"),
        ["Transactions"] = ("全部流水", "搜索、核对和管理本地账单记录"),
        ["Insights"] = ("消费洞察", "看清消费去向、小额支出和可优化空间"),
        ["Budgets"] = ("预算计划", "规划每月支出，控制消费节奏"),
        ["Subscriptions"] = ("订阅与月卡", "看清自动续费、会员和游戏月卡的长期成本"),
        ["Accounts"] = ("账户管理", "管理现金、银行卡、电子钱包和信用账户"),
        ["Categories"] = ("分类设置", "建立适合自己的收支分类体系"),
        ["Backup"] = ("数据备份", "复制和保护本地账本文件"),
        ["Settings"] = ("偏好设置", "按自己的习惯调整分析标准和提醒计划")
    };

    public MainWindow()
    {
        InitializeComponent();
        _syncService = new S3SyncService(_store);
        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.9.0";
        AppTitleBar.Subtitle = $"个人财务中心 · v{version}";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "duxiu-logo.ico");
        if (File.Exists(iconPath)) AppWindow.SetIcon(iconPath);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.Resize(new SizeInt32(1320, 850));
        WeeklySummaryDayBox.ItemsSource = new[] { "星期一", "星期二", "星期三", "星期四", "星期五", "星期六", "星期日" };
        DataPathText.Text = _store.DatabasePath;
        LoadSettings();
        InitializeTransactionFilters();
        _isInitialized = true;
        LoadDashboard();
        TrySyncOnStartup();
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
        if (_transactionFiltersReady) ApplyTransactionQuery();
        RecordCountText.Text = $"共 {allRecords.Count} 条记录 · 本月 {records.Count} 条";
        LoadSubscriptions(allRecords);
        LoadInsights(allRecords);
        LoadBudgets(allRecords);
        LoadAccounts();
        LoadCategories();
        StatusText.Text = $"已读取本地账本 · 本月 {records.Count} 条流水";
    }

    private void LoadAccounts() => AccountsList.ItemsSource = _store.ListAccounts();
    private void LoadCategories() => CategoriesList.ItemsSource = _store.ListCategories();

    private void LoadBudgets(IReadOnlyList<TransactionRecord> allRecords)
    {
        BudgetMonthPicker.Date ??= DateTimeOffset.Now;
        var month = BudgetMonthPicker.Date.Value.ToString("yyyy-MM");
        var budgets = _store.ListBudgets(month);
        var settings = _store.LoadSettings();
        var monthSpent = allRecords.Where(row => row.Direction == "支出" && row.OccurredOn.ToString("yyyy-MM") == month).Sum(row => row.Amount);
        var totalBudget = settings.MonthlyBudget > 0 ? settings.MonthlyBudget : budgets.Sum(item => item.Amount);
        var remaining = totalBudget - monthSpent;
        BudgetTotalText.Text = totalBudget > 0 ? $"¥{totalBudget:N2}" : "未设置";
        BudgetSpentText.Text = $"¥{monthSpent:N2}";
        BudgetRemainingText.Text = totalBudget <= 0 ? "—" : remaining >= 0 ? $"¥{remaining:N2}" : $"超出 ¥{Math.Abs(remaining):N2}";
        BudgetTotalProgress.Maximum = 1;
        BudgetTotalProgress.Value = totalBudget <= 0 ? 0 : Math.Min(1, (double)(monthSpent / totalBudget));
        BudgetsList.ItemsSource = budgets;
        SavingsGoalsList.ItemsSource = _store.ListSavingsGoals();
    }

    private void LoadInsights(IReadOnlyList<TransactionRecord> allRecords)
    {
        var settings = _store.LoadSettings();
        var result = _analysisService.Analyze(allRecords, settings, _analysisPeriod, _analysisAnchor);
        var currentStart = FinancialAnalysisService.GetRange(_analysisPeriod, DateTime.Today).Start;
        AnalysisRangeText.Text = result.PeriodLabel;
        AnalysisNextButton.IsEnabled = result.Start < currentStart;
        AnalysisIncomeText.Text = $"¥{result.Income:N2}";
        AnalysisExpenseText.Text = $"¥{result.NetExpense:N2}";
        AnalysisExpenseDetailText.Text = result.Refunds > 0 ? $"原支出 ¥{result.GrossExpense:N2} · 已扣退款/报销 ¥{result.Refunds:N2}" : $"{result.TransactionCount} 笔非转账记录";
        AnalysisBalanceText.Text = $"¥{result.Balance:N2}";
        AnalysisSavingsRateText.Text = result.Income <= 0 ? "—" : $"{result.SavingsRate:P1}";
        AnalysisSavingsDetailText.Text = result.Income <= 0 ? "录入收入后计算" : result.SavingsRate >= 0.2m ? "高于 20%，结余空间良好" : "建议提高到 20% 以上";
        InsightSmallText.Text = $"¥{result.SmallExpense:N2}";
        InsightSmallDetailText.Text = $"{result.SmallExpenseCount} 笔 · 单笔不超过 ¥{settings.SmallExpenseThreshold:N0}";
        InsightOptionalText.Text = $"¥{result.OptionalExpense:N2}";
        InsightOptionalDetailText.Text = result.GrossExpense > 0 ? $"占支出 {result.OptionalExpense / result.GrossExpense:P1}" : "暂无支出";
        InsightTopCategoryText.Text = result.CategoryRanks.Count == 0 ? "暂无数据" : result.CategoryRanks[0].Name;
        InsightTopCategoryDetailText.Text = result.CategoryRanks.Count == 0 ? "完善分类后生成" : $"{result.CategoryRanks[0].AmountDisplay} · {result.CategoryRanks[0].ShareDisplay}";
        InsightBudgetText.Text = $"¥{result.SuggestedLimit:N0}";
        InsightBudgetDetailText.Text = "当前周期的系统建议控制额度";
        AnalysisSummaryText.Text = $"{result.PeriodLabel}共记录 {result.TransactionCount} 笔非转账流水，收入 ¥{result.Income:N2}，净支出 ¥{result.NetExpense:N2}，结余 ¥{result.Balance:N2}，日均支出 ¥{result.DailyAverage:N2}。";
        AnalysisComparisonText.Text = result.ExpenseChangeRate is null
            ? $"{result.PreviousPeriodLabel}暂无足够支出，继续记录后可进行同期对比。"
            : $"与{result.PreviousPeriodLabel}同期相比，净支出{(result.ExpenseChangeRate >= 0 ? "增加" : "减少")} {Math.Abs(result.ExpenseChangeRate.Value):P1}（上期 ¥{result.PreviousNetExpense:N2}）。";
        AnalysisLargestText.Text = result.LargestExpense is null ? "暂无单笔支出" : $"最大单笔：{result.LargestExpense.Merchant} · ¥{result.LargestExpense.Amount:N2} · {result.LargestExpense.OccurredOn:MM-dd}";
        AnalysisTrendTitle.Text = _analysisPeriod switch { AnalysisPeriodKind.Week => "每日收支趋势", AnalysisPeriodKind.Year => "月度收支趋势", _ => "每周收支趋势" };
        AnalysisTrendList.ItemsSource = result.Trend;
        CategoryRankingList.ItemsSource = result.CategoryRanks.Take(8).ToList();
        MerchantRankingList.ItemsSource = result.MerchantRanks.Take(8).ToList();
        InsightSuggestionsList.ItemsSource = result.Suggestions;
    }

    private void AnalysisPeriodChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized || AnalysisPeriodBox.SelectedIndex < 0) return;
        _analysisPeriod = (AnalysisPeriodKind)AnalysisPeriodBox.SelectedIndex;
        _analysisAnchor = DateTime.Today;
        LoadInsights(_store.List().ToList());
    }

    private void AnalysisPreviousClick(object sender, RoutedEventArgs e)
    {
        _analysisAnchor = _analysisPeriod switch { AnalysisPeriodKind.Week => _analysisAnchor.AddDays(-7), AnalysisPeriodKind.Year => _analysisAnchor.AddYears(-1), _ => _analysisAnchor.AddMonths(-1) };
        LoadInsights(_store.List().ToList());
    }

    private void AnalysisNextClick(object sender, RoutedEventArgs e)
    {
        _analysisAnchor = _analysisPeriod switch { AnalysisPeriodKind.Week => _analysisAnchor.AddDays(7), AnalysisPeriodKind.Year => _analysisAnchor.AddYears(1), _ => _analysisAnchor.AddMonths(1) };
        LoadInsights(_store.List().ToList());
    }

    private void AnalysisTodayClick(object sender, RoutedEventArgs e)
    {
        _analysisAnchor = DateTime.Today;
        LoadInsights(_store.List().ToList());
    }

    private void LoadSubscriptions(IReadOnlyList<TransactionRecord> allRecords)
    {
        var keywords = _store.LoadSettings().SubscriptionKeywords.Split([',', '，', ';', '；', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var since = DateTime.Now.Date.AddMonths(-12);
        var detected = allRecords.Where(row => row.Direction == "支出" && row.OccurredOn >= since).Where(row => row.Category == "订阅消费" || keywords.Any(keyword => $"{row.Merchant} {row.Category} {row.Note}".Contains(keyword, StringComparison.OrdinalIgnoreCase))).ToList();
        var summaries = detected.GroupBy(row => string.IsNullOrWhiteSpace(row.Merchant) ? "未注明交易对方" : row.Merchant.Trim(), StringComparer.OrdinalIgnoreCase).Select(group =>
        {
            var latest = group.OrderByDescending(row => row.OccurredOn).ThenByDescending(row => row.Id).First();
            var billingMonths = Math.Max(1, latest.SubscriptionMonths);
            return new SubscriptionSummary
            {
                Merchant = group.Key,
                Category = group.GroupBy(row => row.Category).OrderByDescending(item => item.Count()).First().Key,
                PaymentCount = group.Count(),
                PaidLast12Months = group.Sum(row => row.Amount),
                MonthlyAverage = latest.Amount / billingMonths,
                BillingMonths = billingMonths,
                LatestPayment = latest.OccurredOn
            };
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
        OptionalCategoriesBox.Text = settings.OptionalCategories;
        S3SyncEnabledCheck.IsChecked = settings.S3SyncEnabled;
        SyncOnStartupCheck.IsChecked = settings.SyncOnStartup;
        S3AccessUrlBox.Text = settings.S3AccessUrl;
        S3EndpointBox.Text = settings.S3Endpoint;
        S3RegionBox.Text = settings.S3Region;
        S3BucketBox.Text = settings.S3Bucket;
        S3ObjectKeyBox.Text = settings.S3ObjectKey;
        S3AccessKeyIdBox.Text = settings.S3AccessKeyId;
        S3ForcePathStyleCheck.IsChecked = settings.S3ForcePathStyle;
        S3SecretKeyBox.PlaceholderText = string.IsNullOrEmpty(_store.LoadS3SecretKey()) ? "请输入 Secret Access Key" : "密钥已由 Windows 当前用户加密保存";
        S3SessionTokenBox.PlaceholderText = string.IsNullOrEmpty(_store.LoadS3SessionToken()) ? "可选，仅临时凭据需要" : "令牌已由 Windows 当前用户加密保存";
    }

    private void NavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is not string key || !_pages.TryGetValue(key, out var page)) return;
        PageTitle.Text = page.Title;
        PageSubtitle.Text = page.Subtitle;
        DashboardPage.Visibility = key == "Dashboard" ? Visibility.Visible : Visibility.Collapsed;
        TransactionsPage.Visibility = key == "Transactions" ? Visibility.Visible : Visibility.Collapsed;
        InsightsPage.Visibility = key == "Insights" ? Visibility.Visible : Visibility.Collapsed;
        BudgetsPage.Visibility = key == "Budgets" ? Visibility.Visible : Visibility.Collapsed;
        SubscriptionsPage.Visibility = key == "Subscriptions" ? Visibility.Visible : Visibility.Collapsed;
        AccountsPage.Visibility = key == "Accounts" ? Visibility.Visible : Visibility.Collapsed;
        CategoriesPage.Visibility = key == "Categories" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = key == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        BackupPage.Visibility = key == "Backup" ? Visibility.Visible : Visibility.Collapsed;
        PlaceholderPage.Visibility = Visibility.Collapsed;
        PlaceholderTitle.Text = $"{page.Title}正在建设";
    }

    private void BudgetMonthChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (sender.Date is not null) LoadBudgets(_store.List());
    }

    private async void AddBudgetClick(object sender, RoutedEventArgs e)
    {
        var dialog = new BudgetDialog(_store.ListCategories()) { XamlRoot = ContentHost.XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || dialog.Result is null) return;
        try { _store.SaveBudget(dialog.Result); LoadDashboard(); SelectNavigation("Budgets"); StatusText.Text = "分类预算已保存"; }
        catch (Exception ex) { await ShowMessage("预算保存失败", ex.Message); }
    }

    private async void DeleteBudgetClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: BudgetRecord budget }) return;
        var confirm = new ContentDialog { XamlRoot = ContentHost.XamlRoot, Title = "删除这条预算？", Content = $"{budget.Month} · {budget.Category} · {budget.AmountDisplay}", PrimaryButtonText = "删除", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
        _store.DeleteBudget(budget.Id); LoadDashboard(); SelectNavigation("Budgets"); StatusText.Text = "分类预算已删除";
    }

    private async void AddSavingsGoalClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SavingsGoalDialog { XamlRoot = ContentHost.XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || dialog.Result is null) return;
        try { _store.SaveSavingsGoal(dialog.Result); LoadDashboard(); SelectNavigation("Budgets"); StatusText.Text = "储蓄目标已添加"; }
        catch (Exception ex) { await ShowMessage("目标保存失败", ex.Message); }
    }

    private async void EditSavingsGoalClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SavingsGoalRecord goal }) return;
        var dialog = new SavingsGoalDialog(goal) { XamlRoot = ContentHost.XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || dialog.Result is null) return;
        try { _store.SaveSavingsGoal(dialog.Result); LoadDashboard(); SelectNavigation("Budgets"); StatusText.Text = "储蓄目标已更新"; }
        catch (Exception ex) { await ShowMessage("目标保存失败", ex.Message); }
    }

    private async void DeleteSavingsGoalClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SavingsGoalRecord goal }) return;
        var confirm = new ContentDialog { XamlRoot = ContentHost.XamlRoot, Title = "删除这个储蓄目标？", Content = $"{goal.Name}\n{goal.TargetDisplay}", PrimaryButtonText = "删除", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
        _store.DeleteSavingsGoal(goal.Id); LoadDashboard(); SelectNavigation("Budgets"); StatusText.Text = "储蓄目标已删除";
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
        var previewWindow = new ImportPreviewWindow(previews, _store.ListAccounts(), _store.ListCategories(), _store.List());
        if (!await previewWindow.ShowAsync(this)) return;
        var imported = _store.Import(previewWindow.RowsToImport);
        LoadDashboard();
        SelectNavigation("Transactions");
        StatusText.Text = $"导入完成：新增 {imported} 条，重复 {previewWindow.DuplicateCount} 条，问题行 {previewWindow.IssueCount} 条";
    }

    private async void ScreenshotImportClick(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".bmp");
        picker.FileTypeFilter.Add(".tif");
        picker.FileTypeFilter.Add(".tiff");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var files = await picker.PickMultipleFilesAsync();
        if (files.Count == 0) return;

        await ImportScreenshotFilesAsync(files.OfType<StorageFile>().ToList());
    }

    private void ScreenshotDragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems) && !e.DataView.Contains(StandardDataFormats.Bitmap)) return;
        e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "松开后识别账单截图";
        e.DragUIOverride.IsCaptionVisible = true;
        e.DragUIOverride.IsContentVisible = true;
    }

    private async void ScreenshotDrop(object sender, DragEventArgs e)
    {
        try
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                var files = items.OfType<StorageFile>().Where(IsSupportedScreenshot).ToList();
                if (files.Count > 0) { await ImportScreenshotFilesAsync(files); return; }
            }
            if (e.DataView.Contains(StandardDataFormats.Bitmap))
            {
                var bitmap = await e.DataView.GetBitmapAsync();
                using var stream = await bitmap.OpenReadAsync();
                await ImportScreenshotStreamAsync(stream, "拖拽图片.png");
                return;
            }
            StatusText.Text = "拖入的内容不是支持的图片，请使用 PNG、JPG、BMP 或 TIFF";
        }
        catch (Exception ex) { await ShowMessage("拖拽图片失败", ex.Message); }
    }

    private async void PasteScreenshotClick(object sender, RoutedEventArgs e) => await PasteScreenshotAsync();

    private async void PasteScreenshotAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (IsTextInputFocused())
        {
            args.Handled = false;
            return;
        }
        args.Handled = true;
        await PasteScreenshotAsync();
    }

    private bool IsTextInputFocused()
    {
        var focused = FocusManager.GetFocusedElement(Content.XamlRoot);
        return focused is TextBox or PasswordBox or RichEditBox or NumberBox or AutoSuggestBox;
    }

    private async Task PasteScreenshotAsync()
    {
        try
        {
            var content = Clipboard.GetContent();
            if (content.Contains(StandardDataFormats.Bitmap))
            {
                var bitmap = await content.GetBitmapAsync();
                using var stream = await bitmap.OpenReadAsync();
                await ImportScreenshotStreamAsync(stream, $"剪贴板-{DateTime.Now:yyyyMMdd-HHmmss}.png");
                return;
            }
            if (content.Contains(StandardDataFormats.StorageItems))
            {
                var items = await content.GetStorageItemsAsync();
                var files = items.OfType<StorageFile>().Where(IsSupportedScreenshot).ToList();
                if (files.Count > 0) { await ImportScreenshotFilesAsync(files); return; }
            }
            await ShowMessage("剪贴板中没有图片", "请先复制微信或支付宝账单截图，然后点击“粘贴截图”或按 Ctrl+V。");
        }
        catch (Exception ex) { await ShowMessage("读取剪贴板失败", ex.Message); }
    }

    private async Task ImportScreenshotFilesAsync(IReadOnlyList<StorageFile> files)
    {
        if (files.Count == 0) return;
        StatusText.Text = $"正在本地识别 {files.Count} 张账单截图…";
        var previews = new List<ImportPreviewResult>();
        foreach (var file in files)
        {
            try { previews.Add(await _screenshotImporter.PreviewAsync(file.Path)); }
            catch (Exception ex)
            {
                previews.Add(new ImportPreviewResult
                {
                    Source = file.Name,
                    Issues = [new ImportIssue { Source = file.Name, RowNumber = 0, Reason = "截图识别失败", RawValue = ex.Message }]
                });
            }
        }
        await ShowScreenshotPreviewsAsync(previews);
    }

    private async Task ImportScreenshotStreamAsync(IRandomAccessStream stream, string source)
    {
        StatusText.Text = "正在本地识别粘贴的账单截图…";
        var previews = new List<ImportPreviewResult>();
        try { previews.Add(await _screenshotImporter.PreviewAsync(stream, source)); }
        catch (Exception ex)
        {
            previews.Add(new ImportPreviewResult
            {
                Source = source,
                Issues = [new ImportIssue { Source = source, RowNumber = 0, Reason = "截图识别失败", RawValue = ex.Message }]
            });
        }
        await ShowScreenshotPreviewsAsync(previews);
    }

    private async Task ShowScreenshotPreviewsAsync(IReadOnlyList<ImportPreviewResult> previews)
    {
        var previewWindow = new ImportPreviewWindow(previews, _store.ListAccounts(), _store.ListCategories(), _store.List());
        if (!await previewWindow.ShowAsync(this))
        {
            StatusText.Text = "已取消截图导入，识别结果没有写入账本";
            return;
        }
        var imported = _store.Import(previewWindow.RowsToImport);
        LoadDashboard();
        SelectNavigation("Transactions");
        StatusText.Text = $"截图导入完成：新增 {imported} 条，重复 {previewWindow.DuplicateCount} 条，问题记录 {previewWindow.IssueCount} 条";
    }

    private static bool IsSupportedScreenshot(StorageFile file) => ScreenshotExtensions.Contains(Path.GetExtension(file.Name));

    private async void AddClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ManualEntryDialog(_store.ListAccounts(), _store.ListCategories()) { XamlRoot = ContentHost.XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || dialog.Result is null) return;
        _store.Import([dialog.Result]);
        LoadDashboard();
        SelectNavigation("Transactions");
        StatusText.Text = "手动流水已保存到本地账本";
    }

    private async void EditTransactionClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TransactionRecord record }) return;
        await EditTransactionAsync(record);
    }

    private async void EditTransactionMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: TransactionRecord record }) return;
        await EditTransactionAsync(record);
    }

    private async void TransactionItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TransactionRecord record) await EditTransactionAsync(record);
    }

    private async Task EditTransactionAsync(TransactionRecord record)
    {
        var dialog = new ManualEntryDialog(record, _store.ListAccounts(), _store.ListCategories()) { XamlRoot = ContentHost.XamlRoot };
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
        await DeleteTransactionAsync(record);
    }

    private async void DeleteTransactionMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: TransactionRecord record }) return;
        await DeleteTransactionAsync(record);
    }

    private async Task DeleteTransactionAsync(TransactionRecord record)
    {
        var confirm = new ContentDialog
        {
            XamlRoot = ContentHost.XamlRoot,
            Title = "删除这条流水？",
            Content = $"{record.DateDisplay} · {record.Merchant}\n{record.Direction} {record.AmountDisplay}\n\n删除后只能通过账本备份恢复。",
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

    private async void AddCategoryClick(object sender, RoutedEventArgs e)
    {
        var dialog = new CategoryDialog { XamlRoot = ContentHost.XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || dialog.Result is null) return;
        try
        {
            _store.SaveCategory(dialog.Result);
            LoadDashboard(); SelectNavigation("Categories"); StatusText.Text = "分类已添加";
        }
        catch (Exception ex) { await ShowMessage("分类保存失败", ex.Message); }
    }

    private async void EditCategoryClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: CategoryRecord category }) return;
        var dialog = new CategoryDialog(category) { XamlRoot = ContentHost.XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || dialog.Result is null) return;
        try
        {
            _store.SaveCategory(dialog.Result);
            LoadDashboard(); SelectNavigation("Categories"); StatusText.Text = "分类修改已保存";
        }
        catch (Exception ex) { await ShowMessage("分类保存失败", ex.Message); }
    }

    private async void DeleteCategoryClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: CategoryRecord category }) return;
        var confirm = new ContentDialog { XamlRoot = ContentHost.XamlRoot, Title = "删除这个分类？", Content = $"{category.Name} · {category.Type}\n{category.UsageDisplay}", PrimaryButtonText = "删除", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            _store.DeleteCategory(category.Id);
            LoadDashboard(); SelectNavigation("Categories"); StatusText.Text = "分类已删除";
        }
        catch (Exception ex) { await ShowMessage("分类不能删除", ex.Message); }
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

    private void InitializeTransactionFilters()
    {
        TransactionSortBox.SelectedIndex = 0;
        RefreshTransactionFilterOptions();
        _transactionFiltersReady = true;
    }

    private void RefreshTransactionFilterOptions()
    {
        SyncTransactionOptions(_transactionCategoryOptions, _store.ListCategories().Where(item => item.IsActive).Select(item => (item.Name, item.Name)));
        SyncTransactionOptions(_transactionAccountOptions, _store.ListAccounts().Where(item => item.IsActive).Select(item => (item.Id.ToString(), item.Name)));
        SyncTransactionOptions(_transactionSourceOptions, _store.ListTransactionSources().Select(item => (item, item)));
        FilterTransactionOptionLists();
    }

    private static void SyncTransactionOptions(List<TransactionFilterOption> target, IEnumerable<(string Key, string Display)> values)
    {
        var selected = target.Where(item => item.IsSelected).Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
        target.Clear();
        target.AddRange(values.DistinctBy(item => item.Key).Select(item => new TransactionFilterOption { Key = item.Key, Display = item.Display, IsSelected = selected.Contains(item.Key) }));
    }

    private void FilterTransactionOptionLists()
    {
        var categorySearch = TransactionCategorySearchBox.Text.Trim();
        var accountSearch = TransactionAccountSearchBox.Text.Trim();
        TransactionCategoryFilterList.ItemsSource = _transactionCategoryOptions.Where(item => item.Display.Contains(categorySearch, StringComparison.OrdinalIgnoreCase)).ToList();
        TransactionAccountFilterList.ItemsSource = _transactionAccountOptions.Where(item => item.Display.Contains(accountSearch, StringComparison.OrdinalIgnoreCase)).ToList();
        TransactionSourceFilterList.ItemsSource = _transactionSourceOptions;
    }

    private void ApplyTransactionQuery()
    {
        RefreshTransactionFilterOptions();
        var result = _store.QueryTransactions(_transactionQuery);
        IReadOnlyList<TransactionDateGroup> groups = _transactionQuery.SortBy is TransactionSortOption.DateAscending or TransactionSortOption.DateDescending
            ? result.Rows.GroupBy(row => row.OccurredOn.Date).Select(group => new TransactionDateGroup(group.Key, group)).ToList()
            : [new TransactionDateGroup(TransactionSortLabel(_transactionQuery.SortBy), result.Rows)];
        _transactionGroupsSource.Source = groups;
        TransactionsList.ItemsSource = _transactionGroupsSource.View;
        TransactionsList.Visibility = result.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        TransactionsEmptyState.Visibility = result.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        FilteredTransactionCountText.Text = $"找到 {result.Count} 笔";
        FilteredExpenseText.Text = $"净支出 ¥{result.NetExpense:N2}";
        FilteredIncomeText.Text = $"收入 ¥{result.Income:N2}";
        FilteredRefundText.Text = result.Refunds > 0 ? $"退款/报销 ¥{result.Refunds:N2}" : "无退款/报销";
        FilteredBalanceText.Text = result.Balance >= 0 ? $"筛选结余 ¥{result.Balance:N2}" : $"筛选净流出 ¥{Math.Abs(result.Balance):N2}";
        UpdateTransactionFilterPresentation();
        StatusText.Text = $"当前筛选显示 {result.Count} 条流水 · 净支出 ¥{result.NetExpense:N2}";
    }

    private static string TransactionSortLabel(TransactionSortOption sort) => sort switch
    {
        TransactionSortOption.AmountDescending => "按金额从高到低",
        TransactionSortOption.AmountAscending => "按金额从低到高",
        TransactionSortOption.MerchantAscending => "按商户名称排序",
        _ => "筛选结果"
    };

    private void UpdateTransactionFilterPresentation()
    {
        var chips = new List<TransactionFilterChip>();
        if (!string.IsNullOrWhiteSpace(_transactionQuery.SearchText)) chips.Add(new() { Key = "search", Label = $"关键词：{_transactionQuery.SearchText}  ×" });
        if (_transactionQuery.StartDate is not null || _transactionQuery.EndDate is not null)
        {
            var start = _transactionQuery.StartDate?.ToString("yyyy-MM-dd") ?? "最早";
            var end = _transactionQuery.EndDate?.ToString("yyyy-MM-dd") ?? "今天";
            chips.Add(new() { Key = "date", Label = $"{start} 至 {end}  ×" });
        }
        if (_transactionQuery.Directions.Count > 0) chips.Add(new() { Key = "direction", Label = $"类型：{string.Join('、', _transactionQuery.Directions)}  ×" });
        if (_transactionQuery.Categories.Count > 0) chips.Add(new() { Key = "category", Label = $"分类：{JoinFilterValues(_transactionQuery.Categories)}  ×" });
        if (_transactionQuery.AccountIds.Count > 0)
        {
            var names = _transactionAccountOptions.Where(item => _transactionQuery.AccountIds.Contains(long.Parse(item.Key))).Select(item => item.Display);
            chips.Add(new() { Key = "account", Label = $"账户：{JoinFilterValues(names)}  ×" });
        }
        if (_transactionQuery.MinimumAmount is not null || _transactionQuery.MaximumAmount is not null) chips.Add(new() { Key = "amount", Label = $"金额：{_transactionQuery.MinimumAmount?.ToString("N2") ?? "0"}～{_transactionQuery.MaximumAmount?.ToString("N2") ?? "不限"}  ×" });
        if (_transactionQuery.Sources.Count > 0) chips.Add(new() { Key = "source", Label = $"来源：{JoinFilterValues(_transactionQuery.Sources)}  ×" });
        if (_transactionQuery.UncategorizedOnly) chips.Add(new() { Key = "uncategorized", Label = "只看未分类  ×" });
        if (_transactionQuery.SubscriptionOnly) chips.Add(new() { Key = "subscription", Label = "只看订阅消费  ×" });
        if (_transactionQuery.UnassignedAccountOnly) chips.Add(new() { Key = "unassigned", Label = "只看未指定账户  ×" });
        TransactionFilterChipsControl.ItemsSource = chips;
        ClearAllTransactionFiltersButton.Visibility = chips.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        DirectionFilterButton.Content = _transactionQuery.Directions.Count == 0 ? "收支类型" : $"收支类型 · {_transactionQuery.Directions.Count}";
        CategoryFilterButton.Content = _transactionQuery.Categories.Count == 0 ? "分类" : $"分类 · {_transactionQuery.Categories.Count}";
        AccountFilterButton.Content = _transactionQuery.AccountIds.Count == 0 ? "账户" : $"账户 · {_transactionQuery.AccountIds.Count}";
        var moreCount = _transactionQuery.Sources.Count + (_transactionQuery.MinimumAmount is null ? 0 : 1) + (_transactionQuery.MaximumAmount is null ? 0 : 1)
            + (_transactionQuery.UncategorizedOnly ? 1 : 0) + (_transactionQuery.SubscriptionOnly ? 1 : 0) + (_transactionQuery.UnassignedAccountOnly ? 1 : 0);
        MoreTransactionFiltersButton.Content = moreCount == 0 ? "更多筛选" : $"更多筛选 · {moreCount}";
    }

    private static string JoinFilterValues(IEnumerable<string> values)
    {
        var list = values.ToList();
        return list.Count <= 3 ? string.Join('、', list) : $"{string.Join('、', list.Take(2))} 等 {list.Count} 项";
    }

    private async void TransactionsSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (!_transactionFiltersReady || args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        _transactionSearchDebounce?.Cancel();
        var debounce = _transactionSearchDebounce = new CancellationTokenSource();
        try
        {
            await Task.Delay(300, debounce.Token);
            _transactionQuery.SearchText = sender.Text.Trim();
            ApplyTransactionQuery();
        }
        catch (OperationCanceledException) { }
    }

    private void TransactionsSearchSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (!_transactionFiltersReady) return;
        _transactionSearchDebounce?.Cancel();
        _transactionQuery.SearchText = args.QueryText.Trim();
        ApplyTransactionQuery();
    }

    private void TransactionDatePresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_transactionFiltersReady) return;
        var today = DateTime.Today;
        (_transactionQuery.StartDate, _transactionQuery.EndDate) = TransactionDatePresetBox.SelectedIndex switch
        {
            1 => (new DateTime(today.Year, today.Month, 1), new DateTime(today.Year, today.Month, 1).AddMonths(1).AddDays(-1)),
            2 => (new DateTime(today.Year, today.Month, 1).AddMonths(-1), new DateTime(today.Year, today.Month, 1).AddDays(-1)),
            3 => (today.AddDays(-6), today),
            4 => (today.AddDays(-29), today),
            5 => (new DateTime(today.Year, 1, 1), new DateTime(today.Year, 12, 31)),
            6 => (_transactionQuery.StartDate, _transactionQuery.EndDate),
            _ => (null, null)
        };
        if (TransactionDatePresetBox.SelectedIndex == 6)
        {
            TransactionFilterSplitOpen();
            return;
        }
        TransactionStartDatePicker.Date = _transactionQuery.StartDate;
        TransactionEndDatePicker.Date = _transactionQuery.EndDate;
        ApplyTransactionQuery();
    }

    private void OpenTransactionFiltersClick(object sender, RoutedEventArgs e) => TransactionFilterSplitOpen();
    private void TransactionFilterSplitOpen() => TransactionsPage.IsPaneOpen = true;
    private void CloseTransactionFiltersClick(object sender, RoutedEventArgs e) => TransactionsPage.IsPaneOpen = false;

    private void TransactionCategorySearchChanged(object sender, TextChangedEventArgs e)
    {
        if (_transactionFiltersReady) FilterTransactionOptionLists();
    }

    private void TransactionAccountSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (_transactionFiltersReady) FilterTransactionOptionLists();
    }

    private void ApplyTransactionFiltersClick(object sender, RoutedEventArgs e)
    {
        _transactionQuery.StartDate = TransactionStartDatePicker.Date?.DateTime.Date;
        _transactionQuery.EndDate = TransactionEndDatePicker.Date?.DateTime.Date;
        if (_transactionQuery.StartDate > _transactionQuery.EndDate) (_transactionQuery.StartDate, _transactionQuery.EndDate) = (_transactionQuery.EndDate, _transactionQuery.StartDate);
        _transactionQuery.Directions = SelectedDirections();
        _transactionQuery.Categories = _transactionCategoryOptions.Where(item => item.IsSelected).Select(item => item.Key).ToList();
        _transactionQuery.AccountIds = _transactionAccountOptions.Where(item => item.IsSelected).Select(item => long.Parse(item.Key)).ToList();
        _transactionQuery.Sources = _transactionSourceOptions.Where(item => item.IsSelected).Select(item => item.Key).ToList();
        _transactionQuery.MinimumAmount = double.IsNaN(TransactionMinimumAmountBox.Value) ? null : (decimal)Math.Max(0, TransactionMinimumAmountBox.Value);
        _transactionQuery.MaximumAmount = double.IsNaN(TransactionMaximumAmountBox.Value) ? null : (decimal)Math.Max(0, TransactionMaximumAmountBox.Value);
        if (_transactionQuery.MinimumAmount > _transactionQuery.MaximumAmount) (_transactionQuery.MinimumAmount, _transactionQuery.MaximumAmount) = (_transactionQuery.MaximumAmount, _transactionQuery.MinimumAmount);
        _transactionQuery.UncategorizedOnly = TransactionUncategorizedCheck.IsChecked == true;
        _transactionQuery.SubscriptionOnly = TransactionSubscriptionCheck.IsChecked == true;
        _transactionQuery.UnassignedAccountOnly = TransactionUnassignedAccountCheck.IsChecked == true;
        _transactionQuery.SortBy = (TransactionSortOption)Math.Max(0, TransactionSortBox.SelectedIndex);
        if (_transactionQuery.StartDate is not null || _transactionQuery.EndDate is not null)
        {
            _transactionFiltersReady = false;
            TransactionDatePresetBox.SelectedIndex = 6;
            _transactionFiltersReady = true;
        }
        TransactionStartDatePicker.Date = _transactionQuery.StartDate;
        TransactionEndDatePicker.Date = _transactionQuery.EndDate;
        TransactionMinimumAmountBox.Value = _transactionQuery.MinimumAmount is null ? double.NaN : (double)_transactionQuery.MinimumAmount.Value;
        TransactionMaximumAmountBox.Value = _transactionQuery.MaximumAmount is null ? double.NaN : (double)_transactionQuery.MaximumAmount.Value;
        TransactionsPage.IsPaneOpen = false;
        ApplyTransactionQuery();
    }

    private IReadOnlyList<string> SelectedDirections()
    {
        var values = new List<string>();
        if (FilterExpenseCheck.IsChecked == true) values.Add("支出");
        if (FilterIncomeCheck.IsChecked == true) values.Add("收入");
        if (FilterTransferCheck.IsChecked == true) values.Add("转账");
        if (FilterRefundCheck.IsChecked == true) values.Add("退款");
        if (FilterReimbursementCheck.IsChecked == true) values.Add("报销");
        return values;
    }

    private void ResetTransactionFiltersClick(object sender, RoutedEventArgs e) => ResetTransactionFilters();

    private void ResetTransactionFilters()
    {
        _transactionFiltersReady = false;
        _transactionSearchDebounce?.Cancel();
        _transactionQuery = new TransactionQuery();
        TransactionsSearchBox.Text = "";
        TransactionDatePresetBox.SelectedIndex = 0;
        TransactionStartDatePicker.Date = null;
        TransactionEndDatePicker.Date = null;
        FilterExpenseCheck.IsChecked = FilterIncomeCheck.IsChecked = FilterTransferCheck.IsChecked = FilterRefundCheck.IsChecked = FilterReimbursementCheck.IsChecked = false;
        foreach (var option in _transactionCategoryOptions.Concat(_transactionAccountOptions).Concat(_transactionSourceOptions)) option.IsSelected = false;
        TransactionMinimumAmountBox.Value = double.NaN;
        TransactionMaximumAmountBox.Value = double.NaN;
        TransactionUncategorizedCheck.IsChecked = TransactionSubscriptionCheck.IsChecked = TransactionUnassignedAccountCheck.IsChecked = false;
        TransactionSortBox.SelectedIndex = 0;
        TransactionCategorySearchBox.Text = "";
        TransactionAccountSearchBox.Text = "";
        _transactionFiltersReady = true;
        TransactionsPage.IsPaneOpen = false;
        FilterTransactionOptionLists();
        ApplyTransactionQuery();
    }

    private void RemoveTransactionFilterChipClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TransactionFilterChip chip }) return;
        switch (chip.Key)
        {
            case "search": _transactionQuery.SearchText = ""; TransactionsSearchBox.Text = ""; break;
            case "date": _transactionQuery.StartDate = _transactionQuery.EndDate = null; TransactionStartDatePicker.Date = TransactionEndDatePicker.Date = null; TransactionDatePresetBox.SelectedIndex = 0; break;
            case "direction": _transactionQuery.Directions = []; FilterExpenseCheck.IsChecked = FilterIncomeCheck.IsChecked = FilterTransferCheck.IsChecked = FilterRefundCheck.IsChecked = FilterReimbursementCheck.IsChecked = false; break;
            case "category": _transactionQuery.Categories = []; foreach (var option in _transactionCategoryOptions) option.IsSelected = false; break;
            case "account": _transactionQuery.AccountIds = []; foreach (var option in _transactionAccountOptions) option.IsSelected = false; break;
            case "amount": _transactionQuery.MinimumAmount = _transactionQuery.MaximumAmount = null; TransactionMinimumAmountBox.Value = TransactionMaximumAmountBox.Value = double.NaN; break;
            case "source": _transactionQuery.Sources = []; foreach (var option in _transactionSourceOptions) option.IsSelected = false; break;
            case "uncategorized": _transactionQuery.UncategorizedOnly = false; TransactionUncategorizedCheck.IsChecked = false; break;
            case "subscription": _transactionQuery.SubscriptionOnly = false; TransactionSubscriptionCheck.IsChecked = false; break;
            case "unassigned": _transactionQuery.UnassignedAccountOnly = false; TransactionUnassignedAccountCheck.IsChecked = false; break;
        }
        ApplyTransactionQuery();
    }

    private void SortTransactionsByDateClick(object sender, RoutedEventArgs e)
    {
        _transactionQuery.SortBy = _transactionQuery.SortBy == TransactionSortOption.DateDescending ? TransactionSortOption.DateAscending : TransactionSortOption.DateDescending;
        TransactionSortBox.SelectedIndex = (int)_transactionQuery.SortBy;
        ApplyTransactionQuery();
    }

    private void SortTransactionsByAmountClick(object sender, RoutedEventArgs e)
    {
        _transactionQuery.SortBy = _transactionQuery.SortBy == TransactionSortOption.AmountDescending ? TransactionSortOption.AmountAscending : TransactionSortOption.AmountDescending;
        TransactionSortBox.SelectedIndex = (int)_transactionQuery.SortBy;
        ApplyTransactionQuery();
    }

    private void SortTransactionsByMerchantClick(object sender, RoutedEventArgs e)
    {
        _transactionQuery.SortBy = TransactionSortOption.MerchantAscending;
        TransactionSortBox.SelectedIndex = (int)_transactionQuery.SortBy;
        ApplyTransactionQuery();
    }

    private void SaveSettingsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = CollectSettings();
            _store.SaveSettings(settings);
            ReminderScheduler.Update(settings);
            LoadDashboard();
            StatusText.Text = "偏好设置和 Windows 提醒计划已保存";
        }
        catch (Exception ex) { _ = ShowMessage("设置保存失败", ex.Message); }
    }

    private async void TestNotificationClick(object sender, RoutedEventArgs e)
    {
        try { new NotificationService().Show("独秀账本提醒测试", "通知功能工作正常。每天记一笔，钱的去向会越来越清楚。 "); StatusText.Text = "测试通知已发送"; }
        catch (Exception ex) { await ShowMessage("通知发送失败", ex.Message); }
    }

    private AppSettings CollectSettings()
    {
        var dayIndex = Math.Max(0, WeeklySummaryDayBox.SelectedIndex);
        return new AppSettings
        {
            SmallExpenseThreshold = (decimal)Math.Max(0, SmallExpenseThresholdBox.Value), MonthlyBudget = (decimal)Math.Max(0, MonthlyBudgetBox.Value),
            DailyReminderEnabled = DailyReminderCheck.IsChecked == true, DailyReminderTime = DailyReminderTimePicker.Time.ToString(@"hh\:mm"),
            WeeklySummaryEnabled = WeeklySummaryCheck.IsChecked == true, WeeklySummaryDay = dayIndex == 6 ? DayOfWeek.Sunday : (DayOfWeek)(dayIndex + 1), WeeklySummaryTime = WeeklySummaryTimePicker.Time.ToString(@"hh\:mm"),
            SubscriptionKeywords = SubscriptionKeywordsBox.Text.Trim(), OptionalCategories = OptionalCategoriesBox.Text.Trim(), S3SyncEnabled = S3SyncEnabledCheck.IsChecked == true, SyncOnStartup = SyncOnStartupCheck.IsChecked == true,
            S3AccessUrl = S3AccessUrlBox.Text.Trim(), S3Endpoint = S3EndpointBox.Text.Trim(), S3Region = S3RegionBox.Text.Trim(), S3Bucket = S3BucketBox.Text.Trim(), S3ObjectKey = S3ObjectKeyBox.Text.Trim(),
            S3AccessKeyId = S3AccessKeyIdBox.Text.Trim(), S3ForcePathStyle = S3ForcePathStyleCheck.IsChecked == true
        };
    }

    private void SaveCloudSettings()
    {
        _store.SaveSettings(CollectSettings());
        if (!string.IsNullOrEmpty(S3SecretKeyBox.Password) || !string.IsNullOrEmpty(S3SessionTokenBox.Password))
        {
            var secretKey = string.IsNullOrEmpty(S3SecretKeyBox.Password) ? _store.LoadS3SecretKey() : S3SecretKeyBox.Password;
            var sessionToken = string.IsNullOrEmpty(S3SessionTokenBox.Password) ? _store.LoadS3SessionToken() : S3SessionTokenBox.Password;
            _store.SaveS3Credentials(secretKey, sessionToken);
            S3SecretKeyBox.Password = ""; S3SessionTokenBox.Password = "";
            S3SecretKeyBox.PlaceholderText = "密钥已由 Windows 当前用户加密保存";
            if (!string.IsNullOrEmpty(sessionToken)) S3SessionTokenBox.PlaceholderText = "令牌已由 Windows 当前用户加密保存";
        }
    }

    private async void TestS3Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveCloudSettings(); CloudSyncProgress.IsActive = true; CloudSyncInfo.IsOpen = false;
            var target = await _syncService.TestConnectionAsync(_store.LoadSettings(), _store.LoadS3SecretKey(), _store.LoadS3SessionToken());
            CloudSyncInfo.Severity = InfoBarSeverity.Success; CloudSyncInfo.Title = "连接成功"; CloudSyncInfo.Message = $"已验证 S3 对象的读取和写入权限：{target}"; CloudSyncInfo.IsOpen = true;
        }
        catch (Exception ex) { CloudSyncInfo.Severity = InfoBarSeverity.Error; CloudSyncInfo.Title = "连接失败"; CloudSyncInfo.Message = SafeCloudError(ex); CloudSyncInfo.IsOpen = true; }
        finally { CloudSyncProgress.IsActive = false; }
    }

    private async void SyncNowClick(object sender, RoutedEventArgs e) => await RunCloudSyncAsync(showResult: true);

    private async void TrySyncOnStartup()
    {
        var settings = _store.LoadSettings();
        if (!settings.S3SyncEnabled || !settings.SyncOnStartup || string.IsNullOrEmpty(_store.LoadS3SecretKey())) return;
        await RunCloudSyncAsync(showResult: false);
    }

    private async Task RunCloudSyncAsync(bool showResult)
    {
        try
        {
            SaveCloudSettings();
            var settings = _store.LoadSettings();
            if (!settings.S3SyncEnabled) throw new InvalidOperationException("请先启用 S3 同步。 ");
            CloudSyncProgress.IsActive = true; CloudSyncInfo.IsOpen = false;
            var result = await _syncService.SyncAsync(settings, _store.LoadS3SecretKey(), _store.LoadS3SessionToken());
            LoadDashboard();
            CloudSyncInfo.Severity = InfoBarSeverity.Success; CloudSyncInfo.Title = "同步完成"; CloudSyncInfo.Message = result.Display; CloudSyncInfo.IsOpen = true;
            StatusText.Text = $"云同步完成：{result.Display}";
        }
        catch (Exception ex)
        {
            CloudSyncInfo.Severity = InfoBarSeverity.Error; CloudSyncInfo.Title = "同步失败，本地数据未受影响"; CloudSyncInfo.Message = SafeCloudError(ex); CloudSyncInfo.IsOpen = true;
            if (showResult) StatusText.Text = "S3 同步失败，请查看对象存储设置";
        }
        finally { CloudSyncProgress.IsActive = false; }
    }

    private static string SafeCloudError(Exception exception)
    {
        var message = exception.Message.Replace("Password", "凭据", StringComparison.OrdinalIgnoreCase);
        return message.Length > 300 ? message[..300] : message;
    }

    private async void ExportAnalysisReportClick(object sender, RoutedEventArgs e)
    {
        var result = _analysisService.Analyze(_store.List().ToList(), _store.LoadSettings(), _analysisPeriod, _analysisAnchor);
        var reportName = _analysisPeriod switch { AnalysisPeriodKind.Week => "周报", AnalysisPeriodKind.Year => "年报", _ => "月报" };
        var picker = new Windows.Storage.Pickers.FileSavePicker { SuggestedFileName = $"独秀账本{reportName}-{result.Start:yyyyMMdd}" };
        picker.FileTypeChoices.Add("CSV 表格", [".csv"]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
        var rows = _store.List().Where(row => row.OccurredOn >= result.Start && row.OccurredOn < result.EndExclusive).ToList();
        var lines = new List<string>
        {
            $"周期,{Csv(result.PeriodLabel)}",
            $"收入,{result.Income:0.00}",
            $"净支出,{result.NetExpense:0.00}",
            $"结余,{result.Balance:0.00}",
            $"储蓄率,{result.SavingsRate:P2}",
            "",
            "周期,交易时间,类型,金额,分类,交易对方,账户,备注"
        };
        lines.AddRange(rows.Select(row => string.Join(',', Csv(result.PeriodLabel), Csv(row.DateDisplay), Csv(row.Direction), row.Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), Csv(row.Category), Csv(row.Merchant), Csv(row.AccountDisplay), Csv(row.Note))));
        await File.WriteAllTextAsync(file.Path, "\uFEFF" + string.Join(Environment.NewLine, lines));
        StatusText.Text = $"{reportName}已导出：{file.Path}";
    }

    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

    private async void BackupClick(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileSavePicker
        {
            SuggestedFileName = $"duxiu-ledger-{DateTime.Now:yyyyMMdd-HHmm}"
        };
        picker.FileTypeChoices.Add("独秀账本备份", [".duxiu"]);
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
