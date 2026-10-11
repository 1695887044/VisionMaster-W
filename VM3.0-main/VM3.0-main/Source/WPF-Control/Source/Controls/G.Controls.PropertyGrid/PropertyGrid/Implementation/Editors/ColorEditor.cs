// Copyright © 2024 By G(https://github.com/G) https://github.com/G/WPF-Control

using System.Windows;

namespace G.Controls.PropertyGrid
{
    public class ColorEditor : TypeEditor<ColorPicker>
    {
        protected override ColorPicker CreateEditor()
        {
            return new PropertyGridEditorColorPicker();
        }

        protected override void SetControlProperties(PropertyItem propertyItem)
        {
            this.Editor.BorderThickness = new System.Windows.Thickness(0);
            this.Editor.DisplayColorAndName = true;
        }
        protected override void SetValueDependencyProperty()
        {
            this.ValueProperty = ColorPicker.SelectedColorProperty;
        }
    }

    public class PropertyGridEditorColorPicker : ColorPicker
    {
        static PropertyGridEditorColorPicker()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(PropertyGridEditorColorPicker), new FrameworkPropertyMetadata(typeof(PropertyGridEditorColorPicker)));
        }
    }
}

