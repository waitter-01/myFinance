namespace DuxiuLedger.Desktop.Models;

public sealed class FinancialAnalysisResult
{
    public AnalysisPeriodKind Period { get; set; }
    public DateTime Start { get; set; }
    public DateTime EndExclusive { get; set; }
    public string PeriodLabel { get; set; } = "";
    public string PreviousPeriodLabel { get; set; } = "";
    public decimal Income { get; set; }
    public decimal GrossExpense { get; set; }
    public decimal Refunds { get; set; }
    public decimal NetExpense { get; set; }
    public decimal Balance => Income - NetExpense;
    public decimal SavingsRate => Income <= 0 ? 0 : Balance / Income;
    public decimal PreviousNetExpense { get; set; }
    public decimal? ExpenseChangeRate => PreviousNetExpense <= 0 ? null : (NetExpense - PreviousNetExpense) / PreviousNetExpense;
    public decimal SmallExpense { get; set; }
    public int SmallExpenseCount { get; set; }
    public decimal OptionalExpense { get; set; }
    public int TransactionCount { get; set; }
    public decimal DailyAverage { get; set; }
    public decimal SuggestedLimit { get; set; }
    public TransactionRecord? LargestExpense { get; set; }
    public IReadOnlyList<SpendingRankItem> CategoryRanks { get; set; } = [];
    public IReadOnlyList<SpendingRankItem> MajorCategoryRanks { get; set; } = [];
    public IReadOnlyList<SpendingRankItem> MerchantRanks { get; set; } = [];
    public IReadOnlyList<AnalysisTrendItem> Trend { get; set; } = [];
    public IReadOnlyList<InsightSuggestion> Suggestions { get; set; } = [];
}
