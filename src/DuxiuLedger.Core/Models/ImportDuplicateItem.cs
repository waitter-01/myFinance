namespace DuxiuLedger.Desktop.Models;

public sealed class ImportDuplicateItem
{
    public required TransactionRecord Incoming { get; init; }
    public required TransactionRecord Existing { get; init; }
    public required string Reason { get; init; }
    public string IncomingDisplay => $"{Incoming.DateDisplay}　{Incoming.Direction}　{Incoming.AmountDisplay}　{Incoming.Merchant}";
    public string ExistingDisplay => $"账本已有：{Existing.DateDisplay}　{Existing.Direction}　{Existing.AmountDisplay}　{Existing.Merchant}";
}
