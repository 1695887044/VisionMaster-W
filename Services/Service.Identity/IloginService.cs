

namespace Service.Identity
{
    public interface IloginService
    {
        /// <summary>
        /// 登录
        /// </summary>
        /// <returns></returns>
        bool LoginAsync();

        /// <summary>
        /// 退出登录
        /// </summary>
        /// <returns></returns>
        bool LogoutAsync();
    }
}
