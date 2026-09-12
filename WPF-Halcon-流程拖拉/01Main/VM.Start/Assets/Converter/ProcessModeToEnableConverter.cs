using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows;
using VM.Start.Common.Enums;

namespace VM.Start.Assets.Converter
{
    public class ProcessModeToEnableConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if ((eProcessMode)value == eProcessMode.旋转焊接)
            {
                return true;
            }
            else
            {
                return false;

            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }
}
