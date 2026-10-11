using G.DataBases.Share;

#if NETFRAMEWORK
using System.Data.Entity;
#endif

#if NETCOREAPP
using Microsoft.EntityFrameworkCore;
#endif
namespace G.DataBases.Sqlite
{
    public class SqliteDbConnectService<TDbContext> : DbConnectServiceBase<TDbContext> where TDbContext : DbContext
    {
        protected override IDbSettable GetSetting()
        {
            return SqliteSettable.Instance;
        }
    }
}
