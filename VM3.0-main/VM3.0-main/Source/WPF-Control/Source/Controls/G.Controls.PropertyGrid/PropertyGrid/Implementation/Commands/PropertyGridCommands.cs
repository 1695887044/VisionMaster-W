// Copyright © 2024 By G(https://github.com/G) https://github.com/G/WPF-Control

using System.Windows.Input;

namespace G.Controls.PropertyGrid
{
    public class PropertyGridCommands
    {
        private static RoutedCommand _clearFilterCommand = new RoutedCommand();
        public static RoutedCommand ClearFilter
        {
            get
            {
                return _clearFilterCommand;
            }
        }
    }
}

