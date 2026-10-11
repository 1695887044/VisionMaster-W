using G.ValueConverter;
using System.Globalization;
using System.Windows;

namespace G.Controls.PagerBox
{
    public class GetPageIndexsConverter : MarkupValueConverterBase
    {
        public override object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int v)
                return Enumerable.Range(1, v);
            return DependencyProperty.UnsetValue;
        }
    }
}
