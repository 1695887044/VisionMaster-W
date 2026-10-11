#if NETFRAMEWORK
using System.Data.Entity;
#endif

#if NETCOREAPP
#endif
namespace G.DataBases.Share
{
    public interface IDbSettable
    {
        string GetConnect();
    }
}
