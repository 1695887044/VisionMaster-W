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
                    var label = context.RootCellGrid.Children.OfType<Border>().FirstOrDefault()?.Child as StackPanel;
                    var textBlock = label?.Children.OfType<TextBlock>().FirstOrDefault(t => t.Text != " *"); // 避开红星
                    if (textBlock != null)
                    {
                        textBlock.Inlines.Add(new Run { Text = " 🔒", Foreground = Brushes.Red });
                        textBlock.ToolTip = $"权限不足！需要：{attr.RequiredRole}";
                    }
                }
            }
        }
    }
}
