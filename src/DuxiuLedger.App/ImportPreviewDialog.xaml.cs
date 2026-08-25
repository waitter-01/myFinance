using DuxiuLedger.Desktop.Models;
using DuxiuLedger.Desktop.Services;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;
using System.Globalization;

namespace DuxiuLedger.WinUI;

public sealed partial class ImportPreviewDialog : ContentDialog
{
    private readonly ObservableCollection<TransactionRecord> _candidates;
    private readonly ObservableCollection<ImportDuplicateItem> _duplicates;
    private readonly IReadOnlyList<TransactionRecord> _existingRecords;
    private readonly TransactionDuplicateDetector _duplicateDetector = new();

    public IReadOnlyList<TransactionRecord> RowsToImport => _candidates;
    public int DuplicateCount => _duplicates.Count;
    public int DetectedDuplicateCount { get; }
    public int IssueCount { get; }

    public ImportPreviewDialog(
        IReadOnlyList<ImportPreviewResult> previews,
        IReadOnlyList<AccountRecord> accounts,
        IReadOnlyList<CategoryRecord> categories,
        IReadOnlyList<TransactionRecord> existingRecords)
    {
        InitializeComponent();
        _existingRecords = existingRecords;
        var activeCategories = categories.Where(category => category.IsActive).Select(category => category.Name).ToHashSet(StringComparer.Ordinal);
        var candidates = new List<TransactionRecord>();
        var duplicates = new List<ImportDuplicateItem>();
        var comparisonRows = existingRecords.ToList();

        foreach (var record in previews.SelectMany(preview => preview.Records))
        {
            var suggestedCategory = TransactionCategorizer.Suggest(record);
            if (activeCategories.Contains(suggestedCategory)) record.Category = suggestedCategory;

            var duplicate = _duplicateDetector.FindMatch(record, comparisonRows);
            if (duplicate is null)
            {
                candidates.Add(record);
                comparisonRows.Add(record);
            }
            else
            {
                duplicates.Add(duplicate);
            }
        }

        _candidates = new ObservableCollection<TransactionRecord>(candidates);
        _duplicates = new ObservableCollection<ImportDuplicateItem>(duplicates);
        DetectedDuplicateCount = duplicates.Count;
        var issues = previews.SelectMany(preview => preview.Issues).ToList();
        IssueCount = issues.Count;
        FileCountText.Text = $"{previews.Count} 个";
        IssueCountText.Text = $"{issues.Count} 条";
        RecordsList.ItemsSource = _candidates;
        DuplicateList.ItemsSource = _duplicates;
        IssuesList.ItemsSource = issues;

        var choices = new List<AccountRecord> { new() { Id = 0, Name = "暂不指定账户", Type = "" } };
        choices.AddRange(accounts.Where(account => account.IsActive));
        AccountBox.ItemsSource = choices;
        AccountBox.SelectedIndex = 0;
        EditDirectionBox.ItemsSource = new[] { "支出", "收入", "转账", "退款", "报销" };
        EditCategoryBox.ItemsSource = categories.Where(category => category.IsActive).Select(category => category.Name).Distinct().ToList();
        UpdateCounts();
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
        selected.Fingerprint = TransactionFingerprint.Create(selected);
        var index = _candidates.IndexOf(selected);
        var duplicate = _duplicateDetector.FindMatch(selected, _existingRecords.Concat(_candidates.Where(row => !ReferenceEquals(row, selected))));
        if (duplicate is not null)
        {
            _candidates.RemoveAt(index);
            _duplicates.Add(duplicate);
            RecordsList.SelectedItem = null;
            UpdateCounts();
            return;
        }

        _candidates.RemoveAt(index);
        _candidates.Insert(index, selected);
        RecordsList.SelectedIndex = index;
        UpdateCounts();
    }

    private void RemoveRowClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (RecordsList.SelectedItem is not TransactionRecord selected) return;
        _candidates.Remove(selected);
        UpdateCounts();
        RecordsList.SelectedItem = null;
    }

    private void ImportDuplicateClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (DuplicateList.SelectedItem is not ImportDuplicateItem selected) return;
        selected.Incoming.Fingerprint = TransactionFingerprint.CreateForced(selected.Incoming);
        _candidates.Add(selected.Incoming);
        _duplicates.Remove(selected);
        DuplicateList.SelectedItem = null;
        UpdateCounts();
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var account = AccountBox.SelectedItem as AccountRecord;
        var accountId = account is { Id: > 0 } ? account.Id : (long?)null;
        foreach (var row in RowsToImport) row.AccountId = accountId;
    }

    private void UpdateCounts()
    {
        ValidCountText.Text = $"{_candidates.Count} 条";
        DuplicateCountText.Text = $"{_duplicates.Count} 条";
        IsPrimaryButtonEnabled = _candidates.Count > 0;
    }
}
