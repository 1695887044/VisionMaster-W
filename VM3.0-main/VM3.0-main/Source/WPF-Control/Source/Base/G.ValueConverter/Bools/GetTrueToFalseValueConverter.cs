using System.Globalization;

namespace G.ValueConverter.Bools;

public class GetTrueToFalseValueConverter : MarkupValueConverterBase
{
    public override object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
            return !b;
        return value;
    }
}
