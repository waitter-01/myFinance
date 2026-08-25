using DuxiuLedger.Desktop.Models;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace DuxiuLedger.WinUI;

public sealed partial class ImportPreviewDialog : ContentDialog
{
    private readonly ObservableCollection<TransactionRecord> _candidates;
    public IReadOnlyList<TransactionRecord> RowsToImport => _candidates;
    public int DuplicateCount { get; }
    public int IssueCount { get; }

    public ImportPreviewDialog(IReadOnlyList<ImportPreviewResult> previews, IReadOnlyList<AccountRecord> accounts, IReadOnlyList<CategoryRecord> categories, IReadOnlySet<string> existingFingerprints)
    {
        InitializeComponent();
        var seen = new HashSet<string>(existingFingerprints, StringComparer.Ordinal);
        var candidates = new List<TransactionRecord>();
        var duplicates = 0;
        foreach (var record in previews.SelectMany(preview => preview.Records))
        {
            if (seen.Add(record.Fingerprint)) candidates.Add(record); else duplicates++;
        }
        _candidates = new ObservableCollection<TransactionRecord>(candidates);
        DuplicateCount = duplicates;
        var issues = previews.SelectMany(preview => preview.Issues).ToList();
        IssueCount = issues.Count;
        FileCountText.Text = $"{previews.Count} 个";
        ValidCountText.Text = $"{candidates.Count} 条";
        DuplicateCountText.Text = $"{duplicates} 条";
        IssueCountText.Text = $"{issues.Count} 条";
        RecordsList.ItemsSource = _candidates;
        IssuesList.ItemsSource = issues;
        var choices = new List<AccountRecord> { new() { Id = 0, Name = "暂不指定账户", Type = "" } };
        choices.AddRange(accounts.Where(account => account.IsActive));
        AccountBox.ItemsSource = choices;
        AccountBox.SelectedIndex = 0;
        EditDirectionBox.ItemsSource = new[] { "支出", "收入", "转账", "退款", "报销" };
        EditCategoryBox.ItemsSource = categories.Where(category => category.IsActive).Select(category => category.Name).Distinct().ToList();
        IsPrimaryButtonEnabled = candidates.Count > 0;
    }

    private void RecordsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = RecordsList.SelectedItem as TransactionRecord;
        var enabled = selected is not null;
        EditDirectionBox.IsEnabled = enabled;
        EditAmountBox.IsEnabled = enabled;
        EditCategoryBox.IsEnabled = enabled;
        EditMerchantBox.IsEnabled = enabled;
        EditDateBox.IsEnabled = enabled;
        ApplyEditButton.IsEnabled = enabled;
        RemoveRowButton.IsEnabled = enabled;
        if (selected is null) return;
        EditDirectionBox.SelectedItem = selected.Direction;
        EditAmountBox.Value = (double)selected.Amount;
        EditCategoryBox.Text = selected.Category;
        EditMerchantBox.Text = selected.Merchant;
        EditDateBox.Header = "时间";
        EditDateBox.Text = selected.DateDisplay;
    }

    private void ApplyEditClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (RecordsList.SelectedItem is not TransactionRecord selected || double.IsNaN(EditAmountBox.Value) || EditAmountBox.Value <= 0) return;
        if (!DateTime.TryParseExact(EditDateBox.Text.Trim(), "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var occurredOn))
        {
            EditDateBox.Header = "时间（格式不正确）";
            return;
        }
        EditDateBox.Header = "时间";
        selected.OccurredOn = occurredOn;
        selected.Direction = EditDirectionBox.SelectedItem?.ToString() ?? selected.Direction;
        selected.Amount = Math.Round((decimal)EditAmountBox.Value, 2);
        selected.Category = string.IsNullOrWhiteSpace(EditCategoryBox.Text) ? "未分类" : EditCategoryBox.Text.Trim();
        selected.Merchant = EditMerchantBox.Text.Trim();
        selected.Fingerprint = CreateFingerprint(selected);
        var index = _candidates.IndexOf(selected);
        _candidates.RemoveAt(index);
        _candidates.Insert(index, selected);
        RecordsList.SelectedIndex = index;
    }

    private void RemoveRowClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (RecordsList.SelectedItem is not TransactionRecord selected) return;
        _candidates.Remove(selected);
        ValidCountText.Text = $"{_candidates.Count} 条";
        IsPrimaryButtonEnabled = _candidates.Count > 0;
        RecordsList.SelectedItem = null;
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var account = AccountBox.SelectedItem as AccountRecord;
        var accountId = account is { Id: > 0 } ? account.Id : (long?)null;
        foreach (var row in RowsToImport) row.AccountId = accountId;
    }

    private static string CreateFingerprint(TransactionRecord row)
    {
        var text = $"{row.OccurredOn:yyyy-MM-dd HH:mm}|{row.Direction}|{row.Amount.ToString(CultureInfo.InvariantCulture)}|{row.Merchant}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
