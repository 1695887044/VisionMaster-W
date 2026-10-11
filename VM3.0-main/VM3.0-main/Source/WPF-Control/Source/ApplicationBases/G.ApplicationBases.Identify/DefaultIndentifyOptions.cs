using G.DataBases.Sqlite;
using G.Extensions.ApplicationBase;
using G.Modules.Identity;
using G.Modules.Login;

namespace G.ApplicationBases.Identify
{
    public class DefaultIndentifyOptions : CacheActionOptionsBase, IDefaultIndentifyOptions
    {
        public void UseLoginOptions(Action<ILoginOptions> action)
        {
            this.ConfigOptions(action);
        }

        public void UseIdentifyOptions(Action<IIdentifyOptions> action)
        {
            this.ConfigOptions(action);
        }

        public void UseRegistorOptions(Action<IRegistorOptions> action)
        {
            this.ConfigOptions(action);
        }

        public void UseSqliteSettable(Action<ISqliteSettable> action)
        {
            this.ConfigOptions(action);
        }
    }
}
