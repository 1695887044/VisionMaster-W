using System.Windows.Media;

namespace G.Modules.Theme;

public interface IIconFontFamilysOptions
{
    FontFamily IconFontFamily { get; set; }
    List<FontFamily> IconFontFamilys { get; }
}