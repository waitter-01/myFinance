namespace DuxiuLedger.Desktop.Models;

public sealed class BudgetRecord
{
    public long Id { get; set; }
    public string Month { get; set; } = DateTime.Now.ToString("yyyy-MM");
    public string Category { get; set; } = "日常餐饮";
    public decimal Amount { get; set; }
    public decimal Spent { get; set; }
    public decimal Remaining => Amount - Spent;
    public double Progress => Amount <= 0 ? 0 : Math.Min(1, (double)(Spent / Amount));
    public string AmountDisplay => $"¥{Amount:N2}";
    public string SpentDisplay => $"¥{Spent:N2}";
    public string RemainingDisplay => Remaining >= 0 ? $"剩余 ¥{Remaining:N2}" : $"超出 ¥{Math.Abs(Remaining):N2}";
    public string StatusDisplay => Remaining < 0 ? "已超支" : Progress >= 0.8 ? "接近上限" : "正常";
}
