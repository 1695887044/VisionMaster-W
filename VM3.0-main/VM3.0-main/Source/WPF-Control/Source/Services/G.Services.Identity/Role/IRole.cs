namespace G.Services.Identity.Role;

public interface IRole
{
    string ID { get; }
    string Name { get; set; }
    bool IsValid(string authorId);
}