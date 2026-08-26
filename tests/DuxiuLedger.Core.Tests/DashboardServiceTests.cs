using DuxiuLedger.Desktop.Models;
using DuxiuLedger.Desktop.Services;
using Xunit;

namespace DuxiuLedger.Core.Tests;

public sealed class DashboardServiceTests
{
    [Fact]
    public void Build_CalculatesSafeToSpendAndSamePeriodComparison()
    {
        var records = new List<TransactionRecord>
        {
            Row("2026-08-02", "收入", 8000), Row("2026-08-03", "支出", 1200, "住房租金"),
            Row("2026-08-08", "支出", 300, "日常餐饮"), Row("2026-08-09", "退款", 100),
            Row("2026-07-03", "支出", 1000),
            new() { OccurredOn = new DateTime(2026, 7, 1), Direction = "转账", Amount = 600, Merchant = "视频会员", RecurringType = "订阅", NextPaymentDate = new DateTime(2026, 8, 20) }
        };

        var result = new DashboardService().Build(records, new AppSettings { MonthlyBudget = 5000 }, [], today: new DateTime(2026, 8, 10));

        Assert.Equal(1400, result.NetExpense);
        Assert.Equal(1000, result.PreviousNetExpense);
        Assert.Equal(3000, result.SafeToSpend);
        Assert.Equal(0.4m, result.ExpenseChangeRate);
        Assert.Equal("居住生活", result.TopCategories[0].Name);
    }

    [Fact]
    public void Build_DeduplicatesUpcomingRecurringByMerchantAndType()
    {
        var records = new[]
        {
            new TransactionRecord { OccurredOn = new DateTime(2026, 6, 1), Direction = "支出", Amount = 30, Merchant = "云服务", RecurringType = "订阅", NextPaymentDate = new DateTime(2026, 8, 18) },
            new TransactionRecord { OccurredOn = new DateTime(2026, 7, 1), Direction = "支出", Amount = 35, Merchant = "云服务", RecurringType = "订阅", NextPaymentDate = new DateTime(2026, 8, 18) }
        };

        var result = new DashboardService().Build(records, new AppSettings { MonthlyBudget = 1000 }, [], today: new DateTime(2026, 8, 10));

        Assert.Equal(35, result.UpcomingRecurring);
        Assert.Equal(965, result.SafeToSpend);
    }

    [Fact]
    public void Build_CreatesActionableAttentionItems()
    {
        var records = new[]
        {
            Row("2026-08-03", "支出", 20), Row("2026-08-03", "支出", 20),
            new TransactionRecord { OccurredOn = new DateTime(2026, 8, 4), Direction = "支出", Amount = 8, Category = "未分类", RequiresReview = true }
        };

        var result = new DashboardService().Build(records, new AppSettings { MonthlyBudget = 40 }, [], [], new DateTime(2026, 8, 10));

        Assert.Contains(result.AttentionItems, item => item.ActionKey == "uncategorized");
        Assert.Contains(result.AttentionItems, item => item.ActionKey == "review");
        Assert.Contains(result.AttentionItems, item => item.ActionKey == "duplicates");
        Assert.Contains(result.AttentionItems, item => item.ActionKey == "budgets");
    }

    private static TransactionRecord Row(string date, string direction, decimal amount, string category = "未分类")
        => new() { OccurredOn = DateTime.Parse(date), Direction = direction, Amount = amount, Category = category, Merchant = category };
}
