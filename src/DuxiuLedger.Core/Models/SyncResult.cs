namespace DuxiuLedger.Desktop.Models;

public sealed class SyncResult
{
    public int Uploaded { get; set; }
    public int Downloaded { get; set; }
    public int Deleted { get; set; }
    public int Conflicts { get; set; }
    public int MergeAttempts { get; set; } = 1;
    public string Display => $"上传 {Uploaded} 项，下载/更新 {Downloaded} 项，删除同步 {Deleted} 项，冲突 {Conflicts} 项"
        + (MergeAttempts > 1 ? $"（并发重试 {MergeAttempts - 1} 次）" : "");
}
