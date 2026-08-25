using DuxiuLedger.Desktop.Models;
using Microsoft.UI.Xaml.Controls;

namespace DuxiuLedger.WinUI;

public sealed partial class ManualEntryDialog : ContentDialog
{
    private readonly IReadOnlyList<CategoryRecord> _categories;
    public TransactionRecord? Result { get; private set; }

    public ManualEntryDialog(IReadOnlyList<AccountRecord>? accounts = null, IReadOnlyList<CategoryRecord>? categories = null)
    {
        _categories = categories ?? [];
        InitializeComponent();
        OccurredOnPicker.Date = DateTimeOffset.Now;
        DirectionBox.ItemsSource = new[] { "支出", "收入", "转账", "退款", "报销" };
        DirectionBox.SelectedIndex = 0;
        AccountBox.ItemsSource = accounts ?? [];
        ToAccountBox.ItemsSource = accounts ?? [];
        if (accounts?.Count > 0) AccountBox.SelectedIndex = 0;
        RefreshCategories();
        CategoryBox.Text = "未分类";
    }

    public ManualEntryDialog(TransactionRecord record, IReadOnlyList<AccountRecord> accounts, IReadOnlyList<CategoryRecord> categories) : this(accounts, categories)
    {
        Title = "编辑流水";
        OccurredOnPicker.Date = new DateTimeOffset(record.OccurredOn);
        DirectionBox.SelectedItem = record.Direction;
        AmountBox.Value = (double)record.Amount;
        CategoryBox.Text = record.Category;
        MerchantBox.Text = record.Merchant;
        NoteBox.Text = record.Note;
        SubscriptionMonthsBox.Value = Math.Max(1, record.SubscriptionMonths);
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
        var subscriptionMonths = double.IsNaN(SubscriptionMonthsBox.Value) ? 1 : (int)SubscriptionMonthsBox.Value;
        if (string.Equals(CategoryBox.Text.Trim(), "订阅消费", StringComparison.Ordinal) && subscriptionMonths < 1)
        {
            ValidationInfo.Message = "订阅消费必须填写价格覆盖的月数。";
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
        Result.SubscriptionMonths = Math.Max(1, subscriptionMonths);
    }

    private void DirectionSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ToAccountBox is null) return;
        ToAccountBox.IsEnabled = DirectionBox.SelectedItem?.ToString() == "转账";
        RefreshCategories();
    }

    private void RefreshCategories()
    {
        if (CategoryBox is null) return;
        var direction = DirectionBox.SelectedItem?.ToString();
        var categoryType = direction == "收入" ? "收入" : direction == "转账" ? "通用" : "支出";
        CategoryBox.ItemsSource = _categories.Where(category => category.IsActive && (category.Type == categoryType || category.Type == "通用")).Select(category => category.Name).ToList();
    }
}
