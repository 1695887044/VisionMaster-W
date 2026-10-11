namespace G.Extensions.Unit
{
    public interface IUnitable<T> where T : IComparable<T>
    {
        string ToString(T value);
        T Parse(string str);
    }

}

