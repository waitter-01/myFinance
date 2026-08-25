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
}
