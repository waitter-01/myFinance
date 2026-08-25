using DuxiuLedger.Desktop.Models;
using DuxiuLedger.Desktop.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;
using System.Globalization;
using Windows.Graphics;

namespace DuxiuLedger.WinUI;

public sealed partial class ImportPreviewWindow : Window
{
    private readonly ObservableCollection<TransactionRecord> _candidates;
    private readonly ObservableCollection<ImportDuplicateItem> _duplicates;
    private readonly ObservableCollection<ImportIssue> _issues;
    private readonly IReadOnlyList<TransactionRecord> _existingRecords;
    private readonly TransactionDuplicateDetector _duplicateDetector = new();
    private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _confirmed;
    private bool _shown;

    public IReadOnlyList<TransactionRecord> RowsToImport => _candidates;
    public int DuplicateCount => _duplicates.Count;
    public int DetectedDuplicateCount { get; }
    public int IssueCount => _issues.Count;

    public ImportPreviewWindow(
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
            else duplicates.Add(duplicate);
        }

        _candidates = new ObservableCollection<TransactionRecord>(candidates);
        _duplicates = new ObservableCollection<ImportDuplicateItem>(duplicates);
        DetectedDuplicateCount = duplicates.Count;
        _issues = new ObservableCollection<ImportIssue>(previews.SelectMany(preview => preview.Issues));
        FileCountText.Text = $"{previews.Count} 个 / {previews.Sum(preview => preview.TotalRows)} 条";
        RecordsList.ItemsSource = _candidates;
        DuplicateList.ItemsSource = _duplicates;
        IssuesList.ItemsSource = _issues;

        var choices = new List<AccountRecord> { new() { Id = 0, Name = "暂不指定账户", Type = "" } };
        choices.AddRange(accounts.Where(account => account.IsActive));
        AccountBox.ItemsSource = choices;
        AccountBox.SelectedIndex = 0;
        EditDirectionBox.ItemsSource = new[] { "支出", "收入", "转账", "退款", "报销" };
        EditCategoryBox.ItemsSource = categories.Where(category => category.IsActive).Select(category => category.Name).Distinct().ToList();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(ImportTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;
        AppWindow.Resize(new SizeInt32(1240, 820));
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "duxiu-logo.ico");
        if (File.Exists(iconPath)) AppWindow.SetIcon(iconPath);
        Closed += ImportPreviewWindowClosed;

        UpdateCounts();
        if (_duplicates.Count > 0) ImportTabs.SelectedItem = DuplicateTab;
        else if (_issues.Count > 0) ImportTabs.SelectedItem = IssuesTab;
    }

    public Task<bool> ShowAsync(Window owner)
    {
        if (_shown) return _completion.Task;
        _shown = true;
        var ownerPosition = owner.AppWindow.Position;
        var ownerSize = owner.AppWindow.Size;
        var width = Math.Min(1240, Math.Max(900, ownerSize.Width - 60));
        var height = Math.Min(820, Math.Max(650, ownerSize.Height - 60));
        AppWindow.Resize(new SizeInt32(width, height));
        AppWindow.Move(new PointInt32(ownerPosition.X + Math.Max(24, (ownerSize.Width - width) / 2), ownerPosition.Y + Math.Max(24, (ownerSize.Height - height) / 2)));
        Activate();
        return _completion.Task;
    }

    private void WindowRootSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var compact = e.NewSize.Width < 1060;
        SummaryColumn2.Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        SummaryColumn3.Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetRow(DuplicateSummaryCard, compact ? 1 : 0);
        Grid.SetColumn(DuplicateSummaryCard, compact ? 0 : 2);
        Grid.SetRow(IssueSummaryCard, compact ? 1 : 0);
        Grid.SetColumn(IssueSummaryCard, compact ? 1 : 3);
        if (compact)
        {
            Grid.SetRow(DuplicateInfoBar, 1);
            Grid.SetColumn(DuplicateInfoBar, 0);
            Grid.SetColumnSpan(DuplicateInfoBar, 2);
            Grid.SetRow(EditDirectionBox, 0); Grid.SetColumn(EditDirectionBox, 0); Grid.SetColumnSpan(EditDirectionBox, 4);
            Grid.SetRow(EditAmountBox, 0); Grid.SetColumn(EditAmountBox, 4); Grid.SetColumnSpan(EditAmountBox, 4);
            Grid.SetRow(EditCategoryBox, 0); Grid.SetColumn(EditCategoryBox, 8); Grid.SetColumnSpan(EditCategoryBox, 4);
            Grid.SetRow(EditMerchantBox, 1); Grid.SetColumn(EditMerchantBox, 0); Grid.SetColumnSpan(EditMerchantBox, 7);
            Grid.SetRow(EditDateBox, 1); Grid.SetColumn(EditDateBox, 7); Grid.SetColumnSpan(EditDateBox, 5);
            Grid.SetRow(EditActions, 2); Grid.SetColumn(EditActions, 0); Grid.SetColumnSpan(EditActions, 12);
        }
        else
        {
            Grid.SetRow(DuplicateInfoBar, 0); Grid.SetColumn(DuplicateInfoBar, 1); Grid.SetColumnSpan(DuplicateInfoBar, 1);
            Grid.SetRow(EditDirectionBox, 0); Grid.SetColumn(EditDirectionBox, 0); Grid.SetColumnSpan(EditDirectionBox, 2);
            Grid.SetRow(EditAmountBox, 0); Grid.SetColumn(EditAmountBox, 2); Grid.SetColumnSpan(EditAmountBox, 2);
            Grid.SetRow(EditCategoryBox, 0); Grid.SetColumn(EditCategoryBox, 4); Grid.SetColumnSpan(EditCategoryBox, 2);
            Grid.SetRow(EditMerchantBox, 0); Grid.SetColumn(EditMerchantBox, 6); Grid.SetColumnSpan(EditMerchantBox, 3);
            Grid.SetRow(EditDateBox, 0); Grid.SetColumn(EditDateBox, 9); Grid.SetColumnSpan(EditDateBox, 2);
            Grid.SetRow(EditActions, 1); Grid.SetColumn(EditActions, 0); Grid.SetColumnSpan(EditActions, 12);
        }

        var narrowFooter = e.NewSize.Width < 760;
        Grid.SetRow(FooterActions, narrowFooter ? 1 : 0);
        Grid.SetColumn(FooterActions, narrowFooter ? 0 : 1);
        Grid.SetColumnSpan(FooterActions, narrowFooter ? 2 : 1);
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

    private void ApplyEditClick(object sender, RoutedEventArgs e)
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
        selected.RequiresReview = false;
        selected.Fingerprint = TransactionFingerprint.Create(selected);
        RemoveResolvedIssues(selected);
        var index = _candidates.IndexOf(selected);
        var duplicate = _duplicateDetector.FindMatch(selected, _existingRecords.Concat(_candidates.Where(row => !ReferenceEquals(row, selected))));
        if (duplicate is not null)
        {
            _candidates.RemoveAt(index);
            _duplicates.Add(duplicate);
            RecordsList.SelectedItem = null;
            UpdateCounts();
            ImportTabs.SelectedItem = DuplicateTab;
            return;
        }

        _candidates.RemoveAt(index);
        _candidates.Insert(index, selected);
        RecordsList.SelectedIndex = index;
        UpdateCounts();
    }

    private void RemoveRowClick(object sender, RoutedEventArgs e)
    {
        if (RecordsList.SelectedItem is not TransactionRecord selected) return;
        _candidates.Remove(selected);
        RemoveResolvedIssues(selected);
        UpdateCounts();
        RecordsList.SelectedItem = null;
    }

    private void ImportDuplicateClick(object sender, RoutedEventArgs e)
    {
        if (DuplicateList.SelectedItem is not ImportDuplicateItem selected) return;
        selected.Incoming.Fingerprint = TransactionFingerprint.CreateForced(selected.Incoming);
        _candidates.Add(selected.Incoming);
        _duplicates.Remove(selected);
        DuplicateList.SelectedItem = null;
        UpdateCounts();
        ImportTabs.SelectedItem = _duplicates.Count > 0 ? DuplicateTab : PendingTab;
    }

    private void ShowDuplicatesClick(object sender, RoutedEventArgs e) => ImportTabs.SelectedItem = DuplicateTab;
    private void ShowIssuesClick(object sender, RoutedEventArgs e) => ImportTabs.SelectedItem = IssuesTab;

    private void ScrollToLastClick(object sender, RoutedEventArgs e)
    {
        if (_candidates.Count == 0) return;
        var last = _candidates[^1];
        ImportTabs.SelectedItem = PendingTab;
        RecordsList.SelectedItem = last;
        RecordsList.UpdateLayout();
        RecordsList.ScrollIntoView(last, ScrollIntoViewAlignment.Leading);
        DispatcherQueue.TryEnqueue(() =>
        {
            RecordsList.UpdateLayout();
            RecordsList.ScrollIntoView(last, ScrollIntoViewAlignment.Leading);
            RecordsList.Focus(FocusState.Programmatic);
        });
    }

    private void ReviewIssueClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ImportIssue issue } || issue.Record is null) return;
        var duplicate = _duplicates.FirstOrDefault(item => ReferenceEquals(item.Incoming, issue.Record));
        if (duplicate is not null)
        {
            ImportTabs.SelectedItem = DuplicateTab;
            DuplicateList.SelectedItem = duplicate;
            DuplicateList.ScrollIntoView(duplicate);
            return;
        }

        if (!_candidates.Contains(issue.Record)) return;
        ImportTabs.SelectedItem = PendingTab;
        RecordsList.SelectedItem = issue.Record;
        RecordsList.UpdateLayout();
        RecordsList.ScrollIntoView(issue.Record, ScrollIntoViewAlignment.Leading);
        DispatcherQueue.TryEnqueue(() =>
        {
            RecordsList.ScrollIntoView(issue.Record, ScrollIntoViewAlignment.Leading);
            if (issue.Reason.Contains("时间")) EditDateBox.Focus(FocusState.Programmatic);
            else EditMerchantBox.Focus(FocusState.Programmatic);
        });
    }

    private void ConfirmClick(object sender, RoutedEventArgs e)
    {
        if (!ConfirmButton.IsEnabled) return;
        var account = AccountBox.SelectedItem as AccountRecord;
        var accountId = account is { Id: > 0 } ? account.Id : (long?)null;
        foreach (var row in RowsToImport) row.AccountId = accountId;
        _confirmed = true;
        Close();
    }

    private void CancelClick(object sender, RoutedEventArgs e) => Close();

    private void ImportPreviewWindowClosed(object sender, WindowEventArgs args)
    {
        Closed -= ImportPreviewWindowClosed;
        _completion.TrySetResult(_confirmed);
    }

    private void UpdateCounts()
    {
        ValidCountText.Text = $"{_candidates.Count} 条";
        DuplicateCountText.Text = $"{_duplicates.Count} 条";
        IssueCountText.Text = $"{_issues.Count} 条";
        PendingTab.Header = $"待导入流水（{_candidates.Count}）";
        DuplicateTab.Header = $"疑似重复（{_duplicates.Count}，需要确认）";
        IssuesTab.Header = $"需要核对（{_issues.Count}，可修改）";
        ScrollToLastButton.IsEnabled = _candidates.Count > 0;
        DuplicateSummaryButton.IsEnabled = _duplicates.Count > 0;
        IssueSummaryButton.IsEnabled = _issues.Count > 0;
        var hasUnresolvedCandidate = _issues.Any(issue => issue.Record is not null && _candidates.Contains(issue.Record));
        ConfirmButton.IsEnabled = _candidates.Count > 0 && !hasUnresolvedCandidate;
        ImportHintText.Text = hasUnresolvedCandidate
            ? "仍有需要核对的流水，请先在“需要核对”页定位并修正或跳过。"
            : "确认前请核对金额、时间和收支类型；取消不会写入账本。";
    }

    private void RemoveResolvedIssues(TransactionRecord record)
    {
        foreach (var issue in _issues.Where(item => ReferenceEquals(item.Record, record)).ToList()) _issues.Remove(issue);
    }
}
