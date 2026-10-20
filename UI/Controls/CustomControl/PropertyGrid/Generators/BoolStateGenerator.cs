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
                var customStyle = (Application.Current?.TryFindResource("M.S.Button1") ??
                                   Application.Current?.TryFindResource("FlatButtonVariantStyle")) as Style;

                if (customStyle != null)
                {
                    btn.Style = customStyle;
                }
                else
                {
                    // 兜底外观：走令牌，不再写死 #409EFF（那是全库第四份强调色）
                    btn.Padding = new Thickness(13, 0, 13, 0);
                    btn.Height = PropertyGridMetrics.ControlHeight - 4;
                    btn.Background = PropertyGridMetrics.Brush("DialogAccentBrush", 14, 165, 233);
                    btn.Foreground = Brushes.White;
                    btn.BorderThickness = new Thickness(0);
                    btn.Resources.Add(typeof(Border), new Style(typeof(Border)) { Setters = { new Setter(Border.CornerRadiusProperty, new CornerRadius(6)) } });
                }

                return btn;
            }

            // 开关状态。
            // 减动效：系统「辅助功能 → 显示动画」关闭（以及远程桌面/低配虚拟机）时改用
            // 无 Storyboard 的静态变体 —— 状态直接切换，零位移零过渡。这类设置运行期极少变，
            // 按"生成时"取一次即可。样式解析失败时 TryFindResource 返回 null，兜底回常规版。
            //
            // Application.Current 可能为 null（设计器 / 断言宿主 / 单元测试）：
            // 本族的其它生成器与 PropertyGridMetrics 一律用 `?.`，这里也必须一样——
            // 否则"没有 Application 的宿主"一渲染带 bool 的草稿就 NRE（2026-10-20 实测）。
            var styleKey = SystemParameters.ClientAreaAnimation
                ? "Grid_SwitchToggleStyle"
                : "FluentToggleSwitchStyleStatic";
            var toggle = new ToggleButton
            {
                IsEnabled = !isReadOnly,
                Style = Application.Current?.TryFindResource(styleKey) as Style
                        ?? Application.Current?.TryFindResource("Grid_SwitchToggleStyle") as Style,
            };
            // v1 基线：开关与输入框同一行高（模板轨道本身 38×22）
            PropertyGridMetrics.ApplySwitchLook(toggle);
            ControlBindHelper.SetTwoWayBinding(toggle, ToggleButton.IsCheckedProperty, prop, bindingSource, BindingMode.TwoWay);

            // 焦点环只认键盘来的焦点（:focus-visible）：鼠标点击不亮环，
            // 否则蓝环叠蓝轨道会被看成"画坏了"。行为直接驱动模板里的 FocusRing。
            UI.Behaviors.FocusVisibleBehavior.SetEnabled(toggle, true);

            // 键盘可达性：Space 是 ToggleButton 自带的；Enter 在这里补上（规格要求两者都能切换）。
            // 每次生成都是新实例，事件跟着实例走，不会累积。
            toggle.KeyDown += (_, e) =>
            {
                if (e.Key != System.Windows.Input.Key.Enter) return;
                toggle.IsChecked = !(toggle.IsChecked ?? false);
                e.Handled = true;
            };
            return toggle;
        }
    }
    #endregion
}
