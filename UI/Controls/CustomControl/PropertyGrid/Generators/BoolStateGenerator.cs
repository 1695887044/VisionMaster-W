using System;
using System.Collections.Generic;
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
    #region 布尔状态生成器 (修正按钮 Style 寻找方式)
    public class BoolStateGenerator : IControlGenerator
    {
        public int Priority => 100;
        public bool CanProcess(PropertyInfo prop, Type targetType, bool isReadOnly) => targetType == typeof(bool);

        public FrameworkElement Create(PropertyInfo prop, object bindingSource, bool isReadOnly)
        {
            var cmdAttr = prop.GetCustomAttribute<CommandAttribute>();

            if (cmdAttr != null)
            {
                var btn = new Button { Content = "执 行" }; // 给个默认文字

                // ✅ 核心修复：从主题中寻找扁平按钮 Style 并应用
                // (如果找不到自定义资源 M.S.Button1，则兜底使用定义的扁平高亮 Style)
                var customStyle = (Application.Current.TryFindResource("M.S.Button1") ??
                                   Application.Current.TryFindResource("FlatButtonVariantStyle")) as Style;

                if (customStyle != null)
                {
                    btn.Style = customStyle;
                }
                else
                {
                    // 兜底中的兜底外观
                    btn.Padding = new Thickness(15, 6, 15, 6);
                    btn.Background = new SolidColorBrush(Color.FromRgb(64, 158, 255)); // 蓝色
                    btn.Foreground = Brushes.White;
                    btn.BorderThickness = new Thickness(0);
                    btn.Resources.Add(typeof(Border), new Style(typeof(Border)) { Setters = { new Setter(Border.CornerRadiusProperty, new CornerRadius(4)) } });
                }

                return btn;
            }

            // 开关状态
            var toggle = new ToggleButton { IsEnabled = !isReadOnly, Style = (Style)Application.Current.TryFindResource("Grid_SwitchToggleStyle") };
            ControlBindHelper.SetTwoWayBinding(toggle, ToggleButton.IsCheckedProperty, prop, bindingSource, BindingMode.TwoWay);
            return toggle;
        }
    }
    #endregion
}
