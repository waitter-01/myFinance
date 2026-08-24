using System.IO;
using Microsoft.Data.Sqlite;
using DuxiuLedger.Desktop.Models;

namespace DuxiuLedger.Desktop.Services;

public sealed class LocalStore
{
    private readonly string _connectionString;
    public string DatabasePath { get; }
    public LocalStore()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DuxiuLedger");
        Directory.CreateDirectory(folder);
        DatabasePath = Path.Combine(folder, "ledger.db");
        _connectionString = $"Data Source={DatabasePath}";
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS transactions (
              id INTEGER PRIMARY KEY AUTOINCREMENT, occurred_on TEXT NOT NULL, direction TEXT NOT NULL,
              amount REAL NOT NULL, category TEXT NOT NULL, merchant TEXT NOT NULL, note TEXT NOT NULL,
              source TEXT NOT NULL, fingerprint TEXT NOT NULL UNIQUE, created_at TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_transactions_occurred_on ON transactions(occurred_on);
            """;
        command.ExecuteNonQuery();
    }
    private SqliteConnection Open() { var c = new SqliteConnection(_connectionString); c.Open(); return c; }
    public IReadOnlyList<TransactionRecord> List(string? search = null)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id, occurred_on, direction, amount, category, merchant, note, source, fingerprint FROM transactions WHERE $search = '' OR merchant LIKE $like OR note LIKE $like ORDER BY occurred_on DESC, id DESC";
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
}
