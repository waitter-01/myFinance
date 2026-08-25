namespace DuxiuLedger.Desktop.Models;

public sealed class SpendingRankItem
{
    public string Name { get; set; } = "";
    public decimal Amount { get; set; }
    public int Count { get; set; }
    public decimal Share { get; set; }
    public string AmountDisplay => $"¥{Amount:N2}";
    public string CountDisplay => $"{Count} 笔";
    public string ShareDisplay => $"{Share:P1}";
    public double SharePercent => (double)(Share * 100);
}
