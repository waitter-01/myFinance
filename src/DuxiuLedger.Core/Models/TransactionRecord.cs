namespace DuxiuLedger.Desktop.Models;

public sealed class TransactionRecord
{
    public long Id { get; set; }
    public DateTime OccurredOn { get; set; }
    public string Direction { get; set; } = "支出";
    public decimal Amount { get; set; }
    public string Category { get; set; } = "未分类";
    public string Merchant { get; set; } = "";
    public string Note { get; set; } = "";
    public string Source { get; set; } = "手动录入";
    public string Fingerprint { get; set; } = "";
    public long? AccountId { get; set; }
    public long? ToAccountId { get; set; }
    public string AccountName { get; set; } = "";
    public string ToAccountName { get; set; } = "";
    public int SubscriptionMonths { get; set; } = 1;
    public string RecurringType { get; set; } = "";
    public DateTime? CoverageStart { get; set; }
    public DateTime? NextPaymentDate { get; set; }
    public bool IsEssential { get; set; }
    public bool RequiresReview { get; set; }
    public string DateDisplay => OccurredOn.ToString("yyyy-MM-dd HH:mm");
    public string AmountDisplay => $"¥{Amount:N2}";
    public string SignedAmountDisplay => Direction switch
    {
        "支出" => $"-¥{Amount:N2}",
        "收入" or "退款" or "报销" => $"+¥{Amount:N2}",
        _ => $"¥{Amount:N2}"
    };
    public string AccountDisplay => Direction == "转账" && !string.IsNullOrWhiteSpace(ToAccountName)
        ? $"{AccountName} → {ToAccountName}"
        : AccountName;
    public string SubscriptionMonthsDisplay => $"{Math.Max(1, SubscriptionMonths)} 个月";
    public string CoveragePeriodDisplay
    {
        get
        {
            var start = (CoverageStart ?? OccurredOn).Date;
            var end = start.AddMonths(Math.Max(1, SubscriptionMonths)).AddDays(-1);
            return $"{start:yyyy-MM-dd} 至 {end:yyyy-MM-dd}";
        }
    }
    public string PreviewSourceDisplay => RequiresReview ? $"⚠ 待核对 · {Source}" : Source;
}
