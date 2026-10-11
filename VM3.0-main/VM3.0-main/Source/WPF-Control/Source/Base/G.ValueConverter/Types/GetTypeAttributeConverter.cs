using System.Globalization;

namespace G.ValueConverter.Types;

public class GetTypeAttributeConverter : MarkupValueConverterBase
{
    public Type AttributeType { get; set; } = typeof(DescriptionAttribute);
    public int Index { get; set; } = 0;
    public override object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null)
            return null;
        Type type = value is Type t ? t : value.GetType();
        object[] attributes = type.GetCustomAttributes(this.AttributeType, false);
        if (attributes == null || attributes.Count() == 0)
            return DependencyProperty.UnsetValue;
        return attributes[0];

    }
}
