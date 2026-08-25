namespace DuxiuLedger.Desktop.Models;

public sealed class CategoryRecord
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string OriginalName { get; set; } = "";
    public string Type { get; set; } = "支出";
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public int UsageCount { get; set; }
    public string StatusDisplay => IsActive ? "使用中" : "已停用";
    public string UsageDisplay => $"{UsageCount} 条流水";
}
