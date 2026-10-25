using System.Globalization;
using System.Windows.Data;

namespace UnifiedGameLauncher.Services;

public sealed class LocalizedTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        LocalizationService.Translate(value?.ToString() ?? string.Empty);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value?.ToString() ?? string.Empty;
}
