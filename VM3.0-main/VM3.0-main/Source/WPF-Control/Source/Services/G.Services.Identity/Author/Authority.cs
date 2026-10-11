namespace G.Services.Identity.Author;

public class Authority : IAuthority
{
    public string ID { get; set; }
    public string Name { get; set; }
    public string GroupName { get; set; }
    public bool IsAuthority { get; set; }
}