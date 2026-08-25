using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DuxiuLedger.Desktop.Models;

namespace DuxiuLedger.Desktop.Services;

public static class TransactionFingerprint
{
    public static string Create(TransactionRecord row)
    {
        var text = $"{row.OccurredOn:yyyy-MM-dd HH:mm}|{Normalize(row.Direction)}|{row.Amount.ToString("0.00", CultureInfo.InvariantCulture)}|{Normalize(row.Merchant)}";
        return Hash(text);
    }

    public static string CreateForced(TransactionRecord row)
        => Hash($"{Create(row)}|USER-CONFIRMED|{Guid.NewGuid():N}");

    private static string Normalize(string value)
        => string.Concat(value.Where(character => !char.IsWhiteSpace(character))).Trim().ToUpperInvariant();

    private static string Hash(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}

public sealed class TransactionDuplicateDetector
{
    public ImportDuplicateItem? FindMatch(TransactionRecord incoming, IEnumerable<TransactionRecord> existingRows)
    {
        foreach (var existing in existingRows)
        {
            if (!string.IsNullOrWhiteSpace(incoming.Fingerprint)
                && string.Equals(incoming.Fingerprint, existing.Fingerprint, StringComparison.Ordinal))
            {
                return new ImportDuplicateItem { Incoming = incoming, Existing = existing, Reason = "导入指纹与账本记录完全相同" };
            }

            if (SameMinute(incoming.OccurredOn, existing.OccurredOn)
                && decimal.Round(incoming.Amount, 2) == decimal.Round(existing.Amount, 2)
                && string.Equals(incoming.Direction.Trim(), existing.Direction.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return new ImportDuplicateItem { Incoming = incoming, Existing = existing, Reason = "交易时间、金额和收支类型相同，请确认是否确为两笔交易" };
            }
        }

        return null;
    }

    private static bool SameMinute(DateTime left, DateTime right)
        => left.Year == right.Year && left.Month == right.Month && left.Day == right.Day
            && left.Hour == right.Hour && left.Minute == right.Minute;
}
