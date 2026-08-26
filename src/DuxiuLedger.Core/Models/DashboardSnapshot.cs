namespace DuxiuLedger.Desktop.Models;

public sealed class DashboardSnapshot
{
    public string PeriodLabel { get; set; } = "";
    public decimal Income { get; set; }
    public decimal NetExpense { get; set; }
    public decimal Balance => Income - NetExpense;
    public decimal PreviousNetExpense { get; set; }
    public decimal MonthlyBudget { get; set; }
    public decimal UpcomingRecurring { get; set; }
    public decimal SafeToSpend { get; set; }
    public decimal ProjectedExpense { get; set; }
    public decimal SavingsProgress { get; set; }
    public int CurrentRecordCount { get; set; }
    public IReadOnlyList<DashboardTrendPoint> Trend { get; set; } = [];
    public IReadOnlyList<SpendingRankItem> TopCategories { get; set; } = [];
    public IReadOnlyList<TransactionRecord> RecentTransactions { get; set; } = [];
    public IReadOnlyList<DashboardAttentionItem> AttentionItems { get; set; } = [];
    public IReadOnlyList<DashboardUpcomingItem> UpcomingItems { get; set; } = [];
    public decimal? ExpenseChangeRate => PreviousNetExpense <= 0 ? null : (NetExpense - PreviousNetExpense) / PreviousNetExpense;
    public double BudgetProgress => MonthlyBudget <= 0 ? 0 : Math.Min(1, (double)(NetExpense / MonthlyBudget));
    public string IncomeDisplay => $"¥{Income:N2}";
    public string ExpenseDisplay => $"¥{NetExpense:N2}";
    public string BalanceDisplay => $"¥{Balance:N2}";
    public string SafeToSpendDisplay => MonthlyBudget <= 0 ? "设置月度预算" : SafeToSpend >= 0 ? $"¥{SafeToSpend:N2}" : $"超出 ¥{Math.Abs(SafeToSpend):N2}";
    public string ExpenseComparisonDisplay => ExpenseChangeRate is null ? "正在建立上月同期基线" : $"较上月同期 {ExpenseChangeRate:+0.0%;-0.0%;0.0%}";
    public string ProjectionDisplay => $"按当前速度预计月底支出 ¥{ProjectedExpense:N0}";
    public string SafeToSpendDetail => MonthlyBudget <= 0
        ? "设置月度总额度后，系统会扣除已花费和本月待支付项目"
        : $"预算 ¥{MonthlyBudget:N0} − 已花 ¥{NetExpense:N0} − 待支付 ¥{UpcomingRecurring:N0}";
    public string SavingsProgressDisplay => SavingsProgress <= 0 ? "尚未建立储蓄目标" : $"储蓄目标完成 {SavingsProgress:P0}";
}

public sealed class DashboardAttentionItem
{
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    public string Severity { get; set; } = "info";
    public string ActionKey { get; set; } = "";
    public string Icon => Severity == "danger" ? "\uE7BA" : Severity == "warning" ? "\uE7E7" : "\uE946";
}

public sealed class DashboardUpcomingItem
{
    public string Merchant { get; set; } = "";
    public string RecurringType { get; set; } = "";
    public DateTime PaymentDate { get; set; }
    public decimal Amount { get; set; }
    public int CoverageMonths { get; set; } = 1;
    public string DateDisplay => PaymentDate.ToString("M月d日");
    public string AmountDisplay => $"¥{Amount:N2}";
    public string MonthlyCostDisplay => CoverageMonths <= 1 ? RecurringType : $"{RecurringType} · 月均 ¥{Amount / CoverageMonths:N2}";
}

public sealed class DashboardTrendPoint
{
    public int Day { get; set; }
    public decimal Actual { get; set; }
    public decimal Ideal { get; set; }
}
