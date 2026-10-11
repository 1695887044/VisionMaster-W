using G.Iocable;
using G.Extensions.Mvvm.ViewModels.Base;

namespace G.Extensions.IocPresenter;

public abstract class IocDisplayBindableBase<T, Interface> : DisplayBindableBase where T : class, Interface, new()
{
    public static T Instance => Ioc.GetService<Interface>() as T;
}
