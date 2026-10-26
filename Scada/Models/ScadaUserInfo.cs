using System.Collections.Generic;

namespace VisionMaster.Scada
{
    /// <summary>
    /// 校验通过那一刻的身份快照：账号名 + 组 Id + 组显示名 + 该组的权限集合。
    ///
    /// <b>权限快照必须在登录时复制</b>：组后来被改权限，已登录会话不能"自动涨权"；
    /// 协调器会在 <c>GroupChanged</c> 时重新登录/撤销会话，而不是让判定侧每次去查账号本。
    ///
    /// <see cref="GroupName"/> 是<b>显示名</b>（"产线操作员"），只用于上屏与审计；
    /// 判定一律用 <see cref="GroupId"/>——改个组名不该让任何判据失效。
    /// </summary>
    public sealed record ScadaUserInfo(
        string Name,
        string GroupId,
        IReadOnlyList<string> Permissions,
        string GroupName = "");
}
