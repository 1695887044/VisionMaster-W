#if NETFRAMEWORK
using System.Data.Entity;
#endif

#if NETCOREAPP
#endif

namespace G.DataBases.Sqlite
{
    public interface ISqliteSettable
    {
        string InitialCatalog { get; set; }
        string FilePath { get; set; }
        string ConfigPath { get; set; }
    }
}
