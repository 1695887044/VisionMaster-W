global using G.ValueConverter;
global using System.Globalization;

namespace G.Extensions.ValueConverter.Files;

public class GetFilePathSizeToDisplayConverter : MarkupValueConverterBase
{
    public override object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null)
            return null;
        return value.ToString().ToFileEx().GetFileSizeToDisplay();
    }
}
