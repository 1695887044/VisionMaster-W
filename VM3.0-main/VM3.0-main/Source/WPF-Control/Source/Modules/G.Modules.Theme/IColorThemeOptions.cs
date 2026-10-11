using G.Themes.Colors;

namespace G.Modules.Theme;

public interface IColorThemeOptions
{
    IColorResource ColorResource { get; set; }
    List<IColorResource> ColorResources { get; }
}
