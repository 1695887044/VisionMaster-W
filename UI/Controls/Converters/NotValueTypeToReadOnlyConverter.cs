using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UI.Converters
{
    public class NotValueTypeToReadOnlyConverter : BaseMarkupConverter
    {
        public override object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Type dataType)
            {
                bool isValueTypeOrString = dataType.IsValueType || dataType == typeof(string);
                return !isValueTypeOrString; // 不是值类型，就锁死 (ReadOnly = true)
            }
            return true;
        }
    }
}
