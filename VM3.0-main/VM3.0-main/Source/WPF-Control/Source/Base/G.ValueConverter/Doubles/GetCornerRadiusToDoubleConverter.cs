using System.Globalization;

namespace G.ValueConverter.Doubles;

public class GetCornerRadiusToDoubleConverter : MarkupValueConverterBase
{
    public override object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (parameter != null)
            return ((CornerRadius)value).TopLeft * System.Convert.ToDouble(parameter);
        return ((CornerRadius)value).TopLeft;
    }

    public override object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (parameter != null)
            return new CornerRadius((double)value / System.Convert.ToDouble(parameter));
        return new CornerRadius((double)value);
    }
}
