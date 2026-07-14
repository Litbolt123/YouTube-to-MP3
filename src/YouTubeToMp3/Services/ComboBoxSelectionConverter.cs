using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;

namespace YouTubeToMp3.Services;

public sealed class ComboBoxSelectionConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ComboBoxItem item)
            return item.Content?.ToString() ?? "";

        return value?.ToString() ?? "";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
