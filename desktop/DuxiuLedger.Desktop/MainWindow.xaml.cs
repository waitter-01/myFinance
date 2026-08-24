using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using DuxiuLedger.Desktop.Models;
using DuxiuLedger.Desktop.Services;

namespace DuxiuLedger.Desktop;

public partial class MainWindow : Window
{
    private readonly LocalStore _store = new();
    private readonly BillImporter _importer = new();
    private readonly ObservableCollection<TransactionRecord> _records = new();
    private readonly Dictionary<string, (Grid Page, Button Button, string Title, string Subtitle)> _navigation;

    public MainWindow()
    {
        InitializeComponent();
        _navigation = new()
        {
            ["Dashboard"] = (DashboardPage, DashboardNav, "总览", "查看本月财务情况和最近流水"),
            ["Transactions"] = (TransactionsPage, TransactionsNav, "全部流水", "搜索、核对和管理本地账单记录"),
            ["Budgets"] = (BudgetsPage, BudgetsNav, "预算计划", "规划每月支出，控制消费节奏"),
            ["Categories"] = (CategoriesPage, CategoriesNav, "分类设置", "建立适合自己的收支分类体系"),
            ["Backup"] = (BackupPage, BackupNav, "数据备份", "复制和保护本地账本数据库")
        };
        DashboardGrid.ItemsSource = _records;
        TransactionsGrid.ItemsSource = _records;
        DataPathText.Text = _store.DatabasePath;
        LoadRecords();
        ShowPage("Dashboard");
    }

    private void NavigationClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string key }) ShowPage(key);
    }

    private void ShowPage(string key)
    {
        foreach (var item in _navigation.Values)
        {
            item.Page.Visibility = Visibility.Collapsed;
            item.Button.Style = (Style)FindResource("NavButton");
        }
        var selected = _navigation[key];
        selected.Page.Visibility = Visibility.Visible;
        selected.Button.Style = (Style)FindResource("SelectedNavButton");
        PageTitle.Text = selected.Title;
        PageSubtitle.Text = selected.Subtitle;
    }

    private void LoadRecords(string? search = null)
    {
        var rows = _store.List(search);
        _records.Clear();
        foreach (var row in rows) _records.Add(row);
        var month = DateTime.Now.ToString("yyyy-MM");
        var current = rows.Where(r => r.OccurredOn.ToString("yyyy-MM") == month).ToList();
        var income = current.Where(r => r.Direction == "收入").Sum(r => r.Amount);
        var expense = current.Where(r => r.Direction == "支出").Sum(r => r.Amount);
        IncomeText.Text = $"¥{income:N2}";
        ExpenseText.Text = $"¥{expense:N2}";
        BalanceText.Text = $"¥{income - expense:N2}";
        CountText.Text = $"共 {_records.Count} 条记录 · 本月 {current.Count} 条";
        StatusText.Text = search is null ? "数据已从本地数据库加载" : $"搜索到 {_records.Count} 条记录";
    }

    private void SearchClick(object sender, RoutedEventArgs e) => LoadRecords(SearchBox.Text.Trim());

    private void ImportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "账单文件|*.xlsx;*.xlsm;*.csv|Excel 文件|*.xlsx;*.xlsm|CSV 文件|*.csv", Multiselect = true };
        if (dialog.ShowDialog() != true) return;
        var imported = 0;
        try
        {
            foreach (var file in dialog.FileNames) imported += _store.Import(_importer.Read(file));
            LoadRecords();
            ShowPage("Transactions");
            StatusText.Text = $"导入完成：新增 {imported} 条，重复记录已跳过";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "导入失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void AddClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ManualEntryWindow { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is null) return;
        _store.Import([dialog.Result]);
        LoadRecords();
        ShowPage("Transactions");
        StatusText.Text = "手动流水已保存到本地数据库";
    }

    private void BackupClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "SQLite 备份|*.db", FileName = $"duxiu-ledger-{DateTime.Now:yyyyMMdd-HHmm}.db" };
        if (dialog.ShowDialog() != true) return;
        File.Copy(_store.DatabasePath, dialog.FileName, true);
        StatusText.Text = $"备份完成：{dialog.FileName}";
    }

    private void OpenDataFolderClick(object sender, RoutedEventArgs e)
    {
        var folder = Path.GetDirectoryName(_store.DatabasePath)!;
        Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
    }
}
