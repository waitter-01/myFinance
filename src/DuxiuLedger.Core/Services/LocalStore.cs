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
              ('支付宝小荷包','电子钱包',0,1,datetime('now'),datetime('now')),
              ('信用卡','信用卡',0,1,datetime('now'),datetime('now'));
            CREATE TABLE IF NOT EXISTS categories (
              id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, type TEXT NOT NULL,
              is_active INTEGER NOT NULL DEFAULT 1, sort_order INTEGER NOT NULL DEFAULT 0,
              created_at TEXT NOT NULL, updated_at TEXT NOT NULL, UNIQUE(name,type));
            INSERT OR IGNORE INTO categories(name,type,is_active,sort_order,created_at,updated_at) VALUES
              ('日常餐饮','支出',1,10,datetime('now'),datetime('now')),
              ('零食饮料','支出',1,20,datetime('now'),datetime('now')),
              ('生活日用','支出',1,30,datetime('now'),datetime('now')),
              ('交通出行','支出',1,40,datetime('now'),datetime('now')),
              ('居住物业','支出',1,50,datetime('now'),datetime('now')),
              ('水电燃气','支出',1,60,datetime('now'),datetime('now')),
              ('通讯网络','支出',1,70,datetime('now'),datetime('now')),
              ('医疗健康','支出',1,80,datetime('now'),datetime('now')),
              ('保险保障','支出',1,85,datetime('now'),datetime('now')),
              ('学习教育','支出',1,90,datetime('now'),datetime('now')),
              ('数码家电','支出',1,100,datetime('now'),datetime('now')),
              ('服饰美容','支出',1,110,datetime('now'),datetime('now')),
              ('人情往来','支出',1,120,datetime('now'),datetime('now')),
              ('娱乐休闲','支出',1,130,datetime('now'),datetime('now')),
              ('游戏消费','支出',1,140,datetime('now'),datetime('now')),
              ('旅行度假','支出',1,150,datetime('now'),datetime('now')),
              ('宠物消费','支出',1,160,datetime('now'),datetime('now')),
              ('订阅消费','支出',1,170,datetime('now'),datetime('now')),
              ('小额杂项','支出',1,180,datetime('now'),datetime('now')),
              ('其他支出','支出',1,190,datetime('now'),datetime('now')),
              ('工资收入','收入',1,10,datetime('now'),datetime('now')),
              ('奖金补贴','收入',1,20,datetime('now'),datetime('now')),
              ('兼职副业','收入',1,30,datetime('now'),datetime('now')),
              ('投资收益','收入',1,40,datetime('now'),datetime('now')),
              ('利息收入','收入',1,50,datetime('now'),datetime('now')),
              ('礼金收入','收入',1,60,datetime('now'),datetime('now')),
              ('其他收入','收入',1,70,datetime('now'),datetime('now')),
              ('未分类','通用',1,999,datetime('now'),datetime('now'));
            CREATE TABLE IF NOT EXISTS budgets (
              id INTEGER PRIMARY KEY AUTOINCREMENT, month TEXT NOT NULL, category TEXT NOT NULL,
              amount REAL NOT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL,
              UNIQUE(month,category));
            CREATE TABLE IF NOT EXISTS savings_goals (
              id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, target_amount REAL NOT NULL,
              saved_amount REAL NOT NULL DEFAULT 0, target_date TEXT NULL, is_completed INTEGER NOT NULL DEFAULT 0,
              created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
            """;
        command.ExecuteNonQuery();
        EnsureColumn(connection, "transactions", "account_id", "INTEGER");
        EnsureColumn(connection, "transactions", "to_account_id", "INTEGER");
        EnsureColumn(connection, "transactions", "subscription_months", "INTEGER NOT NULL DEFAULT 1");
        EnsureColumn(connection, "transactions", "updated_at", "TEXT");
        EnsureColumn(connection, "transactions", "sync_id", "TEXT");
        EnsureColumn(connection, "accounts", "sync_id", "TEXT");
        EnsureColumn(connection, "categories", "sync_id", "TEXT");
        EnsureColumn(connection, "budgets", "sync_id", "TEXT");
        EnsureColumn(connection, "savings_goals", "sync_id", "TEXT");
        using var syncCommand = connection.CreateCommand();
        syncCommand.CommandText = """
            UPDATE transactions SET updated_at=COALESCE(updated_at,created_at,datetime('now'));
            UPDATE transactions SET sync_id='transaction:' || fingerprint WHERE sync_id IS NULL OR sync_id='';
            UPDATE accounts SET sync_id='account:' || lower(name) WHERE sync_id IS NULL OR sync_id='';
            UPDATE categories SET sync_id='category:' || type || ':' || lower(name) WHERE sync_id IS NULL OR sync_id='';
            UPDATE budgets SET sync_id='budget:' || month || ':' || lower(category) WHERE sync_id IS NULL OR sync_id='';
            UPDATE savings_goals SET sync_id=lower(hex(randomblob(16))) WHERE sync_id IS NULL OR sync_id='';
            CREATE UNIQUE INDEX IF NOT EXISTS ux_transactions_sync_id ON transactions(sync_id);
            CREATE UNIQUE INDEX IF NOT EXISTS ux_accounts_sync_id ON accounts(sync_id);
            CREATE UNIQUE INDEX IF NOT EXISTS ux_categories_sync_id ON categories(sync_id);
            CREATE UNIQUE INDEX IF NOT EXISTS ux_budgets_sync_id ON budgets(sync_id);
            CREATE UNIQUE INDEX IF NOT EXISTS ux_savings_goals_sync_id ON savings_goals(sync_id);
            CREATE TABLE IF NOT EXISTS sync_tombstones (
              entity_type TEXT NOT NULL, sync_id TEXT NOT NULL, deleted_at TEXT NOT NULL,
              PRIMARY KEY(entity_type,sync_id));
            DELETE FROM app_settings WHERE key LIKE 'mysql_%';
            """;
        syncCommand.ExecuteNonQuery();
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
              COALESCE(a.name,''), COALESCE(ta.name,''), COALESCE(t.subscription_months,1)
            FROM transactions t
            LEFT JOIN accounts a ON a.id=t.account_id
            LEFT JOIN accounts ta ON ta.id=t.to_account_id
            WHERE $search = '' OR t.merchant LIKE $like OR t.note LIKE $like OR t.category LIKE $like
              OR a.name LIKE $like OR ta.name LIKE $like
            ORDER BY t.occurred_on DESC, t.id DESC
            """;
        cmd.Parameters.AddWithValue("$search", search ?? ""); cmd.Parameters.AddWithValue("$like", $"%{search}%");
        using var reader = cmd.ExecuteReader(); var rows = new List<TransactionRecord>();
        while (reader.Read()) rows.Add(new TransactionRecord { Id = reader.GetInt64(0), OccurredOn = DateTime.Parse(reader.GetString(1)), Direction = reader.GetString(2), Amount = reader.GetDecimal(3), Category = reader.GetString(4), Merchant = reader.GetString(5), Note = reader.GetString(6), Source = reader.GetString(7), Fingerprint = reader.GetString(8), AccountId = reader.IsDBNull(9) ? null : reader.GetInt64(9), ToAccountId = reader.IsDBNull(10) ? null : reader.GetInt64(10), AccountName = reader.GetString(11), ToAccountName = reader.GetString(12), SubscriptionMonths = reader.GetInt32(13) });
        return rows;
    }
    public int Import(IEnumerable<TransactionRecord> rows)
    {
        using var c = Open(); using var tx = c.BeginTransaction(); var count = 0;
        foreach (var row in rows) { if (string.IsNullOrWhiteSpace(row.Fingerprint)) row.Fingerprint = TransactionFingerprint.Create(row); using var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = "INSERT OR IGNORE INTO transactions(occurred_on,direction,amount,category,merchant,note,source,fingerprint,account_id,to_account_id,subscription_months,created_at,updated_at,sync_id) VALUES($date,$direction,$amount,$category,$merchant,$note,$source,$fingerprint,$account,$toAccount,$months,$created,$created,'transaction:' || $fingerprint)"; cmd.Parameters.AddWithValue("$date", row.OccurredOn.ToString("yyyy-MM-dd HH:mm:ss")); cmd.Parameters.AddWithValue("$direction", row.Direction); cmd.Parameters.AddWithValue("$amount", row.Amount); cmd.Parameters.AddWithValue("$category", row.Category); cmd.Parameters.AddWithValue("$merchant", row.Merchant); cmd.Parameters.AddWithValue("$note", row.Note); cmd.Parameters.AddWithValue("$source", row.Source); cmd.Parameters.AddWithValue("$fingerprint", row.Fingerprint); cmd.Parameters.AddWithValue("$account", (object?)row.AccountId ?? DBNull.Value); cmd.Parameters.AddWithValue("$toAccount", (object?)row.ToAccountId ?? DBNull.Value); cmd.Parameters.AddWithValue("$months", Math.Max(1, row.SubscriptionMonths)); cmd.Parameters.AddWithValue("$created", DateTime.Now.ToString("O")); count += cmd.ExecuteNonQuery(); }
        tx.Commit(); return count;
    }

    public IReadOnlySet<string> ExistingFingerprints()
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT fingerprint FROM transactions";
        using var reader = cmd.ExecuteReader(); var values = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read()) values.Add(reader.GetString(0));
        return values;
    }

    public bool Update(TransactionRecord row)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = """
            UPDATE transactions SET occurred_on=$date, direction=$direction, amount=$amount,
              category=$category, merchant=$merchant, note=$note, source=$source,
              account_id=$account, to_account_id=$toAccount, subscription_months=$months, updated_at=$updated
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
        cmd.Parameters.AddWithValue("$months", Math.Max(1, row.SubscriptionMonths));
        cmd.Parameters.AddWithValue("$updated", DateTime.Now.ToString("O"));
        return cmd.ExecuteNonQuery() == 1;
    }

    public bool Delete(long id)
    {
        using var c = Open();
        RecordTombstone(c, "transaction", GetSyncId(c, "transactions", id));
        using var cmd = c.CreateCommand();
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
            cmd.CommandText = "INSERT INTO accounts(name,type,opening_balance,is_active,created_at,updated_at,sync_id) VALUES($name,$type,$balance,$active,$now,$now,lower(hex(randomblob(16)))); SELECT last_insert_rowid();";
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
        RecordTombstone(c, "account", GetSyncId(c, "accounts", id));
        using var cmd = c.CreateCommand(); cmd.CommandText = "DELETE FROM accounts WHERE id=$id"; cmd.Parameters.AddWithValue("$id", id);
        return cmd.ExecuteNonQuery() == 1;
    }

    public IReadOnlyList<CategoryRecord> ListCategories(bool includeInactive = true)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT c.id,c.name,c.type,c.is_active,c.sort_order,
              (SELECT COUNT(*) FROM transactions t WHERE t.category=c.name) AS usage_count
            FROM categories c WHERE $includeInactive=1 OR c.is_active=1
            ORDER BY CASE c.type WHEN '支出' THEN 0 WHEN '收入' THEN 1 ELSE 2 END, c.sort_order, c.id
            """;
        cmd.Parameters.AddWithValue("$includeInactive", includeInactive ? 1 : 0);
        using var reader = cmd.ExecuteReader(); var rows = new List<CategoryRecord>();
        while (reader.Read()) rows.Add(new CategoryRecord { Id = reader.GetInt64(0), Name = reader.GetString(1), OriginalName = reader.GetString(1), Type = reader.GetString(2), IsActive = reader.GetBoolean(3), SortOrder = reader.GetInt32(4), UsageCount = reader.GetInt32(5) });
        return rows;
    }

    public long SaveCategory(CategoryRecord category)
    {
        using var c = Open(); using var tx = c.BeginTransaction();
        using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        if (category.Id == 0)
        {
            cmd.CommandText = "INSERT INTO categories(name,type,is_active,sort_order,created_at,updated_at,sync_id) VALUES($name,$type,$active,$sort,$now,$now,lower(hex(randomblob(16)))); SELECT last_insert_rowid();";
        }
        else
        {
            cmd.CommandText = "UPDATE categories SET name=$name,type=$type,is_active=$active,sort_order=$sort,updated_at=$now WHERE id=$id; SELECT $id;";
            cmd.Parameters.AddWithValue("$id", category.Id);
        }
        cmd.Parameters.AddWithValue("$name", category.Name.Trim());
        cmd.Parameters.AddWithValue("$type", category.Type);
        cmd.Parameters.AddWithValue("$active", category.IsActive ? 1 : 0);
        cmd.Parameters.AddWithValue("$sort", category.SortOrder);
        cmd.Parameters.AddWithValue("$now", DateTime.Now.ToString("O"));
        var id = Convert.ToInt64(cmd.ExecuteScalar());
        if (category.Id > 0 && !string.IsNullOrWhiteSpace(category.OriginalName) && !string.Equals(category.OriginalName, category.Name, StringComparison.Ordinal))
        {
            using var rename = c.CreateCommand(); rename.Transaction = tx;
            rename.CommandText = "UPDATE transactions SET category=$newName WHERE category=$oldName";
            rename.Parameters.AddWithValue("$newName", category.Name.Trim()); rename.Parameters.AddWithValue("$oldName", category.OriginalName);
            rename.ExecuteNonQuery();
        }
        tx.Commit(); return id;
    }

    public bool DeleteCategory(long id)
    {
        using var c = Open(); using var name = c.CreateCommand();
        name.CommandText = "SELECT name FROM categories WHERE id=$id"; name.Parameters.AddWithValue("$id", id);
        var categoryName = name.ExecuteScalar()?.ToString();
        if (categoryName is null) return false;
        using var used = c.CreateCommand(); used.CommandText = "SELECT COUNT(*) FROM transactions WHERE category=$name"; used.Parameters.AddWithValue("$name", categoryName);
        if (Convert.ToInt32(used.ExecuteScalar()) > 0) throw new InvalidOperationException("该分类已经关联流水，不能删除。可以将它编辑为停用状态。 ");
        RecordTombstone(c, "category", GetSyncId(c, "categories", id));
        using var cmd = c.CreateCommand(); cmd.CommandText = "DELETE FROM categories WHERE id=$id"; cmd.Parameters.AddWithValue("$id", id);
        return cmd.ExecuteNonQuery() == 1;
    }

    public IReadOnlyList<BudgetRecord> ListBudgets(string month)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT b.id,b.month,b.category,b.amount,
              COALESCE((SELECT SUM(t.amount) FROM transactions t
                WHERE t.direction='支出' AND t.category=b.category AND substr(t.occurred_on,1,7)=b.month),0)
            FROM budgets b WHERE b.month=$month ORDER BY b.category
            """;
        cmd.Parameters.AddWithValue("$month", month);
        using var reader = cmd.ExecuteReader(); var rows = new List<BudgetRecord>();
        while (reader.Read()) rows.Add(new BudgetRecord { Id = reader.GetInt64(0), Month = reader.GetString(1), Category = reader.GetString(2), Amount = reader.GetDecimal(3), Spent = reader.GetDecimal(4) });
        return rows;
    }

    public long SaveBudget(BudgetRecord budget)
    {
        if (!DateTime.TryParseExact(budget.Month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) throw new InvalidOperationException("预算月份格式无效。 ");
        if (budget.Amount <= 0) throw new InvalidOperationException("预算金额必须大于 0。 ");
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO budgets(month,category,amount,created_at,updated_at,sync_id) VALUES($month,$category,$amount,$now,$now,'budget:' || $month || ':' || lower($category))
            ON CONFLICT(month,category) DO UPDATE SET amount=$amount,updated_at=$now;
            SELECT id FROM budgets WHERE month=$month AND category=$category;
            """;
        cmd.Parameters.AddWithValue("$month", budget.Month); cmd.Parameters.AddWithValue("$category", budget.Category.Trim());
        cmd.Parameters.AddWithValue("$amount", budget.Amount); cmd.Parameters.AddWithValue("$now", DateTime.Now.ToString("O"));
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public bool DeleteBudget(long id)
    {
        using var c = Open(); RecordTombstone(c, "budget", GetSyncId(c, "budgets", id)); using var cmd = c.CreateCommand(); cmd.CommandText = "DELETE FROM budgets WHERE id=$id"; cmd.Parameters.AddWithValue("$id", id);
        return cmd.ExecuteNonQuery() == 1;
    }

    public IReadOnlyList<SavingsGoalRecord> ListSavingsGoals()
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id,name,target_amount,saved_amount,target_date,is_completed FROM savings_goals ORDER BY is_completed, target_date IS NULL, target_date, id";
        using var reader = cmd.ExecuteReader(); var rows = new List<SavingsGoalRecord>();
        while (reader.Read()) rows.Add(new SavingsGoalRecord { Id = reader.GetInt64(0), Name = reader.GetString(1), TargetAmount = reader.GetDecimal(2), SavedAmount = reader.GetDecimal(3), TargetDate = reader.IsDBNull(4) ? null : DateTime.Parse(reader.GetString(4)), IsCompleted = reader.GetBoolean(5) });
        return rows;
    }

    public long SaveSavingsGoal(SavingsGoalRecord goal)
    {
        if (string.IsNullOrWhiteSpace(goal.Name)) throw new InvalidOperationException("请输入储蓄目标名称。 ");
        if (goal.TargetAmount <= 0 || goal.SavedAmount < 0) throw new InvalidOperationException("目标金额必须大于 0，已存金额不能为负数。 ");
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = goal.Id == 0
            ? "INSERT INTO savings_goals(name,target_amount,saved_amount,target_date,is_completed,created_at,updated_at,sync_id) VALUES($name,$target,$saved,$date,$completed,$now,$now,lower(hex(randomblob(16)))); SELECT last_insert_rowid();"
            : "UPDATE savings_goals SET name=$name,target_amount=$target,saved_amount=$saved,target_date=$date,is_completed=$completed,updated_at=$now WHERE id=$id; SELECT $id;";
        if (goal.Id > 0) cmd.Parameters.AddWithValue("$id", goal.Id);
        cmd.Parameters.AddWithValue("$name", goal.Name.Trim()); cmd.Parameters.AddWithValue("$target", goal.TargetAmount); cmd.Parameters.AddWithValue("$saved", goal.SavedAmount);
        cmd.Parameters.AddWithValue("$date", goal.TargetDate is null ? DBNull.Value : goal.TargetDate.Value.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$completed", goal.IsCompleted || goal.SavedAmount >= goal.TargetAmount ? 1 : 0); cmd.Parameters.AddWithValue("$now", DateTime.Now.ToString("O"));
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public bool DeleteSavingsGoal(long id)
    {
        using var c = Open(); RecordTombstone(c, "savings_goal", GetSyncId(c, "savings_goals", id)); using var cmd = c.CreateCommand(); cmd.CommandText = "DELETE FROM savings_goals WHERE id=$id"; cmd.Parameters.AddWithValue("$id", id);
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
        if (values.TryGetValue("optional_categories", out var optionalCategories)) settings.OptionalCategories = optionalCategories;
        if (TryBool(values, "s3_sync_enabled", out var s3Enabled)) settings.S3SyncEnabled = s3Enabled;
        if (TryBool(values, "sync_on_startup", out var syncOnStartup)) settings.SyncOnStartup = syncOnStartup;
        if (values.TryGetValue("s3_access_url", out var accessUrl)) settings.S3AccessUrl = accessUrl;
        if (values.TryGetValue("s3_endpoint", out var endpoint)) settings.S3Endpoint = endpoint;
        if (values.TryGetValue("s3_region", out var region)) settings.S3Region = region;
        if (values.TryGetValue("s3_bucket", out var bucket) && S3SyncService.IsPlainBucketName(bucket)) settings.S3Bucket = bucket.Trim();
        if (values.TryGetValue("s3_object_key", out var objectKey)) settings.S3ObjectKey = objectKey;
        if (values.TryGetValue("s3_access_key_id", out var accessKeyId)) settings.S3AccessKeyId = accessKeyId;
        if (TryBool(values, "s3_force_path_style", out var forcePathStyle)) settings.S3ForcePathStyle = forcePathStyle;
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
            ["subscription_keywords"] = settings.SubscriptionKeywords,
            ["optional_categories"] = settings.OptionalCategories,
            ["s3_sync_enabled"] = settings.S3SyncEnabled.ToString(),
            ["sync_on_startup"] = settings.SyncOnStartup.ToString(),
            ["s3_access_url"] = settings.S3AccessUrl,
            ["s3_endpoint"] = settings.S3Endpoint,
            ["s3_region"] = settings.S3Region,
            ["s3_bucket"] = settings.S3Bucket,
            ["s3_object_key"] = settings.S3ObjectKey,
            ["s3_access_key_id"] = settings.S3AccessKeyId,
            ["s3_force_path_style"] = settings.S3ForcePathStyle.ToString()
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

    public string LoadS3SecretKey()
    {
        using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT value FROM app_settings WHERE key='s3_secret_key'";
        return CredentialProtector.Unprotect(cmd.ExecuteScalar()?.ToString() ?? "");
    }

    public string LoadS3SessionToken()
    {
        using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT value FROM app_settings WHERE key='s3_session_token'";
        return CredentialProtector.Unprotect(cmd.ExecuteScalar()?.ToString() ?? "");
    }

    public void SaveS3Credentials(string secretKey, string sessionToken)
    {
        using var c = Open(); using var tx = c.BeginTransaction();
        SaveSecret("s3_secret_key", secretKey);
        SaveSecret("s3_session_token", sessionToken);
        tx.Commit();

        void SaveSecret(string key, string value)
        {
            using var cmd = c.CreateCommand(); cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO app_settings(key,value,updated_at) VALUES($key,$value,$updated) ON CONFLICT(key) DO UPDATE SET value=$value,updated_at=$updated";
            cmd.Parameters.AddWithValue("$key", key); cmd.Parameters.AddWithValue("$value", CredentialProtector.Protect(value)); cmd.Parameters.AddWithValue("$updated", DateTime.Now.ToString("O")); cmd.ExecuteNonQuery();
        }
    }

    private static string? GetSyncId(SqliteConnection connection, string table, long id)
    {
        using var cmd = connection.CreateCommand(); cmd.CommandText = $"SELECT sync_id FROM {table} WHERE id=$id"; cmd.Parameters.AddWithValue("$id", id);
        return cmd.ExecuteScalar()?.ToString();
    }

    private static void RecordTombstone(SqliteConnection connection, string entityType, string? syncId)
    {
        if (string.IsNullOrWhiteSpace(syncId)) return;
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT INTO sync_tombstones(entity_type,sync_id,deleted_at) VALUES($type,$id,$now) ON CONFLICT(entity_type,sync_id) DO UPDATE SET deleted_at=$now";
        cmd.Parameters.AddWithValue("$type", entityType); cmd.Parameters.AddWithValue("$id", syncId); cmd.Parameters.AddWithValue("$now", DateTime.Now.ToString("O")); cmd.ExecuteNonQuery();
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
