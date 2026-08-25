namespace DuxiuLedger.Desktop.Models;

public sealed class AppSettings
{
    public decimal SmallExpenseThreshold { get; set; } = 50m;
    public decimal MonthlyBudget { get; set; }
    public bool DailyReminderEnabled { get; set; } = true;
    public string DailyReminderTime { get; set; } = "21:00";
    public bool WeeklySummaryEnabled { get; set; } = true;
    public DayOfWeek WeeklySummaryDay { get; set; } = DayOfWeek.Sunday;
    public string WeeklySummaryTime { get; set; } = "20:00";
    public string SubscriptionKeywords { get; set; } = "会员,订阅,月卡,季卡,年卡,续费,自动续费,游戏通行证,云服务,网盘,音乐会员,视频会员";
    public string OptionalCategories { get; set; } = "零食饮料,娱乐休闲,游戏消费,订阅消费,小额杂项";
    public bool S3SyncEnabled { get; set; }
    public bool SyncOnStartup { get; set; }
    public string S3Endpoint { get; set; } = "";
    public string S3Region { get; set; } = "us-east-1";
    public string S3Bucket { get; set; } = "";
    public string S3ObjectKey { get; set; } = "duxiu-ledger/sync-v1.json";
    public string S3AccessKeyId { get; set; } = "";
    public bool S3ForcePathStyle { get; set; }
}
