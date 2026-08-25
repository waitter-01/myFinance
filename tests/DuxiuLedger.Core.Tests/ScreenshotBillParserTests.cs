using DuxiuLedger.WinUI;
using Xunit;

namespace DuxiuLedger.Core.Tests;

public sealed class ScreenshotBillParserTests
{
    [Fact]
    public void Parse_KeepsLastRowWhenAmountExistsButMerchantIsMissing()
    {
        const int width = 1000;
        const int height = 1900;
        var tokens = new List<ScreenshotOcrToken>
        {
            new("全部账单", 100, 50, 160, 36),
            new("2026年8月", 100, 145, 180, 36)
        };

        for (var index = 0; index < 8; index++)
        {
            var y = 280 + index * 190;
            if (index < 7) tokens.Add(new ScreenshotOcrToken($"测试商户{index + 1}", 180, y, 220, 42));
            tokens.Add(new ScreenshotOcrToken("8月17日 21:18", 180, y + 48, 230, 34));
            tokens.Add(new ScreenshotOcrToken($"-{index + 2}.00", 760, y, 130, 42));
        }

        var result = ScreenshotBillParser.Parse(tokens, width, height, "测试长截图.png", new DateTime(2026, 8, 25));

        Assert.Equal(8, result.TotalRows);
        Assert.Equal(8, result.Records.Count);
        Assert.True(result.Records[^1].RequiresReview);
        Assert.Contains("待核对交易对方", result.Records[^1].Merchant);
        var issue = Assert.Single(result.Issues, item => item.RowNumber == 8 && item.Reason.Contains("交易对方"));
        Assert.Same(result.Records[^1], issue.Record);
        Assert.True(issue.CanReview);
    }

    [Fact]
    public void Parse_FlagsDateThatBreaksDescendingBillOrder()
    {
        var tokens = new List<ScreenshotOcrToken>
        {
            new("全部账单", 100, 50, 160, 36),
            new("2026年7月", 100, 145, 180, 36),
            new("第一笔", 180, 300, 180, 42),
            new("7月24日 07:41", 180, 348, 230, 34),
            new("-110.00", 760, 300, 130, 42),
            new("第二笔", 180, 500, 180, 42),
            new("7月25日 14:44", 180, 548, 230, 34),
            new("-6.00", 760, 500, 130, 42)
        };

        var result = ScreenshotBillParser.Parse(tokens, 1000, 900, "日期异常.png", new DateTime(2026, 8, 25));

        Assert.Equal(2, result.Records.Count);
        Assert.False(result.Records[0].RequiresReview);
        Assert.True(result.Records[1].RequiresReview);
        var issue = Assert.Single(result.Issues, item => item.Record == result.Records[1]);
        Assert.Contains("倒序不一致", issue.Reason);
    }

    [Fact]
    public void Parse_RecognizesCiticRowsBeforeAlipayWordsInsideTransactions()
    {
        var tokens = new List<ScreenshotOcrToken>
        {
            new("交易明细", 280, 120, 180, 40), new("借记卡4054", 290, 260, 180, 36), new("本月", 60, 390, 80, 34),
            new("支付宝-东莞市海莫智选", 65, 520, 390, 42), new("2026-08-24 11:03:33", 65, 575, 330, 34), new("-¥159.00", 760, 520, 170, 42), new("余额965.68", 760, 575, 160, 34),
            new("拼多多支付-见喜眼镜屋", 65, 690, 390, 42), new("2026-08-24 23:41:52", 65, 745, 330, 34), new("-¥4.90", 780, 690, 150, 42), new("余额957.03", 760, 745, 160, 34)
        };

        var result = ScreenshotBillParser.Parse(tokens, 1000, 1000, "中信.png", new DateTime(2026, 8, 25));

        Assert.Equal(2, result.Records.Count);
        Assert.All(result.Records, row => Assert.StartsWith("中信银行截图", row.Source));
        Assert.Equal(new DateTime(2026, 8, 24, 11, 3, 33), result.Records[0].OccurredOn);
        Assert.Equal("支付宝-东莞市海莫智选", result.Records[0].Merchant);
    }

    [Fact]
    public void Parse_RecognizesIcbcDayTimeIncomeAndRepayment()
    {
        var tokens = new List<ScreenshotOcrToken>
        {
            new("查询明细", 370, 100, 190, 42), new("工银借记卡5436", 220, 270, 250, 38), new("人民币余额756.17", 220, 320, 260, 34), new("2026年08月", 50, 640, 180, 36),
            new("25", 40, 780, 55, 42), new("还款", 140, 780, 90, 42), new("美团支付-美团月付还款", 140, 835, 360, 36), new("工银借记卡5436 10:15:06", 140, 890, 390, 34), new("-1,330.79", 760, 780, 180, 44),
            new("15", 40, 1030, 55, 42), new("工资", 140, 1030, 90, 42), new("华夏航空科技（北京）有限公司", 140, 1085, 420, 36), new("工银借记卡5436 01:15:51", 140, 1140, 390, 34), new("+2,086.96", 760, 1030, 180, 44)
        };

        var result = ScreenshotBillParser.Parse(tokens, 1000, 1300, "工商.png", new DateTime(2026, 8, 25));

        Assert.Equal(2, result.Records.Count);
        Assert.Equal(new DateTime(2026, 8, 25, 10, 15, 6), result.Records[0].OccurredOn);
        Assert.Equal("转账", result.Records[0].Direction);
        Assert.Equal("美团支付-美团月付还款", result.Records[0].Merchant);
        Assert.Equal("收入", result.Records[1].Direction);
        Assert.Equal("工资收入", result.Records[1].Category);
    }

    [Fact]
    public void Parse_CorrectsCiticThreeMisreadAsFiveUsingAdjacentBalances()
    {
        var tokens = new List<ScreenshotOcrToken>
        {
            new("交易明细", 280, 120, 180, 40), new("借记卡4054", 290, 260, 180, 36), new("本月", 60, 390, 80, 34),
            new("支付宝-Sapphire Enter", 65, 520, 390, 42), new("2026-08-24 13:31:34", 65, 575, 330, 34), new("-¥5.75", 760, 520, 170, 42), new("余额961.93", 760, 575, 160, 34),
            new("支付宝-东莞市海莫智选", 65, 690, 390, 42), new("2026-08-24 11:03:33", 65, 745, 330, 34), new("-¥159.00", 760, 690, 170, 42), new("余额965.68", 760, 745, 160, 34)
        };

        var result = ScreenshotBillParser.Parse(tokens, 1000, 1000, "中信纠错.png", new DateTime(2026, 8, 25));

        Assert.Equal(3.75m, result.Records[0].Amount);
        Assert.True(result.Records[0].RequiresReview);
        Assert.Contains("从 ¥5.75 校正为 ¥3.75", result.Records[0].Note);
        Assert.Contains(result.Issues, issue => issue.Record == result.Records[0] && issue.Reason.Contains("余额自动校正"));
    }

    [Fact]
    public void Parse_DoesNotReplaceAmountWhenBalanceDifferenceIsNotTypicalDigitConfusion()
    {
        var tokens = new List<ScreenshotOcrToken>
        {
            new("交易明细", 280, 120, 180, 40), new("借记卡4054", 290, 260, 180, 36), new("本月", 60, 390, 80, 34),
            new("商户一", 65, 520, 220, 42), new("2026-08-24 13:31:34", 65, 575, 330, 34), new("-¥20.00", 760, 520, 170, 42), new("余额900.00", 760, 575, 160, 34),
            new("商户二", 65, 690, 220, 42), new("2026-08-24 11:03:33", 65, 745, 330, 34), new("-¥100.00", 760, 690, 170, 42), new("余额950.00", 760, 745, 160, 34)
        };

        var result = ScreenshotBillParser.Parse(tokens, 1000, 1000, "中信保守校验.png", new DateTime(2026, 8, 25));

        Assert.Equal(20m, result.Records[0].Amount);
        Assert.DoesNotContain("余额差额", result.Records[0].Note);
    }

    [Fact]
    public void Parse_UsesWordLevelAmountWhenIcbcMergedLineCannotBeAnAnchor()
    {
        var tokens = new List<ScreenshotOcrToken>
        {
            new("查询明细", 370, 100, 190, 42), new("工银借记卡5436", 220, 270, 250, 38), new("人民币余额756.17", 220, 320, 260, 34), new("2026年08月", 50, 640, 180, 36),
            new("25", 40, 780, 55, 42), new("还款-1,330.79", 140, 780, 800, 44), new("还款", 140, 780, 90, 42), new("-1,330.79", 760, 780, 180, 44),
            new("美团支付-美团月付还款", 140, 835, 360, 36), new("工银借记卡5436 10:15:06", 140, 890, 390, 34), new("10:15:06", 390, 890, 130, 34),
            new("15", 40, 1030, 55, 42), new("工资+2,086.96", 140, 1030, 800, 44), new("工资", 140, 1030, 90, 42), new("+2,086.96", 760, 1030, 180, 44),
            new("华夏航空科技（北京）有限公司", 140, 1085, 420, 36), new("工银借记卡5436 01:15:51", 140, 1140, 390, 34), new("01:15:51", 390, 1140, 130, 34)
        };

        var result = ScreenshotBillParser.Parse(tokens, 1000, 1300, "工商分词.png", new DateTime(2026, 8, 25));

        Assert.Equal(2, result.TotalRows);
        Assert.Equal(2, result.Records.Count);
        Assert.Equal("美团支付-美团月付还款", result.Records[0].Merchant);
        Assert.Equal(1330.79m, result.Records[0].Amount);
    }

    [Fact]
    public void Parse_UsesTrustedDigitCorrectionWithoutMarkingRecordAsUncertain()
    {
        var tokens = new List<ScreenshotOcrToken>
        {
            new("交易明细", 280, 120, 180, 40), new("借记卡4054", 290, 260, 180, 36),
            new("测试商户", 65, 500, 220, 42), new("2026-08-20 19:25:12", 65, 555, 330, 34),
            new("2026-08-20 19:23:12", 65, 555, 330, 34, "时间经固定区域多轮识别从 2026-08-20 19:25:12 校正为 2026-08-20 19:23:12", false),
            new("-¥11.90", 760, 500, 170, 42), new("余额1215.30", 760, 555, 160, 34)
        };

        var result = ScreenshotBillParser.Parse(tokens, 1000, 800, "中信时间纠错.png", new DateTime(2026, 8, 25));

        var record = Assert.Single(result.Records);
        Assert.Equal(new DateTime(2026, 8, 20, 19, 23, 12), record.OccurredOn);
        Assert.False(record.RequiresReview);
        Assert.Empty(result.Issues);
        Assert.Contains("时间经固定区域多轮识别", record.Note);
    }

    [Fact]
    public void Parse_CorrectsWechatDayFiveToThreeWhenBillOrderWouldOtherwiseReverse()
    {
        var tokens = new List<ScreenshotOcrToken>
        {
            new("全部账单", 50, 100, 160, 40), new("2026年7月", 50, 180, 180, 36),
            new("商户一", 180, 300, 180, 40), new("7月4日 00:02", 180, 350, 220, 34), new("-14.50", 820, 300, 130, 40),
            new("高德打车", 180, 500, 180, 40), new("7月5日 22:41", 180, 550, 220, 34), new("-32.00", 820, 500, 130, 40)
        };

        var result = ScreenshotBillParser.Parse(tokens, 1000, 800, "微信日期纠错.png", new DateTime(2026, 8, 26));

        Assert.Equal(new DateTime(2026, 7, 3, 22, 41, 0), result.Records[1].OccurredOn);
        Assert.False(result.Records[1].RequiresReview);
        Assert.Contains("账单倒序", result.Records[1].Note);
    }

    [Fact]
    public void Parse_PrefersTrustedWechatDateTokenOverRawOcrDate()
    {
        var tokens = new List<ScreenshotOcrToken>
        {
            new("全部账单", 50, 100, 160, 40), new("2026年7月", 50, 180, 180, 36),
            new("测试商户", 180, 300, 180, 40), new("7月5日 15:58", 180, 350, 220, 34),
            new("7月5日 13:38", 180, 350, 220, 34, "时间经数字专用识别从 7月5日 15:58 校正为 7月5日 13:38", false),
            new("-3.00", 820, 300, 130, 40)
        };

        var result = ScreenshotBillParser.Parse(tokens, 1000, 600, "微信时间纠错.png", new DateTime(2026, 8, 26));

        var record = Assert.Single(result.Records);
        Assert.Equal(new DateTime(2026, 7, 5, 13, 38, 0), record.OccurredOn);
        Assert.False(record.RequiresReview);
    }

    [Fact]
    public void Parse_AlipayMerchantBankNameDoesNotOverrideScreenshotPlatform()
    {
        var tokens = new List<ScreenshotOcrToken>
        {
            new("搜索交易记录", 100, 80, 220, 40), new("收支分析", 700, 180, 160, 36), new("8月", 50, 240, 80, 34),
            new("信用卡还款-中信银行", 180, 360, 360, 40), new("08-19 19:53", 180, 420, 200, 34), new("446.64", 820, 360, 130, 40)
        };

        var result = ScreenshotBillParser.Parse(tokens, 1000, 650, "支付宝银行商户.png", new DateTime(2026, 8, 26));

        var record = Assert.Single(result.Records);
        Assert.Contains("支付宝账单截图识别", record.Note);
        Assert.Equal(new DateTime(2026, 8, 19, 19, 53, 0), record.OccurredOn);
    }

    [Fact]
    public void Parse_AlipayAcceptsDotMisreadAsLeadingMinus()
    {
        var tokens = new List<ScreenshotOcrToken>
        {
            new("搜索交易记录", 100, 80, 220, 40), new("收支分析", 700, 180, 160, 36), new("8月", 50, 240, 80, 34),
            new("杭州深度求索", 180, 360, 260, 40), new("08-20 09:35", 180, 420, 200, 34), new("·10.00", 820, 360, 130, 40)
        };

        var result = ScreenshotBillParser.Parse(tokens, 1000, 650, "支付宝负号纠错.png", new DateTime(2026, 8, 26));

        var record = Assert.Single(result.Records);
        Assert.Equal(10m, record.Amount);
        Assert.Equal(new DateTime(2026, 8, 20, 9, 35, 0), record.OccurredOn);
    }
}
