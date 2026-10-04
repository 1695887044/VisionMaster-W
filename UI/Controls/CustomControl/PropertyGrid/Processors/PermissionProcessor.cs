using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace UI.CustomControl.PropertyGrid
{

    public class PermissionProcessor : IControlProcessor
    {
        public static string CurrentMockRole = "Admin";

        public void Execute(ControlContext context)
        {
            var attr = context.Property.GetCustomAttribute<PermissionAttribute>();
            if (attr == null) return;

            if (CurrentMockRole != "Admin" && CurrentMockRole != attr.RequiredRole)
            {
                if (attr.HideIfDenied)
                {
                    context.RootCellGrid.Visibility = Visibility.Collapsed;
                }
                else
                {
                    context.Control.IsEnabled = false;
                    context.Control.Opacity = 0.4;
                    var textBlock = FindLabelTextBlock(context);
                    if (textBlock != null)
                    {
                        textBlock.Inlines.Add(new Run { Text = " 🔒", Foreground = Brushes.Red });
                        textBlock.ToolTip = $"权限不足！需要：{attr.RequiredRole}";
                    }
                }
            }
        }

        /// <summary>
        /// 找行标签的 TextBlock。布局处理器跑在本处理器之前，标签已在 RootCellGrid 里，
        /// 但还**不在可视树/逻辑树上**（网格尚未挂进窗口）——只能手动遍历子元素；
        /// 文本匹配显示名（缺省回退属性名），不依赖具体布局层级。
        /// </summary>
        private static TextBlock? FindLabelTextBlock(ControlContext context)
        {
            var display = context.Property.GetCustomAttribute<SuperDisplayAttribute>();
            var wanted = string.IsNullOrEmpty(display?.Name) ? context.Property.Name : display!.Name;

            foreach (var textBlock in EnumerateElements(context.RootCellGrid).OfType<TextBlock>())
                if (textBlock.Text == wanted)
                    return textBlock;

            return null;
        }

        private static System.Collections.Generic.IEnumerable<FrameworkElement> EnumerateElements(FrameworkElement root)
        {
            yield return root;
            switch (root)
            {
                case Panel panel:
                    foreach (var child in panel.Children.OfType<FrameworkElement>())
                        foreach (var el in EnumerateElements(child))
                            yield return el;
                    break;
                case Border border when border.Child is FrameworkElement child:
                    foreach (var el in EnumerateElements(child))
                        yield return el;
                    break;
                case Decorator decorator when decorator.Child is FrameworkElement child:
                    foreach (var el in EnumerateElements(child))
                        yield return el;
                    break;
                case ContentControl content when content.Content is FrameworkElement child:
                    foreach (var el in EnumerateElements(child))
                        yield return el;
                    break;
            }
        }
    }
}
