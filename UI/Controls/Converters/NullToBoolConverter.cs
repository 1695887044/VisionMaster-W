using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UI.Converters
{
    /// <summary>
    /// 判断对象是否为空 空的话返回true
    /// </summary>
    public class NullToBoolConverter : BaseMarkupConverter
    {
        public override object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value == null;
        }
    }
}
