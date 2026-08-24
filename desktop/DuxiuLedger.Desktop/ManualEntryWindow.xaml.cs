using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using DuxiuLedger.Desktop.Models;

namespace DuxiuLedger.Desktop;

public partial class ManualEntryWindow : Window
{
    public TransactionRecord? Result { get; private set; }

    public ManualEntryWindow()
    {
        InitializeComponent();
        OccurredOnPicker.SelectedDate = DateTime.Today;
        AmountBox.Focus();
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = "";
        if (OccurredOnPicker.SelectedDate is not DateTime occurredOn)
        {
            ErrorText.Text = "请选择交易日期。";
            return;
        }

        var amountText = AmountBox.Text.Trim();
        var parsed = decimal.TryParse(amountText, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount)
            || decimal.TryParse(amountText, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
        if (!parsed || amount <= 0 || decimal.Round(amount, 2) != amount)
        {
            ErrorText.Text = "金额必须大于 0，且最多保留两位小数。";
            return;
        }

        var direction = (DirectionBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "支出";
        var category = CategoryBox.Text.Trim();
        Result = new TransactionRecord
        {
            OccurredOn = occurredOn,
            Direction = direction,
            Amount = amount,
            Category = string.IsNullOrWhiteSpace(category) ? "未分类" : category,
            Merchant = MerchantBox.Text.Trim(),
            Note = NoteBox.Text.Trim(),
            Source = "手动录入",
            Fingerprint = $"MANUAL-{Guid.NewGuid():N}"
        };
        DialogResult = true;
    }

    private void CancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
