using System.Globalization;

namespace G.ValueConverter;

public class GetGemeotryFromStringConverter : MarkupValueConverterBase
{
    public override object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null)
            return null;
        return Geometry.Parse(value.ToString());
    }
}
