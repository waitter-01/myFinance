using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using DuxiuLedger.Desktop.Models;
using Microsoft.Data.Sqlite;

namespace DuxiuLedger.Desktop.Services;

public sealed class S3SyncService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly LocalStore _store;

    public S3SyncService(LocalStore store) => _store = store;

    public async Task<string> TestConnectionAsync(AppSettings settings, string secretKey, string sessionToken, CancellationToken cancellationToken = default)
    {
        Validate(settings, secretKey);
        using var client = CreateClient(settings, secretKey, sessionToken);
        var document = await ReadRemoteDocumentAsync(client, settings, cancellationToken);
        if (document is null)
            await WriteRemoteDocumentAsync(client, settings, new SyncDocument(), cancellationToken);
        return $"{settings.S3Bucket.Trim()}/{NormalizeObjectKey(settings.S3ObjectKey)}";
    }

    public async Task<SyncResult> SyncAsync(AppSettings settings, string secretKey, string sessionToken, CancellationToken cancellationToken = default)
    {
        Validate(settings, secretKey);
        using var client = CreateClient(settings, secretKey, sessionToken);
        var remoteDocument = await ReadRemoteDocumentAsync(client, settings, cancellationToken) ?? new SyncDocument();
        var merged = remoteDocument.Items.ToDictionary(ItemKey, StringComparer.Ordinal);
        var result = new SyncResult();

        foreach (var localItem in ReadLocalItems())
        {
            var key = ItemKey(localItem);
            if (!merged.TryGetValue(key, out var remoteItem) || IsNewer(localItem, remoteItem))
            {
                merged[key] = localItem;
                result.Uploaded++;
            }
        }

        foreach (var item in merged.Values.OrderBy(item => EntityOrder(item.EntityType)))
        {
            if (!ApplyRemoteItem(item)) continue;
            if (item.IsDeleted) result.Deleted++; else result.Downloaded++;
        }

        foreach (var localItem in ReadLocalItems())
        {
            var key = ItemKey(localItem);
            if (!merged.TryGetValue(key, out var current) || IsNewer(localItem, current)) merged[key] = localItem;
        }

        var output = new SyncDocument
        {
            UpdatedAt = DateTime.UtcNow.ToString("O"),
            Items = merged.Values.OrderBy(item => EntityOrder(item.EntityType)).ThenBy(item => item.SyncId, StringComparer.Ordinal).ToList()
        };
        await WriteRemoteDocumentAsync(client, settings, output, cancellationToken);
        return result;
    }

    private static IAmazonS3 CreateClient(AppSettings settings, string secretKey, string sessionToken)
    {
        AWSCredentials credentials = string.IsNullOrWhiteSpace(sessionToken)
            ? new BasicAWSCredentials(settings.S3AccessKeyId.Trim(), secretKey)
            : new SessionAWSCredentials(settings.S3AccessKeyId.Trim(), secretKey, sessionToken);
        var region = string.IsNullOrWhiteSpace(settings.S3Region) ? "us-east-1" : settings.S3Region.Trim();
        var config = new AmazonS3Config
        {
            ForcePathStyle = settings.S3ForcePathStyle,
            Timeout = TimeSpan.FromSeconds(30),
            MaxErrorRetry = 2
        };
        if (string.IsNullOrWhiteSpace(settings.S3Endpoint))
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(region);
        }
        else
        {
            config.ServiceURL = settings.S3Endpoint.Trim().TrimEnd('/');
            config.AuthenticationRegion = region;
        }
        return new AmazonS3Client(credentials, config);
    }

    private static void Validate(AppSettings settings, string secretKey)
    {
        if (string.IsNullOrWhiteSpace(settings.S3Bucket)) throw new InvalidOperationException("Bucket 不能为空。");
        if (string.IsNullOrWhiteSpace(settings.S3ObjectKey)) throw new InvalidOperationException("对象路径不能为空。");
        if (string.IsNullOrWhiteSpace(settings.S3AccessKeyId)) throw new InvalidOperationException("Access Key ID 不能为空。");
        if (string.IsNullOrWhiteSpace(secretKey)) throw new InvalidOperationException("请先输入并保存 Secret Access Key。");
        if (!string.IsNullOrWhiteSpace(settings.S3Endpoint) && !Uri.TryCreate(settings.S3Endpoint.Trim(), UriKind.Absolute, out _))
            throw new InvalidOperationException("Endpoint 必须是完整的 http:// 或 https:// 地址。");
    }

    private static async Task<SyncDocument?> ReadRemoteDocumentAsync(IAmazonS3 client, AppSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.GetObjectAsync(settings.S3Bucket.Trim(), NormalizeObjectKey(settings.S3ObjectKey), cancellationToken);
            using var reader = new StreamReader(response.ResponseStream, Encoding.UTF8);
            var json = await reader.ReadToEndAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(json)) return new SyncDocument();
            return JsonSerializer.Deserialize<SyncDocument>(json, JsonOptions)
                ?? throw new InvalidDataException("S3同步文件内容无效。");
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound || ex.ErrorCode is "NoSuchKey" or "NotFound")
        {
            return null;
        }
    }

    private static async Task WriteRemoteDocumentAsync(IAmazonS3 client, AppSettings settings, SyncDocument document, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(document, JsonOptions));
        using var stream = new MemoryStream(bytes, writable: false);
        var request = new PutObjectRequest
        {
            BucketName = settings.S3Bucket.Trim(),
            Key = NormalizeObjectKey(settings.S3ObjectKey),
            InputStream = stream,
            ContentType = "application/json",
            AutoCloseStream = false
        };
        await client.PutObjectAsync(request, cancellationToken);
    }

    private IEnumerable<SyncItem> ReadLocalItems()
    {
        using var connection = new SqliteConnection($"Data Source={_store.DatabasePath}"); connection.Open();
        foreach (var item in ReadTransactions(connection)) yield return item;
        foreach (var item in ReadAccounts(connection)) yield return item;
        foreach (var item in ReadCategories(connection)) yield return item;
        foreach (var item in ReadBudgets(connection)) yield return item;
        foreach (var item in ReadSavingsGoals(connection)) yield return item;
        using var tombstones = connection.CreateCommand(); tombstones.CommandText = "SELECT entity_type,sync_id,deleted_at FROM sync_tombstones";
        using var reader = tombstones.ExecuteReader();
        while (reader.Read()) yield return new SyncItem(reader.GetString(0), reader.GetString(1), null, reader.GetString(2), true);
    }

    private static IEnumerable<SyncItem> ReadTransactions(SqliteConnection connection)
    {
        using var command = connection.CreateCommand(); command.CommandText = """
            SELECT t.sync_id,t.updated_at,t.occurred_on,t.direction,t.amount,t.category,t.merchant,t.note,t.source,t.fingerprint,
              t.subscription_months,COALESCE(a.sync_id,''),COALESCE(ta.sync_id,'')
            FROM transactions t LEFT JOIN accounts a ON a.id=t.account_id LEFT JOIN accounts ta ON ta.id=t.to_account_id
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var payload = JsonSerializer.Serialize(new { occurredOn=reader.GetString(2), direction=reader.GetString(3), amount=reader.GetDecimal(4), category=reader.GetString(5), merchant=reader.GetString(6), note=reader.GetString(7), source=reader.GetString(8), fingerprint=reader.GetString(9), subscriptionMonths=reader.GetInt32(10), accountSyncId=reader.GetString(11), toAccountSyncId=reader.GetString(12) });
            yield return new SyncItem("transaction", reader.GetString(0), payload, reader.GetString(1), false);
        }
    }

    private static IEnumerable<SyncItem> ReadAccounts(SqliteConnection connection)
    {
        using var command = connection.CreateCommand(); command.CommandText = "SELECT sync_id,updated_at,name,type,opening_balance,is_active FROM accounts"; using var reader = command.ExecuteReader();
        while (reader.Read()) yield return new SyncItem("account", reader.GetString(0), JsonSerializer.Serialize(new { name=reader.GetString(2), type=reader.GetString(3), openingBalance=reader.GetDecimal(4), isActive=reader.GetBoolean(5) }), reader.GetString(1), false);
    }

    private static IEnumerable<SyncItem> ReadCategories(SqliteConnection connection)
    {
        using var command = connection.CreateCommand(); command.CommandText = "SELECT sync_id,updated_at,name,type,is_active,sort_order FROM categories"; using var reader = command.ExecuteReader();
        while (reader.Read()) yield return new SyncItem("category", reader.GetString(0), JsonSerializer.Serialize(new { name=reader.GetString(2), type=reader.GetString(3), isActive=reader.GetBoolean(4), sortOrder=reader.GetInt32(5) }), reader.GetString(1), false);
    }

    private static IEnumerable<SyncItem> ReadBudgets(SqliteConnection connection)
    {
        using var command = connection.CreateCommand(); command.CommandText = "SELECT sync_id,updated_at,month,category,amount FROM budgets"; using var reader = command.ExecuteReader();
        while (reader.Read()) yield return new SyncItem("budget", reader.GetString(0), JsonSerializer.Serialize(new { month=reader.GetString(2), category=reader.GetString(3), amount=reader.GetDecimal(4) }), reader.GetString(1), false);
    }

    private static IEnumerable<SyncItem> ReadSavingsGoals(SqliteConnection connection)
    {
        using var command = connection.CreateCommand(); command.CommandText = "SELECT sync_id,updated_at,name,target_amount,saved_amount,target_date,is_completed FROM savings_goals"; using var reader = command.ExecuteReader();
        while (reader.Read()) yield return new SyncItem("savings_goal", reader.GetString(0), JsonSerializer.Serialize(new { name=reader.GetString(2), targetAmount=reader.GetDecimal(3), savedAmount=reader.GetDecimal(4), targetDate=reader.IsDBNull(5)?null:reader.GetString(5), isCompleted=reader.GetBoolean(6) }), reader.GetString(1), false);
    }

    private bool ApplyRemoteItem(SyncItem item)
    {
        using var connection = new SqliteConnection($"Data Source={_store.DatabasePath}"); connection.Open();
        var table = item.EntityType switch { "transaction" => "transactions", "account" => "accounts", "category" => "categories", "budget" => "budgets", "savings_goal" => "savings_goals", _ => "" };
        if (table.Length == 0) return false;
        using var current = connection.CreateCommand(); current.CommandText = $"SELECT updated_at FROM {table} WHERE sync_id=$id"; current.Parameters.AddWithValue("$id", item.SyncId);
        var localUpdated = current.ExecuteScalar()?.ToString();
        if (localUpdated is not null && string.CompareOrdinal(localUpdated, item.UpdatedAt) >= 0) return false;
        if (item.IsDeleted)
        {
            using var delete = connection.CreateCommand(); delete.CommandText = $"DELETE FROM {table} WHERE sync_id=$id"; delete.Parameters.AddWithValue("$id", item.SyncId); delete.ExecuteNonQuery();
            UpsertLocalTombstone(connection, item); return true;
        }
        if (item.Payload is null) return false;
        using var document = JsonDocument.Parse(item.Payload); var root = document.RootElement;
        using var command = connection.CreateCommand();
        switch (item.EntityType)
        {
            case "account":
                command.CommandText = "INSERT INTO accounts(name,type,opening_balance,is_active,created_at,updated_at,sync_id) VALUES($name,$type,$balance,$active,$updated,$updated,$id) ON CONFLICT(sync_id) DO UPDATE SET name=$name,type=$type,opening_balance=$balance,is_active=$active,updated_at=$updated";
                Add(command,"$name",Text(root,"name")); Add(command,"$type",Text(root,"type")); Add(command,"$balance",Decimal(root,"openingBalance")); Add(command,"$active",Bool(root,"isActive")?1:0); break;
            case "category":
                command.CommandText = "INSERT INTO categories(name,type,is_active,sort_order,created_at,updated_at,sync_id) VALUES($name,$type,$active,$sort,$updated,$updated,$id) ON CONFLICT(sync_id) DO UPDATE SET name=$name,type=$type,is_active=$active,sort_order=$sort,updated_at=$updated";
                Add(command,"$name",Text(root,"name")); Add(command,"$type",Text(root,"type")); Add(command,"$active",Bool(root,"isActive")?1:0); Add(command,"$sort",Int(root,"sortOrder")); break;
            case "budget":
                command.CommandText = "INSERT INTO budgets(month,category,amount,created_at,updated_at,sync_id) VALUES($month,$category,$amount,$updated,$updated,$id) ON CONFLICT(sync_id) DO UPDATE SET month=$month,category=$category,amount=$amount,updated_at=$updated";
                Add(command,"$month",Text(root,"month")); Add(command,"$category",Text(root,"category")); Add(command,"$amount",Decimal(root,"amount")); break;
            case "savings_goal":
                command.CommandText = "INSERT INTO savings_goals(name,target_amount,saved_amount,target_date,is_completed,created_at,updated_at,sync_id) VALUES($name,$target,$saved,$date,$completed,$updated,$updated,$id) ON CONFLICT(sync_id) DO UPDATE SET name=$name,target_amount=$target,saved_amount=$saved,target_date=$date,is_completed=$completed,updated_at=$updated";
                Add(command,"$name",Text(root,"name")); Add(command,"$target",Decimal(root,"targetAmount")); Add(command,"$saved",Decimal(root,"savedAmount")); Add(command,"$date",NullableText(root,"targetDate") is string date ? date : DBNull.Value); Add(command,"$completed",Bool(root,"isCompleted")?1:0); break;
            case "transaction":
                command.CommandText = """
                    INSERT INTO transactions(occurred_on,direction,amount,category,merchant,note,source,fingerprint,account_id,to_account_id,subscription_months,created_at,updated_at,sync_id)
                    VALUES($occurred,$direction,$amount,$category,$merchant,$note,$source,$fingerprint,(SELECT id FROM accounts WHERE sync_id=$account),(SELECT id FROM accounts WHERE sync_id=$toAccount),$months,$updated,$updated,$id)
                    ON CONFLICT(sync_id) DO UPDATE SET occurred_on=$occurred,direction=$direction,amount=$amount,category=$category,merchant=$merchant,note=$note,source=$source,account_id=(SELECT id FROM accounts WHERE sync_id=$account),to_account_id=(SELECT id FROM accounts WHERE sync_id=$toAccount),subscription_months=$months,updated_at=$updated
                    """;
                Add(command,"$occurred",Text(root,"occurredOn")); Add(command,"$direction",Text(root,"direction")); Add(command,"$amount",Decimal(root,"amount")); Add(command,"$category",Text(root,"category")); Add(command,"$merchant",Text(root,"merchant")); Add(command,"$note",Text(root,"note")); Add(command,"$source",Text(root,"source")); Add(command,"$fingerprint",Text(root,"fingerprint")); Add(command,"$account",Text(root,"accountSyncId")); Add(command,"$toAccount",Text(root,"toAccountSyncId")); Add(command,"$months",Int(root,"subscriptionMonths")); break;
        }
        Add(command,"$id",item.SyncId); Add(command,"$updated",item.UpdatedAt); command.ExecuteNonQuery();
        using var clear = connection.CreateCommand(); clear.CommandText = "DELETE FROM sync_tombstones WHERE entity_type=$type AND sync_id=$id"; Add(clear,"$type",item.EntityType); Add(clear,"$id",item.SyncId); clear.ExecuteNonQuery();
        return true;
    }

    private static void UpsertLocalTombstone(SqliteConnection connection, SyncItem item)
    {
        using var command = connection.CreateCommand(); command.CommandText = "INSERT INTO sync_tombstones(entity_type,sync_id,deleted_at) VALUES($type,$id,$updated) ON CONFLICT(entity_type,sync_id) DO UPDATE SET deleted_at=$updated";
        Add(command,"$type",item.EntityType); Add(command,"$id",item.SyncId); Add(command,"$updated",item.UpdatedAt); command.ExecuteNonQuery();
    }

    private static bool IsNewer(SyncItem candidate, SyncItem current) => string.CompareOrdinal(candidate.UpdatedAt, current.UpdatedAt) > 0;
    private static string ItemKey(SyncItem item) => $"{item.EntityType}\n{item.SyncId}";
    private static string NormalizeObjectKey(string value) => value.Trim().TrimStart('/');
    private static int EntityOrder(string type) => type switch { "account" => 0, "category" => 1, "budget" => 2, "savings_goal" => 3, "transaction" => 4, _ => 9 };
    private static void Add(SqliteCommand command, string name, object value) => command.Parameters.AddWithValue(name, value);
    private static string Text(JsonElement root,string name) => root.GetProperty(name).GetString() ?? "";
    private static string? NullableText(JsonElement root,string name) => root.TryGetProperty(name,out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;
    private static decimal Decimal(JsonElement root,string name) => root.GetProperty(name).GetDecimal();
    private static int Int(JsonElement root,string name) => root.GetProperty(name).GetInt32();
    private static bool Bool(JsonElement root,string name) => root.GetProperty(name).GetBoolean();

    private sealed class SyncDocument
    {
        public int SchemaVersion { get; set; } = 1;
        public string UpdatedAt { get; set; } = DateTime.UtcNow.ToString("O");
        public List<SyncItem> Items { get; set; } = [];
    }

    private sealed record SyncItem(string EntityType, string SyncId, string? Payload, string UpdatedAt, bool IsDeleted);
}
