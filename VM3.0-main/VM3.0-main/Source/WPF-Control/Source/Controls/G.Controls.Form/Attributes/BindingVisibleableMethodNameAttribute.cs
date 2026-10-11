namespace G.Controls.Form.Attributes;

[AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
public class BindingVisiblableMethodNameAttribute : Attribute
{
    public BindingVisiblableMethodNameAttribute(string methodName)
    {
        this.MethodName = methodName;
    }

    public string MethodName { get; private set; }
}
