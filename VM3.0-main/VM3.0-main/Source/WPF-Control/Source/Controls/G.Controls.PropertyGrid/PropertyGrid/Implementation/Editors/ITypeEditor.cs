// Copyright © 2024 By G(https://github.com/G) https://github.com/G/WPF-Control

using System.Windows;

namespace G.Controls.PropertyGrid
{
    public interface ITypeEditor
    {
        FrameworkElement ResolveEditor(PropertyItem propertyItem);
    }
}

