namespace G.Services.Identity.Author;

public interface IAuthority
{
    string ID { get; }
    string Name { get; }
    string GroupName { get; }
    bool IsAuthority { get; }
}