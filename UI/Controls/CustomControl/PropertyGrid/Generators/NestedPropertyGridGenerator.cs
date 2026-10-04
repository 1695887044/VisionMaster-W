using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace UI.CustomControl.PropertyGrid
{
    /// <summary>
    /// 🌟 递归嵌套生成器：当检测到属性是一个复杂的嵌套对象时，
    /// 自动创建并嵌入一个子 PropertyGrid，实现深度反射。
    /// </summary>
    public class NestedPropertyGridGenerator : IControlGenerator
    {
        // 优先级设为 10，高于普通的 TypeGenerator，确保优先拦截
        public int Priority => 10;

        /// <summary>宿主网格，用于继承其布局形态（卡片/扁平）。由 PropertyGridBase 构造时注入。</summary>
        public PropertyGridBase? Owner { get; set; }

        public bool CanProcess(PropertyInfo prop, Type targetType, bool isReadOnly)
        {
            var att = prop.GetCustomAttribute<PropertyItemAttribute>();
            return att != null && (att.Type == typeof(Control) || att.Type == typeof(FrameworkElement));
        }

        public FrameworkElement Create(PropertyInfo prop, object bindingSource, bool isReadOnly)
        {
            if (bindingSource == null)
            {
                return new TextBlock
                {
                    Text = "尚未初始化链路参数",
                    Foreground = System.Windows.Media.Brushes.Orange,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 4, 0, 4)
                };
            }

            // 🌟 核心魔法：直接"套娃"创建一个子属性编辑器！
            // 跟随父级布局形态（卡片式也用卡片式，保证视觉一致），并扒掉边框/边距融入父级。
            PropertyGridBase childGrid = Owner is { IsCardLayout: true }
                ? new CardPropertyGrid()
                : new FlatPropertyGrid();

            childGrid.BindingObject = bindingSource;
            childGrid.Background = System.Windows.Media.Brushes.Transparent; // 融入父级背景
            childGrid.BorderThickness = new Thickness(0);                    // 扒掉边框
            childGrid.Padding = new Thickness(0);                            // 去掉内边距

            return childGrid;
        }
    }
}
