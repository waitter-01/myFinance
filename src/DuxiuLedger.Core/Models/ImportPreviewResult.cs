namespace DuxiuLedger.Desktop.Models;

public sealed class ImportPreviewResult
{
    public string Source { get; set; } = "";
    public int TotalRows { get; set; }
    public IReadOnlyList<TransactionRecord> Records { get; set; } = [];
    public IReadOnlyList<ImportIssue> Issues { get; set; } = [];
}

public sealed class ImportIssue
{
    public string Source { get; set; } = "";
    public int RowNumber { get; set; }
    public string Reason { get; set; } = "";
    public string RawValue { get; set; } = "";
    public TransactionRecord? Record { get; set; }
    public string RowDisplay => $"第 {RowNumber} 行";
    public bool CanReview => Record is not null;
    public string ReviewActionText => CanReview ? "定位并修改" : "仅查看详情";
}
