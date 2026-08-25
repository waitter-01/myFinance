namespace DuxiuLedger.Desktop.Models;

public sealed class MonthlyTrendItem
{
    public string Month { get; set; } = "";
    public decimal Income { get; set; }
    public decimal Expense { get; set; }
    public decimal Balance => Income - Expense;
    public string IncomeDisplay => $"¥{Income:N0}";
    public string ExpenseDisplay => $"¥{Expense:N0}";
    public string BalanceDisplay => $"¥{Balance:N0}";
}
