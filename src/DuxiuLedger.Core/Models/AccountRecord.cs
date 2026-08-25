namespace DuxiuLedger.Desktop.Models;

public sealed class AccountRecord
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = "其他";
    public decimal OpeningBalance { get; set; }
    public decimal CurrentBalance { get; set; }
    public bool IsActive { get; set; } = true;
    public string OpeningBalanceDisplay => $"¥{OpeningBalance:N2}";
    public string CurrentBalanceDisplay => $"¥{CurrentBalance:N2}";
    public string StatusDisplay => IsActive ? "使用中" : "已停用";
}
