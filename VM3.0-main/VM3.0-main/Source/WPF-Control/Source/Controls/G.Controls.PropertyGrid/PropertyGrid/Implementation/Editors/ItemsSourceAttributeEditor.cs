// Copyright © 2024 By G(https://github.com/G) https://github.com/G/WPF-Control

using System;


namespace G.Controls.PropertyGrid
{
    public class ItemsSourceAttributeEditor : TypeEditor<System.Windows.Controls.ComboBox>
    {
        private readonly ItemsSourceAttribute _attribute;

        public ItemsSourceAttributeEditor(ItemsSourceAttribute attribute)
        {
            _attribute = attribute;
        }

        protected override void SetValueDependencyProperty()
        {
            this.ValueProperty = System.Windows.Controls.ComboBox.SelectedValueProperty;
        }

        protected override System.Windows.Controls.ComboBox CreateEditor()
        {
            return new PropertyGridEditorComboBox();
        }

        protected override void ResolveValueBinding(PropertyItem propertyItem)
        {
            SetItemsSource();
            base.ResolveValueBinding(propertyItem);
        }

        protected override void SetControlProperties(PropertyItem propertyItem)
        {
            this.Editor.DisplayMemberPath = "DisplayName";
            this.Editor.SelectedValuePath = "Value";
        }

        private void SetItemsSource()
        {
            this.Editor.ItemsSource = CreateItemsSource();
        }

        private System.Collections.IEnumerable CreateItemsSource()
        {
            object instance = Activator.CreateInstance(_attribute.Type);
            return (instance as IItemsSource).GetValues();
        }
    }
}

