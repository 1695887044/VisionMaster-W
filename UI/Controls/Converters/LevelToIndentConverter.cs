using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace UI.Converters
{
    [ValueConversion(typeof(int), typeof(double))]
    public class LevelToIndentConverter : BaseMarkupConverter
    {
        public override object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {

            if (value is int level)
            {
                return level * 22;
            }
            return 0;
        }
    }
}
