using G.Controls.Form.PropertyItem.Base;

namespace G.Controls.Form.PropertyItem.ComboBoxPropertyItems
{
    public class PresenterComboBoxPropertyItem : SelectSourcePropertyItem<object>, IHitTestPropertyViewItem
    {
        public PresenterComboBoxPropertyItem(PropertyInfo property, object obj) : base(property, obj)
        {

        }
    }
}
