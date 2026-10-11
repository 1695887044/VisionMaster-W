using System.Globalization;

namespace G.ValueConverter;

public abstract class DependencyConverterBase : DependencyObject, IValueConverter
{
    public abstract object Convert(object value, Type targetType, object parameter, CultureInfo culture);
    public abstract object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture);
}
