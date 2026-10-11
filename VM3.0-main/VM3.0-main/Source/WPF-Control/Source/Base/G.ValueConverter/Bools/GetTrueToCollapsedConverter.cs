using System.Globalization;

namespace G.ValueConverter.Bools;

public class GetTrueToCollapsedConverter : MarkupValueConverterBase
{
    public override object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && b)
            return Visibility.Collapsed;
        return Visibility.Visible;
    }
}

