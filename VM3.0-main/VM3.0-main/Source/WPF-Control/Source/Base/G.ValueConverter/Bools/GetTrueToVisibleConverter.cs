using System.Globalization;

namespace G.ValueConverter.Bools;

public class GetTrueToVisibleConverter : MarkupValueConverterBase
{
    public override object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && b)
            return Visibility.Visible;
        return Visibility.Collapsed;
    }
}

