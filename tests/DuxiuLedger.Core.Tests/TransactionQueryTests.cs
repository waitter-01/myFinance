using DuxiuLedger.Desktop.Models;
using DuxiuLedger.Desktop.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DuxiuLedger.Core.Tests;

public sealed class TransactionQueryTests
{
    [Fact]
    public void Query_AppliesCompoundFiltersAndCalculatesSummary()
    {
        var path = CreateDatabasePath();
        try
        {
            var store = new LocalStore(path);
            var account = store.ListAccounts().First(item => item.Name == "支付宝");
            store.Import([
                Row(new DateTime(2026, 8, 5, 9, 0, 0), "支出", 36, "零食饮料", "瑞幸咖啡", "支付宝账单", account.Id),
                Row(new DateTime(2026, 8, 7, 12, 0, 0), "支出", 82, "日常餐饮", "餐厅", "微信账单"),
                Row(new DateTime(2026, 8, 8, 10, 0, 0), "退款", 6, "零食饮料", "瑞幸咖啡", "支付宝账单", account.Id),
                Row(new DateTime(2026, 7, 20, 8, 0, 0), "收入", 5000, "工资收入", "公司", "手动录入")
            ]);

            var result = store.QueryTransactions(new TransactionQuery
            {
                SearchText = "瑞幸",
                StartDate = new DateTime(2026, 8, 1),
                EndDate = new DateTime(2026, 8, 31),
                Directions = ["支出", "退款"],
                Categories = ["零食饮料"],
                AccountIds = [account.Id],
                Sources = ["支付宝账单"],
                MinimumAmount = 5,
                MaximumAmount = 50
            });

            Assert.Equal(2, result.Count);
            Assert.Equal(36, result.GrossExpense);
            Assert.Equal(6, result.Refunds);
            Assert.Equal(30, result.NetExpense);
            Assert.Equal(0, result.Income);
            Assert.Equal(-30, result.Balance);
        }
        finally { DeleteDatabase(path); }
    }

    [Fact]
    public void Query_SupportsSpecialFiltersAndSorting()
    {
        var path = CreateDatabasePath();
        try
        {
            var store = new LocalStore(path);
            store.Import([
                Row(new DateTime(2026, 8, 1), "支出", 19, "未分类", "待整理", "微信截图"),
                Row(new DateTime(2026, 8, 2), "支出", 120, "订阅消费", "视频会员", "支付宝截图", months: 12),
                Row(new DateTime(2026, 8, 3), "支出", 30, "日常餐饮", "早餐", "手动录入"),
                Row(new DateTime(2026, 8, 4), "支出", 16, "零食饮料", "蜜雪冰城", "微信截图 · 八月账单.jpg")
            ]);

            var unassigned = store.QueryTransactions(new TransactionQuery { UnassignedAccountOnly = true, SortBy = TransactionSortOption.AmountDescending });
            Assert.Equal([120m, 30m, 19m, 16m], unassigned.Rows.Select(row => row.Amount));
            Assert.Single(store.QueryTransactions(new TransactionQuery { UncategorizedOnly = true }).Rows);
            Assert.Single(store.QueryTransactions(new TransactionQuery { SubscriptionOnly = true }).Rows);
            Assert.Contains("支付宝截图", store.ListTransactionSources());
            Assert.Equal(2, store.QueryTransactions(new TransactionQuery { Sources = ["微信截图"] }).Count);
        }
        finally { DeleteDatabase(path); }
    }

    [Fact]
    public void Store_SupportsBatchOperationsAndSavedFilters()
    {
        var path = CreateDatabasePath();
        try
        {
            var store = new LocalStore(path);
            var account = store.ListAccounts().First(item => item.Name == "微信");
            store.Import([
                Row(new DateTime(2026, 8, 1), "支出", 10, "未分类", "商户甲", "手动录入"),
                Row(new DateTime(2026, 8, 2), "支出", 20, "未分类", "商户乙", "手动录入"),
                Row(new DateTime(2026, 8, 3), "支出", 30, "未分类", "商户丙", "手动录入")
            ]);
            var rows = store.List();
            var selectedIds = rows.Take(2).Select(item => item.Id).ToList();

            Assert.Equal(2, store.BatchUpdateTransactionCategory(selectedIds, "零食饮料"));
            Assert.Equal(2, store.BatchUpdateTransactionAccount(selectedIds, account.Id));
            Assert.All(store.List().Where(item => selectedIds.Contains(item.Id)), item => { Assert.Equal("零食饮料", item.Category); Assert.Equal(account.Id, item.AccountId); });
            Assert.Equal(2, store.BatchDeleteTransactions(selectedIds));
            Assert.Single(store.List());

            store.SaveTransactionFilters([new SavedTransactionFilter { Name = "本月零食", DatePreset = "ThisMonth", Categories = ["零食饮料"], GroupMode = "Week" }]);
            var saved = Assert.Single(store.LoadSavedTransactionFilters());
            Assert.Equal("本月零食", saved.Name);
            Assert.Equal("ThisMonth", saved.DatePreset);
            Assert.Equal("Week", saved.GroupMode);
        }
        finally { DeleteDatabase(path); }
    }

    [Fact]
    public void Store_PersistsRecurringExpenseDetailsAndIncludesThemInFilter()
    {
        var path = CreateDatabasePath();
        try
        {
            var store = new LocalStore(path);
            var rent = Row(new DateTime(2026, 8, 1), "支出", 9000, "住房租金", "房东", "手动录入", months: 3);
            rent.RecurringType = "房租";
            rent.CoverageStart = new DateTime(2026, 8, 1);
            rent.NextPaymentDate = new DateTime(2026, 11, 1);
            rent.IsEssential = true;

            store.Import([rent]);

            var saved = Assert.Single(store.QueryTransactions(new TransactionQuery { SubscriptionOnly = true }).Rows);
            Assert.Equal("房租", saved.RecurringType);
            Assert.Equal(new DateTime(2026, 8, 1), saved.CoverageStart);
            Assert.Equal(new DateTime(2026, 11, 1), saved.NextPaymentDate);
            Assert.True(saved.IsEssential);
            Assert.Equal("房租", RecurringExpenseTypes.Infer(saved));
        }
        finally { DeleteDatabase(path); }
    }

    private static TransactionRecord Row(DateTime date, string direction, decimal amount, string category, string merchant, string source, long? accountId = null, int months = 1)
    {
        var row = new TransactionRecord { OccurredOn = date, Direction = direction, Amount = amount, Category = category, Merchant = merchant, Source = source, AccountId = accountId, SubscriptionMonths = months };
        row.Fingerprint = TransactionFingerprint.CreateForced(row);
        return row;
    }

    private static string CreateDatabasePath() => Path.Combine(Path.GetTempPath(), $"duxiu-query-{Guid.NewGuid():N}.db");
    private static void DeleteDatabase(string path)
    {
        SqliteConnection.ClearAllPools();
        foreach (var candidate in new[] { path, $"{path}-wal", $"{path}-shm" }) if (File.Exists(candidate)) File.Delete(candidate);
    }
}
