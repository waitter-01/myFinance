namespace DuxiuLedger.Desktop.Models;

public enum TransactionSortOption
{
    DateDescending,
    DateAscending,
    AmountDescending,
    AmountAscending,
    MerchantAscending
}

public sealed class TransactionQuery
{
    public string SearchText { get; set; } = "";
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public IReadOnlyCollection<string> Directions { get; set; } = [];
    public IReadOnlyCollection<string> Categories { get; set; } = [];
    public IReadOnlyCollection<long> AccountIds { get; set; } = [];
    public IReadOnlyCollection<string> Sources { get; set; } = [];
    public decimal? MinimumAmount { get; set; }
    public decimal? MaximumAmount { get; set; }
    public bool UncategorizedOnly { get; set; }
    public bool SubscriptionOnly { get; set; }
    public bool UnassignedAccountOnly { get; set; }
    public TransactionSortOption SortBy { get; set; } = TransactionSortOption.DateDescending;
}

public sealed class TransactionQueryResult
{
    public IReadOnlyList<TransactionRecord> Rows { get; init; } = [];
    public int Count => Rows.Count;
    public decimal Income { get; init; }
    public decimal GrossExpense { get; init; }
    public decimal Refunds { get; init; }
    public decimal NetExpense => GrossExpense - Refunds;
    public decimal Balance => Income - NetExpense;
}
