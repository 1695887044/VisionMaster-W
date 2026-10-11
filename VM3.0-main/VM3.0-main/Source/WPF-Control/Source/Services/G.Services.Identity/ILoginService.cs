namespace G.Services.Identity;

public interface ILoginService
{
    IUser User { get; }
    bool Login(string name, string password, out string message);
    bool Logout(out string message);
}