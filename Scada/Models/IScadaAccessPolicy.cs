using System.Collections.Generic;

namespace VisionMaster.Scada
{
    /// <summary>
    /// 运行态的权限出口（B3）：此刻是谁、属于哪个组、该组有哪些权限、能不能做某个动作。
    ///
    /// 规则只有一条：<c>Can(key)</c> 在 <c>key == Always</c> 时恒真；空/未知 Key 一律拒绝；
    /// 其余按登录时复制的权限快照做精确匹配。**没有 Stop 特例，也没有角色高低包含**。
    /// </summary>
    public interface IScadaAccessPolicy
    {
        /// <summary>当前组 Id；未登录 = 空串。</summary>
        string CurrentGroupId { get; }

        /// <summary>当前组显示名；未登录 = "未登录"。</summary>
        string CurrentGroupName { get; }

        /// <summary>登录时复制的权限快照；未登录 = 空集合。改组后由协调器重新登录刷新。</summary>
        IReadOnlyList<string> Permissions { get; }

        /// <summary>是不是有人登录着（只影响显示与审计署名，不参与权限判定）。</summary>
        bool IsLoggedIn { get; }

        /// <summary>当前操作者显示名；未登录 = "未登录"。</summary>
        string CurrentUserName { get; }

        /// <summary>形如 "admin（模板·管理员）" / "未登录"。</summary>
        string LoginStateText { get; }

        /// <summary>
        /// 能不能执行要求 <paramref name="requiredKey"/> 的动作。
        /// 拒绝时 <paramref name="reason"/> 给一句可直接写日志/回执的原因；放行时为 null。
        /// </summary>
        bool Can(string requiredKey, out string? reason);
    }
}
