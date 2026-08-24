using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace UI.Converters
{
    public class InverseBoolConverter : BaseMarkupConverter
    {

        public override object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is bool b ? !b : true;
    }
}
