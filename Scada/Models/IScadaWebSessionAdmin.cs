using System;
using System.Collections.Generic;

namespace VisionMaster.Scada
{
    /// <summary>在线 Web 会话（B3：身份是组 + 权限快照，不再有角色）。</summary>
    public sealed record ScadaWebSessionInfo(
        string SessionRef,
        string? UserName,
        string GroupId,
        string GroupName,
        IReadOnlyList<string> Permissions,
        string? RemoteIp,
        DateTime ConnectedAtUtc,
        DateTime LastSeenUtc,
        bool ReadOnly,
        bool CanOperate);

    /// <summary>在线会话管理口（Web 运行态模块实现，桌面端消费）。</summary>
    public interface IScadaWebSessionAdmin
    {
        bool IsAvailable { get; }
        IReadOnlyList<ScadaWebSessionInfo> ListOnline();
        bool TryKick(string sessionRef);
        int KickAll();

        /// <summary>撤销该组全部 Web 会话；返回撤销数量。</summary>
        int RevokeGroup(string groupId);
    }
}
