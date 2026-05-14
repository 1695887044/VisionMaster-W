using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UI.Converters
{
    public class GreaterThanZeroConverter : BaseMarkupConverter
    {
        public override object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is long l) return l > 0;
            if (value is int i) return i > 0;
            return false;
        }
    }
}
