using DuxiuLedger.Desktop.Models;
using DuxiuLedger.Desktop.Services;
using Xunit;

namespace DuxiuLedger.Core.Tests;

public sealed class TransactionDuplicateDetectorTests
{
    private readonly TransactionDuplicateDetector _detector = new();

    [Fact]
    public void FindMatch_DetectsSameMinuteAmountAndDirectionDespiteDifferentLegacyFingerprints()
    {
        var existing = Row(new DateTime(2026, 8, 17, 21, 18, 42), "支出", 2m, "蜜雪冰城", "OLD-EXCEL-FINGERPRINT");
        var incoming = Row(new DateTime(2026, 8, 17, 21, 18, 0), "支出", 2m, "蜜雪冰城", "NEW-SCREENSHOT-FINGERPRINT");

        var match = _detector.FindMatch(incoming, [existing]);

        Assert.NotNull(match);
        Assert.Same(existing, match.Existing);
        Assert.Contains("交易时间、金额和收支类型相同", match.Reason);
    }

    [Fact]
    public void FindMatch_DoesNotMergeDifferentDirections()
    {
        var existing = Row(new DateTime(2026, 8, 17, 21, 18, 0), "支出", 25m, "测试商户", "A");
        var incoming = Row(new DateTime(2026, 8, 17, 21, 18, 0), "退款", 25m, "测试商户", "B");

        Assert.Null(_detector.FindMatch(incoming, [existing]));
    }

    [Fact]
    public void CreateForced_AllowsUserConfirmedIdenticalTransactionsToRemainDistinct()
    {
        var first = Row(new DateTime(2026, 8, 17, 21, 18, 0), "支出", 7m, "蜜雪冰城", "");
        var second = Row(new DateTime(2026, 8, 17, 21, 18, 0), "支出", 7m, "蜜雪冰城", "");

        first.Fingerprint = TransactionFingerprint.CreateForced(first);
        second.Fingerprint = TransactionFingerprint.CreateForced(second);

        Assert.NotEqual(first.Fingerprint, second.Fingerprint);
    }

    [Fact]
    public void Create_NormalizesMerchantWhitespaceAndAmountPrecision()
    {
        var first = Row(new DateTime(2026, 8, 17, 21, 18, 1), "支出", 7m, "蜜雪 冰城", "");
        var second = Row(new DateTime(2026, 8, 17, 21, 18, 59), "支出", 7.000m, "蜜雪冰城", "");

        Assert.Equal(TransactionFingerprint.Create(first), TransactionFingerprint.Create(second));
    }

    [Fact]
    public void FindMatch_DetectsBankDeductionAndAlipayAsSamePayment()
    {
        var bank = Row(new DateTime(2026, 8, 24, 11, 3, 33), "支出", 159m, "支付宝-东莞市海莫智选", "BANK");
        bank.Source = "中信银行截图 · 中信.png";
        var alipay = Row(new DateTime(2026, 8, 24, 11, 3, 0), "支出", 159m, "东莞市海莫智选", "ALIPAY");
        alipay.Source = "支付宝截图 · 支付宝.png";

        var match = _detector.FindMatch(bank, [alipay]);

        Assert.NotNull(match);
        Assert.Contains("同一笔支付", match.Reason);
    }

    [Fact]
    public void FindMatch_DoesNotTreatRepaymentAsOriginalPlatformPurchase()
    {
        var repayment = Row(new DateTime(2026, 8, 25, 10, 15, 6), "转账", 1330.79m, "美团支付-美团月付还款", "BANK");
        repayment.Source = "工商银行截图 · 工行.png";
        var purchase = Row(new DateTime(2026, 8, 25, 10, 15, 0), "支出", 1330.79m, "美团支付", "PLATFORM");
        purchase.Source = "微信截图 · 微信.png";

        Assert.Null(_detector.FindMatch(repayment, [purchase]));
    }

    [Fact]
    public void FindMatch_DoesNotMergeSameAmountAcrossBankAndPlatformWhenTimeIsFarApart()
    {
        var bank = Row(new DateTime(2026, 8, 24, 11, 30, 0), "支出", 20m, "支付宝", "BANK");
        bank.Source = "中信银行截图";
        var alipay = Row(new DateTime(2026, 8, 24, 11, 0, 0), "支出", 20m, "便利店", "ALIPAY");
        alipay.Source = "支付宝截图";

        Assert.Null(_detector.FindMatch(bank, [alipay]));
    }

    private static TransactionRecord Row(DateTime occurredOn, string direction, decimal amount, string merchant, string fingerprint)
        => new() { OccurredOn = occurredOn, Direction = direction, Amount = amount, Merchant = merchant, Fingerprint = fingerprint };
}
