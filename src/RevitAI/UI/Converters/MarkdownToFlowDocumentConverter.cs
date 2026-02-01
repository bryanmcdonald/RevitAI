using System.Globalization;
using System.Windows.Data;
using System.Windows.Documents;
using RevitAI.Services;

namespace RevitAI.UI.Converters;

/// <summary>
/// Converts markdown text to a WPF FlowDocument for rich text display.
/// </summary>
public class MarkdownToFlowDocumentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string markdown)
        {
            return MarkdownService.Instance.ConvertToFlowDocument(markdown);
        }
        return new FlowDocument();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException("Converting FlowDocument back to markdown is not supported.");
    }
}
