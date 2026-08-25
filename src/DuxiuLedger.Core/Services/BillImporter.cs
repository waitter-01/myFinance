using System.Globalization;
using System.IO;
using System.Text;
using ClosedXML.Excel;
using DuxiuLedger.Desktop.Models;

namespace DuxiuLedger.Desktop.Services;

public sealed class BillImporter
{
    public IReadOnlyList<TransactionRecord> Read(string path)
        => Preview(path).Records;

    public ImportPreviewResult Preview(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".xlsx" or ".xlsm" ? ReadExcel(path) : ReadCsv(path);
    }
    private ImportPreviewResult ReadExcel(string path)
    {
        using var book = new XLWorkbook(path); var sheet = book.Worksheets.First();
        var rows = sheet.RowsUsed().Select(r => r.CellsUsed().Select(c => c.GetString().Trim()).ToList()).ToList();
        return ParseRows(rows, Path.GetFileName(path));
    }
    private ImportPreviewResult ReadCsv(string path)
    {
        var lines = File.ReadAllLines(path, Encoding.UTF8); var rows = lines.Select(ParseCsvLine).ToList();
        if (rows.Count == 0) throw new InvalidDataException("文件没有可读取的数据。");
        return ParseRows(rows, Path.GetFileName(path));
    }
    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>(); var current = new StringBuilder(); var quoted = false;
        foreach (var ch in line) { if (ch == '"') quoted = !quoted; else if (ch == ',' && !quoted) { result.Add(current.ToString().Trim()); current.Clear(); } else current.Append(ch); }
        result.Add(current.ToString().Trim()); return result;
    }
    private static ImportPreviewResult ParseRows(List<List<string>> rows, string source)
    {
        var headerIndex = rows.FindIndex(r => r.Any(v => Contains(v, "时间", "日期", "交易时间")) && r.Any(v => Contains(v, "金额", "收支金额", "金额(元)")));
        if (headerIndex < 0) throw new InvalidDataException("无法识别账单表头。请使用微信、支付宝官方导出的账单文件，或提供包含日期和金额列的表格。");
        var headers = rows[headerIndex]; int date = Find(headers, "时间", "日期", "交易时间"), amount = Find(headers, "金额", "收支金额", "金额(元)"), type = Find(headers, "收支", "交易类型", "类型"), merchant = Find(headers, "交易对方", "商户", "商品", "备注"), note = Find(headers, "备注", "商品说明", "描述"), category = Find(headers, "分类", "交易分类"), months = Find(headers, "订阅月数", "计费月数", "覆盖月数");
        var result = new List<TransactionRecord>(); var issues = new List<ImportIssue>();
        for (var index = headerIndex + 1; index < rows.Count; index++)
        {
            var values = rows[index];
            if (values.All(string.IsNullOrWhiteSpace)) continue;
            var raw = string.Join(" | ", values.Take(6));
            if (values.Count <= Math.Max(date, amount)) { issues.Add(new ImportIssue { Source = source, RowNumber = index + 1, Reason = "缺少日期或金额列", RawValue = raw }); continue; }
            if (!TryDate(values[date], out var when)) { issues.Add(new ImportIssue { Source = source, RowNumber = index + 1, Reason = "无法识别交易日期", RawValue = raw }); continue; }
            if (!TryAmount(values[amount], out var money) || money == 0) { issues.Add(new ImportIssue { Source = source, RowNumber = index + 1, Reason = "金额无效或为 0", RawValue = raw }); continue; }
            var kind = type >= 0 && values.Count > type ? values[type] : ""; var direction = MapTransactionType(kind); var merchantText = merchant >= 0 && values.Count > merchant ? values[merchant] : ""; var noteText = note >= 0 && values.Count > note ? values[note] : ""; var categoryText = category >= 0 && values.Count > category && !string.IsNullOrWhiteSpace(values[category]) ? values[category] : "未分类"; var subscriptionMonths = months >= 0 && values.Count > months && int.TryParse(values[months], out var parsedMonths) ? Math.Max(1, parsedMonths) : 1; var record = new TransactionRecord { OccurredOn = when, Direction = direction, Amount = Math.Abs(money), Category = categoryText, Merchant = merchantText, Note = noteText, Source = source, SubscriptionMonths = subscriptionMonths }; record.Fingerprint = TransactionFingerprint.Create(record); result.Add(record);
        }
        return new ImportPreviewResult { Source = source, TotalRows = Math.Max(0, rows.Count - headerIndex - 1), Records = result, Issues = issues };
    }
    private static string MapTransactionType(string value)
    {
        if (Contains(value, "退款", "退回")) return "退款";
        if (Contains(value, "报销")) return "报销";
        if (Contains(value, "转账", "转入", "转出")) return "转账";
        if (Contains(value, "收入", "入账", "收款")) return "收入";
        if (Contains(value, "支出", "付款")) return "支出";
        return "支出";
    }
    private static bool Contains(string value, params string[] keys) => keys.Any(k => value.Contains(k, StringComparison.OrdinalIgnoreCase));
    private static int Find(List<string> headers, params string[] keys) => headers.FindIndex(h => Contains(h, keys));
    private static bool TryDate(string value, out DateTime date) => DateTime.TryParse(value, CultureInfo.GetCultureInfo("zh-CN"), DateTimeStyles.AllowWhiteSpaces, out date) || DateTime.TryParse(value, out date);
    private static bool TryAmount(string value, out decimal amount) => decimal.TryParse(value.Replace("¥", "").Replace(",", "").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out amount);
}
