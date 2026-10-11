using System.Collections;

namespace G.ValueConverter.Visiblilitys;

public class GetItemInListToVisibilityConverter : MarkupValueConverterBase
{
    public Visibility Visibility { get; set; } = Visibility.Visible;
    public override object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (parameter is IList list)
        {
            if (list.Contains(value))
                return this.Visibility;
        }
        return this.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    }
}
