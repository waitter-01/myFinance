using DuxiuLedger.Desktop.Models;
using Microsoft.UI.Xaml.Controls;

namespace DuxiuLedger.WinUI;

public sealed partial class BudgetDialog : ContentDialog
{
    public BudgetRecord? Result { get; private set; }

    public BudgetDialog(IReadOnlyList<CategoryRecord> categories, BudgetRecord? budget = null)
    {
        InitializeComponent();
        CategoryBox.ItemsSource = categories.Where(item => item.IsActive && item.Type is "支出" or "通用").Select(item => item.Name).ToList();
        MonthPicker.Date = budget is null ? DateTimeOffset.Now : new DateTimeOffset(DateTime.Parse(budget.Month + "-01"));
        CategoryBox.Text = budget?.Category ?? "日常餐饮";
        AmountBox.Value = budget is null ? 500 : (double)budget.Amount;
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var month = MonthPicker.Date?.ToString("yyyy-MM") ?? "";
        var category = CategoryBox.Text.Trim();
        if (month.Length != 7 || string.IsNullOrWhiteSpace(category) || double.IsNaN(AmountBox.Value) || AmountBox.Value <= 0)
        {
            args.Cancel = true;
            return;
        }
        Result = new BudgetRecord { Month = month, Category = category, Amount = (decimal)AmountBox.Value };
    }
}
