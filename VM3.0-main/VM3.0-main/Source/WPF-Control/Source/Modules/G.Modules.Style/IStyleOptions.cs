using G.Styles.StyleResources;

namespace G.Modules.Style;

public interface IStyleOptions
{
    IStyleResource StyleResource { get; set; }
    List<IStyleResource> StyleResources { get; set; }
}
