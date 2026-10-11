namespace G.Controls.Form.Attributes;

[AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
public class NotifyMethodNameAttribute : Attribute
{
    public NotifyMethodNameAttribute(string methodName)
    {
        this.MethodName = methodName;
    }

    public string MethodName { get; private set; }
}
