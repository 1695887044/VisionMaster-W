using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using UI.Attributes;

namespace UI.CustomControl.PropertyGrid
{
    /// <summary>
    /// 校验拦截器：读取属性上的 <see cref="ValidationBaseAttribute"/>，失焦时校验并就地显示错误。
    ///
    /// 【为什么单独成文件】原先它藏在 FlatLayoutProcessor.cs 的末尾，
    /// 而它是**与布局无关**的处理器 —— 放在布局文件里，读代码的人会以为扁平面板才有校验。
    ///
    /// 【v1 设计基线】错误态从"只给 TextBox 描红边"升级成**行级**：
    ///   · 行容器浅红底（基线 `--pg-danger-soft`）
    ///   · 控件红描边 + 红字
    ///   · 行内错误文案（11.5px、危险色文字令牌、`AutomationProperties.LiveSetting`）
    ///   · **覆盖非 TextBox 控件**：下拉走 SelectionChanged、开关走 Checked/Unchecked、
    ///     数值框走 LostFocus（`NumericBox` 继承 TextBox，天然覆盖）
    ///
    /// 【触发时机】Text 类仍是 LostFocus（打字中途报错会打断输入）；下拉/开关是"选完即定"，
    /// 用变更事件即时反馈。两条口径都不改值，只标状态。
    /// </summary>
    public class ValidationProcessor : IControlProcessor
    {
        public void Execute(ControlContext context)
        {
            var validators = context.Property.GetCustomAttributes<ValidationBaseAttribute>().ToList();
            if (validators.Count == 0) return;

            var errorText = new TextBlock
            {
                FontSize = 11.5,
                Foreground = PropertyGridMetrics.Brush("DialogDangerTextBrush", 185, 28, 28),
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
            };
            // 屏幕阅读器：错误文案出现时主动播报（与 aria-live 等价）
            System.Windows.Automation.AutomationProperties.SetLiveSetting(
                errorText, System.Windows.Automation.AutomationLiveSetting.Polite);

            if (context.WrapPanel is StackPanel sp) sp.Children.Add(errorText);

            // 行容器：LayoutProcessor 把内容放进 RootCellGrid，行底色要染在**行**上。
            // 布局处理器已经跑过，RootCellGrid 里已有那层 Border（扁平式）；
            // 卡片式没有行容器，退化为只染控件 + 文案。
            var rowContainer = FindRowContainer(context.RootCellGrid);
            // 控件在管线里是 FrameworkElement；BorderBrush 在 Control 上。
            // 非 Control 的自定义控件没有边框可染，退化处理。
            var borderTarget = context.Control as Control;

            void ShowError(string message)
            {
                if (rowContainer != null)
                    rowContainer.Background = PropertyGridMetrics.Brush("DialogDangerSubtleBrush", 254, 242, 242);

                if (borderTarget != null)
                    borderTarget.BorderBrush = PropertyGridMetrics.Brush("DialogDangerBrush", 239, 68, 68);

                errorText.Text = message;
                errorText.Visibility = Visibility.Visible;
                context.HasValidationError = true;
            }

            void ClearError()
            {
                if (rowContainer != null)
                    rowContainer.ClearValue(Border.BackgroundProperty);

                borderTarget?.ClearValue(Control.BorderBrushProperty);
                errorText.Visibility = Visibility.Collapsed;
                context.HasValidationError = false;
            }

            void Validate(object? value)
            {
                var firstError = validators.FirstOrDefault(v => !v.IsValid(value));
                if (firstError != null) ShowError(firstError.ErrorMessage);
                else ClearError();
            }

            Action validateNow;
            RoutedEvent? triggerEvent;

            switch (context.Control)
            {
                case TextBox tb:
                    validateNow = () => Validate(tb.Text);
                    triggerEvent = UIElement.LostFocusEvent;
                    break;

                case ComboBox cb:
                    validateNow = () => Validate(cb.Text ?? cb.SelectedValue?.ToString());
                    triggerEvent = Selector.SelectionChangedEvent;
                    break;

                case ToggleButton toggle:
                    validateNow = () => Validate(toggle.IsChecked);
                    triggerEvent = ToggleButton.CheckedEvent;
                    break;

                default:
                    // 自定义控件（TypeGenerator 的 UserControl 等）没有通用取值入口，
                    // 直接按当前属性值校一次，且不挂事件 —— 不猜它的事件语义。
                    Validate(context.Property.GetValue(context.BindingSource));
                    return;
            }

            RoutedEventHandler handler = (_, _) => validateNow();
            if (context.Control is UIElement element)
            {
                element.AddHandler(triggerEvent, handler);

                // 注册清理：重绘时解绑，否则旧控件会被事件链一直拽着不释放
                context.RegisterCleanup?.Invoke(() => element.RemoveHandler(triggerEvent, handler));
            }

            // 开关还有一个"取消勾选"入口（Checked 只在选中时触发）
            if (context.Control is ToggleButton toggleButton)
            {
                RoutedEventHandler uncheckHandler = (_, _) => validateNow();
                toggleButton.AddHandler(ToggleButton.UncheckedEvent, uncheckHandler);
                context.RegisterCleanup?.Invoke(() =>
                    toggleButton.RemoveHandler(ToggleButton.UncheckedEvent, uncheckHandler));
            }

            // 初次渲染就按当前值校一次：绑定对象本来就是非法值时不该等用户操作才报。
            //
            // 【为什么还要挂 Loaded】处理器跑在"单元格刚造好、还没进可视树"的时刻，
            // 此时 TextBox/NumericBox 的 Text 尚未从绑定同步过来（读到的还是空串），
            // 直接校会得到"空值合法"的假结论。Loaded 之后绑定已完成，那一次才是真实值。
            validateNow();
            if (context.Control is FrameworkElement element2)
            {
                RoutedEventHandler loadedHandler = (_, _) => validateNow();
                element2.Loaded += loadedHandler;
                context.RegisterCleanup?.Invoke(() => element2.Loaded -= loadedHandler);
            }
        }

        /// <summary>
        /// 找行容器（扁平式那层带 BorderThickness 的 Border）。布局处理器把它作为
        /// RootCellGrid 的直接子元素塞进去；卡片式没有这一层，返回 null 由调用方降级。
        /// </summary>
        private static Border? FindRowContainer(Grid root)
        {
            foreach (var child in root.Children)
                if (child is Border border && border.Child is Grid)
                    return border;

            return null;
        }
    }
}
