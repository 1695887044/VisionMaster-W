#if NETFRAMEWORK
using System.Data.Entity;
#endif

#if NETCOREAPP
#endif
using G.DataBases.Share;
using G.Services.Setting;
using System.ComponentModel.DataAnnotations;

namespace G.DataBases.Sqlite
{
    public class SqliteSettable : SqliteSettableBase<SqliteSettable>, ISqliteSettable, IDbSettable
    {

    }
}
