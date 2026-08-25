namespace DuxiuLedger.Desktop.Models;

public sealed class SubscriptionSummary
{
    public string Merchant { get; set; } = "";
    public string Category { get; set; } = "";
    public string RecurringType { get; set; } = "";
    public int PaymentCount { get; set; }
    public decimal PaidLast12Months { get; set; }
    public decimal MonthlyAverage { get; set; }
    public decimal LatestAmount { get; set; }
    public DateTime LatestPayment { get; set; }
    public int BillingMonths { get; set; } = 1;
    public DateTime CoverageStart { get; set; }
    public DateTime? NextPaymentDate { get; set; }
    public bool IsEssential { get; set; }
    public string PaidDisplay => $"¥{PaidLast12Months:N2}";
    public string MonthlyAverageDisplay => $"¥{MonthlyAverage:N2}/月";
    public string LatestPaymentDisplay => LatestPayment.ToString("yyyy-MM-dd");
    public string BillingCycleDisplay => $"{BillingMonths} 个月";
    public string LatestAmountDisplay => $"¥{LatestAmount:N2}";
    public string CoveragePeriodDisplay => $"{CoverageStart:yyyy-MM-dd} 至 {CoverageStart.AddMonths(Math.Max(1, BillingMonths)).AddDays(-1):yyyy-MM-dd}";
    public string NextPaymentDisplay => NextPaymentDate is null ? "未设置" : NextPaymentDate.Value.ToString("yyyy-MM-dd");
    public string EssentialDisplay => IsEssential ? "必要" : "可调整";
}
