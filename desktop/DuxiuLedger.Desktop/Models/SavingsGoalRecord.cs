namespace DuxiuLedger.Desktop.Models;

public sealed class SavingsGoalRecord
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public decimal TargetAmount { get; set; }
    public decimal SavedAmount { get; set; }
    public DateTime? TargetDate { get; set; }
    public bool IsCompleted { get; set; }
    public decimal Remaining => Math.Max(0, TargetAmount - SavedAmount);
    public int RemainingMonths => TargetDate is null ? 0 : Math.Max(1, ((TargetDate.Value.Year - DateTime.Today.Year) * 12) + TargetDate.Value.Month - DateTime.Today.Month);
    public decimal MonthlyRequired => RemainingMonths <= 0 ? Remaining : Remaining / RemainingMonths;
    public double Progress => TargetAmount <= 0 ? 0 : Math.Min(1, (double)(SavedAmount / TargetAmount));
    public string TargetDisplay => $"目标 ¥{TargetAmount:N2}";
    public string SavedDisplay => $"已存 ¥{SavedAmount:N2}";
    public string MonthlyRequiredDisplay => Remaining <= 0 ? "目标已完成" : TargetDate is null ? "尚未设置目标日期" : $"每月需存 ¥{MonthlyRequired:N2}";
    public string TargetDateDisplay => TargetDate?.ToString("yyyy-MM-dd") ?? "长期目标";
}
