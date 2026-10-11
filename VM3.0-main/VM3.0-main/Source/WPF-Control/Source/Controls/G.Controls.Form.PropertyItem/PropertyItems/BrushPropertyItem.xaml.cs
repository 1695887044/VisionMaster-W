using System.Windows.Media;

namespace G.Controls.Form.PropertyItem.PropertyItems
{
    public class BrushPropertyItem : ObjectPropertyItem<SolidColorBrush>
    {
        public BrushPropertyItem(PropertyInfo property, object obj) : base(property, obj)
        {
        }
    }
}
