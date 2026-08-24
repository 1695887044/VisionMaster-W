using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace UI.AttachedProperties
{
    public static class WindowExtensions
    {
        public static readonly DependencyProperty IsAutoWireCommandsProperty =
            DependencyProperty.RegisterAttached(
                "IsAutoWireCommands",
                typeof(bool),
                typeof(WindowExtensions),
                new PropertyMetadata(false, OnIsAutoWireCommandsChanged));

        public static void SetIsAutoWireCommands(DependencyObject element, bool value)
        {
            element.SetValue(IsAutoWireCommandsProperty, value);
        }

        public static bool GetIsAutoWireCommands(DependencyObject element)
        {
            return (bool)element.GetValue(IsAutoWireCommandsProperty);
        }

        private static void OnIsAutoWireCommandsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is Window window && (bool)e.NewValue)
            {
                window.CommandBindings.Clear();

                window.CommandBindings.Add(new CommandBinding(SystemCommands.CloseWindowCommand, (s, args) => SystemCommands.CloseWindow(window)));
                window.CommandBindings.Add(new CommandBinding(SystemCommands.MaximizeWindowCommand, (s, args) =>
                window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized));
                window.CommandBindings.Add(new CommandBinding(SystemCommands.MinimizeWindowCommand, (s, args) => SystemCommands.MinimizeWindow(window)));
                window.CommandBindings.Add(new CommandBinding(SystemCommands.RestoreWindowCommand, (s, args) => SystemCommands.RestoreWindow(window)));
            }
        }
    }
}
