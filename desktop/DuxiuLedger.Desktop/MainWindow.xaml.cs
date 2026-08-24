using System.Collections.ObjectModel;
using System.Windows;
using Microsoft.Win32;
using DuxiuLedger.Desktop.Models;
using DuxiuLedger.Desktop.Services;

namespace DuxiuLedger.Desktop;

public partial class MainWindow : Window
{
    private readonly LocalStore _store = new();
    private readonly BillImporter _importer = new();
    private readonly ObservableCollection<TransactionRecord> _records = new();
    public MainWindow() { InitializeComponent(); LedgerGrid.ItemsSource = _records; LoadRecords(); }
    private void LoadRecords(string? search = null)
    {
        var rows = _store.List(search);
        _records.Clear(); foreach (var row in rows) _records.Add(row);
        var month = DateTime.Now.ToString("yyyy-MM");
        var current = rows.Where(r => r.OccurredOn.ToString("yyyy-MM") == month);
        var income = current.Where(r => r.Direction == "收入").Sum(r => r.Amount);
        var expense = current.Where(r => r.Direction == "支出").Sum(r => r.Amount);
        IncomeText.Text = $"¥{income:N2}"; ExpenseText.Text = $"¥{expense:N2}"; BalanceText.Text = $"¥{income - expense:N2}";
        CountText.Text = $"共 {_records.Count} 条记录 · 本月 {current.Count()} 条";
        StatusText.Text = "数据保存在本机应用数据目录";
    }
    private void SearchClick(object sender, RoutedEventArgs e) => LoadRecords(SearchBox.Text.Trim());
    private void ImportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "账单文件|*.xlsx;*.xlsm;*.csv|Excel 文件|*.xlsx;*.xlsm|CSV 文件|*.csv", Multiselect = true };
        if (dialog.ShowDialog() != true) return;
        var imported = 0;
        try { foreach (var file in dialog.FileNames) { var rows = _importer.Read(file); imported += _store.Import(rows); } LoadRecords(); StatusText.Text = $"导入完成：新增 {imported} 条；重复记录会自动跳过。"; }
        catch (Exception ex) { MessageBox.Show(ex.Message, "导入失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private void AddClick(object sender, RoutedEventArgs e) => MessageBox.Show("手动录入界面将在下一步加入。当前可先导入 Excel、微信或支付宝账单。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
}
