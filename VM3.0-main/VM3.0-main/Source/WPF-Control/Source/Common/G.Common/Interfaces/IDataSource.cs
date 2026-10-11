global using System.Collections.Generic;

namespace G.Common.Interfaces;

public interface IDataSource<T>
{
    IEnumerable<T> Collection { get; }
    void Add(params T[] ts);
    void Delete(params T[] ts);
    event EventHandler CollectionChanged;
}