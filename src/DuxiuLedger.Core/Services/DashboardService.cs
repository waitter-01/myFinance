using DuxiuLedger.Desktop.Models;

namespace DuxiuLedger.Desktop.Services;

public sealed class DashboardService
{
    public DashboardSnapshot Build(IReadOnlyList<TransactionRecord> records, AppSettings settings, IReadOnlyList<SavingsGoalRecord> goals, DateTime? today = null)
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
        var upcoming = UpcomingRecurring(records, now, end);
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
            RecentTransactions = records.OrderByDescending(row => row.OccurredOn).ThenByDescending(row => row.Id).Take(8).ToList()
        };
    }

    private static decimal NetExpense(IEnumerable<TransactionRecord> rows)
        => Math.Max(0, rows.Where(row => row.Direction == "支出").Sum(row => row.Amount)
            - rows.Where(row => row.Direction is "退款" or "报销").Sum(row => row.Amount));

    private static decimal UpcomingRecurring(IReadOnlyList<TransactionRecord> records, DateTime today, DateTime monthEnd)
        => records.Where(row => row.NextPaymentDate >= today && row.NextPaymentDate < monthEnd)
            .GroupBy(row => $"{row.Merchant.Trim()}|{RecurringExpenseTypes.Infer(row)}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(row => row.OccurredOn).First().Amount)
            .Sum();

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
