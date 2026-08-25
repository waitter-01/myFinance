using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ClosedXML.Excel;
using DuxiuLedger.Desktop.Models;

namespace DuxiuLedger.Desktop.Services;

public sealed class BillImporter
{
    public IReadOnlyList<TransactionRecord> Read(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".xlsx" or ".xlsm" ? ReadExcel(path) : ReadCsv(path);
    }
    private IReadOnlyList<TransactionRecord> ReadExcel(string path)
    {
        using var book = new XLWorkbook(path); var sheet = book.Worksheets.First();
        var rows = sheet.RowsUsed().Select(r => r.CellsUsed().Select(c => c.GetString().Trim()).ToList()).ToList();
        return ParseRows(rows, Path.GetFileName(path));
    }
    private IReadOnlyList<TransactionRecord> ReadCsv(string path)
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
    private static IReadOnlyList<TransactionRecord> ParseRows(List<List<string>> rows, string source)
    {
        var headerIndex = rows.FindIndex(r => r.Any(v => Contains(v, "时间", "日期", "交易时间")) && r.Any(v => Contains(v, "金额", "收支金额", "金额(元)")));
        if (headerIndex < 0) throw new InvalidDataException("无法识别账单表头。请使用微信、支付宝官方导出的账单文件，或提供包含日期和金额列的表格。");
        var headers = rows[headerIndex]; int date = Find(headers, "时间", "日期", "交易时间"), amount = Find(headers, "金额", "收支金额", "金额(元)"), type = Find(headers, "收支", "交易类型", "类型"), merchant = Find(headers, "交易对方", "商户", "商品", "备注"), note = Find(headers, "备注", "商品说明", "描述");
        var result = new List<TransactionRecord>();
        foreach (var values in rows.Skip(headerIndex + 1)) { if (values.Count <= Math.Max(date, amount)) continue; if (!TryDate(values[date], out var when) || !TryAmount(values[amount], out var money) || money == 0) continue; var kind = type >= 0 && values.Count > type ? values[type] : ""; var direction = MapTransactionType(kind); var merchantText = merchant >= 0 && values.Count > merchant ? values[merchant] : ""; var noteText = note >= 0 && values.Count > note ? values[note] : ""; var fingerprint = Hash($"{when:O}|{direction}|{money.ToString(CultureInfo.InvariantCulture)}|{merchantText}|{noteText}"); result.Add(new TransactionRecord { OccurredOn = when, Direction = direction, Amount = Math.Abs(money), Merchant = merchantText, Note = noteText, Source = source, Fingerprint = fingerprint }); }
        return result;
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
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
