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
    public string DateDisplay => OccurredOn.ToString("yyyy-MM-dd HH:mm");
    public string AmountDisplay => $"¥{Amount:N2}";
}
