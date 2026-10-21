using System;
using System.Collections.Generic;
using System.Linq;
using VisionMaster.Models;
using VisionMaster.Scada;

namespace VisionMaster.ScadaWeb
{
    /// <summary>
    /// 在线会话列表（<c>presence</c> 消息 / <c>/api/status</c> 的数据源，M2）。
    ///
    /// <b>为什么单独一个类，而不是直接遍历宿主的 <c>_sessions</c> 字典</b>
    /// ---------
    /// 三处都要这份数据（在线列表、权限策略算"最高角色"、审计/诊断），而宿主的会话字典
    /// 是<b>连接生命周期</b>的私有状态（按 socket.Id 索引、断开即摘）。把它直接暴露出去，
    /// 任何调用方都能顺手改它。这里只提供"登记 / 摘除 / 快照"三件事，内部键仍是服务端
    /// <c>SessionId</c>，但<b>对外 DTO 只给 <c>SessionRef</c></b>。
    ///
    /// <b>不暴露 <c>SessionId</c> 是硬要求</b>
    /// ---------
    /// <c>SessionId</c> 是审计与日志里的服务端标识（32 hex）；<c>SessionRef</c> 才是下发给
    /// 客户端的不透明短标识（16 hex）。两者随机生成、互不可推导。在线列表发给<b>所有</b>
    /// 浏览器，若带 <c>SessionId</c>，等于把审计里那个稳定标识广播出去——一旦有人拿到审计文件，
    /// 就能把"某条操作"对应到"当时那个浏览器"。所以 DTO 里<b>没有</b>这个字段，
    /// 断言按"序列化后不含任何会话的 SessionId 原文"判。
    ///
    /// <b>线程</b>：登记/摘除来自 Watson 的握手与收尾（不同线程），快照来自 HTTP/命令泵。
    /// 全部走一把锁；快照返回副本，调用方拿到的列表不会被后续连接改动。
    /// </summary>
    public sealed class ScadaWebPresence
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, ScadaWebSession> _sessions = new(StringComparer.Ordinal);
        private readonly ScadaWebSettings _settings;

        /// <param name="settings">配置快照（算 <c>canOperate</c> 用；与命令路由共用同一份）</param>
        public ScadaWebPresence(ScadaWebSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        /// <summary>当前在线会话数</summary>
        public int Count
        {
            get { lock (_gate) return _sessions.Count; }
        }

        /// <summary>
        /// 在线会话快照（给权限策略算最高角色）。
        /// 返回<b>副本</b>：策略遍历期间有连接进出不会抛"集合已更改"。
        /// </summary>
        public IReadOnlyList<ScadaWebSession> Sessions
        {
            get
            {
                lock (_gate)
                    return _sessions.Values.ToArray();
            }
        }

        /// <summary>登记一条刚建立的会话（重复登记按同一条处理：以 <c>SessionId</c> 为键）</summary>
        public void Register(ScadaWebSession session)
        {
            if (session == null) return;

            lock (_gate)
                _sessions[session.SessionId] = session;
        }

        /// <summary>摘除一条会话（断开 / 停服）</summary>
        public void Unregister(ScadaWebSession? session)
        {
            if (session == null) return;

            Unregister(session.SessionId);
        }

        /// <summary>按服务端会话 Id 摘除（收尾路径只有 id 时用）</summary>
        public void Unregister(string? sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId)) return;

            lock (_gate)
                _sessions.Remove(sessionId!);
        }

        /// <summary>停服清空（宿主 <c>Stop()</c> 调用）</summary>
        public void Clear()
        {
            lock (_gate)
                _sessions.Clear();
        }

        /// <summary>
        /// 生成在线列表 DTO（<b>按连接时刻升序</b>，同刻按 <c>SessionRef</c> 排——
        /// 排序稳定，两个客户端看到的名次一致，刷新时也不会因为字典序变化而跳行）。
        ///
        /// <c>canOperate</c> 用与命令执行<b>同一个</b>判定核心
        /// （<see cref="ScadaWebAccessPolicy.Evaluate"/>）：列表里显示可操作、点下去却被拒，
        /// 是这类系统里最招人烦的不一致。
        /// </summary>
        public IReadOnlyList<ScadaWebPresenceEntry> Snapshot()
        {
            ScadaWebSession[] sessions;

            lock (_gate)
                sessions = _sessions.Values.ToArray();

            var entries = new List<ScadaWebPresenceEntry>(sessions.Length);

            foreach (var session in sessions.OrderBy(s => s.ConnectedAtUtc).ThenBy(s => s.SessionRef, StringComparer.Ordinal))
            {
                var canOperate = ScadaWebAccessPolicy.Evaluate(
                    session,
                    requiredRole: null,
                    _settings,
                    sessions,
                    out _,
                    out var reason);

                entries.Add(new ScadaWebPresenceEntry
                {
                    // ★ 只给 SessionRef；SessionId 绝不出现（理由见类注释）。
                    SessionRef = session.SessionRef,
                    User = session.UserName,
                    Role = session.Role.ToString(),
                    RoleDisplay = session.Role.DisplayName(),
                    Ip = session.RemoteIp,
                    ConnectedAtUtc = session.ConnectedAtUtc,
                    LastSeenUtc = session.LastSeenUtc,
                    ReadOnly = session.ReadOnly,
                    CanOperate = canOperate,
                    OperateReason = canOperate ? null : reason
                });
            }

            return entries.AsReadOnly();
        }
    }

    /// <summary>
    /// 在线列表的一行（<c>presence</c> 消息的 <c>users[]</c> 元素）。
    ///
    /// <b>字段口径</b>：<see cref="Role"/> 是 <see cref="ScadaRole"/> 的<b>成员名</b>（协议值），
    /// <see cref="RoleDisplay"/> 是中文显示名（界面用）。两者都给，是因为客户端既要按值判颜色，
    /// 又要直接上屏——只给一个的话，前端就得自己维护一份"Engineer = 工程师"的映射表，
    /// 那份表迟早与领域层的 <see cref="ScadaRoleExtensions.DisplayName"/> 分叉。
    /// </summary>
    public sealed class ScadaWebPresenceEntry
    {
        /// <summary>对外会话引用（16 hex）。<b>不是</b> SessionId，反查不出服务端标识</summary>
        public string SessionRef { get; init; } = string.Empty;

        /// <summary>登录用户名；<c>null</c> = 匿名只读观众</summary>
        public string? User { get; init; }

        /// <summary>角色成员名（<c>Operator</c> / <c>Engineer</c> / <c>Administrator</c>）</summary>
        public string Role { get; init; } = string.Empty;

        /// <summary>角色显示名（"操作员" / "工程师" / "管理员"）</summary>
        public string RoleDisplay { get; init; } = string.Empty;

        /// <summary>远端 IP</summary>
        public string Ip { get; init; } = string.Empty;

        /// <summary>连接建立时刻（UTC）</summary>
        public DateTime ConnectedAtUtc { get; init; }

        /// <summary>最近一次收到帧的时刻（UTC）</summary>
        public DateTime LastSeenUtc { get; init; }

        /// <summary>这条会话是否只读（匿名观众 / 签票时命令总闸关着）</summary>
        public bool ReadOnly { get; init; }

        /// <summary>此刻这条会话有没有操作资格（策略层，不含具体图元的 <c>RequiredRole</c>）</summary>
        public bool CanOperate { get; init; }

        /// <summary>不可操作的原因；可操作时为 <c>null</c></summary>
        public string? OperateReason { get; init; }
    }
}
