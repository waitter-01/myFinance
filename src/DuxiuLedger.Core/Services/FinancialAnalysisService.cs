using DuxiuLedger.Desktop.Models;

namespace DuxiuLedger.Desktop.Services;

public sealed class FinancialAnalysisService
{
    public FinancialAnalysisResult Analyze(IReadOnlyList<TransactionRecord> records, AppSettings settings, AnalysisPeriodKind period, DateTime anchor, DateTime? today = null)
    {
        var now = (today ?? DateTime.Today).Date;
        var (start, end) = GetRange(period, anchor);
        var (previousStart, previousEnd) = GetPreviousRange(period, start);
        var effectiveEnd = now >= start && now < end ? now.AddDays(1) : end;
        var elapsed = Math.Max(1, (effectiveEnd - start).Days);
        var previousCompareEnd = previousStart.AddDays(elapsed) > previousEnd ? previousEnd : previousStart.AddDays(elapsed);

        var current = records.Where(row => row.OccurredOn >= start && row.OccurredOn < end).ToList();
        var previous = records.Where(row => row.OccurredOn >= previousStart && row.OccurredOn < previousCompareEnd).ToList();
        var expenseRows = current.Where(row => row.Direction == "支出").ToList();
        var grossExpense = expenseRows.Sum(row => row.Amount);
        var refunds = current.Where(row => row.Direction is "退款" or "报销").Sum(row => row.Amount);
        var netExpense = Math.Max(0, grossExpense - refunds);
        var income = current.Where(row => row.Direction == "收入").Sum(row => row.Amount);
        var previousNetExpense = NetExpense(previous);
        var threshold = settings.SmallExpenseThreshold;
        var smallRows = expenseRows.Where(row => row.Amount <= threshold).ToList();
        var optionalNames = ParseNames(settings.OptionalCategories);
        var optionalRows = expenseRows.Where(row => optionalNames.Contains(row.Category)).ToList();
        var categoryRanks = BuildRanks(expenseRows, row => row.Category, grossExpense);
        var merchantRanks = BuildRanks(expenseRows, row => string.IsNullOrWhiteSpace(row.Merchant) ? "未注明交易对方" : row.Merchant.Trim(), grossExpense);
        var totalDays = Math.Max(1, (end - start).Days);
        var projection = netExpense / elapsed * totalDays;
        var suggestedLimit = SuggestedLimit(period, settings.MonthlyBudget, projection, previousNetExpense, netExpense);

        var result = new FinancialAnalysisResult
        {
            Period = period,
            Start = start,
            EndExclusive = end,
            PeriodLabel = FormatPeriod(period, start, end),
            PreviousPeriodLabel = FormatPeriod(period, previousStart, previousEnd),
            Income = income,
            GrossExpense = grossExpense,
            Refunds = refunds,
            NetExpense = netExpense,
            PreviousNetExpense = previousNetExpense,
            SmallExpense = smallRows.Sum(row => row.Amount),
            SmallExpenseCount = smallRows.Count,
            OptionalExpense = optionalRows.Sum(row => row.Amount),
            TransactionCount = current.Count(row => row.Direction != "转账"),
            DailyAverage = netExpense / elapsed,
            SuggestedLimit = suggestedLimit,
            LargestExpense = expenseRows.OrderByDescending(row => row.Amount).FirstOrDefault(),
            CategoryRanks = categoryRanks,
            MerchantRanks = merchantRanks,
            Trend = BuildTrend(records, period, start, end)
        };
        result.Suggestions = BuildSuggestions(result, settings);
        return result;
    }

    public static (DateTime Start, DateTime EndExclusive) GetRange(AnalysisPeriodKind period, DateTime anchor)
    {
        anchor = anchor.Date;
        return period switch
        {
            AnalysisPeriodKind.Week => WeekRange(anchor),
            AnalysisPeriodKind.Year => (new DateTime(anchor.Year, 1, 1), new DateTime(anchor.Year + 1, 1, 1)),
            _ => (new DateTime(anchor.Year, anchor.Month, 1), new DateTime(anchor.Year, anchor.Month, 1).AddMonths(1))
        };
    }

    private static (DateTime Start, DateTime EndExclusive) WeekRange(DateTime anchor)
    {
        var daysFromMonday = ((int)anchor.DayOfWeek + 6) % 7;
        var start = anchor.AddDays(-daysFromMonday);
        return (start, start.AddDays(7));
    }

    private static (DateTime Start, DateTime EndExclusive) GetPreviousRange(AnalysisPeriodKind period, DateTime start)
        => period switch
        {
            AnalysisPeriodKind.Week => (start.AddDays(-7), start),
            AnalysisPeriodKind.Year => (start.AddYears(-1), start),
            _ => (start.AddMonths(-1), start)
        };

    private static decimal NetExpense(IEnumerable<TransactionRecord> rows)
        => Math.Max(0, rows.Where(row => row.Direction == "支出").Sum(row => row.Amount)
            - rows.Where(row => row.Direction is "退款" or "报销").Sum(row => row.Amount));

    private static HashSet<string> ParseNames(string value)
        => value.Split([',', '，', ';', '；', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static List<SpendingRankItem> BuildRanks(IEnumerable<TransactionRecord> rows, Func<TransactionRecord, string> selector, decimal total)
        => rows.GroupBy(selector, StringComparer.OrdinalIgnoreCase)
            .Select(group => new SpendingRankItem { Name = group.Key, Amount = group.Sum(row => row.Amount), Count = group.Count(), Share = total <= 0 ? 0 : group.Sum(row => row.Amount) / total })
            .OrderByDescending(item => item.Amount)
            .ToList();

    private static decimal SuggestedLimit(AnalysisPeriodKind period, decimal monthlyBudget, decimal projection, decimal previous, decimal current)
    {
        if (monthlyBudget > 0)
            return period switch { AnalysisPeriodKind.Week => monthlyBudget * 12 / 52, AnalysisPeriodKind.Year => monthlyBudget * 12, _ => monthlyBudget };
        var basis = projection > 0 ? projection : previous > 0 ? previous : current;
        return Math.Round(basis * 0.9m, 0);
    }

    private static string FormatPeriod(AnalysisPeriodKind period, DateTime start, DateTime end)
        => period switch
        {
            AnalysisPeriodKind.Week => $"{start:yyyy年M月d日} – {end.AddDays(-1):M月d日}",
            AnalysisPeriodKind.Year => $"{start:yyyy年}",
            _ => $"{start:yyyy年M月}"
        };

    private static IReadOnlyList<AnalysisTrendItem> BuildTrend(IReadOnlyList<TransactionRecord> records, AnalysisPeriodKind period, DateTime start, DateTime end)
    {
        var buckets = new List<(DateTime Start, DateTime End, string Label)>();
        if (period == AnalysisPeriodKind.Week)
        {
            var labels = new[] { "周一", "周二", "周三", "周四", "周五", "周六", "周日" };
            for (var index = 0; index < 7; index++) buckets.Add((start.AddDays(index), start.AddDays(index + 1), labels[index]));
        }
        else if (period == AnalysisPeriodKind.Year)
        {
            for (var month = 0; month < 12; month++) buckets.Add((start.AddMonths(month), start.AddMonths(month + 1), $"{month + 1}月"));
        }
        else
        {
            var cursor = start;
            var number = 1;
            while (cursor < end)
            {
                var bucketEnd = cursor.AddDays(7) < end ? cursor.AddDays(7) : end;
                buckets.Add((cursor, bucketEnd, $"第{number++}周"));
                cursor = bucketEnd;
            }
        }

        var items = buckets.Select(bucket =>
        {
            var rows = records.Where(row => row.OccurredOn >= bucket.Start && row.OccurredOn < bucket.End).ToList();
            return new AnalysisTrendItem
            {
                Label = bucket.Label,
                Income = rows.Where(row => row.Direction == "收入").Sum(row => row.Amount),
                Expense = NetExpense(rows)
            };
        }).ToList();
        var maximum = items.SelectMany(item => new[] { item.Income, item.Expense }).DefaultIfEmpty(0).Max();
        foreach (var item in items)
        {
            item.IncomeHeight = maximum <= 0 ? 2 : Math.Max(2, (double)(item.Income / maximum) * 150);
            item.ExpenseHeight = maximum <= 0 ? 2 : Math.Max(2, (double)(item.Expense / maximum) * 150);
        }
        return items;
    }

    private static IReadOnlyList<InsightSuggestion> BuildSuggestions(FinancialAnalysisResult result, AppSettings settings)
    {
        var label = result.Period switch { AnalysisPeriodKind.Week => "本周", AnalysisPeriodKind.Year => "本年", _ => "本月" };
        var suggestions = new List<InsightSuggestion>();
        if (result.NetExpense <= 0)
        {
            suggestions.Add(new InsightSuggestion { Title = "等待消费数据", Detail = $"{label}还没有可分析的支出，导入或录入后会自动生成总结。" });
            return suggestions;
        }

        var change = result.ExpenseChangeRate;
        suggestions.Add(new InsightSuggestion
        {
            Title = change is null ? "正在建立同期基线" : change > 0.1m ? "支出较上期明显提高" : change < -0.1m ? "支出较上期有所下降" : "支出与上期基本持平",
            Detail = change is null ? "上一个周期的数据不足，继续记录后可获得更准确的变化判断。" : $"{label}净支出 ¥{result.NetExpense:N2}，上期同期 ¥{result.PreviousNetExpense:N2}，变化 {change:+0.0%;-0.0%;0.0%}。"
        });
        suggestions.Add(new InsightSuggestion
        {
            Title = result.SmallExpense / result.GrossExpense >= 0.2m ? "小额消费需要留意" : "小额消费目前可控",
            Detail = $"{result.SmallExpenseCount} 笔不超过 ¥{settings.SmallExpenseThreshold:N0} 的消费合计 ¥{result.SmallExpense:N2}，占支出 {result.SmallExpense / result.GrossExpense:P1}。"
        });
        suggestions.Add(result.CategoryRanks.Count == 0
            ? new InsightSuggestion { Title = "主要去向尚不明确", Detail = "完善流水分类后，可以得到更准确的去向判断。" }
            : new InsightSuggestion { Title = $"钱主要花在“{result.CategoryRanks[0].Name}”", Detail = $"共 {result.CategoryRanks[0].Count} 笔、¥{result.CategoryRanks[0].Amount:N2}，占支出 {result.CategoryRanks[0].Share:P1}。" });
        suggestions.Add(new InsightSuggestion
        {
            Title = result.SavingsRate >= 0.2m ? "储蓄空间良好" : result.Income <= 0 ? "尚无收入数据" : "结余空间需要提高",
            Detail = result.Income <= 0 ? "补充收入记录后才能准确计算储蓄率。" : $"{label}结余 ¥{result.Balance:N2}，储蓄率 {result.SavingsRate:P1}。建议优先压缩可选消费 ¥{result.OptionalExpense:N2}。"
        });
        suggestions.Add(new InsightSuggestion { Title = $"建议额度 ¥{result.SuggestedLimit:N0}", Detail = settings.MonthlyBudget > 0 ? "按你设置的月度总预算换算到当前周期。" : "根据当前消费速度预估，并预留约 10% 的控制空间。" });
        return suggestions;
    }
}
