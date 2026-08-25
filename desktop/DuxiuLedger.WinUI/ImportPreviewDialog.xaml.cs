using DuxiuLedger.Desktop.Models;
using Microsoft.UI.Xaml.Controls;

namespace DuxiuLedger.WinUI;

public sealed partial class ImportPreviewDialog : ContentDialog
{
    public IReadOnlyList<TransactionRecord> RowsToImport { get; }
    public int DuplicateCount { get; }
    public int IssueCount { get; }

    public ImportPreviewDialog(IReadOnlyList<ImportPreviewResult> previews, IReadOnlyList<AccountRecord> accounts, IReadOnlySet<string> existingFingerprints)
    {
        InitializeComponent();
        var seen = new HashSet<string>(existingFingerprints, StringComparer.Ordinal);
        var candidates = new List<TransactionRecord>();
        var duplicates = 0;
        foreach (var record in previews.SelectMany(preview => preview.Records))
        {
            if (seen.Add(record.Fingerprint)) candidates.Add(record); else duplicates++;
        }
        RowsToImport = candidates;
        DuplicateCount = duplicates;
        var issues = previews.SelectMany(preview => preview.Issues).ToList();
        IssueCount = issues.Count;
        FileCountText.Text = $"{previews.Count} 个";
        ValidCountText.Text = $"{candidates.Count} 条";
        DuplicateCountText.Text = $"{duplicates} 条";
        IssueCountText.Text = $"{issues.Count} 条";
        RecordsList.ItemsSource = candidates;
        IssuesList.ItemsSource = issues;
        var choices = new List<AccountRecord> { new() { Id = 0, Name = "暂不指定账户", Type = "" } };
        choices.AddRange(accounts.Where(account => account.IsActive));
        AccountBox.ItemsSource = choices;
        AccountBox.SelectedIndex = 0;
        IsPrimaryButtonEnabled = candidates.Count > 0;
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var account = AccountBox.SelectedItem as AccountRecord;
        var accountId = account is { Id: > 0 } ? account.Id : (long?)null;
        foreach (var row in RowsToImport) row.AccountId = accountId;
    }
}
