using System.IO;
using System.Globalization;
using Microsoft.Data.Sqlite;
using DuxiuLedger.Desktop.Models;

namespace DuxiuLedger.Desktop.Services;

public sealed class LocalStore
{
    private readonly string _connectionString;
    public string DatabasePath { get; }
    public LocalStore(string? databasePath = null)
    {
        var folder = databasePath is null
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DuxiuLedger")
            : Path.GetDirectoryName(Path.GetFullPath(databasePath))!;
        Directory.CreateDirectory(folder);
        DatabasePath = databasePath is null ? Path.Combine(folder, "ledger.db") : Path.GetFullPath(databasePath);
        _connectionString = $"Data Source={DatabasePath}";
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS transactions (
              id INTEGER PRIMARY KEY AUTOINCREMENT, occurred_on TEXT NOT NULL, direction TEXT NOT NULL,
              amount REAL NOT NULL, category TEXT NOT NULL, merchant TEXT NOT NULL, note TEXT NOT NULL,
              source TEXT NOT NULL, fingerprint TEXT NOT NULL UNIQUE, created_at TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_transactions_occurred_on ON transactions(occurred_on);
            CREATE TABLE IF NOT EXISTS app_settings (
              key TEXT PRIMARY KEY, value TEXT NOT NULL, updated_at TEXT NOT NULL);
            """;
        command.ExecuteNonQuery();
    }
    private SqliteConnection Open() { var c = new SqliteConnection(_connectionString); c.Open(); return c; }
    public IReadOnlyList<TransactionRecord> List(string? search = null)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id, occurred_on, direction, amount, category, merchant, note, source, fingerprint FROM transactions WHERE $search = '' OR merchant LIKE $like OR note LIKE $like OR category LIKE $like ORDER BY occurred_on DESC, id DESC";
        cmd.Parameters.AddWithValue("$search", search ?? ""); cmd.Parameters.AddWithValue("$like", $"%{search}%");
        using var reader = cmd.ExecuteReader(); var rows = new List<TransactionRecord>();
        while (reader.Read()) rows.Add(new TransactionRecord { Id = reader.GetInt64(0), OccurredOn = DateTime.Parse(reader.GetString(1)), Direction = reader.GetString(2), Amount = reader.GetDecimal(3), Category = reader.GetString(4), Merchant = reader.GetString(5), Note = reader.GetString(6), Source = reader.GetString(7), Fingerprint = reader.GetString(8) });
        return rows;
    }
    public int Import(IEnumerable<TransactionRecord> rows)
    {
        using var c = Open(); using var tx = c.BeginTransaction(); var count = 0;
        foreach (var row in rows) { using var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = "INSERT OR IGNORE INTO transactions(occurred_on,direction,amount,category,merchant,note,source,fingerprint,created_at) VALUES($date,$direction,$amount,$category,$merchant,$note,$source,$fingerprint,$created)"; cmd.Parameters.AddWithValue("$date", row.OccurredOn.ToString("yyyy-MM-dd HH:mm:ss")); cmd.Parameters.AddWithValue("$direction", row.Direction); cmd.Parameters.AddWithValue("$amount", row.Amount); cmd.Parameters.AddWithValue("$category", row.Category); cmd.Parameters.AddWithValue("$merchant", row.Merchant); cmd.Parameters.AddWithValue("$note", row.Note); cmd.Parameters.AddWithValue("$source", row.Source); cmd.Parameters.AddWithValue("$fingerprint", row.Fingerprint); cmd.Parameters.AddWithValue("$created", DateTime.Now.ToString("O")); count += cmd.ExecuteNonQuery(); }
        tx.Commit(); return count;
    }

    public bool Update(TransactionRecord row)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = """
            UPDATE transactions SET occurred_on=$date, direction=$direction, amount=$amount,
              category=$category, merchant=$merchant, note=$note, source=$source
            WHERE id=$id
            """;
        cmd.Parameters.AddWithValue("$id", row.Id);
        cmd.Parameters.AddWithValue("$date", row.OccurredOn.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("$direction", row.Direction);
        cmd.Parameters.AddWithValue("$amount", row.Amount);
        cmd.Parameters.AddWithValue("$category", row.Category);
        cmd.Parameters.AddWithValue("$merchant", row.Merchant);
        cmd.Parameters.AddWithValue("$note", row.Note);
        cmd.Parameters.AddWithValue("$source", row.Source);
        return cmd.ExecuteNonQuery() == 1;
    }

    public bool Delete(long id)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM transactions WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        return cmd.ExecuteNonQuery() == 1;
    }

    public AppSettings LoadSettings()
    {
        var values = new Dictionary<string, string>();
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT key, value FROM app_settings";
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) values[reader.GetString(0)] = reader.GetString(1);

        var settings = new AppSettings();
        if (TryDecimal(values, "small_expense_threshold", out var threshold)) settings.SmallExpenseThreshold = threshold;
        if (TryDecimal(values, "monthly_budget", out var budget)) settings.MonthlyBudget = budget;
        if (TryBool(values, "daily_reminder_enabled", out var dailyEnabled)) settings.DailyReminderEnabled = dailyEnabled;
        if (values.TryGetValue("daily_reminder_time", out var dailyTime)) settings.DailyReminderTime = dailyTime;
        if (TryBool(values, "weekly_summary_enabled", out var weeklyEnabled)) settings.WeeklySummaryEnabled = weeklyEnabled;
        if (values.TryGetValue("weekly_summary_day", out var weeklyDay) && Enum.TryParse<DayOfWeek>(weeklyDay, out var day)) settings.WeeklySummaryDay = day;
        if (values.TryGetValue("weekly_summary_time", out var weeklyTime)) settings.WeeklySummaryTime = weeklyTime;
        if (values.TryGetValue("subscription_keywords", out var keywords)) settings.SubscriptionKeywords = keywords;
        return settings;
    }

    public void SaveSettings(AppSettings settings)
    {
        var values = new Dictionary<string, string>
        {
            ["small_expense_threshold"] = settings.SmallExpenseThreshold.ToString(CultureInfo.InvariantCulture),
            ["monthly_budget"] = settings.MonthlyBudget.ToString(CultureInfo.InvariantCulture),
            ["daily_reminder_enabled"] = settings.DailyReminderEnabled.ToString(),
            ["daily_reminder_time"] = settings.DailyReminderTime,
            ["weekly_summary_enabled"] = settings.WeeklySummaryEnabled.ToString(),
            ["weekly_summary_day"] = settings.WeeklySummaryDay.ToString(),
            ["weekly_summary_time"] = settings.WeeklySummaryTime,
            ["subscription_keywords"] = settings.SubscriptionKeywords
        };
        using var c = Open(); using var tx = c.BeginTransaction();
        foreach (var pair in values)
        {
            using var cmd = c.CreateCommand(); cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO app_settings(key,value,updated_at) VALUES($key,$value,$updated) ON CONFLICT(key) DO UPDATE SET value=$value, updated_at=$updated";
            cmd.Parameters.AddWithValue("$key", pair.Key); cmd.Parameters.AddWithValue("$value", pair.Value); cmd.Parameters.AddWithValue("$updated", DateTime.Now.ToString("O"));
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    private static bool TryDecimal(IReadOnlyDictionary<string, string> values, string key, out decimal result)
    {
        result = 0;
        return values.TryGetValue(key, out var value) && decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
    }

    private static bool TryBool(IReadOnlyDictionary<string, string> values, string key, out bool result)
    {
        result = false;
        return values.TryGetValue(key, out var value) && bool.TryParse(value, out result);
    }
}
