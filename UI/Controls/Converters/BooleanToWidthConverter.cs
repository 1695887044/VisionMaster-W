using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace UI.Converters
{
    [ValueConversion(typeof(bool), typeof(double))]
    public class BooleanToWidthConverter : BaseMarkupConverter
    {
        public override object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {

            if (value is bool isTrue && parameter is string widthStr && double.TryParse(widthStr, out double width))
            {
                return isTrue ? 0 : width;
            }
            return 0;
        }
    }
}
