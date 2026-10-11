using System.Globalization;

namespace G.ValueConverter.Doubles;

public class GetThicknessToDoubleConverter : MarkupValueConverterBase
{
    public override object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        Thickness thickness = (Thickness)value;
        return thickness.Left;
    }
}
