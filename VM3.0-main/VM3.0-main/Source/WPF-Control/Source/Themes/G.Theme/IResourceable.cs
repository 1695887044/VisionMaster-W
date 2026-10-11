namespace G.Themes;

public interface IResourceable
{
    string Name { get; }
    ResourceDictionary Resource { get; }
}
