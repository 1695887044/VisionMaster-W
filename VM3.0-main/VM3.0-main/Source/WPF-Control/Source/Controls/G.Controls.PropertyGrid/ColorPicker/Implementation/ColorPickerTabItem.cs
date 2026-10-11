// Copyright © 2024 By G(https://github.com/G) https://github.com/G/WPF-Control

using System.Windows.Controls;
using System.Windows.Input;

namespace G.Controls.PropertyGrid
{
    public class ColorPickerTabItem : TabItem
    {
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            if (e.Source == this || !this.IsSelected)
                return;

            base.OnMouseLeftButtonDown(e);
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            //Selection on Mouse Up
            if (e.Source == this || !this.IsSelected)
            {
                base.OnMouseLeftButtonDown(e);
            }

            base.OnMouseLeftButtonUp(e);
        }
    }
}

