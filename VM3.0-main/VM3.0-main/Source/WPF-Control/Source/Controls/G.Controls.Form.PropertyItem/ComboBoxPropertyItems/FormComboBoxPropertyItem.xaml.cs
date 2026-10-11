using G.Controls.Form.PropertyItem.Base;

namespace G.Controls.Form.PropertyItem.ComboBoxPropertyItems
{
    public class FormComboBoxPropertyItem : SelectSourcePropertyItem<object>, IHitTestPropertyViewItem
    {
        public FormComboBoxPropertyItem(PropertyInfo property, object obj) : base(property, obj)
        {

        }
    }
}
