using G.Extensions.ApplicationBase;
using G.Modules.Theme;

namespace G.ApplicationBases.Themes
{

    public class DefaultThemeOptions : CacheActionOptionsBase, IDefaultThemeOptions
    {
        public void UseThemeOptions(Action<IThemeOptions> action)
        {
            this.ConfigOptions(action);
        }

        public void UseColorThemeOptions(Action<IColorThemeOptions> action)
        {
            this.ConfigOptions(action);
        }

        public void UseIconFontFamilysOptions(Action<IIconFontFamilysOptions> action)
        {
            this.ConfigOptions(action);
        }
    }
}
