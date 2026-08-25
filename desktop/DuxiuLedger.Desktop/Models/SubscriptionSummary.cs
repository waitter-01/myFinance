namespace DuxiuLedger.Desktop.Models;

public sealed class SubscriptionSummary
{
    public string Merchant { get; set; } = "";
    public string Category { get; set; } = "";
    public int PaymentCount { get; set; }
    public decimal PaidLast12Months { get; set; }
    public decimal MonthlyAverage { get; set; }
    public DateTime LatestPayment { get; set; }
    public string PaidDisplay => $"¥{PaidLast12Months:N2}";
    public string MonthlyAverageDisplay => $"¥{MonthlyAverage:N2}/月";
    public string LatestPaymentDisplay => LatestPayment.ToString("yyyy-MM-dd");
}
