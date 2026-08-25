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
            CREATE TABLE IF NOT EXISTS accounts (
              id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL UNIQUE, type TEXT NOT NULL,
              opening_balance REAL NOT NULL DEFAULT 0, is_active INTEGER NOT NULL DEFAULT 1,
              created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
            INSERT OR IGNORE INTO accounts(name,type,opening_balance,is_active,created_at,updated_at) VALUES
              ('现金','现金',0,1,datetime('now'),datetime('now')),
              ('银行卡','银行卡',0,1,datetime('now'),datetime('now')),
              ('微信','电子钱包',0,1,datetime('now'),datetime('now')),
              ('支付宝','电子钱包',0,1,datetime('now'),datetime('now')),
              ('信用卡','信用卡',0,1,datetime('now'),datetime('now'));
            """;
        command.ExecuteNonQuery();
        EnsureColumn(connection, "transactions", "account_id", "INTEGER");
        EnsureColumn(connection, "transactions", "to_account_id", "INTEGER");
    }
    private SqliteConnection Open() { var c = new SqliteConnection(_connectionString); c.Open(); return c; }
    private static void EnsureColumn(SqliteConnection connection, string table, string column, string definition)
    {
        using var check = connection.CreateCommand();
        check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name=$column";
        check.Parameters.AddWithValue("$column", column);
        if (Convert.ToInt32(check.ExecuteScalar()) > 0) return;
        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
        alter.ExecuteNonQuery();
    }
    public IReadOnlyList<TransactionRecord> List(string? search = null)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT t.id, t.occurred_on, t.direction, t.amount, t.category, t.merchant, t.note,
              t.source, t.fingerprint, t.account_id, t.to_account_id,
              COALESCE(a.name,''), COALESCE(ta.name,'')
            FROM transactions t
            LEFT JOIN accounts a ON a.id=t.account_id
            LEFT JOIN accounts ta ON ta.id=t.to_account_id
            WHERE $search = '' OR t.merchant LIKE $like OR t.note LIKE $like OR t.category LIKE $like
              OR a.name LIKE $like OR ta.name LIKE $like
            ORDER BY t.occurred_on DESC, t.id DESC
            """;
        cmd.Parameters.AddWithValue("$search", search ?? ""); cmd.Parameters.AddWithValue("$like", $"%{search}%");
        using var reader = cmd.ExecuteReader(); var rows = new List<TransactionRecord>();
        while (reader.Read()) rows.Add(new TransactionRecord { Id = reader.GetInt64(0), OccurredOn = DateTime.Parse(reader.GetString(1)), Direction = reader.GetString(2), Amount = reader.GetDecimal(3), Category = reader.GetString(4), Merchant = reader.GetString(5), Note = reader.GetString(6), Source = reader.GetString(7), Fingerprint = reader.GetString(8), AccountId = reader.IsDBNull(9) ? null : reader.GetInt64(9), ToAccountId = reader.IsDBNull(10) ? null : reader.GetInt64(10), AccountName = reader.GetString(11), ToAccountName = reader.GetString(12) });
        return rows;
    }
    public int Import(IEnumerable<TransactionRecord> rows)
    {
        using var c = Open(); using var tx = c.BeginTransaction(); var count = 0;
        foreach (var row in rows) { using var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = "INSERT OR IGNORE INTO transactions(occurred_on,direction,amount,category,merchant,note,source,fingerprint,account_id,to_account_id,created_at) VALUES($date,$direction,$amount,$category,$merchant,$note,$source,$fingerprint,$account,$toAccount,$created)"; cmd.Parameters.AddWithValue("$date", row.OccurredOn.ToString("yyyy-MM-dd HH:mm:ss")); cmd.Parameters.AddWithValue("$direction", row.Direction); cmd.Parameters.AddWithValue("$amount", row.Amount); cmd.Parameters.AddWithValue("$category", row.Category); cmd.Parameters.AddWithValue("$merchant", row.Merchant); cmd.Parameters.AddWithValue("$note", row.Note); cmd.Parameters.AddWithValue("$source", row.Source); cmd.Parameters.AddWithValue("$fingerprint", row.Fingerprint); cmd.Parameters.AddWithValue("$account", (object?)row.AccountId ?? DBNull.Value); cmd.Parameters.AddWithValue("$toAccount", (object?)row.ToAccountId ?? DBNull.Value); cmd.Parameters.AddWithValue("$created", DateTime.Now.ToString("O")); count += cmd.ExecuteNonQuery(); }
        tx.Commit(); return count;
    }

    public bool Update(TransactionRecord row)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = """
            UPDATE transactions SET occurred_on=$date, direction=$direction, amount=$amount,
              category=$category, merchant=$merchant, note=$note, source=$source,
              account_id=$account, to_account_id=$toAccount
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
        cmd.Parameters.AddWithValue("$account", (object?)row.AccountId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$toAccount", (object?)row.ToAccountId ?? DBNull.Value);
        return cmd.ExecuteNonQuery() == 1;
    }

    public bool Delete(long id)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM transactions WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        return cmd.ExecuteNonQuery() == 1;
    }

    public IReadOnlyList<AccountRecord> ListAccounts(bool includeInactive = true)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT a.id, a.name, a.type, a.opening_balance, a.is_active,
              a.opening_balance + COALESCE(SUM(CASE
                WHEN t.account_id=a.id AND t.direction='支出' THEN -t.amount
                WHEN t.account_id=a.id AND t.direction IN ('收入','退款','报销') THEN t.amount
                WHEN t.account_id=a.id AND t.direction='转账' THEN -t.amount
                WHEN t.to_account_id=a.id AND t.direction='转账' THEN t.amount
                ELSE 0 END),0) AS current_balance
            FROM accounts a
            LEFT JOIN transactions t ON t.account_id=a.id OR t.to_account_id=a.id
            WHERE $includeInactive=1 OR a.is_active=1
            GROUP BY a.id, a.name, a.type, a.opening_balance, a.is_active
            ORDER BY a.is_active DESC, a.id
            """;
        cmd.Parameters.AddWithValue("$includeInactive", includeInactive ? 1 : 0);
        using var reader = cmd.ExecuteReader(); var rows = new List<AccountRecord>();
        while (reader.Read()) rows.Add(new AccountRecord { Id = reader.GetInt64(0), Name = reader.GetString(1), Type = reader.GetString(2), OpeningBalance = reader.GetDecimal(3), IsActive = reader.GetBoolean(4), CurrentBalance = reader.GetDecimal(5) });
        return rows;
    }

    public long SaveAccount(AccountRecord account)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        if (account.Id == 0)
        {
            cmd.CommandText = "INSERT INTO accounts(name,type,opening_balance,is_active,created_at,updated_at) VALUES($name,$type,$balance,$active,$now,$now); SELECT last_insert_rowid();";
        }
        else
        {
            cmd.CommandText = "UPDATE accounts SET name=$name,type=$type,opening_balance=$balance,is_active=$active,updated_at=$now WHERE id=$id; SELECT $id;";
            cmd.Parameters.AddWithValue("$id", account.Id);
        }
        cmd.Parameters.AddWithValue("$name", account.Name.Trim());
        cmd.Parameters.AddWithValue("$type", account.Type);
        cmd.Parameters.AddWithValue("$balance", account.OpeningBalance);
        cmd.Parameters.AddWithValue("$active", account.IsActive ? 1 : 0);
        cmd.Parameters.AddWithValue("$now", DateTime.Now.ToString("O"));
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public bool DeleteAccount(long id)
    {
        using var c = Open();
        using var used = c.CreateCommand();
        used.CommandText = "SELECT COUNT(*) FROM transactions WHERE account_id=$id OR to_account_id=$id";
        used.Parameters.AddWithValue("$id", id);
        if (Convert.ToInt32(used.ExecuteScalar()) > 0) throw new InvalidOperationException("该账户已经关联流水，不能删除。可以将它编辑为停用状态。 ");
        using var cmd = c.CreateCommand(); cmd.CommandText = "DELETE FROM accounts WHERE id=$id"; cmd.Parameters.AddWithValue("$id", id);
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
