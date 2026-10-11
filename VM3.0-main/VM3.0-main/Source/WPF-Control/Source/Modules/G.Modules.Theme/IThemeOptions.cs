using G.Themes.Backgrounds;
using G.Themes.FontSizes;
using G.Themes.Layouts;
using System.Windows.Media;

namespace G.Modules.Theme;
public interface IThemeOptions : IIconFontFamilysOptions, IColorThemeOptions
{
    IBackgroundResource BackgroundResource { get; set; }
    List<IBackgroundResource> BackgroundResources { get; }
    FontFamily FontFamily { get; set; }
    List<FontFamily> FontFamilys { get; }
    FontSizeThemeType FontSize { get; set; }
    LayoutThemeType Layout { get; set; }
}
