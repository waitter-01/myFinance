using DuxiuLedger.Desktop.Services;
using DuxiuLedger.Desktop.Models;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using XamlPath = Microsoft.UI.Xaml.Shapes.Path;
using XamlLine = Microsoft.UI.Xaml.Shapes.Line;
using XamlPolyline = Microsoft.UI.Xaml.Shapes.Polyline;
using Windows.Graphics;
using Windows.Foundation;
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
    private readonly DashboardService _dashboardService = new();
    private DashboardSnapshot? _dashboardSnapshot;
    private AnalysisPeriodKind _analysisPeriod = AnalysisPeriodKind.Month;
    private DateTime _analysisAnchor = DateTime.Today;
    private FinancialAnalysisResult? _currentAnalysisResult;
    private IReadOnlyList<SpendingRankItem> _currentPieRanks = [];
    private decimal _currentPieTotal;
    private TransactionQuery _transactionQuery = new();
    private TransactionGroupMode _transactionGroupMode = TransactionGroupMode.Day;
    private readonly CollectionViewSource _transactionGroupsSource = new() { IsSourceGrouped = true };
    private readonly List<TransactionFilterOption> _transactionCategoryOptions = [];
    private readonly List<TransactionFilterOption> _transactionAccountOptions = [];
    private readonly List<TransactionFilterOption> _transactionSourceOptions = [];
    private IReadOnlyList<TransactionRecord> _currentTransactionRows = [];
    private CancellationTokenSource? _transactionSearchDebounce;
    private CancellationTokenSource? _cloudSyncDebounce;
    private readonly SemaphoreSlim _cloudSyncGate = new(1, 1);
    private readonly DispatcherTimer _periodicSyncTimer = new() { Interval = TimeSpan.FromMinutes(5) };
    private bool _transactionFiltersReady;
    private bool _transactionBatchMode;
    private bool _isInitialized;
    private readonly Dictionary<string, (string Title, string Subtitle)> _pages = new()
    {
        ["Dashboard"] = ("总览", "查看本月财务情况和最近流水"),
        ["Transactions"] = ("全部流水", "搜索、核对和管理本地账单记录"),
        ["Insights"] = ("消费洞察", "看清消费去向、小额支出和可优化空间"),
        ["Budgets"] = ("预算计划", "规划每月支出，控制消费节奏"),
        ["Subscriptions"] = ("周期性支出", "统一管理房租、订阅、保险和月卡，区分实际付款与月度成本"),
        ["Accounts"] = ("账户管理", "管理现金、银行卡、电子钱包和信用账户"),
        ["Categories"] = ("分类设置", "建立适合自己的收支分类体系"),
        ["Backup"] = ("数据备份", "复制和保护本地账本文件"),
        ["Settings"] = ("偏好设置", "按自己的习惯调整分析标准和提醒计划")
    };

    public MainWindow()
    {
        InitializeComponent();
        _syncService = new S3SyncService(_store);
        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.12.1";
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
        _periodicSyncTimer.Tick += PeriodicSyncTimerTick;
        Closed += MainWindowClosed;
        ConfigureAutomaticSync();
        InitializeTransactionFilters();
        _isInitialized = true;
        LoadDashboard();
        TrySyncOnStartup();
    }

    private void LoadDashboard()
    {
        var allRecords = _store.List().ToList();
        var settings = _store.LoadSettings();
        var snapshot = _dashboardService.Build(allRecords, settings, _store.ListSavingsGoals(), _store.ListBudgets(DateTime.Now.ToString("yyyy-MM")));
        _dashboardSnapshot = snapshot;
        DashboardPeriodText.Text = snapshot.PeriodLabel;
        DashboardSafeToSpendText.Text = snapshot.SafeToSpendDisplay;
        DashboardSafeToSpendDetailText.Text = snapshot.SafeToSpendDetail;
        DashboardProjectionText.Text = snapshot.ProjectionDisplay;
        DashboardBudgetProgress.Value = snapshot.BudgetProgress;
        DashboardBudgetStatusText.Text = snapshot.MonthlyBudget <= 0 ? "尚未设置预算" : snapshot.SafeToSpend < 0 ? "已超出预算" : snapshot.BudgetProgress >= 0.8 ? "接近预算上限" : "消费进度正常";
        IncomeText.Text = snapshot.IncomeDisplay;
        ExpenseText.Text = snapshot.ExpenseDisplay;
        BalanceText.Text = snapshot.BalanceDisplay;
        DashboardExpenseComparisonText.Text = snapshot.ExpenseComparisonDisplay;
        DashboardSavingsText.Text = snapshot.SavingsProgressDisplay;
        DashboardSavingsProgress.Value = (double)snapshot.SavingsProgress;
        DashboardCategoryList.ItemsSource = snapshot.TopCategories;
        DashboardAttentionList.ItemsSource = snapshot.AttentionItems;
        DashboardUpcomingList.ItemsSource = snapshot.UpcomingItems;
        RecentList.ItemsSource = snapshot.RecentTransactions;
        ApplyDashboardLayout(settings);
        DrawDashboardTrend();
        if (_transactionFiltersReady)
        {
            RefreshTransactionBatchChoices();
            ApplyTransactionQuery();
        }
        RecordCountText.Text = $"共 {allRecords.Count} 条记录 · 本月 {snapshot.CurrentRecordCount} 条";
        LoadSubscriptions(allRecords);
        LoadInsights(allRecords);
        LoadBudgets(allRecords);
        LoadAccounts();
        LoadCategories();
        StatusText.Text = $"已读取本地账本 · 本月 {snapshot.CurrentRecordCount} 条流水";
    }

    private void DashboardTrendCanvasSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_isInitialized || Math.Abs(e.NewSize.Width - e.PreviousSize.Width) < 1) return;
        DrawDashboardTrend();
    }

    private void DrawDashboardTrend()
    {
        DashboardTrendCanvas.Children.Clear();
        if (_dashboardSnapshot is null || _dashboardSnapshot.Trend.Count == 0) return;
        var width = Math.Max(320, DashboardTrendCanvas.ActualWidth);
        var height = Math.Max(180, DashboardTrendCanvas.ActualHeight);
        const double left = 12;
        const double top = 12;
        const double bottom = 24;
        var chartHeight = height - top - bottom;
        var maximum = _dashboardSnapshot.Trend.SelectMany(item => new[] { Math.Max(0, item.Actual), item.Ideal }).Append(_dashboardSnapshot.ProjectedExpense).DefaultIfEmpty(1).Max();
        if (maximum <= 0) maximum = 1;
        var dividerBrush = new SolidColorBrush(ColorHelper.FromArgb(55, 100, 116, 139));
        for (var index = 0; index <= 3; index++)
        {
            var y = top + chartHeight * index / 3;
            DashboardTrendCanvas.Children.Add(new XamlLine { X1 = left, X2 = width - left, Y1 = y, Y2 = y, Stroke = dividerBrush, StrokeThickness = 1, IsHitTestVisible = false });
        }
        var actual = new XamlPolyline { Stroke = new SolidColorBrush(ColorHelper.FromArgb(255, 37, 99, 235)), StrokeThickness = 3, StrokeLineJoin = PenLineJoin.Round };
        var ideal = new XamlPolyline { Stroke = new SolidColorBrush(ColorHelper.FromArgb(180, 100, 116, 139)), StrokeThickness = 2, StrokeDashArray = [4, 4] };
        var count = _dashboardSnapshot.Trend.Count;
        foreach (var item in _dashboardSnapshot.Trend)
        {
            var x = left + (width - left * 2) * (item.Day - 1) / Math.Max(1, count - 1);
            ideal.Points.Add(new Point(x, top + chartHeight * (1 - (double)(item.Ideal / maximum))));
            if (item.Actual >= 0) actual.Points.Add(new Point(x, top + chartHeight * (1 - (double)(item.Actual / maximum))));
        }
        DashboardTrendCanvas.Children.Add(ideal);
        DashboardTrendCanvas.Children.Add(actual);
        DashboardTrendCanvas.Children.Add(new TextBlock { Text = "1日", FontSize = 11, Opacity = 0.6 });
        var endLabel = new TextBlock { Text = $"{count}日", FontSize = 11, Opacity = 0.6 };
        Canvas.SetLeft(endLabel, width - 34);
        DashboardTrendCanvas.Children.Add(endLabel);
    }

    private async void DashboardCategoryClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not SpendingRankItem item) return;
        var monthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var rows = _store.List().Where(row => row.Direction == "支出" && row.OccurredOn >= monthStart && row.OccurredOn < monthStart.AddMonths(1)
            && FinancialAnalysisService.MajorCategory(row.Category) == item.Name).OrderByDescending(row => row.OccurredOn).ToList();
        var dialog = new CategoryTransactionsDialog(item.Name, rows, $"{DateTime.Today:yyyy年M月}") { XamlRoot = Content.XamlRoot };
        await dialog.ShowAsync();
    }

    private async void DashboardAttentionClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DashboardAttentionItem item }) return;
        var monthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var monthRows = _store.List().Where(row => row.OccurredOn >= monthStart && row.OccurredOn < monthStart.AddMonths(1)).ToList();
        if (item.ActionKey == "uncategorized")
        {
            _transactionQuery = new TransactionQuery { UncategorizedOnly = true };
            ApplyTransactionQuery();
            SelectNavigation("Transactions");
            return;
        }
        if (item.ActionKey is "review" or "duplicates")
        {
            var rows = item.ActionKey == "review"
                ? monthRows.Where(row => row.RequiresReview).OrderByDescending(row => row.OccurredOn).ToList()
                : _store.List().Where(row => row.Direction != "转账")
                    .GroupBy(row => $"{row.OccurredOn:O}|{row.Direction}|{row.Amount:0.00}").Where(group => group.Count() > 1)
                    .SelectMany(group => group).OrderByDescending(row => row.OccurredOn).ToList();
            var title = item.ActionKey == "review" ? "待核查流水" : "完全同时间同金额流水";
            var dialog = new CategoryTransactionsDialog(title, rows, "请逐笔确认") { XamlRoot = Content.XamlRoot };
            await dialog.ShowAsync();
            return;
        }
        SelectNavigation(item.ActionKey == "budgets" ? "Budgets" : "Insights");
    }

    private void DashboardUpcomingViewAllClick(object sender, RoutedEventArgs e) => SelectNavigation("Subscriptions");
    private void DashboardViewAllClick(object sender, RoutedEventArgs e) => SelectNavigation("Transactions");

    private async void DashboardCustomizeClick(object sender, RoutedEventArgs e)
    {
        var settings = _store.LoadSettings();
        var definitions = DashboardCardDefinitions();
        var hidden = ParseDashboardKeys(settings.DashboardHiddenCards).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var orderedKeys = ParseDashboardKeys(settings.DashboardCardOrder).Concat(definitions.Select(item => item.Key)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var options = orderedKeys.Select(key => definitions.FirstOrDefault(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase)))
            .Where(item => !string.IsNullOrWhiteSpace(item.Key)).Select(item => new DashboardCardOption { Key = item.Key, Title = item.Title, Description = item.Description, IsVisible = !hidden.Contains(item.Key) }).ToList();
        var dialog = new DashboardCustomizeDialog(options) { XamlRoot = Content.XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        settings.DashboardCardOrder = dialog.CardOrder;
        settings.DashboardHiddenCards = dialog.HiddenCards;
        _store.SaveSettings(settings);
        ApplyDashboardLayout(settings);
        StatusText.Text = "总览布局已保存";
        ScheduleCloudSync();
    }

    private void ApplyDashboardLayout(AppSettings settings)
    {
        var sections = new Dictionary<string, FrameworkElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["overview"] = DashboardOverviewSection, ["analysis"] = DashboardAnalysisSection,
            ["action"] = DashboardActionSection, ["recent"] = DashboardRecentSection
        };
        var hidden = ParseDashboardKeys(settings.DashboardHiddenCards).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var order = ParseDashboardKeys(settings.DashboardCardOrder).Concat(sections.Keys).Distinct(StringComparer.OrdinalIgnoreCase).Where(sections.ContainsKey).ToList();
        foreach (var element in sections.Values) DashboardSectionsPanel.Children.Remove(element);
        foreach (var key in order)
        {
            var element = sections[key];
            element.Visibility = hidden.Contains(key) ? Visibility.Collapsed : Visibility.Visible;
            DashboardSectionsPanel.Children.Add(element);
        }
        ApplyDashboardResponsiveLayout(DashboardPage.ActualWidth);
    }

    private void DashboardPageSizeChanged(object sender, SizeChangedEventArgs e) => ApplyDashboardResponsiveLayout(e.NewSize.Width);

    private void ApplyDashboardResponsiveLayout(double width)
    {
        var narrow = width > 0 && width < 920;
        ApplyResponsiveGrid(DashboardOverviewSection, narrow);
        ApplyResponsiveGrid(DashboardAnalysisSection, narrow);
        ApplyResponsiveGrid(DashboardActionSection, narrow);
    }

    private static void ApplyResponsiveGrid(Grid grid, bool narrow)
    {
        if (grid.Children.Count < 2 || grid.RowDefinitions.Count < 2 || grid.ColumnDefinitions.Count < 2) return;
        if (grid.Children[1] is not FrameworkElement second) return;
        Grid.SetColumn(second, narrow ? 0 : 1);
        Grid.SetRow(second, narrow ? 1 : 0);
        grid.RowDefinitions[1].Height = narrow ? GridLength.Auto : new GridLength(0);
        grid.RowSpacing = narrow ? 14 : 0;
        grid.ColumnDefinitions[0].Width = narrow ? new GridLength(1, GridUnitType.Star) : new GridLength(1.45, GridUnitType.Star);
        grid.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
    }

    private static IReadOnlyList<(string Key, string Title, string Description)> DashboardCardDefinitions() =>
    [
        ("overview", "核心概览", "可安心支配、收入、支出、结余和储蓄目标"),
        ("analysis", "消费分析", "本月消费速度和主要消费去向"),
        ("action", "关注与即将发生", "待处理问题和本月周期性支出"),
        ("recent", "最近流水", "最近录入或导入的账目")
    ];

    private static IEnumerable<string> ParseDashboardKeys(string value)
        => value.Split([',', '，', ';', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

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
        _currentAnalysisResult = result;
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
        DrawCategoryPie(result.MajorCategoryRanks, result.GrossExpense);
        MerchantRankingList.ItemsSource = result.MerchantRanks.Take(8).ToList();
        InsightSuggestionsList.ItemsSource = result.Suggestions;
    }

    private void DrawCategoryPie(IReadOnlyList<SpendingRankItem> ranks, decimal total)
    {
        _currentPieRanks = ranks;
        _currentPieTotal = total;
        DrawCategoryPieVisuals();
    }

    private void CategoryPieCanvasSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_isInitialized || Math.Abs(e.NewSize.Width - e.PreviousSize.Width) < 1) return;
        DrawCategoryPieVisuals();
    }

    private void DrawCategoryPieVisuals()
    {
        CategoryPieCanvas.Children.Clear();
        CategoryPieTotalText.Text = $"¥{_currentPieTotal:N2}";
        CategoryPieEmptyText.Visibility = _currentPieTotal <= 0 ? Visibility.Visible : Visibility.Collapsed;
        CategoryPieTotalText.Visibility = _currentPieTotal <= 0 ? Visibility.Collapsed : Visibility.Visible;
        if (_currentPieTotal <= 0) return;

        var colors = new[]
        {
            ColorHelper.FromArgb(255, 37, 99, 235), ColorHelper.FromArgb(255, 20, 184, 166),
            ColorHelper.FromArgb(255, 245, 158, 11), ColorHelper.FromArgb(255, 139, 92, 246),
            ColorHelper.FromArgb(255, 236, 72, 153), ColorHelper.FromArgb(255, 16, 185, 129),
            ColorHelper.FromArgb(255, 249, 115, 22), ColorHelper.FromArgb(255, 6, 182, 212),
            ColorHelper.FromArgb(255, 100, 116, 139), ColorHelper.FromArgb(255, 148, 163, 184)
        };
        var canvasWidth = Math.Max(640, CategoryPieCanvas.ActualWidth);
        var canvasHeight = Math.Max(280, CategoryPieCanvas.ActualHeight);
        var centerX = canvasWidth / 2;
        var centerY = canvasHeight / 2;
        const double outerRadius = 98;
        const double innerRadius = 60;
        var visibleRanks = _currentPieRanks.Take(8).ToList();
        var labels = new List<PieLabelLayout>();
        var angle = -90d;
        for (var index = 0; index < visibleRanks.Count; index++)
        {
            var item = visibleRanks[index];
            var sweep = Math.Min(359.999, (double)(item.Amount / _currentPieTotal) * 360);
            var midAngle = angle + sweep / 2;
            var brush = new SolidColorBrush(colors[index % colors.Length]);
            var path = new XamlPath
            {
                Data = CreateDonutSegment(centerX, centerY, outerRadius, innerRadius, angle, sweep),
                Fill = brush,
                Opacity = 0.9,
                Tag = new PieSliceTag(item, midAngle, centerX, centerY),
                StrokeThickness = 2
            };
            ToolTipService.SetToolTip(path, $"{item.Name}  {item.AmountDisplay}  ·  {item.ShareDisplay}");
            path.PointerEntered += CategoryPieSlicePointerEntered;
            path.PointerExited += CategoryPieSlicePointerExited;
            CategoryPieCanvas.Children.Add(path);
            labels.Add(new PieLabelLayout(item, brush, midAngle));
            angle += sweep;
        }

        DrawPieLabels(labels, canvasWidth, canvasHeight, centerX, centerY, outerRadius);
    }

    private void DrawPieLabels(IReadOnlyList<PieLabelLayout> labels, double canvasWidth, double canvasHeight, double centerX, double centerY, double outerRadius)
    {
        const double labelWidth = 166;
        const double labelHeight = 42;
        var left = labels.Where(item => Math.Cos(item.MidAngle * Math.PI / 180) < 0).OrderBy(item => Math.Sin(item.MidAngle * Math.PI / 180)).ToList();
        var right = labels.Where(item => Math.Cos(item.MidAngle * Math.PI / 180) >= 0).OrderBy(item => Math.Sin(item.MidAngle * Math.PI / 180)).ToList();

        DrawSide(left, false);
        DrawSide(right, true);
        return;

        void DrawSide(IReadOnlyList<PieLabelLayout> sideLabels, bool isRight)
        {
            if (sideLabels.Count == 0) return;
            var top = 18d;
            var bottom = canvasHeight - labelHeight - 18;
            var step = sideLabels.Count == 1 ? 0 : (bottom - top) / (sideLabels.Count - 1);
            var preferredX = isRight
                ? centerX + outerRadius + 38
                : centerX - outerRadius - labelWidth - 38;
            var labelX = Math.Clamp(preferredX, 12, canvasWidth - labelWidth - 12);

            for (var index = 0; index < sideLabels.Count; index++)
            {
                var label = sideLabels[index];
                var labelY = sideLabels.Count == 1 ? centerY - labelHeight / 2 : top + step * index;
                var labelPanel = CreatePieLabel(label, labelWidth, labelHeight, isRight);
                Canvas.SetLeft(labelPanel, labelX);
                Canvas.SetTop(labelPanel, labelY);
                Canvas.SetZIndex(labelPanel, 2);
                CategoryPieCanvas.Children.Add(labelPanel);
            }
        }
    }

    private static Grid CreatePieLabel(PieLabelLayout label, double width, double height, bool isRight)
    {
        var panel = new Grid { Width = width, Height = height, IsHitTestVisible = false };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = isRight ? HorizontalAlignment.Left : HorizontalAlignment.Right
        };
        var dot = new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(4), Background = label.Brush, VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { Text = label.Item.Name, MaxWidth = 104, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        var share = new TextBlock { Text = label.Item.ShareDisplay, FontSize = 11, Opacity = 0.62, VerticalAlignment = VerticalAlignment.Center };
        var amount = new TextBlock
        {
            Text = label.Item.AmountDisplay,
            FontSize = 11,
            Opacity = 0.66,
            Margin = new Thickness(13, 2, 0, 0),
            HorizontalAlignment = isRight ? HorizontalAlignment.Left : HorizontalAlignment.Right
        };
        heading.Children.Add(dot);
        heading.Children.Add(name);
        heading.Children.Add(share);
        Grid.SetRow(amount, 1);
        panel.Children.Add(heading);
        panel.Children.Add(amount);
        return panel;
    }

    private static Geometry CreateDonutSegment(double centerX, double centerY, double outerRadius, double innerRadius, double startAngle, double sweepAngle)
    {
        static Point PointOnCircle(double x, double y, double radius, double angle)
        {
            var radians = angle * Math.PI / 180;
            return new Point(x + radius * Math.Cos(radians), y + radius * Math.Sin(radians));
        }

        var outerStart = PointOnCircle(centerX, centerY, outerRadius, startAngle);
        var outerEnd = PointOnCircle(centerX, centerY, outerRadius, startAngle + sweepAngle);
        var innerEnd = PointOnCircle(centerX, centerY, innerRadius, startAngle + sweepAngle);
        var innerStart = PointOnCircle(centerX, centerY, innerRadius, startAngle);
        var figure = new PathFigure { StartPoint = outerStart, IsClosed = true, IsFilled = true };
        figure.Segments.Add(new ArcSegment { Point = outerEnd, Size = new Size(outerRadius, outerRadius), IsLargeArc = sweepAngle > 180, SweepDirection = SweepDirection.Clockwise });
        figure.Segments.Add(new LineSegment { Point = innerEnd });
        figure.Segments.Add(new ArcSegment { Point = innerStart, Size = new Size(innerRadius, innerRadius), IsLargeArc = sweepAngle > 180, SweepDirection = SweepDirection.Counterclockwise });
        return new PathGeometry { Figures = [figure] };
    }

    private void AnalysisBarPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Border bar) return;
        bar.Opacity = 1;
        bar.RenderTransformOrigin = new Point(0.5, 1);
        bar.RenderTransform = new ScaleTransform { ScaleX = 1.16, ScaleY = 1.04 };
    }

    private void AnalysisBarPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Border bar) return;
        bar.Opacity = 0.8;
        bar.RenderTransform = null;
    }

    private void CategoryPieSlicePointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not XamlPath { Tag: PieSliceTag tag } path) return;
        var radians = tag.MidAngle * Math.PI / 180;
        path.Opacity = 1;
        path.Stroke = new SolidColorBrush(Colors.White);
        path.RenderTransform = new CompositeTransform
        {
            CenterX = tag.CenterX,
            CenterY = tag.CenterY,
            ScaleX = 1.025,
            ScaleY = 1.025,
            TranslateX = Math.Cos(radians) * 4,
            TranslateY = Math.Sin(radians) * 4
        };
        Canvas.SetZIndex(path, 3);
    }

    private void CategoryPieSlicePointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not XamlPath path) return;
        path.Opacity = 0.9;
        path.Stroke = null;
        path.RenderTransform = null;
        Canvas.SetZIndex(path, 0);
    }

    private sealed record PieSliceTag(SpendingRankItem Item, double MidAngle, double CenterX, double CenterY);
    private sealed record PieLabelLayout(SpendingRankItem Item, SolidColorBrush Brush, double MidAngle);

    private async void CategoryRankingItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not SpendingRankItem item || _currentAnalysisResult is null) return;
        var rows = _store.List()
            .Where(row => row.Direction == "支出" && row.Category.Equals(item.Name, StringComparison.OrdinalIgnoreCase)
                && row.OccurredOn >= _currentAnalysisResult.Start && row.OccurredOn < _currentAnalysisResult.EndExclusive)
            .OrderByDescending(row => row.OccurredOn).ThenByDescending(row => row.Id).ToList();
        var dialog = new CategoryTransactionsDialog(item.Name, rows, _currentAnalysisResult.PeriodLabel) { XamlRoot = Content.XamlRoot };
        await dialog.ShowAsync();
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
        var since = DateTime.Now.Date.AddMonths(-24);
        var detected = allRecords.Where(row => row.Direction == "支出" && row.OccurredOn >= since)
            .Select(row => (Row: row, Type: RecurringExpenseTypes.Infer(row, keywords)))
            .Where(item => item.Type.Length > 0).ToList();
        var summaries = detected.GroupBy(item => $"{item.Type}\n{(string.IsNullOrWhiteSpace(item.Row.Merchant) ? "未注明交易对方" : item.Row.Merchant.Trim())}", StringComparer.OrdinalIgnoreCase).Select(group =>
        {
            var latestItem = group.OrderByDescending(item => item.Row.OccurredOn).ThenByDescending(item => item.Row.Id).First();
            var latest = latestItem.Row;
            var billingMonths = Math.Max(1, latest.SubscriptionMonths);
            var coverageStart = (latest.CoverageStart ?? latest.OccurredOn).Date;
            return new SubscriptionSummary
            {
                Merchant = string.IsNullOrWhiteSpace(latest.Merchant) ? "未注明交易对方" : latest.Merchant.Trim(),
                Category = latest.Category,
                RecurringType = latestItem.Type,
                PaymentCount = group.Count(),
                PaidLast12Months = group.Where(item => item.Row.OccurredOn >= DateTime.Today.AddMonths(-12)).Sum(item => item.Row.Amount),
                MonthlyAverage = latest.Amount / billingMonths,
                LatestAmount = latest.Amount,
                BillingMonths = billingMonths,
                LatestPayment = latest.OccurredOn,
                CoverageStart = coverageStart,
                NextPaymentDate = latest.NextPaymentDate ?? coverageStart.AddMonths(billingMonths),
                IsEssential = latest.IsEssential || latestItem.Type is "房租" or "物业与车位" or "保险保障"
            };
        }).OrderByDescending(item => item.MonthlyAverage).ToList();
        SubscriptionsList.ItemsSource = summaries;
        SubscriptionCurrentText.Text = $"¥{detected.Where(item => item.Row.OccurredOn.ToString("yyyy-MM") == DateTime.Now.ToString("yyyy-MM")).Sum(item => item.Row.Amount):N2}";
        SubscriptionAverageText.Text = $"¥{summaries.Sum(item => item.MonthlyAverage):N2}";
        SubscriptionCountText.Text = $"{summaries.Count} 项";
        RecurringUpcomingText.Text = $"¥{summaries.Where(item => item.NextPaymentDate >= DateTime.Today && item.NextPaymentDate <= DateTime.Today.AddDays(90)).Sum(item => item.LatestAmount):N2}";
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
        AutoSyncChangesCheck.IsChecked = settings.AutoSyncChanges;
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
        DashboardCustomizeButton.Visibility = key == "Dashboard" ? Visibility.Visible : Visibility.Collapsed;
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
        try { _store.SaveBudget(dialog.Result); LoadDashboard(); SelectNavigation("Budgets"); StatusText.Text = "分类预算已保存"; ScheduleCloudSync(); }
        catch (Exception ex) { await ShowMessage("预算保存失败", ex.Message); }
    }

    private async void DeleteBudgetClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: BudgetRecord budget }) return;
        var confirm = new ContentDialog { XamlRoot = ContentHost.XamlRoot, Title = "删除这条预算？", Content = $"{budget.Month} · {budget.Category} · {budget.AmountDisplay}", PrimaryButtonText = "删除", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
        _store.DeleteBudget(budget.Id); LoadDashboard(); SelectNavigation("Budgets"); StatusText.Text = "分类预算已删除"; ScheduleCloudSync();
    }

    private async void AddSavingsGoalClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SavingsGoalDialog { XamlRoot = ContentHost.XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || dialog.Result is null) return;
        try { _store.SaveSavingsGoal(dialog.Result); LoadDashboard(); SelectNavigation("Budgets"); StatusText.Text = "储蓄目标已添加"; ScheduleCloudSync(); }
        catch (Exception ex) { await ShowMessage("目标保存失败", ex.Message); }
    }

    private async void EditSavingsGoalClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SavingsGoalRecord goal }) return;
        var dialog = new SavingsGoalDialog(goal) { XamlRoot = ContentHost.XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || dialog.Result is null) return;
        try { _store.SaveSavingsGoal(dialog.Result); LoadDashboard(); SelectNavigation("Budgets"); StatusText.Text = "储蓄目标已更新"; ScheduleCloudSync(); }
        catch (Exception ex) { await ShowMessage("目标保存失败", ex.Message); }
    }

    private async void DeleteSavingsGoalClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SavingsGoalRecord goal }) return;
        var confirm = new ContentDialog { XamlRoot = ContentHost.XamlRoot, Title = "删除这个储蓄目标？", Content = $"{goal.Name}\n{goal.TargetDisplay}", PrimaryButtonText = "删除", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
        _store.DeleteSavingsGoal(goal.Id); LoadDashboard(); SelectNavigation("Budgets"); StatusText.Text = "储蓄目标已删除"; ScheduleCloudSync();
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
        ScheduleCloudSync();
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
            await ShowMessage("剪贴板中没有图片", "请先复制微信、支付宝或银行账单截图，然后点击“粘贴截图”或按 Ctrl+V。");
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
        ScheduleCloudSync();
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
        ScheduleCloudSync();
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
        if (_transactionBatchMode) return;
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
        ScheduleCloudSync();
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
        ScheduleCloudSync();
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
            ScheduleCloudSync();
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
            ScheduleCloudSync();
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
            ScheduleCloudSync();
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
            ScheduleCloudSync();
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
            ScheduleCloudSync();
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
            ScheduleCloudSync();
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
        RefreshTransactionBatchChoices();
        _transactionFiltersReady = true;
    }

    private void RefreshTransactionFilterOptions()
    {
        SyncTransactionOptions(_transactionCategoryOptions, _store.ListCategories().Where(item => item.IsActive).Select(item => (item.Name, item.Name)));
        SyncTransactionOptions(_transactionAccountOptions, _store.ListAccounts().Where(item => item.IsActive).Select(item => (item.Id.ToString(), item.Name)));
        SyncTransactionOptions(_transactionSourceOptions, _store.ListTransactionSources().Select(item => (item, item)));
        FilterTransactionOptionLists();
    }

    private void RefreshTransactionBatchChoices()
    {
        var selectedCategory = BatchCategoryBox.SelectedItem?.ToString();
        BatchCategoryBox.ItemsSource = _store.ListCategories().Where(item => item.IsActive).Select(item => item.Name).Distinct().ToList();
        if (selectedCategory is not null) BatchCategoryBox.SelectedItem = selectedCategory;
        var selectedAccountId = (BatchAccountBox.SelectedItem as AccountRecord)?.Id;
        var accounts = new List<AccountRecord> { new() { Id = 0, Name = "未指定账户", Type = "" } };
        accounts.AddRange(_store.ListAccounts().Where(item => item.IsActive));
        BatchAccountBox.ItemsSource = accounts;
        if (selectedAccountId is not null) BatchAccountBox.SelectedItem = accounts.FirstOrDefault(item => item.Id == selectedAccountId);
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
        _currentTransactionRows = result.Rows;
        if (_transactionBatchMode) TransactionsList.SelectedItems.Clear();
        else TransactionsList.SelectedItem = null;
        if (_transactionGroupMode == TransactionGroupMode.None)
        {
            _transactionGroupsSource.IsSourceGrouped = false;
            _transactionGroupsSource.Source = result.Rows;
        }
        else
        {
            _transactionGroupsSource.IsSourceGrouped = true;
            _transactionGroupsSource.Source = BuildTransactionGroups(result.Rows);
        }
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

    private IReadOnlyList<TransactionDateGroup> BuildTransactionGroups(IReadOnlyList<TransactionRecord> rows)
    {
        DateTime GroupStart(TransactionRecord row) => _transactionGroupMode switch
        {
            TransactionGroupMode.Week => row.OccurredOn.Date.AddDays(-(((int)row.OccurredOn.DayOfWeek + 6) % 7)),
            TransactionGroupMode.Month => new DateTime(row.OccurredOn.Year, row.OccurredOn.Month, 1),
            _ => row.OccurredOn.Date
        };
        var groups = rows.GroupBy(GroupStart);
        groups = _transactionQuery.SortBy == TransactionSortOption.DateAscending ? groups.OrderBy(group => group.Key) : groups.OrderByDescending(group => group.Key);
        return groups.Select(group => _transactionGroupMode switch
        {
            TransactionGroupMode.Week => new TransactionDateGroup(FormatWeekLabel(group.Key), group, true),
            TransactionGroupMode.Month => new TransactionDateGroup(group.Key.ToString("yyyy年M月"), group, true),
            _ => new TransactionDateGroup(group.Key, group)
        }).ToList();
    }

    private static string FormatWeekLabel(DateTime start)
    {
        var end = start.AddDays(6);
        return start.Year == end.Year
            ? start.Month == end.Month ? $"{start:yyyy年M月d日}—{end:d日}" : $"{start:yyyy年M月d日}—{end:M月d日}"
            : $"{start:yyyy年M月d日}—{end:yyyy年M月d日}";
    }

    private void TransactionGroupChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_transactionFiltersReady) return;
        _transactionGroupMode = (TransactionGroupMode)Math.Max(0, TransactionGroupBox.SelectedIndex);
        ApplyTransactionQuery();
    }

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
        if (_transactionQuery.SubscriptionOnly) chips.Add(new() { Key = "subscription", Label = "只看周期性支出  ×" });
        if (_transactionQuery.UnassignedAccountOnly) chips.Add(new() { Key = "unassigned", Label = "只看未指定账户  ×" });
        TransactionFilterChipsControl.ItemsSource = chips;
        ClearAllTransactionFiltersButton.Visibility = chips.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        var advancedCount = _transactionQuery.Directions.Count + _transactionQuery.Categories.Count + _transactionQuery.AccountIds.Count + _transactionQuery.Sources.Count
            + (_transactionQuery.MinimumAmount is null ? 0 : 1) + (_transactionQuery.MaximumAmount is null ? 0 : 1)
            + (_transactionQuery.UncategorizedOnly ? 1 : 0) + (_transactionQuery.SubscriptionOnly ? 1 : 0) + (_transactionQuery.UnassignedAccountOnly ? 1 : 0);
        AdvancedTransactionFiltersButton.Content = advancedCount == 0 ? "高级筛选" : $"高级筛选 · {advancedCount}";
        var savedCount = _store.LoadSavedTransactionFilters().Count;
        SavedFiltersButton.Content = savedCount == 0 ? "常用筛选" : $"常用筛选 · {savedCount}";
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

    private IReadOnlyList<TransactionRecord> SelectedTransactionRows()
        => TransactionsList.SelectedItems.OfType<TransactionRecord>().ToList();

    private void ToggleTransactionBatchModeClick(object sender, RoutedEventArgs e)
    {
        var enable = !_transactionBatchMode;
        if (enable)
        {
            TransactionsList.SelectionMode = ListViewSelectionMode.Multiple;
            TransactionsList.SelectedItem = null;
        }
        else
        {
            TransactionsList.SelectedItems.Clear();
            TransactionsList.SelectionMode = ListViewSelectionMode.None;
        }
        _transactionBatchMode = enable;
        TransactionBatchActionBar.Visibility = _transactionBatchMode ? Visibility.Visible : Visibility.Collapsed;
        BatchModeButton.Content = _transactionBatchMode ? "退出批量" : "批量管理";
        UpdateTransactionBatchSelection();
    }

    private void TransactionsSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateTransactionBatchSelection();

    private void UpdateTransactionBatchSelection()
    {
        var count = TransactionsList.SelectedItems.Count;
        BatchSelectedCountText.Text = $"已选 {count} 条";
    }

    private void SelectAllFilteredTransactionsClick(object sender, RoutedEventArgs e)
    {
        TransactionsList.SelectedItems.Clear();
        foreach (var row in _currentTransactionRows) TransactionsList.SelectedItems.Add(row);
        UpdateTransactionBatchSelection();
    }

    private async void BatchUpdateCategoryClick(object sender, RoutedEventArgs e)
    {
        var rows = SelectedTransactionRows();
        if (rows.Count == 0) { await ShowMessage("尚未选择流水", "请先勾选需要修改分类的流水。 "); return; }
        if (BatchCategoryBox.SelectedItem is not string category) { await ShowMessage("尚未选择分类", "请先在批量操作栏中选择目标分类。 "); return; }
        var count = _store.BatchUpdateTransactionCategory(rows.Select(item => item.Id), category);
        LoadDashboard(); SelectNavigation("Transactions");
        StatusText.Text = $"已将 {count} 条流水修改为“{category}”";
        ScheduleCloudSync();
    }

    private async void BatchUpdateAccountClick(object sender, RoutedEventArgs e)
    {
        var rows = SelectedTransactionRows();
        if (rows.Count == 0) { await ShowMessage("尚未选择流水", "请先勾选需要修改账户的流水。 "); return; }
        if (BatchAccountBox.SelectedItem is not AccountRecord account) { await ShowMessage("尚未选择账户", "请先在批量操作栏中选择目标账户。 "); return; }
        var accountId = account.Id > 0 ? account.Id : (long?)null;
        var count = _store.BatchUpdateTransactionAccount(rows.Select(item => item.Id), accountId);
        LoadDashboard(); SelectNavigation("Transactions");
        StatusText.Text = $"已将 {count} 条流水修改为“{account.Name}”";
        ScheduleCloudSync();
    }

    private async void BatchDeleteTransactionsClick(object sender, RoutedEventArgs e)
    {
        var rows = SelectedTransactionRows();
        if (rows.Count == 0) { await ShowMessage("尚未选择流水", "请先勾选需要删除的流水。 "); return; }
        var amount = rows.Sum(item => item.Amount);
        var confirm = new ContentDialog
        {
            XamlRoot = ContentHost.XamlRoot,
            Title = $"删除选中的 {rows.Count} 条流水？",
            Content = $"选中记录金额合计 ¥{amount:N2}。删除后只能通过账本备份恢复。",
            PrimaryButtonText = "确认删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
        var count = _store.BatchDeleteTransactions(rows.Select(item => item.Id));
        LoadDashboard(); SelectNavigation("Transactions");
        StatusText.Text = $"已删除 {count} 条流水";
        ScheduleCloudSync();
    }

    private async void ExportFilteredTransactionsClick(object sender, RoutedEventArgs e)
        => await ExportTransactionsAsync(_currentTransactionRows, "筛选结果");

    private async void ExportSelectedTransactionsClick(object sender, RoutedEventArgs e)
        => await ExportTransactionsAsync(SelectedTransactionRows(), "所选流水");

    private async Task ExportTransactionsAsync(IReadOnlyList<TransactionRecord> rows, string scope)
    {
        if (rows.Count == 0) { await ShowMessage("没有可以导出的流水", scope == "所选流水" ? "请先勾选需要导出的流水。 " : "当前筛选结果为空。 "); return; }
        var picker = new Windows.Storage.Pickers.FileSavePicker { SuggestedFileName = $"独秀账本-{scope}-{DateTime.Now:yyyyMMdd-HHmm}" };
        picker.FileTypeChoices.Add("CSV 表格", [".csv"]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
        var lines = new List<string> { "交易时间,类型,金额,分类,交易对方,账户,备注,来源,周期支出类型,覆盖月数,覆盖开始,下次付款,必要支出" };
        lines.AddRange(rows.Select(row => string.Join(',', Csv(row.DateDisplay), Csv(row.Direction), row.Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), Csv(row.Category), Csv(row.Merchant), Csv(row.AccountDisplay), Csv(row.Note), Csv(row.Source), Csv(row.RecurringType), row.SubscriptionMonths, row.CoverageStart?.ToString("yyyy-MM-dd") ?? "", row.NextPaymentDate?.ToString("yyyy-MM-dd") ?? "", row.IsEssential ? "是" : "否")));
        await File.WriteAllTextAsync(file.Path, "\uFEFF" + string.Join(Environment.NewLine, lines));
        StatusText.Text = $"已导出 {rows.Count} 条{scope}：{file.Path}";
    }

    private async void SaveCurrentTransactionFilterClick(object sender, RoutedEventArgs e)
    {
        var nameBox = new TextBox { Header = "筛选名称", PlaceholderText = "例如：本月游戏消费", MaxLength = 30 };
        var dialog = new ContentDialog { XamlRoot = ContentHost.XamlRoot, Title = "保存当前筛选", Content = nameBox, PrimaryButtonText = "保存", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Primary };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var name = nameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name)) { await ShowMessage("名称不能为空", "请为常用筛选填写一个便于识别的名称。 "); return; }
        var filters = _store.LoadSavedTransactionFilters().Where(item => !string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
        filters.Add(CreateSavedTransactionFilter(name));
        _store.SaveTransactionFilters(filters);
        UpdateTransactionFilterPresentation();
        StatusText.Text = $"常用筛选“{name}”已保存";
    }

    private SavedTransactionFilter CreateSavedTransactionFilter(string name) => new()
    {
        Name = name,
        SearchText = _transactionQuery.SearchText,
        DatePreset = TransactionDatePresetKey(TransactionDatePresetBox.SelectedIndex),
        StartDate = _transactionQuery.StartDate,
        EndDate = _transactionQuery.EndDate,
        Directions = _transactionQuery.Directions.ToList(),
        Categories = _transactionQuery.Categories.ToList(),
        AccountIds = _transactionQuery.AccountIds.ToList(),
        Sources = _transactionQuery.Sources.ToList(),
        MinimumAmount = _transactionQuery.MinimumAmount,
        MaximumAmount = _transactionQuery.MaximumAmount,
        UncategorizedOnly = _transactionQuery.UncategorizedOnly,
        SubscriptionOnly = _transactionQuery.SubscriptionOnly,
        UnassignedAccountOnly = _transactionQuery.UnassignedAccountOnly,
        SortBy = _transactionQuery.SortBy,
        GroupMode = _transactionGroupMode.ToString()
    };

    private void ShowSavedTransactionFiltersClick(object sender, RoutedEventArgs e)
    {
        var filters = _store.LoadSavedTransactionFilters();
        var flyout = new MenuFlyout();
        if (filters.Count == 0) flyout.Items.Add(new MenuFlyoutItem { Text = "还没有保存常用筛选", IsEnabled = false });
        else
        {
            foreach (var filter in filters.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                var item = new MenuFlyoutItem { Text = filter.Name, Tag = filter };
                item.Click += ApplySavedTransactionFilterClick;
                flyout.Items.Add(item);
            }
            flyout.Items.Add(new MenuFlyoutSeparator());
            var deleteMenu = new MenuFlyoutSubItem { Text = "删除常用筛选" };
            foreach (var filter in filters.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                var item = new MenuFlyoutItem { Text = filter.Name, Tag = filter };
                item.Click += DeleteSavedTransactionFilterClick;
                deleteMenu.Items.Add(item);
            }
            flyout.Items.Add(deleteMenu);
        }
        flyout.ShowAt(SavedFiltersButton);
    }

    private void ApplySavedTransactionFilterClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: SavedTransactionFilter filter }) return;
        _transactionFiltersReady = false;
        RefreshTransactionFilterOptions();
        var dateIndex = TransactionDatePresetIndex(filter.DatePreset);
        var dates = dateIndex == 6 ? (filter.StartDate, filter.EndDate) : ResolveTransactionDatePreset(dateIndex);
        _transactionQuery = new TransactionQuery
        {
            SearchText = filter.SearchText,
            StartDate = dates.Item1,
            EndDate = dates.Item2,
            Directions = filter.Directions.ToList(),
            Categories = filter.Categories.ToList(),
            AccountIds = filter.AccountIds.ToList(),
            Sources = filter.Sources.ToList(),
            MinimumAmount = filter.MinimumAmount,
            MaximumAmount = filter.MaximumAmount,
            UncategorizedOnly = filter.UncategorizedOnly,
            SubscriptionOnly = filter.SubscriptionOnly,
            UnassignedAccountOnly = filter.UnassignedAccountOnly,
            SortBy = filter.SortBy
        };
        _transactionGroupMode = Enum.TryParse<TransactionGroupMode>(filter.GroupMode, out var groupMode) ? groupMode : TransactionGroupMode.Day;
        TransactionsSearchBox.Text = filter.SearchText;
        TransactionDatePresetBox.SelectedIndex = dateIndex;
        TransactionGroupBox.SelectedIndex = (int)_transactionGroupMode;
        TransactionStartDatePicker.Date = _transactionQuery.StartDate;
        TransactionEndDatePicker.Date = _transactionQuery.EndDate;
        FilterExpenseCheck.IsChecked = filter.Directions.Contains("支出");
        FilterIncomeCheck.IsChecked = filter.Directions.Contains("收入");
        FilterTransferCheck.IsChecked = filter.Directions.Contains("转账");
        FilterRefundCheck.IsChecked = filter.Directions.Contains("退款");
        FilterReimbursementCheck.IsChecked = filter.Directions.Contains("报销");
        foreach (var option in _transactionCategoryOptions) option.IsSelected = filter.Categories.Contains(option.Key);
        foreach (var option in _transactionAccountOptions) option.IsSelected = filter.AccountIds.Contains(long.Parse(option.Key));
        foreach (var option in _transactionSourceOptions) option.IsSelected = filter.Sources.Contains(option.Key);
        TransactionMinimumAmountBox.Value = filter.MinimumAmount is null ? double.NaN : (double)filter.MinimumAmount.Value;
        TransactionMaximumAmountBox.Value = filter.MaximumAmount is null ? double.NaN : (double)filter.MaximumAmount.Value;
        TransactionUncategorizedCheck.IsChecked = filter.UncategorizedOnly;
        TransactionSubscriptionCheck.IsChecked = filter.SubscriptionOnly;
        TransactionUnassignedAccountCheck.IsChecked = filter.UnassignedAccountOnly;
        TransactionSortBox.SelectedIndex = (int)filter.SortBy;
        _transactionFiltersReady = true;
        FilterTransactionOptionLists();
        ApplyTransactionQuery();
        StatusText.Text = $"已应用常用筛选“{filter.Name}”";
    }

    private void DeleteSavedTransactionFilterClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: SavedTransactionFilter filter }) return;
        var filters = _store.LoadSavedTransactionFilters().Where(item => item.Id != filter.Id).ToList();
        _store.SaveTransactionFilters(filters);
        UpdateTransactionFilterPresentation();
        StatusText.Text = $"已删除常用筛选“{filter.Name}”";
    }

    private static string TransactionDatePresetKey(int index) => index switch
    {
        1 => "ThisMonth", 2 => "LastMonth", 3 => "Last7Days", 4 => "Last30Days", 5 => "ThisYear", 6 => "Custom", _ => "All"
    };

    private static int TransactionDatePresetIndex(string key) => key switch
    {
        "ThisMonth" => 1, "LastMonth" => 2, "Last7Days" => 3, "Last30Days" => 4, "ThisYear" => 5, "Custom" => 6, _ => 0
    };

    private static (DateTime? Start, DateTime? End) ResolveTransactionDatePreset(int index)
    {
        var today = DateTime.Today;
        return index switch
        {
            1 => (new DateTime(today.Year, today.Month, 1), new DateTime(today.Year, today.Month, 1).AddMonths(1).AddDays(-1)),
            2 => (new DateTime(today.Year, today.Month, 1).AddMonths(-1), new DateTime(today.Year, today.Month, 1).AddDays(-1)),
            3 => (today.AddDays(-6), today),
            4 => (today.AddDays(-29), today),
            5 => (new DateTime(today.Year, 1, 1), new DateTime(today.Year, 12, 31)),
            _ => (null, null)
        };
    }

    private void SaveSettingsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = CollectSettings();
            _store.SaveSettings(settings);
            ReminderScheduler.Update(settings);
            ConfigureAutomaticSync();
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
        var current = _store.LoadSettings();
        return new AppSettings
        {
            SmallExpenseThreshold = (decimal)Math.Max(0, SmallExpenseThresholdBox.Value), MonthlyBudget = (decimal)Math.Max(0, MonthlyBudgetBox.Value),
            DailyReminderEnabled = DailyReminderCheck.IsChecked == true, DailyReminderTime = DailyReminderTimePicker.Time.ToString(@"hh\:mm"),
            WeeklySummaryEnabled = WeeklySummaryCheck.IsChecked == true, WeeklySummaryDay = dayIndex == 6 ? DayOfWeek.Sunday : (DayOfWeek)(dayIndex + 1), WeeklySummaryTime = WeeklySummaryTimePicker.Time.ToString(@"hh\:mm"),
            SubscriptionKeywords = SubscriptionKeywordsBox.Text.Trim(), OptionalCategories = OptionalCategoriesBox.Text.Trim(), S3SyncEnabled = S3SyncEnabledCheck.IsChecked == true, SyncOnStartup = SyncOnStartupCheck.IsChecked == true, AutoSyncChanges = AutoSyncChangesCheck.IsChecked == true,
            S3AccessUrl = S3AccessUrlBox.Text.Trim(), S3Endpoint = S3EndpointBox.Text.Trim(), S3Region = S3RegionBox.Text.Trim(), S3Bucket = S3BucketBox.Text.Trim(), S3ObjectKey = S3ObjectKeyBox.Text.Trim(),
            S3AccessKeyId = S3AccessKeyIdBox.Text.Trim(), S3ForcePathStyle = S3ForcePathStyleCheck.IsChecked == true,
            DashboardCardOrder = current.DashboardCardOrder, DashboardHiddenCards = current.DashboardHiddenCards
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
            ConfigureAutomaticSync();
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

    private async Task RunCloudSyncAsync(bool showResult, bool saveUiSettings = true)
    {
        await _cloudSyncGate.WaitAsync();
        try
        {
            if (saveUiSettings) SaveCloudSettings();
            var settings = _store.LoadSettings();
            if (!settings.S3SyncEnabled) throw new InvalidOperationException("请先启用 S3 同步。 ");
            CloudSyncProgress.IsActive = true; CloudSyncInfo.IsOpen = false;
            var result = await _syncService.SyncAsync(settings, _store.LoadS3SecretKey(), _store.LoadS3SessionToken());
            ConfigureAutomaticSync();
            LoadDashboard();
            CloudSyncInfo.Severity = InfoBarSeverity.Success; CloudSyncInfo.Title = "同步完成"; CloudSyncInfo.Message = result.Display; CloudSyncInfo.IsOpen = true;
            StatusText.Text = $"云同步完成：{result.Display}";
        }
        catch (Exception ex)
        {
            CloudSyncInfo.Severity = InfoBarSeverity.Error; CloudSyncInfo.Title = "同步失败，本地数据未受影响"; CloudSyncInfo.Message = SafeCloudError(ex); CloudSyncInfo.IsOpen = true;
            if (showResult) StatusText.Text = "S3 同步失败，请查看对象存储设置";
        }
        finally { CloudSyncProgress.IsActive = false; _cloudSyncGate.Release(); }
    }

    private void ConfigureAutomaticSync()
    {
        var settings = _store.LoadSettings();
        if (settings.S3SyncEnabled && settings.AutoSyncChanges && !string.IsNullOrEmpty(_store.LoadS3SecretKey())) _periodicSyncTimer.Start();
        else
        {
            _periodicSyncTimer.Stop();
            _cloudSyncDebounce?.Cancel();
        }
    }

    private void ScheduleCloudSync()
    {
        var settings = _store.LoadSettings();
        if (!settings.S3SyncEnabled || !settings.AutoSyncChanges || string.IsNullOrEmpty(_store.LoadS3SecretKey())) return;
        _cloudSyncDebounce?.Cancel();
        _cloudSyncDebounce?.Dispose();
        _cloudSyncDebounce = new CancellationTokenSource();
        _ = RunDebouncedCloudSyncAsync(_cloudSyncDebounce.Token);
    }

    private async Task RunDebouncedCloudSyncAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            await RunCloudSyncAsync(showResult: false, saveUiSettings: false);
        }
        catch (OperationCanceledException) { }
    }

    private async void PeriodicSyncTimerTick(object? sender, object e)
        => await RunCloudSyncAsync(showResult: false, saveUiSettings: false);

    private void MainWindowClosed(object sender, WindowEventArgs args)
    {
        _periodicSyncTimer.Stop();
        _periodicSyncTimer.Tick -= PeriodicSyncTimerTick;
        _cloudSyncDebounce?.Cancel();
        _cloudSyncDebounce?.Dispose();
        Closed -= MainWindowClosed;
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
