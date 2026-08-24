using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace UI.CustomControl.PropertyGrid
{
    #region ====== 4. 自定义嵌套类型生成器 (兜底) ======

    public class TypeGenerator : IControlGenerator
    {
        public int Priority => 0; // 最低优先级，作为兜底

        public bool CanProcess(PropertyInfo prop, Type targetType, bool isReadOnly) =>
            prop.GetCustomAttribute<PropertyItemAttribute>() != null;

        public FrameworkElement Create(PropertyInfo prop, object bindingSource, bool isReadOnly)
        {
            var att = prop.GetCustomAttribute<PropertyItemAttribute>();
            if (att != null)
            {
                try
                {
                    // 实例化你在特性中指定的自定义控件 (如 UserControl)
                    if (Activator.CreateInstance(att.Type) is FrameworkElement frm)
                    {
                        // 🚨 核心逻辑：嵌套对象的上下文必须绑定到它自身的值，而不是它的父级 BindingObject
                        frm.DataContext = bindingSource;
                        return frm;
                    }
                }
                catch (Exception ex)
                {
                    return new TextBlock { Text = $"构建失败: {ex.Message}", Foreground = Brushes.Red, TextWrapping = TextWrapping.Wrap };
                }
            }

            return new TextBlock { Text = $"未配置渲染器: {prop.PropertyType.Name}", Foreground = Brushes.Orange };
        }
    }

    #endregion
}
