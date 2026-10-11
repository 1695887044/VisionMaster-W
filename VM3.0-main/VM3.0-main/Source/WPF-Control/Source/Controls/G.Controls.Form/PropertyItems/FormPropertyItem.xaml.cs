global using System.Reflection;

namespace G.Controls.Form.PropertyItems;

public class FormPropertyItem : ObjectPropertyItem<object>
{
    public FormPropertyItem(PropertyInfo property, object obj) : base(property, obj)
    {

    }
}
