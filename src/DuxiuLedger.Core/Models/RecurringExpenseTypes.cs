namespace DuxiuLedger.Desktop.Models;

public static class RecurringExpenseTypes
{
    public static IReadOnlyList<string> All { get; } =
    [
        "数字订阅", "游戏权益", "生活服务", "房租", "物业与车位", "保险保障", "教育服务", "其他周期支出"
    ];

    public static string Infer(TransactionRecord row, IReadOnlyCollection<string>? subscriptionKeywords = null)
    {
        if (!string.IsNullOrWhiteSpace(row.RecurringType)) return row.RecurringType.Trim();
        var text = $"{row.Merchant} {row.Category} {row.Note}";
        if (row.Category == "住房租金" || ContainsAny(text, "房租", "租金", "公寓租赁")) return "房租";
        if (row.Category == "居住物业" && ContainsAny(text, "物业", "车位", "停车位")) return "物业与车位";
        if (row.Category == "保险保障") return "保险保障";
        if (row.Category == "通讯网络" || ContainsAny(text, "宽带", "话费套餐", "手机套餐")) return "生活服务";
        if (row.Category == "学习教育" && row.SubscriptionMonths > 1) return "教育服务";
        if (row.Category == "游戏消费" && ContainsAny(text, "月卡", "季卡", "年卡", "战令", "通行证")) return "游戏权益";
        if (row.Category == "订阅消费" || subscriptionKeywords?.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase)) == true) return "数字订阅";
        return row.SubscriptionMonths > 1 ? "其他周期支出" : "";
    }

    private static bool ContainsAny(string text, params string[] keywords)
        => keywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));
}
