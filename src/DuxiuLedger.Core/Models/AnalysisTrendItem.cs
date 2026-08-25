namespace DuxiuLedger.Desktop.Models;

public sealed class AnalysisTrendItem
{
    public string Label { get; set; } = "";
    public decimal Income { get; set; }
    public decimal Expense { get; set; }
    public double IncomeHeight { get; set; }
    public double ExpenseHeight { get; set; }
    public string IncomeDisplay => $"收入 ¥{Income:N2}";
    public string ExpenseDisplay => $"支出 ¥{Expense:N2}";
}
