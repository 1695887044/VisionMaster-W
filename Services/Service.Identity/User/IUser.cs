

namespace Service.Identity.User
{
    public interface  IUser
    {
        string ID { get; }
        string Account { get; set; }
        string Password { get; set; }
        string Name { get; set; }
        bool IsValid(string authorId);
    }
}
