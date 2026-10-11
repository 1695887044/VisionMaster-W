namespace G.Controls.Form.Attributes;

[AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
public class BindingVisibleablePropertyNameAttribute : Attribute
{
    public BindingVisibleablePropertyNameAttribute(string propertyName)
    {
        this.PropertyName = propertyName;
    }

    public string PropertyName { get; private set; }
}

