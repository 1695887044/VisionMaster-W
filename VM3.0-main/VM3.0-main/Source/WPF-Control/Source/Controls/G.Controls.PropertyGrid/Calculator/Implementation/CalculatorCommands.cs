// Copyright © 2024 By G(https://github.com/G) https://github.com/G/WPF-Control

using System.Windows.Input;

namespace G.Controls.PropertyGrid
{
    public static class CalculatorCommands
    {
        private static RoutedCommand _calculatorButtonClickCommand = new RoutedCommand();

        public static RoutedCommand CalculatorButtonClick
        {
            get
            {
                return _calculatorButtonClickCommand;
            }
        }
    }
}

