namespace DuxiuLedger.Desktop.Models;

public sealed class SavedTransactionFilter
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string SearchText { get; set; } = "";
    public string DatePreset { get; set; } = "All";
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public List<string> Directions { get; set; } = [];
    public List<string> Categories { get; set; } = [];
    public List<long> AccountIds { get; set; } = [];
    public List<string> Sources { get; set; } = [];
    public decimal? MinimumAmount { get; set; }
    public decimal? MaximumAmount { get; set; }
    public bool UncategorizedOnly { get; set; }
    public bool SubscriptionOnly { get; set; }
    public bool UnassignedAccountOnly { get; set; }
    public TransactionSortOption SortBy { get; set; }
    public string GroupMode { get; set; } = "Day";
}
