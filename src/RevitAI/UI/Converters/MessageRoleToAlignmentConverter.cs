using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RevitAI.UI.Converters;

/// <summary>
/// Converts MessageRole to HorizontalAlignment for message alignment.
/// User messages align right, others align left.
/// </summary>
public class MessageRoleToAlignmentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is MessageRole role)
        {
            return role == MessageRole.User ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        }
        return HorizontalAlignment.Left;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
