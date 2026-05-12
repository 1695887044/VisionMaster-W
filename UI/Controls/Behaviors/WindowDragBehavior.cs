using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows;
using Microsoft.Xaml.Behaviors;

namespace UI.Behaviors
{
    public static class WindowDragBehavior
    {
        public static readonly DependencyProperty IsWindowDraggableProperty =
            DependencyProperty.RegisterAttached(
                "IsWindowDraggable",
                typeof(bool),
                typeof(WindowDragBehavior),
                new UIPropertyMetadata(false, OnIsWindowDraggableChanged));

        public static bool GetIsWindowDraggable(DependencyObject obj) => (bool)obj.GetValue(IsWindowDraggableProperty);
        public static void SetIsWindowDraggable(DependencyObject obj, bool value) => obj.SetValue(IsWindowDraggableProperty, value);

        private static void OnIsWindowDraggableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is UIElement element)
            {
                if ((bool)e.NewValue)
                {
                    element.MouseLeftButtonDown += Element_MouseLeftButtonDown;
                }
                else
                {
                    element.MouseLeftButtonDown -= Element_MouseLeftButtonDown;
                }
            }
        }

        private static void Element_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is DependencyObject element)
            {
                // 顺藤摸瓜，找到当前这个控件所属的最顶层 Window
                Window parentWindow = Window.GetWindow(element);

                // 只有在鼠标左键确实按下的状态下，才调用原生的 DragMove
                if (parentWindow != null && e.LeftButton == MouseButtonState.Pressed)
                {
                    parentWindow.DragMove();
                }
            }
        }
    }
}
