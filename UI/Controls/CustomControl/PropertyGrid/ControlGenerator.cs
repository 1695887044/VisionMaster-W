using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace UI.CustomControl.PropertyGrid
{
    public interface IControlGenerator
    {
        // 优先级
        int Priority { get; }

        // 判断当前属性是否由该生成器处理
        bool CanProcess(PropertyInfo prop, Type targetType, bool ReadOnly = false);

        // 创建控件的具体逻辑
        FrameworkElement Create(PropertyInfo prop, object bindingSource, bool ReadOnly = false);
    }

    /// <summary>
    /// 生成枚举类型的控件
    /// </summary>
    public class EnumGenerator : IControlGenerator
    {
        public int Priority => 100;

        public bool CanProcess(PropertyInfo prop, Type targetType, bool ReadOnly = false)
        {
            return prop.PropertyType.IsEnum ;
        }

        public FrameworkElement Create(
            PropertyInfo prop,
            object bindingSource,
            bool ReadOnly = false
        )
        {
            var comboBox = new ComboBox();
            comboBox.ItemsSource = Enum.GetValues(prop.PropertyType)
                .Cast<object>()
                .Select(value => new
                {
                    Key = value,
                    Value = ControlBindHelper.GetEnumDisplayName(prop.PropertyType, value),
                });
            comboBox.DisplayMemberPath = "Value";
            comboBox.SelectedValuePath = "Key";
            comboBox.IsEnabled = !ReadOnly;
            ControlBindHelper.SetTwoWayBinding(
                comboBox,
                System.Windows.Controls.Primitives.Selector.SelectedValueProperty,
                prop,
                bindingSource
            );
            return comboBox;
        }
    }

    /// <summary>
    /// 生成文本类型控件 针对读可写的字符串和数值类型
    /// </summary>
    public class StructValueGenerator : IControlGenerator
    {
        public int Priority => 100;

        public bool CanProcess(PropertyInfo prop, Type targetType, bool ReadOnly = false)
        {
            if (targetType == typeof(bool))
                return false;

            // 核心判断：字符串或基础数值类型
            bool isBasicType =
                targetType == typeof(string)
                || targetType.IsPrimitive
                || targetType == typeof(decimal);

            return isBasicType;
        }

        public FrameworkElement Create(
            PropertyInfo prop,
            object bindingSource,
            bool ReadOnly = false
        )
        {
            //可读可写
            var att = prop.GetCustomAttribute<RangeAttribute>();
            if (att != null)
            {
                //var ctl = new ModernNumericInput();
                //ctl.Maximum = att.Maximum;
                //ctl.Minimum = att.Minimum;
                //ctl.Increment = att.Increment;
                //ControlGeneratorExtensions.SetTwoWayBinding(
                //    ctl,
                //    ModernNumericInput.ValueProperty,
                //    prop,
                //    bindingSource
                //);
                //return ctl;
            }
            var ctl1 = new TextBox();
            ctl1.IsReadOnly = ReadOnly;
            ControlBindHelper.SetTwoWayBinding(
                ctl1,
                TextBox.TextProperty,
                prop,
                bindingSource
            );
            return ctl1;
        }
    }



    /// <summary>
    /// 针对布尔量状态的生成器  只读 切换开关  CheckBox
    /// </summary>
    public class BoolStateGenerator : IControlGenerator
    {
        public int Priority => 100;

        public bool CanProcess(PropertyInfo prop, Type targetType, bool ReadOnly = false)
        {
            if (targetType != typeof(bool))
                return false;

            return true;
        }

        public FrameworkElement Create(
            PropertyInfo prop,
            object bindingSource,
            bool ReadOnly = false
        )
        {
            //状态显示
            if (ReadOnly)
            {
                //var r_Ctl = new StatusIndicator();
                //ControlGeneratorExtensions.SetTwoWayBinding(
                //    r_Ctl,
                //    StatusIndicator.IsActiveProperty,
                //    prop,
                //    bindingSource,
                //    BindingMode.TwoWay
                //);
                //return r_Ctl;
            }
            var att = prop.GetCustomAttribute<CommandAttribute>();
            //没有命令特性  就生成切换开关
            if (att == null)
            {
                var ctl = new ToggleButton();
                ControlBindHelper.SetTwoWayBinding(
                    ctl,
                    ToggleButton.IsCheckedProperty,
                    prop,
                    bindingSource,
                    BindingMode.TwoWay
                );
                return ctl;
            }
            //命令按钮  
            var ctl2 = new Button();
            ctl2.Style = (Style)Application.Current.FindResource("M.S.Button1");
            return ctl2;
        }
    }

    public class TypeGenerator : IControlGenerator
    {
        PropertyItemAttribute? att;
        public int Priority => 0;

        public bool CanProcess(PropertyInfo prop, Type targetType, bool ReadOnly = true)
        {
            att = prop.GetCustomAttribute<PropertyItemAttribute>();
            return att != null;
        }

        public FrameworkElement Create(
            PropertyInfo prop,
            object bindingSource,
            bool ReadOnly = true
        )
        {
            if (Activator.CreateInstance(att.Type) is FrameworkElement frm)
            {
                frm.DataContext = bindingSource;
                return frm;
            }
            return new TextBlock
            {
                Text = $"Unsupported: {prop.PropertyType.Name}",
                Foreground = Brushes.Red,
            };
        }
    }
}
