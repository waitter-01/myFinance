using DuxiuLedger.Desktop.Models;
using Microsoft.UI.Xaml.Controls;

namespace DuxiuLedger.WinUI;

public sealed partial class AccountDialog : ContentDialog
{
    public AccountRecord? Result { get; private set; }

    public AccountDialog(AccountRecord? account = null)
    {
        InitializeComponent();
        TypeBox.ItemsSource = new[] { "现金", "银行卡", "信用卡", "电子钱包", "储蓄", "投资", "贷款", "其他" };
        TypeBox.SelectedItem = account?.Type ?? "银行卡";
        if (account is null) return;
        Title = "编辑账户";
        NameBox.Text = account.Name;
        OpeningBalanceBox.Value = (double)account.OpeningBalance;
        ActiveSwitch.IsOn = account.IsActive;
        Result = new AccountRecord { Id = account.Id };
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            ValidationInfo.Message = "请输入账户名称。";
            ValidationInfo.IsOpen = true;
            args.Cancel = true;
            return;
        }
        Result ??= new AccountRecord();
        Result.Name = NameBox.Text.Trim();
        Result.Type = TypeBox.SelectedItem?.ToString() ?? "其他";
        Result.OpeningBalance = double.IsNaN(OpeningBalanceBox.Value) ? 0 : (decimal)OpeningBalanceBox.Value;
        Result.IsActive = ActiveSwitch.IsOn;
    }
}
