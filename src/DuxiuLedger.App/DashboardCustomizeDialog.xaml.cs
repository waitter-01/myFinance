using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;

namespace DuxiuLedger.WinUI;

public sealed partial class DashboardCustomizeDialog : ContentDialog
{
    public ObservableCollection<DashboardCardOption> Options { get; }
    public string CardOrder => string.Join(',', Options.Select(item => item.Key));
    public string HiddenCards => string.Join(',', Options.Where(item => !item.IsVisible).Select(item => item.Key));

    public DashboardCustomizeDialog(IEnumerable<DashboardCardOption> options)
    {
        InitializeComponent();
        Options = new ObservableCollection<DashboardCardOption>(options);
        CardsList.ItemsSource = Options;
    }

    private void MoveUpClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: DashboardCardOption option }) return;
        var index = Options.IndexOf(option);
        if (index > 0) Options.Move(index, index - 1);
    }

    private void MoveDownClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: DashboardCardOption option }) return;
        var index = Options.IndexOf(option);
        if (index >= 0 && index < Options.Count - 1) Options.Move(index, index + 1);
    }
}
