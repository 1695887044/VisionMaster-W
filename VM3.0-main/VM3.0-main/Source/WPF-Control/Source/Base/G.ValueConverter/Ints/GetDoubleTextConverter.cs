using System.Globalization;

namespace G.ValueConverter.Ints;

/// <summary> 设置文本框PropertyChanged 可以输入小数点 </summary>
public class GetDoubleTextConverter : MarkupValueConverterBase
{
    public override object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value;
    }

    public override object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string str = value.ToString();
        if (str.EndsWith("."))
            return ".";
        if (str.Contains(".") && str.EndsWith("0"))
            return ".";
        return value;
    }
}
