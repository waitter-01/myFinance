using DuxiuLedger.Desktop.Models;
using Microsoft.UI.Xaml.Controls;

namespace DuxiuLedger.WinUI;

public sealed partial class SavingsGoalDialog : ContentDialog
{
    private readonly long _id;
    public SavingsGoalRecord? Result { get; private set; }

    public SavingsGoalDialog(SavingsGoalRecord? goal = null)
    {
        InitializeComponent();
        _id = goal?.Id ?? 0;
        NameBox.Text = goal?.Name ?? "";
        TargetBox.Value = goal is null ? 10000 : (double)goal.TargetAmount;
        SavedBox.Value = goal is null ? 0 : (double)goal.SavedAmount;
        TargetDatePicker.Date = goal?.TargetDate is null ? null : new DateTimeOffset(goal.TargetDate.Value);
        CompletedCheck.IsChecked = goal?.IsCompleted ?? false;
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text) || double.IsNaN(TargetBox.Value) || TargetBox.Value <= 0 || double.IsNaN(SavedBox.Value) || SavedBox.Value < 0)
        {
            args.Cancel = true;
            return;
        }
        Result = new SavingsGoalRecord { Id = _id, Name = NameBox.Text.Trim(), TargetAmount = (decimal)TargetBox.Value, SavedAmount = (decimal)SavedBox.Value, TargetDate = TargetDatePicker.Date?.DateTime, IsCompleted = CompletedCheck.IsChecked == true };
    }
}
