using System.Globalization;

namespace G.ValueConverter.Bools;

public class GetBoolToValueConverter : MarkupValueConverterBase
{
    public object TrueValue { get; set; }
    public object FalseValue { get; set; }
    public override object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
            return b ? this.TrueValue : this.FalseValue;
        return this.DefaultValue;
    }
}
