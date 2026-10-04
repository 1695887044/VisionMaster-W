using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace UI.CustomControl.PropertyGrid
{
    /// <summary>
    /// 校验拦截器：读取属性上的 <see cref="ValidationBaseAttribute"/>，失焦时校验并就地显示错误。
    ///
    /// 【为什么单独成文件】原先它藏在 FlatLayoutProcessor.cs 的末尾，
    /// 而它是**与布局无关**的处理器 —— 放在布局文件里，读代码的人会以为扁平面板才有校验。
    /// </summary>
    public class ValidationProcessor : IControlProcessor
    {
        public void Execute(ControlContext context)
        {
            var validators = context.Property.GetCustomAttributes<ValidationBaseAttribute>().ToList();
            if (validators.Count == 0) return;

            var errorText = new TextBlock
            {
                FontSize = 10,
                Foreground = Brushes.Red,
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(2, 2, 0, 0),
            };

            if (context.WrapPanel is StackPanel sp) sp.Children.Add(errorText);

            if (context.Control is not TextBox tb) return;

            RoutedEventHandler onLostFocus = (s, e) =>
            {
                var firstError = validators.FirstOrDefault(v => !v.IsValid(tb.Text));
                if (firstError != null)
                {
                    tb.BorderBrush = Brushes.Red;
                    errorText.Text = firstError.ErrorMessage;
                    errorText.Visibility = Visibility.Visible;
                }
                else
                {
                    tb.ClearValue(Control.BorderBrushProperty);
                    errorText.Visibility = Visibility.Collapsed;
                }
            };

            tb.LostFocus += onLostFocus;
            // 注册清理：重绘时解绑，否则旧 TextBox 会被事件链一直拽着不释放
            context.RegisterCleanup?.Invoke(() => tb.LostFocus -= onLostFocus);
        }
    }
}
