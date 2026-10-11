namespace G.Controls.Form.Attributes;

[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public class PropertyAttribute : Attribute
{
    public bool UsePresenter { get; set; }
}
