using Microsoft.UI.Xaml.Controls;

namespace DuxiuLedger.WinUI;

public sealed partial class MonthlyBudgetDialog : ContentDialog
{
    public decimal? Result { get; private set; }

    public MonthlyBudgetDialog(decimal currentBudget)
    {
        InitializeComponent();
        AmountBox.Value = currentBudget > 0 ? (double)currentBudget : double.NaN;
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (double.IsNaN(AmountBox.Value) || double.IsInfinity(AmountBox.Value) || AmountBox.Value <= 0)
        {
            args.Cancel = true;
            return;
        }

        Result = (decimal)AmountBox.Value;
    }

    private void OnSecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        => Result = 0;
}
