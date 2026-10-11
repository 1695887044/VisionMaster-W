using System.Globalization;

namespace G.ValueConverter;

public class GetWriteObjectConverter : MarkupValueConverterBase
{
    public override object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        System.Diagnostics.Debug.WriteLine(value);
        return value;
    }
}
