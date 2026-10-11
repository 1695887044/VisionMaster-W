namespace G.Controls.Form.Attributes;

[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public class UnitAttribute : System.Attribute
{
    public UnitAttribute(string unit)
    {
        this.Unit = unit;
    }
    public string Unit { get; }
}
