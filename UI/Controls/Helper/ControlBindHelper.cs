using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;

namespace UI.Helper
{
   public static class ControlBindHelper
    {
        public static void SetTwoWayBinding(FrameworkElement element, DependencyProperty dp, PropertyInfo prop, object sourceObject, BindingMode mode = BindingMode.TwoWay)
        {
            var binding = new Binding(prop.Name)
            {
                Source = sourceObject,
                Mode = mode,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            };
            element.SetBinding(dp, binding);

        }
        public static string GetEnumDisplayName(Type enumType, object value)
        {
            var field = enumType.GetField(value.ToString());
            var displayAttr = (DisplayAttribute)
                Attribute.GetCustomAttribute(field, typeof(DisplayAttribute));
            return displayAttr?.Name ?? value.ToString();
        }

    }
}
