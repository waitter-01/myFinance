using DuxiuLedger.Desktop.Models;
using DuxiuLedger.Desktop.Services;
using Xunit;

namespace DuxiuLedger.Core.Tests;

public sealed class FinancialAnalysisServiceTests
{
    private readonly FinancialAnalysisService _service = new();
    private readonly AppSettings _settings = new() { SmallExpenseThreshold = 50, OptionalCategories = "零食饮料,娱乐休闲" };

    [Fact]
    public void WeekRange_UsesMondayToSunday()
    {
        var (start, end) = FinancialAnalysisService.GetRange(AnalysisPeriodKind.Week, new DateTime(2026, 8, 26));

        Assert.Equal(new DateTime(2026, 8, 24), start);
        Assert.Equal(new DateTime(2026, 8, 31), end);
    }

    [Fact]
    public void Analyze_ExcludesTransfersAndDeductsRefunds()
    {
        var records = new[]
        {
            Row("支出", 300), Row("退款", 80), Row("报销", 20),
            Row("收入", 1000), Row("转账", 500)
        };

        var result = _service.Analyze(records, _settings, AnalysisPeriodKind.Month, new DateTime(2026, 8, 15), new DateTime(2026, 8, 25));

        Assert.Equal(300, result.GrossExpense);
        Assert.Equal(100, result.Refunds);
        Assert.Equal(200, result.NetExpense);
        Assert.Equal(1000, result.Income);
        Assert.Equal(800, result.Balance);
        Assert.Equal(4, result.TransactionCount);
    }

    [Theory]
    [InlineData(AnalysisPeriodKind.Week, 7)]
    [InlineData(AnalysisPeriodKind.Month, 5)]
    [InlineData(AnalysisPeriodKind.Year, 12)]
    public void Analyze_BuildsExpectedTrendBuckets(AnalysisPeriodKind period, int expectedCount)
    {
        var result = _service.Analyze([], _settings, period, new DateTime(2026, 8, 25), new DateTime(2026, 8, 25));

        Assert.Equal(expectedCount, result.Trend.Count);
    }

    [Fact]
    public void Analyze_ComparesCurrentMonthWithSameElapsedDays()
    {
        var records = new[]
        {
            Row("支出", 100, new DateTime(2026, 8, 2)),
            Row("支出", 200, new DateTime(2026, 7, 10)),
            Row("支出", 999, new DateTime(2026, 7, 28))
        };

        var result = _service.Analyze(records, _settings, AnalysisPeriodKind.Month, new DateTime(2026, 8, 15), new DateTime(2026, 8, 15));

        Assert.Equal(100, result.NetExpense);
        Assert.Equal(200, result.PreviousNetExpense);
        Assert.Equal(-0.5m, result.ExpenseChangeRate);
    }

    [Fact]
    public void Analyze_GroupsDetailedCategoriesIntoUsefulMajorCategories()
    {
        var records = new[]
        {
            Row("支出", 30, category: "日常餐饮"),
            Row("支出", 20, category: "零食饮料"),
            Row("支出", 300, category: "居住物业"),
            Row("支出", 100, category: "水电燃气"),
            Row("支出", 50, category: "未分类")
        };

        var result = _service.Analyze(records, _settings, AnalysisPeriodKind.Month, new DateTime(2026, 8, 15), new DateTime(2026, 8, 15));

        Assert.Equal(400, result.MajorCategoryRanks.Single(item => item.Name == "居住生活").Amount);
        Assert.Equal(50, result.MajorCategoryRanks.Single(item => item.Name == "餐饮饮品").Amount);
        Assert.Equal(50, result.MajorCategoryRanks.Single(item => item.Name == "其他").Amount);
    }

    private static TransactionRecord Row(string direction, decimal amount, DateTime? occurredOn = null, string category = "零食饮料")
        => new()
        {
            OccurredOn = occurredOn ?? new DateTime(2026, 8, 10),
            Direction = direction,
            Amount = amount,
            Category = category,
            Merchant = "测试商户"
        };
}
