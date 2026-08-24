
namespace Service.Identity
{
    public interface IRegisterService
    {
        bool Register(string mail, string account, string password, out string message);
        bool ResetPassword(string mail, string account, string password, out string message);
    }
}
