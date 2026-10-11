using G.DataBases.Sqlite;
using G.Modules.Identity;
using G.Modules.Login;

namespace G.ApplicationBases.Identify;
public interface IDefaultIndentifyOptions
{
    void UseIdentifyOptions(Action<IIdentifyOptions> action);
    void UseLoginOptions(Action<ILoginOptions> action);
    void UseRegistorOptions(Action<IRegistorOptions> action);
    void UseSqliteSettable(Action<ISqliteSettable> action);
}