namespace DuxiuLedger.Desktop.Models;

public sealed class SyncResult
{
    public int Uploaded { get; set; }
    public int Downloaded { get; set; }
    public int Deleted { get; set; }
    public string Display => $"上传 {Uploaded} 项，下载/更新 {Downloaded} 项，删除同步 {Deleted} 项";
}
