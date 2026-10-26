using System;
using System.Threading;

namespace VisionMaster.ScadaWeb
{
    /// <summary>
    /// 运行态 Web 的<b>操作权租约</b>（M2b，C77）。
    ///
    /// <b>解决什么问题</b>
    /// ---------
    /// 按权限 Key 判权（B3）之后，"有同一个 Key 的多个人可以同时点"是默认行为；
    /// 多人同时改同一台设备时，"最后一下覆盖前一下"是合法的，但现场常常需要
    /// "同一时刻只有一个人能操作"。租约就是一把<b>单写者令牌</b>：只有持有者能通过
    /// <see cref="ScadaWebAccessPolicy.CanOperate"/>，其他人拿到 <c>E_LEASE_HELD</c>
    /// 和当前持有者名字。
    ///
    /// <b>为什么是 60 秒 TTL</b>
    /// ---------
    /// 浏览器可能直接关标签页/断网，服务端收不到 release。没有 TTL 的租约会被一个已经离线的
    /// 客户端永久占住；60 秒与方案 §9.4 的 TTL 口径一致，续租由客户端在操作期间显式发
    /// <c>lease.renew</c>。
    ///
    /// <b>线程</b>：命令面在 UI 线程执行，但 lease.* 消息可能从 WS 接收线程直接进来；
    /// 所有读写都在 <see cref="_gate"/> 内，快照返回不可变 record。
    /// </summary>
    public sealed class ScadaWebLease
    {
        /// <summary>租约有效期（与方案 §9.4 一致）</summary>
        public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

        private readonly object _gate = new();
        private readonly Func<DateTime> _utcNow;
        private ScadaWebSession? _holder;
        private DateTime _expiresAtUtc;

        public ScadaWebLease(Func<DateTime>? utcNow = null)
            => _utcNow = utcNow ?? (() => DateTime.UtcNow);

        /// <summary>取得租约；已被他人持有时返回 false，<paramref name="error"/> 带持有者显示名</summary>
        public bool TryAcquire(ScadaWebSession session, out string? error)
        {
            error = null;

            if (session == null)
            {
                error = "会话为空，不能取得租约";
                return false;
            }

            lock (_gate)
            {
                var now = _utcNow();
                PruneLocked(now);

                if (_holder != null && !ReferenceEquals(_holder, session))
                {
                    error = $"操作权租约由「{DisplayActor(_holder)}」持有（{RemainingSeconds(now)} 秒后到期）";
                    return false;
                }

                _holder = session;
                _expiresAtUtc = now + Ttl;
                return true;
            }
        }

        /// <summary>续租；只有当前持有者能续，过期后按 acquire 处理</summary>
        public bool TryRenew(ScadaWebSession session, out string? error)
        {
            error = null;

            if (session == null)
            {
                error = "会话为空，不能续租";
                return false;
            }

            lock (_gate)
            {
                var now = _utcNow();
                PruneLocked(now);

                if (_holder != null && !ReferenceEquals(_holder, session))
                {
                    error = $"操作权租约由「{DisplayActor(_holder)}」持有";
                    return false;
                }

                _holder = session;
                _expiresAtUtc = now + Ttl;
                return true;
            }
        }

        /// <summary>释放租约；非持有者调用是幂等空操作（不报错，避免"踢租约"语义）</summary>
        public void Release(ScadaWebSession? session)
        {
            if (session == null) return;

            lock (_gate)
            {
                if (ReferenceEquals(_holder, session))
                {
                    _holder = null;
                    _expiresAtUtc = default;
                }
            }
        }

        /// <summary>当前会话是否持有有效租约</summary>
        public bool IsHolder(ScadaWebSession? session)
        {
            if (session == null) return false;

            lock (_gate)
            {
                var now = _utcNow();
                PruneLocked(now);
                return ReferenceEquals(_holder, session);
            }
        }

        /// <summary>当前持有者的显示名（没有/过期 = null）</summary>
        public string? HolderName
        {
            get
            {
                lock (_gate)
                {
                    PruneLocked(_utcNow());
                    return _holder == null ? null : DisplayActor(_holder);
                }
            }
        }

        /// <summary>不可变快照（caps / presence / 回执用）</summary>
        public ScadaWebLeaseSnapshot Snapshot()
        {
            lock (_gate)
            {
                var now = _utcNow();
                PruneLocked(now);

                return _holder == null
                    ? new ScadaWebLeaseSnapshot(false, null, null, 0)
                    : new ScadaWebLeaseSnapshot(
                        true,
                        DisplayActor(_holder),
                        _holder.SessionRef,
                        RemainingSeconds(now));
            }
        }

        /// <summary>停服清空</summary>
        public void Clear()
        {
            lock (_gate)
            {
                _holder = null;
                _expiresAtUtc = default;
            }
        }

        private void PruneLocked(DateTime now)
        {
            if (_holder != null && now >= _expiresAtUtc)
            {
                _holder = null;
                _expiresAtUtc = default;
            }
        }

        private int RemainingSeconds(DateTime now)
            => _holder == null ? 0 : Math.Max(0, (int)Math.Ceiling((_expiresAtUtc - now).TotalSeconds));

        private static string DisplayActor(ScadaWebSession session)
            => string.IsNullOrWhiteSpace(session.UserName) ? "未登录" : session.UserName!;
    }

    /// <summary>租约快照（caps / presence / lease.* 回执）</summary>
    /// <param name="Held">是否有人持有</param>
    /// <param name="Holder">持有者显示名（脱敏后的用户名）</param>
    /// <param name="HolderSessionRef">持有者会话引用（16 hex，不泄漏 SessionId）</param>
    /// <param name="RemainingSeconds">剩余秒数（0 = 无持有/已过期）</param>
    public readonly record struct ScadaWebLeaseSnapshot(
        bool Held,
        string? Holder,
        string? HolderSessionRef,
        int RemainingSeconds);
}
