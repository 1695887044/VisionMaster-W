namespace G.Extensions.Setting;

public abstract class IocSettableInstance<Setting, Interface> : SettableBase where Setting : class, Interface, new()
{
    public static Setting Instance => (Setting)Ioc.GetService<Interface>();
}
