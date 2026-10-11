namespace G.Controls.Form.PropertyItems;

public class BoolPropertyItem : ObjectPropertyItem<bool>
{
    public BoolPropertyItem(PropertyInfo property, object obj) : base(property, obj)
    {
    }
}

public class BoolNullablePropertyItem : ObjectPropertyItem<bool?>
{
    public BoolNullablePropertyItem(PropertyInfo property, object obj) : base(property, obj)
    {
    }
}
