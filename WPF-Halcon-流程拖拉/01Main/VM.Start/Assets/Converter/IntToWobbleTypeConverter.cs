using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;

namespace VM.Start.Assets.Converter
{
    public class IntToWobbleTypeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if ((int)value == 0)
            {
                return "螺旋线";
            }
            else if ((int)value == 1)
            {
                return "正弦曲线";
            }
            else if ((int)value == 2)
            {
                return "椭圆";
            }
            else if ((int)value == 3)
            {
                return "垂直8字";
            }
            else if ((int)value == 4)
            {
                return "水平8字";
            }
            return "椭圆";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value.ToString() == "螺旋线")
            {
                return 0;
            }
            else if (value.ToString() == "正弦曲线")
            {
                return 1;
            }
            else if (value.ToString() == "椭圆")
            {
                return 2;
            }
            else if (value.ToString() == "垂直8字")
            {
                return 3;
            }
            else if (value.ToString() == "水平8字")
            {
                return 4;
            }
            return 0;
        }
    }

}
