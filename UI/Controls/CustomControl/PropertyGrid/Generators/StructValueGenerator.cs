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
                Padding = new Thickness(10, 0, 10, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
            };

            // v1 基线：统一 36px 高 / 8px 圆角 / 令牌配色。
            // 原来这里写死 #DCDFE6 / #606266 / #F5F7FA / #909399 —— 换主题时这几格不跟着变。
            PropertyGridMetrics.ApplyInputLook(textBox);
            if (isReadOnly) PropertyGridMetrics.ApplyReadOnlyLook(textBox);

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
