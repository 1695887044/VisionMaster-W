using System.Globalization;

namespace G.ValueConverter.Ints;

/// <summary> 替换字符串 </summary>
public class GetStringReplaceConverter : MarkupValueConverterBase
{
    public override object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null)
            return null;
        if (parameter == null)
            return value;
        return value.ToString().Replace(value.ToString().Split(' ')[0], value.ToString().Split(' ')[1]);
    }
}
