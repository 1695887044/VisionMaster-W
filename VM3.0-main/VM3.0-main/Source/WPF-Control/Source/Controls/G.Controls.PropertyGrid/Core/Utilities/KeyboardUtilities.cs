// Copyright © 2024 By G(https://github.com/G) https://github.com/G/WPF-Control

using System.Windows.Input;

namespace G.Controls.PropertyGrid
{
    internal class KeyboardUtilities
    {
        internal static bool IsKeyModifyingPopupState(KeyEventArgs e)
        {
            return (((Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt) && ((e.SystemKey == Key.Down) || (e.SystemKey == Key.Up)))
                  || (e.Key == Key.F4);
        }
    }
}

