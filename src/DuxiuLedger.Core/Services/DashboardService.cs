using DuxiuLedger.Desktop.Models;

namespace DuxiuLedger.Desktop.Services;

public sealed class DashboardService
{
    public DashboardSnapshot Build(IReadOnlyList<TransactionRecord> records, AppSettings settings, IReadOnlyList<SavingsGoalRecord> goals, IReadOnlyList<BudgetRecord>? budgets = null, DateTime? today = null)
    {
        var now = (today ?? DateTime.Today).Date;
        var start = new DateTime(now.Year, now.Month, 1);
        var end = start.AddMonths(1);
        var elapsedDays = now.Day;
        var previousStart = start.AddMonths(-1);
        var previousEnd = previousStart.AddDays(Math.Min(elapsedDays, DateTime.DaysInMonth(previousStart.Year, previousStart.Month)));
        var current = records.Where(row => row.OccurredOn >= start && row.OccurredOn < end).ToList();
        var previous = records.Where(row => row.OccurredOn >= previousStart && row.OccurredOn < previousEnd.AddDays(1)).ToList();
        var expenseRows = current.Where(row => row.Direction == "支出").ToList();
        var netExpense = NetExpense(current);
        var previousNetExpense = NetExpense(previous);
        var income = current.Where(row => row.Direction == "收入").Sum(row => row.Amount);
        var upcomingItems = BuildUpcoming(records, now, end);
        var upcoming = upcomingItems.Sum(item => item.Amount);
        var budget = Math.Max(0, settings.MonthlyBudget);
        var totalDays = DateTime.DaysInMonth(now.Year, now.Month);
        var goalTarget = goals.Where(item => !item.IsCompleted).Sum(item => item.TargetAmount);
        var goalSaved = goals.Where(item => !item.IsCompleted).Sum(item => item.SavedAmount);

        return new DashboardSnapshot
        {
            PeriodLabel = $"{now:yyyy年M月}",
            Income = income,
            NetExpense = netExpense,
            PreviousNetExpense = previousNetExpense,
            MonthlyBudget = budget,
            UpcomingRecurring = upcoming,
            SafeToSpend = budget - netExpense - upcoming,
            ProjectedExpense = netExpense / Math.Max(1, elapsedDays) * totalDays,
            SavingsProgress = goalTarget <= 0 ? 0 : Math.Min(1, goalSaved / goalTarget),
            CurrentRecordCount = current.Count,
            Trend = BuildTrend(current, budget, now, totalDays),
            TopCategories = expenseRows.GroupBy(row => FinancialAnalysisService.MajorCategory(row.Category))
                .Select(group => new SpendingRankItem
                {
                    Name = group.Key,
                    Amount = group.Sum(row => row.Amount),
                    Count = group.Count(),
                    Share = expenseRows.Sum(row => row.Amount) <= 0 ? 0 : group.Sum(row => row.Amount) / expenseRows.Sum(row => row.Amount)
                }).OrderByDescending(item => item.Amount).Take(5).ToList(),
            RecentTransactions = records.OrderByDescending(row => row.OccurredOn).ThenByDescending(row => row.Id).Take(8).ToList(),
            UpcomingItems = upcomingItems.Take(5).ToList(),
            AttentionItems = BuildAttention(records, current, previous, settings, budgets ?? [], budget, netExpense)
        };
    }

    private static decimal NetExpense(IEnumerable<TransactionRecord> rows)
        => Math.Max(0, rows.Where(row => row.Direction == "支出").Sum(row => row.Amount)
            - rows.Where(row => row.Direction is "退款" or "报销").Sum(row => row.Amount));

    private static IReadOnlyList<DashboardUpcomingItem> BuildUpcoming(IReadOnlyList<TransactionRecord> records, DateTime today, DateTime monthEnd)
        => records.Where(row => row.NextPaymentDate >= today && row.NextPaymentDate < monthEnd)
            .GroupBy(row => $"{row.Merchant.Trim()}|{RecurringExpenseTypes.Infer(row)}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(row => row.OccurredOn).First())
            .Select(row => new DashboardUpcomingItem { Merchant = row.Merchant, RecurringType = RecurringExpenseTypes.Infer(row), PaymentDate = row.NextPaymentDate!.Value, Amount = row.Amount, CoverageMonths = Math.Max(1, row.SubscriptionMonths) })
            .OrderBy(item => item.PaymentDate).ToList();

    private static IReadOnlyList<DashboardAttentionItem> BuildAttention(IReadOnlyList<TransactionRecord> all, IReadOnlyList<TransactionRecord> current, IReadOnlyList<TransactionRecord> previous, AppSettings settings, IReadOnlyList<BudgetRecord> budgets, decimal budget, decimal netExpense)
    {
        var items = new List<DashboardAttentionItem>();
        var uncategorized = current.Count(row => string.IsNullOrWhiteSpace(row.Category) || row.Category == "未分类");
        if (uncategorized > 0) items.Add(new() { Title = $"{uncategorized} 笔流水尚未分类", Detail = "完善分类后，消费去向和建议会更准确。", Severity = "warning", ActionKey = "uncategorized" });
        var review = current.Count(row => row.RequiresReview);
        if (review > 0) items.Add(new() { Title = $"{review} 笔识别结果需要核查", Detail = "时间、金额或商户存在识别不确定项。", Severity = "warning", ActionKey = "review" });
        var exactDuplicateRows = all.Where(row => row.Direction != "转账").GroupBy(row => $"{row.OccurredOn:O}|{row.Direction}|{row.Amount:0.00}").Where(group => group.Count() > 1).Sum(group => group.Count());
        if (exactDuplicateRows > 0) items.Add(new() { Title = $"发现 {exactDuplicateRows} 条完全同时间同金额流水", Detail = "可能是重复导入，也可能是同一时刻发生的多笔交易。", Severity = "danger", ActionKey = "duplicates" });
        var overspent = budgets.Where(item => item.Remaining < 0).OrderBy(item => item.Remaining).FirstOrDefault();
        if (overspent is not null) items.Add(new() { Title = $"“{overspent.Category}”预算已超出", Detail = overspent.RemainingDisplay, Severity = "danger", ActionKey = "budgets" });
        else if (budget > 0 && netExpense / budget >= 0.8m) items.Add(new() { Title = "月度预算已使用八成", Detail = $"本月净支出 ¥{netExpense:N2}，建议降低后续可选消费。", Severity = "warning", ActionKey = "budgets" });
        var currentSmall = current.Where(row => row.Direction == "支出" && row.Amount <= settings.SmallExpenseThreshold).Sum(row => row.Amount);
        var previousSmall = previous.Where(row => row.Direction == "支出" && row.Amount <= settings.SmallExpenseThreshold).Sum(row => row.Amount);
        if (previousSmall > 0 && currentSmall > previousSmall * 1.2m) items.Add(new() { Title = "小额消费较上月同期明显增加", Detail = $"当前 ¥{currentSmall:N2}，上月同期 ¥{previousSmall:N2}。", Severity = "info", ActionKey = "insights" });
        if (items.Count == 0) items.Add(new() { Title = "本月账目状态良好", Detail = "暂未发现需要立即处理的分类、预算或重复问题。", Severity = "info", ActionKey = "insights" });
        return items.Take(4).ToList();
    }

    private static IReadOnlyList<DashboardTrendPoint> BuildTrend(IReadOnlyList<TransactionRecord> rows, decimal budget, DateTime today, int totalDays)
    {
        var result = new List<DashboardTrendPoint>();
        decimal cumulative = 0;
        for (var day = 1; day <= totalDays; day++)
        {
            if (day <= today.Day)
            {
                var date = new DateTime(today.Year, today.Month, day);
                cumulative += NetExpense(rows.Where(row => row.OccurredOn.Date == date));
            }
            result.Add(new DashboardTrendPoint { Day = day, Actual = day <= today.Day ? cumulative : -1, Ideal = budget <= 0 ? 0 : budget * day / totalDays });
        }
        return result;
    }
}
