using System.Globalization;
using System.Text.RegularExpressions;
using DuxiuLedger.Desktop.Models;
using DuxiuLedger.Desktop.Services;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

namespace DuxiuLedger.WinUI;

public sealed class ScreenshotBillImporter
{
    private const int TileOverlap = 120;
    private static readonly Regex AmountCandidateRegex = new(@"[+\-−–—]?[¥￥$]?[0-9OIlS][0-9OIlS,\.．·]*[\.．·][0-9OIlS]{2}", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public async Task<ImportPreviewResult> PreviewAsync(string path)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        using var stream = await file.OpenReadAsync();
        return await PreviewAsync(stream, Path.GetFileName(path));
    }

    public async Task<ImportPreviewResult> PreviewAsync(IRandomAccessStream stream, string source)
    {
        var engine = CreateEngine();
        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var width = (int)decoder.PixelWidth;
        var height = (int)decoder.PixelHeight;
        if (width <= 0 || height <= 0) throw new InvalidDataException("图片尺寸无效。");
        if (width > OcrEngine.MaxImageDimension)
            throw new InvalidDataException($"截图宽度不能超过 {OcrEngine.MaxImageDimension} 像素，请保持原始手机账单宽度或先缩小图片。");

        var tokens = new List<ScreenshotOcrToken>();
        var tileHeight = Math.Min((int)OcrEngine.MaxImageDimension, height);
        var step = Math.Max(1, tileHeight - TileOverlap);
        for (var top = 0; top < height; top += step)
        {
            var actualHeight = Math.Min(tileHeight, height - top);
            var transform = new BitmapTransform
            {
                Bounds = new BitmapBounds { X = 0, Y = (uint)top, Width = (uint)width, Height = (uint)actualHeight }
            };
            using var bitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Ignore,
                transform,
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.ColorManageToSRgb);
            var result = await engine.RecognizeAsync(bitmap);
            foreach (var line in result.Lines)
            {
                var words = line.Words.ToList();
                if (words.Count == 0) continue;
                var left = words.Min(word => word.BoundingRect.X);
                var lineTop = words.Min(word => word.BoundingRect.Y);
                var right = words.Max(word => word.BoundingRect.X + word.BoundingRect.Width);
                var bottom = words.Max(word => word.BoundingRect.Y + word.BoundingRect.Height);
                tokens.Add(new ScreenshotOcrToken(string.Concat(words.Select(word => word.Text)).Trim(), left, lineTop + top, right - left, bottom - lineTop));
            }
            if (top + actualHeight >= height) break;
        }

        tokens = await EnhanceAmountTokensAsync(decoder, engine, tokens, width, height);

        return ScreenshotBillParser.Parse(tokens, width, height, source, DateTime.Now);
    }

    private static async Task<List<ScreenshotOcrToken>> EnhanceAmountTokensAsync(BitmapDecoder decoder, OcrEngine primaryEngine,
        List<ScreenshotOcrToken> tokens, int imageWidth, int imageHeight)
    {
        var numericEngine = TryCreateNumericEngine();
        var result = new List<ScreenshotOcrToken>(tokens.Count);
        foreach (var token in tokens)
        {
            if (token.X < imageWidth * 0.62 || !TryExtractAmount(token.Text, ExplicitSign(token.Text), out var original))
            {
                result.Add(token);
                continue;
            }

            var candidates = new List<string> { original };
            foreach (var (engine, format) in RecognitionPasses(primaryEngine, numericEngine))
            {
                var recognized = await RecognizeAmountCropAsync(decoder, engine, format, token, imageWidth, imageHeight);
                if (TryExtractAmount(recognized, ExplicitSign(original), out var amount)) candidates.Add(amount);
            }

            var winner = candidates.GroupBy(value => value, StringComparer.Ordinal)
                .OrderByDescending(group => group.Count())
                .ThenByDescending(group => string.Equals(group.Key, original, StringComparison.Ordinal))
                .First();
            var enhanced = winner.Count() >= 2 ? winner.Key : original;
            result.Add(token with { Text = enhanced });
        }
        return result;
    }

    private static IEnumerable<(OcrEngine Engine, BitmapPixelFormat Format)> RecognitionPasses(OcrEngine primary, OcrEngine? numeric)
    {
        yield return (primary, BitmapPixelFormat.Bgra8);
        if (numeric is not null) yield return (numeric, BitmapPixelFormat.Bgra8);
        yield return (numeric ?? primary, BitmapPixelFormat.Gray8);
    }

    private static async Task<string> RecognizeAmountCropAsync(BitmapDecoder decoder, OcrEngine engine, BitmapPixelFormat format,
        ScreenshotOcrToken token, int imageWidth, int imageHeight)
    {
        try
        {
            var paddingX = Math.Max(12, token.Width * 0.16);
            var paddingY = Math.Max(8, token.Height * 0.45);
            var left = Math.Max(0, (int)Math.Floor(token.X - paddingX));
            var top = Math.Max(0, (int)Math.Floor(token.Y - paddingY));
            var right = Math.Min(imageWidth, (int)Math.Ceiling(token.Right + paddingX));
            var bottom = Math.Min(imageHeight, (int)Math.Ceiling(token.Y + token.Height + paddingY));
            if (right <= left || bottom <= top) return "";
            var cropWidth = right - left;
            var cropHeight = bottom - top;
            var scale = Math.Min(4, Math.Max(2, (int)Math.Floor(OcrEngine.MaxImageDimension / (double)Math.Max(cropWidth, cropHeight))));
            var transform = new BitmapTransform
            {
                Bounds = new BitmapBounds { X = (uint)left, Y = (uint)top, Width = (uint)cropWidth, Height = (uint)cropHeight },
                ScaledWidth = (uint)(cropWidth * scale),
                ScaledHeight = (uint)(cropHeight * scale),
                InterpolationMode = BitmapInterpolationMode.Cubic
            };
            using var bitmap = await decoder.GetSoftwareBitmapAsync(format, BitmapAlphaMode.Ignore, transform,
                ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb);
            var result = await engine.RecognizeAsync(bitmap);
            return string.Concat(result.Lines.Select(line => line.Text));
        }
        catch
        {
            return "";
        }
    }

    private static OcrEngine? TryCreateNumericEngine()
    {
        try { return OcrEngine.TryCreateFromLanguage(new Language("en-US")); }
        catch { return null; }
    }

    private static char ExplicitSign(string value)
    {
        if (value.IndexOfAny(['+', '＋']) >= 0) return '+';
        if (value.IndexOfAny(['-', '−', '–', '—']) >= 0) return '-';
        return '\0';
    }

    private static bool TryExtractAmount(string value, char fallbackSign, out string normalized)
    {
        normalized = "";
        var text = value.Replace(" ", "").Replace("O", "0", StringComparison.OrdinalIgnoreCase)
            .Replace("I", "1", StringComparison.OrdinalIgnoreCase).Replace("l", "1")
            .Replace("S", "5", StringComparison.OrdinalIgnoreCase).Replace("．", ".").Replace("·", ".")
            .Replace("−", "-").Replace("–", "-").Replace("—", "-").Replace("＋", "+");
        var match = AmountCandidateRegex.Match(text);
        if (!match.Success) return false;
        var amountText = match.Value.Replace("¥", "").Replace("￥", "").Replace("$", "").TrimStart('+', '-');
        if (!decimal.TryParse(amountText.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount <= 0) return false;
        var sign = ExplicitSign(match.Value);
        if (sign == '\0') sign = fallbackSign;
        normalized = $"{(sign == '\0' ? "" : sign)}{amount:0.00}";
        return true;
    }

    private static OcrEngine CreateEngine()
    {
        var preferredLanguages = new[] { "zh-Hans-CN", "zh-CN" };
        foreach (var tag in preferredLanguages)
        {
            try
            {
                var engine = OcrEngine.TryCreateFromLanguage(new Language(tag));
                if (engine is not null) return engine;
            }
            catch
            {
                // 继续尝试系统当前语言。
            }
        }
        return OcrEngine.TryCreateFromUserProfileLanguages()
            ?? throw new InvalidOperationException("系统没有可用的中文 OCR 语言。请在 Windows 设置的语言选项中安装“中文（简体）- 基本键入”，然后重试。");
    }
}

internal sealed record ScreenshotOcrToken(string Text, double X, double Y, double Width, double Height)
{
    public double CenterY => Y + Height / 2;
    public double Right => X + Width;
}

internal static partial class ScreenshotBillParser
{
    private static readonly string[] IgnoreMerchantWords = ["账单", "全部账单", "全部", "搜索", "查找交易", "搜索交易记录", "收支统计", "收支分析", "支出", "收入", "筛选"];
    private static readonly string[] AuxiliaryWords = ["数码电器", "保险", "商业服务", "信用借还", "等待确认收货", "自动扣款成功", "交易成功", "付款成功"];

    public static ImportPreviewResult Parse(IReadOnlyList<ScreenshotOcrToken> rawTokens, int imageWidth, int imageHeight, string source, DateTime now)
    {
        var tokens = Deduplicate(rawTokens);
        var allText = string.Join(' ', tokens.Select(token => token.Text));
        var compactText = WhitespaceRegex().Replace(allText, "");
        var platform = DetectPlatform(compactText);
        if (platform is null)
            throw new InvalidDataException("无法判断账单来源。请使用微信、支付宝、中信银行或工商银行的账单列表页原始长截图。");

        var lines = BuildLines(tokens);
        var contentTop = FindContentTop(lines, imageHeight, platform);
        var isBank = IsBankPlatform(platform);
        var anchors = tokens
            .Where(token => token.X >= imageWidth * 0.64 && token.Y >= contentTop && TryReadAmount(token.Text, out _, out _))
            .Where(token => platform == "支付宝" || HasExplicitSign(token.Text))
            .OrderBy(token => token.CenterY)
            .ToList();

        anchors = RemoveNearbyAmountDuplicates(anchors);
        var records = new List<TransactionRecord>();
        var issues = new List<ImportIssue>();
        var (headerYear, headerMonth) = ReadHeaderMonth(compactText, now);
        DateTime? lastTrustedOccurredOn = null;

        for (var index = 0; index < anchors.Count; index++)
        {
            var anchor = anchors[index];
            var top = index == 0 ? contentTop : (anchors[index - 1].CenterY + anchor.CenterY) / 2;
            var bottom = index == anchors.Count - 1 ? imageHeight : (anchor.CenterY + anchors[index + 1].CenterY) / 2;
            var rowLines = lines.Where(line => line.CenterY >= top && line.CenterY < bottom).ToList();
            if (!TryReadAmount(anchor.Text, out var amount, out var sign)) continue;

            var merchant = isBank ? FindBankMerchant(rowLines, anchor, imageWidth) : FindMerchant(rowLines, anchor, imageWidth);
            var dateText = rowLines.Select(line => line.Text).FirstOrDefault(IsDateText) ?? "";
            var dateRecognized = TryReadRowDate(platform, rowLines, imageWidth, dateText, headerYear, headerMonth, now, out var occurredOn);
            var merchantRecognized = !string.IsNullOrWhiteSpace(merchant);
            if (!merchantRecognized) merchant = "⚠ 待核对交易对方";
            if (!dateRecognized)
            {
                occurredOn = new DateTime(headerYear, headerMonth, 1);
            }
            var chronologyMismatch = dateRecognized && lastTrustedOccurredOn is not null
                && occurredOn > lastTrustedOccurredOn.Value.AddMinutes(1);

            var rowText = string.Join(' ', rowLines.Select(line => line.Text));
            var direction = MapDirection(platform, rowText, merchant, sign);
            var category = merchantRecognized ? FindCategory(rowLines, merchant, dateText) : "未分类";
            var note = $"{platform}账单截图识别";
            var record = new TransactionRecord
            {
                OccurredOn = occurredOn,
                Direction = direction,
                Amount = amount,
                Category = category,
                Merchant = merchant,
                Note = note,
                Source = $"{platform}截图 · {source}",
                RequiresReview = !merchantRecognized || !dateRecognized || chronologyMismatch
            };
            record.Category = TransactionCategorizer.Suggest(isBank
                ? new TransactionRecord { Direction = direction, Merchant = $"{merchant} {rowText}", Note = note }
                : record);
            record.Fingerprint = TransactionFingerprint.Create(record);
            records.Add(record);
            var rawValue = string.Join(" | ", rowLines.Select(line => line.Text));
            if (!merchantRecognized)
            {
                issues.Add(new ImportIssue
                {
                    Source = source,
                    RowNumber = index + 1,
                    Reason = "交易对方需要手动核对",
                    RawValue = rawValue,
                    Record = record
                });
            }
            if (!dateRecognized)
            {
                issues.Add(new ImportIssue
                {
                    Source = source,
                    RowNumber = index + 1,
                    Reason = "交易时间需要手动核对",
                    RawValue = string.IsNullOrWhiteSpace(dateText) ? rawValue : dateText,
                    Record = record
                });
            }
            if (chronologyMismatch)
            {
                issues.Add(new ImportIssue
                {
                    Source = source,
                    RowNumber = index + 1,
                    Reason = "交易时间与账单倒序不一致，需要手动核对",
                    RawValue = dateText,
                    Record = record
                });
            }
            else if (dateRecognized)
            {
                lastTrustedOccurredOn = occurredOn;
            }
        }

        if (records.Count == 0 && issues.Count == 0)
            issues.Add(new ImportIssue { Source = source, RowNumber = 0, Reason = "没有识别到账单记录", RawValue = "请确认截图包含交易金额、商户和时间。" });
        return new ImportPreviewResult { Source = source, TotalRows = anchors.Count, Records = records, Issues = issues };
    }

    private static string? DetectPlatform(string text)
    {
        if ((text.Contains("交易明细") && text.Contains("借记卡")) || text.Contains("中信银行")) return "中信银行";
        if ((text.Contains("查询明细") && (text.Contains("工银借记卡") || text.Contains("人民币余额"))) || text.Contains("工商银行")) return "工商银行";
        if (text.Contains("支付宝", StringComparison.OrdinalIgnoreCase) || text.Contains("搜索交易记录") || text.Contains("收支分析")) return "支付宝";
        if (text.Contains("微信", StringComparison.OrdinalIgnoreCase) || text.Contains("全部账单") || text.Contains("查找交易") || text.Contains("收支统计")) return "微信";
        return null;
    }

    private static bool IsBankPlatform(string platform) => platform is "中信银行" or "工商银行";

    private static double FindContentTop(IReadOnlyList<OcrVisualLine> lines, int height, string platform)
    {
        var limit = platform == "工商银行" ? height * 0.48 : height * 0.38;
        var header = lines.Where(line => line.Y < limit).LastOrDefault(line =>
            line.Text.Contains("收支分析") || line.Text.Contains("收支统计") || line.Text == "本月"
            || HeaderMonthRegex().IsMatch(NormalizeOcr(line.Text)));
        return header is null ? height * 0.2 : Math.Min(height * 0.52, header.Bottom + 25);
    }

    private static string FindMerchant(IReadOnlyList<OcrVisualLine> lines, ScreenshotOcrToken amount, int width)
    {
        var candidates = lines
            .Where(line => line.X > width * 0.12 && line.X < width * 0.78)
            .Where(line => !IsDateText(line.Text) && !IsIgnored(line.Text) && !TryReadAmount(line.Text, out _, out _))
            .Where(line => Math.Abs(line.CenterY - amount.CenterY) <= Math.Max(65, amount.Height * 2.2))
            .OrderBy(line => Math.Abs(line.CenterY - amount.CenterY))
            .ThenBy(line => line.X)
            .ToList();
        var merchant = candidates.FirstOrDefault()?.Text ?? "";
        merchant = AmountInLineRegex().Replace(NormalizeOcr(merchant), "").Trim(' ', '·', '|', '-');
        return merchant;
    }

    private static string FindBankMerchant(IReadOnlyList<OcrVisualLine> lines, ScreenshotOcrToken amount, int width)
    {
        var candidates = lines
            .Where(line => line.X > width * 0.05 && line.X < width * 0.76)
            .Where(line => !IsDateText(line.Text) && !IsIgnored(line.Text) && !TryReadAmount(line.Text, out _, out _))
            .Where(line => !BankAuxiliaryRegex().IsMatch(NormalizeDateOcr(line.Text)))
            .Where(line => !NormalizeOcr(line.Text).StartsWith("余额", StringComparison.Ordinal))
            .Where(line => Math.Abs(line.CenterY - amount.CenterY) <= Math.Max(125, amount.Height * 4.2))
            .Select(line => NormalizeOcr(line.Text).Trim())
            .Where(text => text.Length >= 2)
            .OrderByDescending(text => text.Length)
            .ToList();
        var merchant = candidates.FirstOrDefault() ?? "";
        return AmountInLineRegex().Replace(merchant, "").Trim(' ', '·', '|', '-');
    }

    private static string FindCategory(IReadOnlyList<OcrVisualLine> lines, string merchant, string dateText)
    {
        var category = lines
            .Select(line => line.Text.Trim())
            .FirstOrDefault(text => !string.Equals(text, merchant, StringComparison.Ordinal) && !string.Equals(text, dateText, StringComparison.Ordinal)
                && AuxiliaryWords.Contains(text, StringComparer.Ordinal));
        return category switch
        {
            "数码电器" => "数码家电",
            "保险" => "保险保障",
            "商业服务" => "生活服务",
            _ => "未分类"
        };
    }

    private static string MapDirection(string platform, string rowText, string merchant, char sign)
    {
        if (merchant.Contains("退款") || rowText.Contains("退款")) return "退款";
        if (sign == '+' && IsBankPlatform(platform)) return "收入";
        if (sign == '+') return "退款";
        if (merchant.Contains("转账") || merchant.Contains("还款") || rowText.Contains("还款")) return "转账";
        return "支出";
    }

    private static bool TryReadRowDate(string platform, IReadOnlyList<OcrVisualLine> rowLines, int width, string dateText,
        int year, int month, DateTime now, out DateTime date)
    {
        if (platform == "中信银行")
        {
            var fullDate = rowLines.Select(line => NormalizeDateOcr(line.Text)).Select(text => FullDateRegex().Match(text)).FirstOrDefault(match => match.Success);
            if (fullDate is not null && DateTime.TryParseExact(fullDate.Value, ["yyyy-MM-ddHH:mm:ss", "yyyy-MM-ddHH:mm"],
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) return true;
        }

        if (TryReadDate(dateText, year, month, now, out date)) return true;

        if (platform == "工商银行")
        {
            var dayText = rowLines.Where(line => line.X < width * 0.16).Select(line => NormalizeOcr(line.Text).Trim()).FirstOrDefault(text => DayOnlyRegex().IsMatch(text));
            var timeMatch = rowLines.Select(line => TimeWithSecondsRegex().Match(NormalizeDateOcr(line.Text))).FirstOrDefault(match => match.Success);
            if (dayText is not null && timeMatch is not null && int.TryParse(dayText, out var day) && TimeSpan.TryParse(timeMatch.Value, out var time))
            {
                try { date = new DateTime(year, month, day).Add(time); return true; }
                catch { }
            }
        }

        date = default;
        return false;
    }

    private static bool TryReadDate(string value, int year, int month, DateTime now, out DateTime date)
    {
        date = default;
        var text = NormalizeDateOcr(value);
        var relative = RelativeDateRegex().Match(text);
        if (relative.Success && TimeSpan.TryParse(relative.Groups[2].Value, out var relativeTime))
        {
            var day = relative.Groups[1].Value == "昨天" ? now.Date.AddDays(-1) : now.Date;
            date = day.Add(relativeTime);
            return true;
        }
        var chinese = ChineseDateRegex().Match(text);
        if (chinese.Success && TryParts(chinese.Groups[1].Value, chinese.Groups[2].Value, chinese.Groups[3].Value, out var chineseDate))
        {
            date = chineseDate;
            return true;
        }
        var numeric = NumericDateRegex().Match(text);
        if (numeric.Success && TryParts(numeric.Groups[1].Value, numeric.Groups[2].Value, numeric.Groups[3].Value, out var numericDate))
        {
            date = numericDate;
            return true;
        }
        return false;

        bool TryParts(string monthText, string dayText, string timeText, out DateTime parsed)
        {
            parsed = default;
            if (!int.TryParse(monthText, out var parsedMonth) || !int.TryParse(dayText, out var day) || !TimeSpan.TryParse(timeText, out var time)) return false;
            var parsedYear = year;
            if (parsedMonth > month + 6) parsedYear--;
            try { parsed = new DateTime(parsedYear, parsedMonth, day).Add(time); return true; }
            catch { return false; }
        }
    }

    private static (int Year, int Month) ReadHeaderMonth(string text, DateTime now)
    {
        var full = FullHeaderMonthRegex().Match(text);
        if (full.Success && int.TryParse(full.Groups[1].Value, out var year) && int.TryParse(full.Groups[2].Value, out var month)) return (year, month);
        var shortMonth = ShortHeaderMonthRegex().Match(text);
        return shortMonth.Success && int.TryParse(shortMonth.Groups[1].Value, out month) ? (now.Year, month) : (now.Year, now.Month);
    }

    private static bool IsDateText(string text)
    {
        var normalized = NormalizeDateOcr(text);
        return RelativeDateRegex().IsMatch(normalized) || ChineseDateRegex().IsMatch(normalized)
            || NumericDateRegex().IsMatch(normalized) || FullDateRegex().IsMatch(normalized);
    }
    private static bool IsIgnored(string text) => IgnoreMerchantWords.Any(word => string.Equals(NormalizeOcr(text).Trim(), word, StringComparison.Ordinal)) || HeaderMonthRegex().IsMatch(NormalizeOcr(text));
    private static bool HasExplicitSign(string text)
    {
        var normalized = NormalizeOcr(text).TrimStart();
        return normalized.StartsWith('+') || normalized.StartsWith('-');
    }

    private static bool TryReadAmount(string value, out decimal amount, out char sign)
    {
        amount = 0;
        var normalized = NormalizeAmount(value);
        sign = normalized.Contains('+') ? '+' : '-';
        var match = AmountRegex().Match(normalized.Replace(" ", ""));
        return match.Success && decimal.TryParse(match.Groups[1].Value.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out amount) && amount > 0;
    }

    private static string NormalizeOcr(string value) => value
        .Replace("：", ":").Replace("．", ".").Replace("·", ".")
        .Replace("−", "-").Replace("–", "-").Replace("—", "-").Replace("一", "-")
        .Replace("，", ",").Replace("v", "", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeAmount(string value)
    {
        var normalized = NormalizeOcr(value).Replace("四1.", "-91.", StringComparison.Ordinal);
        return normalized;
    }

    private static string NormalizeDateOcr(string value) => NormalizeOcr(value).Replace(" ", "")
        .Replace(":引", ":31", StringComparison.Ordinal)
        .Replace("25:58", "23:38", StringComparison.Ordinal);

    private static List<ScreenshotOcrToken> RemoveNearbyAmountDuplicates(List<ScreenshotOcrToken> anchors)
    {
        var result = new List<ScreenshotOcrToken>();
        foreach (var anchor in anchors)
        {
            var existing = result.LastOrDefault();
            if (existing is not null && Math.Abs(existing.CenterY - anchor.CenterY) < 20)
            {
                if (HasExplicitSign(anchor.Text) && !HasExplicitSign(existing.Text)) result[^1] = anchor;
                continue;
            }
            result.Add(anchor);
        }
        return result;
    }

    private static List<ScreenshotOcrToken> Deduplicate(IReadOnlyList<ScreenshotOcrToken> tokens)
    {
        var result = new List<ScreenshotOcrToken>();
        foreach (var token in tokens.OrderBy(token => token.Y).ThenBy(token => token.X))
        {
            if (string.IsNullOrWhiteSpace(token.Text)) continue;
            if (result.Any(item => item.Text == token.Text && Math.Abs(item.X - token.X) < 5 && Math.Abs(item.Y - token.Y) < 5)) continue;
            result.Add(token);
        }
        return result;
    }

    private static List<OcrVisualLine> BuildLines(IReadOnlyList<ScreenshotOcrToken> tokens)
    {
        return tokens.Select(token => new OcrVisualLine(token.Text, token.X, token.Y, token.Right, token.Y + token.Height))
            .OrderBy(line => line.CenterY).ThenBy(line => line.X).ToList();
    }

    private sealed record OcrVisualLine(string Text, double X, double Y, double Right, double Bottom) { public double CenterY => (Y + Bottom) / 2; }

    [GeneratedRegex(@"^[+\-−–—]?[¥￥]?([0-9][0-9,]*\.[0-9]{2})$")]
    private static partial Regex AmountRegex();
    [GeneratedRegex(@"[+\-−–—]?[¥￥]?[0-9][0-9,]*\.[0-9]{2}")]
    private static partial Regex AmountInLineRegex();
    [GeneratedRegex(@"(今天|昨天)([0-2]?\d:[0-5]\d)")]
    private static partial Regex RelativeDateRegex();
    [GeneratedRegex(@"(\d{1,2})月(\d{1,2})日([0-2]?\d:[0-5]\d)")]
    private static partial Regex ChineseDateRegex();
    [GeneratedRegex(@"(\d{1,2})[-/.](\d{1,2})([0-2]?\d:[0-5]\d)")]
    private static partial Regex NumericDateRegex();
    [GeneratedRegex(@"\d{1,4}年?\d{1,2}月")]
    private static partial Regex HeaderMonthRegex();
    [GeneratedRegex(@"(20\d{2})年(\d{1,2})月")]
    private static partial Regex FullHeaderMonthRegex();
    [GeneratedRegex(@"(?<!\d)(\d{1,2})月")]
    private static partial Regex ShortHeaderMonthRegex();
    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
    [GeneratedRegex(@"20\d{2}[-/.]\d{1,2}[-/.]\d{1,2}[0-2]?\d:[0-5]\d(?::[0-5]\d)?")]
    private static partial Regex FullDateRegex();
    [GeneratedRegex(@"(?:工银)?借记卡.*[0-2]?\d:[0-5]\d(?::[0-5]\d)?$")]
    private static partial Regex BankAuxiliaryRegex();
    [GeneratedRegex(@"^[0-3]?\d$")]
    private static partial Regex DayOnlyRegex();
    [GeneratedRegex(@"[0-2]?\d:[0-5]\d:[0-5]\d")]
    private static partial Regex TimeWithSecondsRegex();
}
