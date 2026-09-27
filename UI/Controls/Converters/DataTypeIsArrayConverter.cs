using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace UI.Converters
{
    public class DataTypeIsArrayConverter : BaseMarkupConverter
    {
        public bool IsInverse { get; set; } = false;
        public override object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isArray = false;
            if (value is Type type)
            {
                // 判断是否是数组，或者泛型 List
                isArray = type.IsArray || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>));
            }

            if (IsInverse) isArray = !isArray;

            return isArray ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
