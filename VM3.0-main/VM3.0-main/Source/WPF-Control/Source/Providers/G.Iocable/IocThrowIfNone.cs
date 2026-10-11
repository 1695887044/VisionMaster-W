namespace G.Iocable;

public abstract class IocThrowIfNone<Interface>
{
    public static Interface Instance => Ioc.GetService<Interface>();
}
