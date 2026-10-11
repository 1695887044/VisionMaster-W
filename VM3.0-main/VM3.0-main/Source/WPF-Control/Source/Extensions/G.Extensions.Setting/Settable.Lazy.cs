namespace G.Extensions.Setting;

public abstract class LazySettableInstance<T> : SettableBase where T : new()
{
    public static T Instance = new Lazy<T>(() => new T()).Value;
}
