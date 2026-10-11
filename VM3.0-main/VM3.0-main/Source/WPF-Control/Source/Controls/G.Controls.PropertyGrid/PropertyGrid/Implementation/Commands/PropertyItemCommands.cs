// Copyright © 2024 By G(https://github.com/G) https://github.com/G/WPF-Control

using System.Windows.Input;

namespace G.Controls.PropertyGrid
{
    public static class PropertyItemCommands
    {
        private static RoutedCommand _resetValueCommand = new RoutedCommand();
        public static RoutedCommand ResetValue
        {
            get
            {
                return _resetValueCommand;
            }
        }
    }
}

