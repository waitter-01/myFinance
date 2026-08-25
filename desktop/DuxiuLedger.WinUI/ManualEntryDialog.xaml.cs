using DuxiuLedger.Desktop.Models;
using Microsoft.UI.Xaml.Controls;

namespace DuxiuLedger.WinUI;

public sealed partial class ManualEntryDialog : ContentDialog
{
    public TransactionRecord? Result { get; private set; }

    public ManualEntryDialog(IReadOnlyList<AccountRecord>? accounts = null)
    {
        InitializeComponent();
        OccurredOnPicker.Date = DateTimeOffset.Now;
        DirectionBox.ItemsSource = new[] { "支出", "收入", "转账", "退款", "报销" };
        DirectionBox.SelectedIndex = 0;
        AccountBox.ItemsSource = accounts ?? [];
        ToAccountBox.ItemsSource = accounts ?? [];
        if (accounts?.Count > 0) AccountBox.SelectedIndex = 0;
        CategoryBox.Text = "未分类";
    }

    public ManualEntryDialog(TransactionRecord record, IReadOnlyList<AccountRecord> accounts) : this(accounts)
    {
        Title = "编辑流水";
        OccurredOnPicker.Date = new DateTimeOffset(record.OccurredOn);
        DirectionBox.SelectedItem = record.Direction;
        AmountBox.Value = (double)record.Amount;
        CategoryBox.Text = record.Category;
        MerchantBox.Text = record.Merchant;
        NoteBox.Text = record.Note;
        AccountBox.SelectedItem = accounts.FirstOrDefault(account => account.Id == record.AccountId);
        ToAccountBox.SelectedItem = accounts.FirstOrDefault(account => account.Id == record.ToAccountId);
        Result = new TransactionRecord
        {
            Id = record.Id,
            Source = record.Source,
            Fingerprint = record.Fingerprint
        };
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
        var direction = DirectionBox.SelectedItem?.ToString() ?? "支出";
        var account = AccountBox.SelectedItem as AccountRecord;
        var toAccount = ToAccountBox.SelectedItem as AccountRecord;
        if (direction == "转账" && (account is null || toAccount is null || account.Id == toAccount.Id))
        {
            ValidationInfo.Message = "转账必须选择两个不同的转出和转入账户。";
            ValidationInfo.IsOpen = true;
            args.Cancel = true;
            return;
        }
        Result ??= new TransactionRecord
        {
            Source = "手动录入",
            Fingerprint = $"MANUAL-{Guid.NewGuid():N}"
        };
        Result.OccurredOn = selectedDate.LocalDateTime.Date.Add(DateTime.Now.TimeOfDay);
        Result.Direction = direction;
        Result.Amount = amount;
        Result.Category = string.IsNullOrWhiteSpace(CategoryBox.Text) ? "未分类" : CategoryBox.Text.Trim();
        Result.Merchant = MerchantBox.Text.Trim();
        Result.Note = NoteBox.Text.Trim();
        Result.AccountId = account?.Id;
        Result.ToAccountId = direction == "转账" ? toAccount?.Id : null;
    }

    private void DirectionSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ToAccountBox is null) return;
        ToAccountBox.IsEnabled = DirectionBox.SelectedItem?.ToString() == "转账";
    }
}
