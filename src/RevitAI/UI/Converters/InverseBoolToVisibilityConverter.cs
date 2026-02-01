using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RevitAI.UI.Converters;

/// <summary>
/// Converts boolean values to Visibility (true = Collapsed, false = Visible).
/// Inverse of BoolToVisibilityConverter.
/// </summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? Visibility.Collapsed : Visibility.Visible;
        }
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Visibility visibility)
        {
            return visibility != Visibility.Visible;
        }
        return true;
    }
}
