namespace G.Themes.Colors;

public interface IColorResource : IResourceable
{
    string GroupName { get; }
    bool IsDark { get; set; }
}