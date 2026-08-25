using DuxiuLedger.Desktop.Models;
using Microsoft.UI.Xaml.Controls;

namespace DuxiuLedger.WinUI;

public sealed partial class CategoryTransactionsDialog : ContentDialog
{
    public IReadOnlyList<TransactionRecord> Rows { get; }

    public CategoryTransactionsDialog(string category, IReadOnlyList<TransactionRecord> rows, string periodLabel)
    {
        Rows = rows;
        InitializeComponent();
        Title = $"{category} · 流水明细";
        SummaryText.Text = $"{periodLabel} · {rows.Count} 笔 · 合计 ¥{rows.Sum(row => row.Amount):N2}";
    }
}
