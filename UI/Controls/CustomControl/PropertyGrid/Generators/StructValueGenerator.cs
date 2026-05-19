using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace UI.CustomControl.PropertyGrid
{
    #region ====== 2. 文本与数值生成器 (增加现代 UI 样式) ======

    public class StructValueGenerator : IControlGenerator
    {
        public int Priority => 100;

        public bool CanProcess(PropertyInfo prop, Type targetType, bool isReadOnly) =>
            targetType != typeof(bool) && (targetType == typeof(string) || targetType.IsPrimitive || targetType == typeof(decimal));

        public FrameworkElement Create(PropertyInfo prop, object bindingSource, bool isReadOnly)
        {
            var textBox = new TextBox
            {
                IsReadOnly = isReadOnly,
                // 🌟 问题排查与修复：告别裸奔，赋予 Web 级输入框质感
                Padding = new Thickness(10, 6, 10, 6),
                VerticalContentAlignment = VerticalAlignment.Center,
                BorderBrush = new SolidColorBrush(Color.FromRgb(220, 223, 230)),
                Background = isReadOnly ? new SolidColorBrush(Color.FromRgb(245, 247, 250)) : Brushes.White,
                Foreground = isReadOnly ? new SolidColorBrush(Color.FromRgb(144, 147, 153)) : new SolidColorBrush(Color.FromRgb(96, 98, 102))
            };

            // 纯代码设置微圆角 (如果是卡片风格，这里尤为重要)
            textBox.Resources.Add(typeof(Border), new Style(typeof(Border))
            {
                Setters = { new Setter(Border.CornerRadiusProperty, new CornerRadius(4)) }
            });

            // 绑定：失去焦点或回车时触发验证
            var binding = new Binding(prop.Name)
            {
                Source = bindingSource,
                Mode = isReadOnly ? BindingMode.OneWay : BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            };
            textBox.SetBinding(TextBox.TextProperty, binding);

            return textBox;
        }
    }

    #endregion

}
