using DuxiuLedger.Desktop.Models;
using Microsoft.UI.Xaml.Controls;

namespace DuxiuLedger.WinUI;

public sealed partial class CategoryDialog : ContentDialog
{
    public CategoryRecord? Result { get; private set; }

    public CategoryDialog(CategoryRecord? category = null)
    {
        InitializeComponent();
        TypeBox.ItemsSource = new[] { "支出", "收入", "通用" };
        TypeBox.SelectedItem = category?.Type ?? "支出";
        SortOrderBox.Value = category?.SortOrder ?? 100;
        if (category is null) return;
        Title = "编辑分类";
        NameBox.Text = category.Name;
        ActiveSwitch.IsOn = category.IsActive;
        Result = new CategoryRecord { Id = category.Id, OriginalName = category.OriginalName };
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            ValidationInfo.Message = "请输入分类名称。";
            ValidationInfo.IsOpen = true;
            args.Cancel = true;
            return;
        }
        Result ??= new CategoryRecord();
        Result.Name = NameBox.Text.Trim();
        Result.Type = TypeBox.SelectedItem?.ToString() ?? "支出";
        Result.SortOrder = double.IsNaN(SortOrderBox.Value) ? 100 : (int)SortOrderBox.Value;
        Result.IsActive = ActiveSwitch.IsOn;
    }
}
