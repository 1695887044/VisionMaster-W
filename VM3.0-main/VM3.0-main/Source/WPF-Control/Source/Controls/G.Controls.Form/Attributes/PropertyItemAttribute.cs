namespace G.Controls.Form.Attributes;

[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public class PropertyItemAttribute : Attribute
{
    public PropertyItemAttribute(Type type)
    {
        this.Type = type;
    }
    public Type Type { get; private set; }
}
