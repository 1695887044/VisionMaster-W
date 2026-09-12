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
    public class EnumConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // value:ViewModel.Property,enum,Apple
            // parameter:ConverterParameter,enum,Banana
            return value.Equals(parameter);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // value:bool,RadioButton.IsChecked,value
            // parameter:ConverterParameter,enum,Banana
            if (value is bool b && b)
                return parameter;
            return Binding.DoNothing;
        }
    }

}
