using G.Controls.Form.PropertyItem.Base;

namespace G.Controls.Form.PropertyItem.ComboBoxPropertyItems
{
    public class SelectedIndexComboBoxPropertyItem : SelectSourcePropertyItem<object>, IHitTestPropertyViewItem
    {
        public SelectedIndexComboBoxPropertyItem(PropertyInfo property, object obj) : base(property, obj)
        {

        }
    }

}
