using DuxiuLedger.Desktop.Models;
using Microsoft.UI.Xaml.Controls;

namespace DuxiuLedger.WinUI;

public sealed partial class ManualEntryDialog : ContentDialog
{
    public TransactionRecord? Result { get; private set; }

    public ManualEntryDialog()
    {
        InitializeComponent();
        OccurredOnPicker.Date = DateTimeOffset.Now;
        DirectionBox.ItemsSource = new[] { "支出", "收入" };
        DirectionBox.SelectedIndex = 0;
        CategoryBox.Text = "未分类";
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (double.IsNaN(AmountBox.Value) || AmountBox.Value <= 0)
        {
            ValidationInfo.Message = "请输入大于 0 的金额。";
            ValidationInfo.IsOpen = true;
            args.Cancel = true;
            return;
        }
        var amount = Math.Round((decimal)AmountBox.Value, 2);
        if (amount != (decimal)AmountBox.Value)
        {
            ValidationInfo.Message = "金额最多保留两位小数。";
            ValidationInfo.IsOpen = true;
            args.Cancel = true;
            return;
        }
        var selectedDate = OccurredOnPicker.Date ?? DateTimeOffset.Now;
        Result = new TransactionRecord
        {
            OccurredOn = selectedDate.LocalDateTime.Date.Add(DateTime.Now.TimeOfDay),
            Direction = DirectionBox.SelectedItem?.ToString() ?? "支出",
            Amount = amount,
            Category = string.IsNullOrWhiteSpace(CategoryBox.Text) ? "未分类" : CategoryBox.Text.Trim(),
            Merchant = MerchantBox.Text.Trim(),
            Note = NoteBox.Text.Trim(),
            Source = "手动录入",
            Fingerprint = $"MANUAL-{Guid.NewGuid():N}"
        };
    }
}
