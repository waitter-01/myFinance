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

            if (IsCrossSourcePaymentMatch(incoming, existing, out var reason))
            {
                return new ImportDuplicateItem { Incoming = incoming, Existing = existing, Reason = reason };
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

    private static bool IsCrossSourcePaymentMatch(TransactionRecord left, TransactionRecord right, out string reason)
    {
        reason = "";
        var bank = IsBankSource(left.Source) ? left : IsBankSource(right.Source) ? right : null;
        var platform = IsPaymentPlatformSource(left.Source) ? left : IsPaymentPlatformSource(right.Source) ? right : null;
        if (bank is null || platform is null || ReferenceEquals(bank, platform)) return false;
        if (decimal.Round(bank.Amount, 2) != decimal.Round(platform.Amount, 2)) return false;
        if (!DirectionsCanRepresentSamePayment(bank.Direction, platform.Direction)) return false;
        if (ContainsRepayment(bank) || ContainsRepayment(platform)) return false;

        var difference = (bank.OccurredOn - platform.OccurredOn).Duration();
        if (bank.OccurredOn.Date != platform.OccurredOn.Date) return false;
        var merchantMatches = MerchantsLikelyMatch(bank.Merchant, platform.Merchant);
        var bankNamesPaymentRail = ContainsPaymentRail(bank.Merchant);
        if ((!merchantMatches || difference > TimeSpan.FromMinutes(30))
            && (!bankNamesPaymentRail || difference > TimeSpan.FromMinutes(5))) return false;

        reason = merchantMatches
            ? "银行卡扣款与支付宝/微信流水金额相同、时间接近且交易对方相符，可能是同一笔支付；通常保留支付平台明细即可"
            : "银行卡扣款标注为支付宝/微信支付，且金额和时间与平台流水相符，可能是同一笔支付；请核对后仅保留一条";
        return true;
    }

    private static bool DirectionsCanRepresentSamePayment(string bankDirection, string platformDirection)
        => (bankDirection == "支出" && platformDirection == "支出")
            || (bankDirection == "收入" && platformDirection is "退款" or "收入");

    private static bool IsBankSource(string source)
        => source.Contains("银行", StringComparison.OrdinalIgnoreCase) || source.Contains("银行卡", StringComparison.OrdinalIgnoreCase);

    private static bool IsPaymentPlatformSource(string source)
        => source.Contains("支付宝", StringComparison.OrdinalIgnoreCase) || source.Contains("微信", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsPaymentRail(string merchant)
        => merchant.Contains("支付宝", StringComparison.OrdinalIgnoreCase)
            || merchant.Contains("微信", StringComparison.OrdinalIgnoreCase)
            || merchant.Contains("财付通", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsRepayment(TransactionRecord row)
        => row.Merchant.Contains("还款", StringComparison.OrdinalIgnoreCase) || row.Note.Contains("还款", StringComparison.OrdinalIgnoreCase);

    private static bool MerchantsLikelyMatch(string left, string right)
    {
        var normalizedLeft = NormalizeMerchant(left);
        var normalizedRight = NormalizeMerchant(right);
        if (normalizedLeft.Length < 3 || normalizedRight.Length < 3) return false;
        return normalizedLeft.Contains(normalizedRight, StringComparison.OrdinalIgnoreCase)
            || normalizedRight.Contains(normalizedLeft, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeMerchant(string value)
    {
        var normalized = string.Concat(value.Where(character => char.IsLetterOrDigit(character))).ToUpperInvariant();
        foreach (var prefix in new[] { "支付宝", "微信支付", "微信", "财付通", "快捷支付", "银联", "支付" })
        {
            if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) normalized = normalized[prefix.Length..];
        }
        return normalized.TrimEnd('点');
    }
}
