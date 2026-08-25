using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace DuxiuLedger.WinUI;

public sealed class FinanceVisualBrushConverter : IValueConverter
{
    private static readonly bool UsesDarkColors = IsDarkTheme();
    private static readonly (byte R, byte G, byte B)[] CategoryPalette =
    [
        (69, 125, 201), (39, 145, 112), (191, 100, 54), (139, 92, 180),
        (42, 137, 157), (176, 76, 110), (112, 123, 56), (105, 111, 126)
    ];

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var text = value?.ToString()?.Trim() ?? "";
        var mode = parameter?.ToString() ?? "";
        var dark = UsesDarkColors;
        var color = mode.StartsWith("Category", StringComparison.Ordinal)
            ? CategoryColor(text, dark)
            : DirectionColor(text, dark);

        var alpha = mode switch
        {
            "DirectionBackground" => dark ? (byte)48 : (byte)24,
            "CategoryBackground" => dark ? (byte)44 : (byte)22,
            "CategoryBorder" => dark ? (byte)105 : (byte)72,
            _ => (byte)255
        };
        return new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();

    private static (byte R, byte G, byte B) DirectionColor(string direction, bool dark)
        => direction switch
        {
            "支出" => dark ? ((byte)255, (byte)130, (byte)120) : ((byte)190, (byte)67, (byte)62),
            "收入" or "退款" or "报销" => dark ? ((byte)94, (byte)211, (byte)158) : ((byte)30, (byte)128, (byte)91),
            "转账" => dark ? ((byte)116, (byte)174, (byte)244) : ((byte)46, (byte)104, (byte)178),
            _ => dark ? ((byte)190, (byte)190, (byte)190) : ((byte)96, (byte)96, (byte)96)
        };

    private static (byte R, byte G, byte B) CategoryColor(string category, bool dark)
    {
        var hash = 17;
        foreach (var character in category) hash = unchecked(hash * 31 + character);
        var color = CategoryPalette[(hash & int.MaxValue) % CategoryPalette.Length];
        if (!dark) return color;
        return ((byte)Math.Min(255, color.R + 48), (byte)Math.Min(255, color.G + 48), (byte)Math.Min(255, color.B + 48));
    }

    private static bool IsDarkTheme()
    {
        var background = new UISettings().GetColorValue(UIColorType.Background);
        return background.R + background.G + background.B < 384;
    }
}
