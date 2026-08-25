using DuxiuLedger.Desktop.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace DuxiuLedger.WinUI;

public sealed class TransactionFilterOption : INotifyPropertyChanged
{
    private bool _isSelected;
    public string Key { get; init; } = "";
    public string Display { get; init; } = "";
    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected == value) return; _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class TransactionFilterChip
{
    public string Key { get; init; } = "";
    public string Label { get; init; } = "";
}

public sealed class TransactionDateGroup : ObservableCollection<TransactionRecord>
{
    public string DateLabel { get; }
    public string Summary { get; }

    public TransactionDateGroup(DateTime date, IEnumerable<TransactionRecord> rows) : base(rows)
    {
        DateLabel = date.Date == DateTime.Today ? "今天"
            : date.Date == DateTime.Today.AddDays(-1) ? "昨天"
            : date.Year == DateTime.Today.Year ? date.ToString("M月d日 dddd") : date.ToString("yyyy年M月d日 dddd");
        var expense = this.Where(row => row.Direction == "支出").Sum(row => row.Amount);
        var refunds = this.Where(row => row.Direction is "退款" or "报销").Sum(row => row.Amount);
        var income = this.Where(row => row.Direction == "收入").Sum(row => row.Amount);
        var parts = new List<string>();
        if (expense > 0 || refunds > 0) parts.Add($"净支出 ¥{expense - refunds:N2}");
        if (income > 0) parts.Add($"收入 ¥{income:N2}");
        parts.Add($"{Count} 笔");
        Summary = string.Join(" · ", parts);
    }

    public TransactionDateGroup(string label, IEnumerable<TransactionRecord> rows) : base(rows)
    {
        DateLabel = label;
        Summary = $"{Count} 笔";
    }
}
