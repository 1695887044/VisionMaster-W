using System;
using System.Collections.Generic;

namespace VisionMaster.ScadaWeb
{
    /// <summary>
    /// 准入闸：<c>MaxSessions</c> 个活跃会话 + 8 个"票已核销、WS 还没入册"的握手预留槽。
    ///
    /// <b>为什么要把活跃与握手分开</b>
    /// ---------
    /// 取票（HTTP <c>/api/ticket</c>）与 WS 入册是两次往返。只按"活跃会话数"判容量时，
    /// 一批客户端可以同时取票成功、同时卡在握手，第 17 个明明还没占用任何会话却把票核销掉了；
    /// 反过来，如果取票阶段就按活跃数卡死，那么"16 个活跃 + 1 个正在握手"的瞬间会把
    /// 本可以成功重连的客户端挡在 HTTP 401（前端会显示成登录失败）。
    /// 预留槽只解决"在途握手"的占位，不放大在线容量：活跃仍然严格 ≤ <see cref="MaxActive"/>。
    ///
    /// <b>5 秒 TTL</b>
    /// ---------
    /// 握手失败/客户端中途放弃时，<c>HandleWebSocket</c> 根本不会执行，没有机会显式释放；
    /// 预留槽必须自己过期。TTL 取 <c>min(5 秒, 票剩余寿命)</c>——票本身 30 秒有效，
    /// 但握手正常在毫秒级完成，5 秒足够；票只剩 2 秒时按票的剩余寿命算，避免预留比票活得久。
    ///
    /// <b>幂等释放</b>
    /// ---------
    /// 失败出口多（票无效/容量满/重复入册/停服竞争），逐个手写减计数迟早漏一个。
    /// 这里把"释放"做成无状态操作：<see cref="ReleaseHandshake"/> 对不存在的票是空操作，
    /// <see cref="ReleaseActive"/> 减到 0 为止——重复调用不会把别人的名额减掉。
    /// </summary>
    public sealed class ScadaWebAdmissionGate
    {
        /// <summary>握手预留槽默认数量（在 16 活跃之外额外允许的在途握手数）</summary>
        public const int DefaultHandshakeSlots = 8;

        /// <summary>握手预留的最长寿命（见类注释"5 秒 TTL"）</summary>
        public static readonly TimeSpan HandshakeTtl = TimeSpan.FromSeconds(5);

        private readonly object _gate = new();
        private readonly Dictionary<string, DateTime> _handshakes = new(StringComparer.Ordinal);
        private int _active;

        public ScadaWebAdmissionGate(int maxActive, int maxHandshake = DefaultHandshakeSlots)
        {
            MaxActive = Math.Max(1, maxActive);
            MaxHandshake = Math.Max(0, maxHandshake);
        }

        /// <summary>活跃会话上限（= 配置的 <c>MaxSessions</c>）</summary>
        public int MaxActive { get; }

        /// <summary>在途握手上限（不含活跃）</summary>
        public int MaxHandshake { get; }

        /// <summary>当前活跃会话数</summary>
        public int ActiveCount
        {
            get
            {
                lock (_gate)
                    return _active;
            }
        }

        /// <summary>当前在途握手数（诊断/断言用）</summary>
        public int HandshakeCount
        {
            get
            {
                lock (_gate)
                    return _handshakes.Count;
            }
        }

        /// <summary>
        /// 票核销成功后占一个握手槽。同一张票重复调用是幂等的（返回 true，不重复占位）；
        /// 槽满返回 false，调用方必须回收本次新签的会话令牌（续期令牌不回收）。
        /// </summary>
        public bool TryReserveHandshake(
            string? ticket,
            DateTime ticketExpiresAtUtc,
            DateTime nowUtc,
            out string reason)
        {
            reason = string.Empty;

            if (string.IsNullOrWhiteSpace(ticket))
            {
                reason = "缺少 ticket，无法预留握手槽";
                return false;
            }

            lock (_gate)
            {
                PruneLocked(nowUtc);

                if (_handshakes.ContainsKey(ticket))
                    return true;

                if (_handshakes.Count >= MaxHandshake)
                {
                    reason = $"在途握手已达上限 {MaxHandshake}";
                    return false;
                }

                var deadline = nowUtc + HandshakeTtl;
                if (ticketExpiresAtUtc < deadline)
                    deadline = ticketExpiresAtUtc;

                _handshakes[ticket] = deadline;
                return true;
            }
        }

        /// <summary>
        /// 握手槽转活跃槽（WS 入册前调用）。失败原因只有两种：
        /// 预留不存在/已过期（票被重复使用或握手拖过 5 秒）、活跃已满（回 1013）。
        /// <b>失败时预留槽已被消耗</b>：票已经核销掉了，留着预留也没有下一次机会。
        /// </summary>
        public bool TryPromote(string? ticket, DateTime nowUtc, out string reason)
        {
            reason = string.Empty;

            if (string.IsNullOrWhiteSpace(ticket))
            {
                reason = "缺少 ticket，无法转为活跃会话";
                return false;
            }

            lock (_gate)
            {
                PruneLocked(nowUtc);

                if (!_handshakes.Remove(ticket))
                {
                    reason = "握手预留不存在或已过期";
                    return false;
                }

                if (_active >= MaxActive)
                {
                    reason = $"活跃会话已达上限 {MaxActive}";
                    return false;
                }

                _active++;
                return true;
            }
        }

        /// <summary>释放一个握手槽（不存在时为空操作，可重复调用）</summary>
        public void ReleaseHandshake(string? ticket)
        {
            if (string.IsNullOrWhiteSpace(ticket))
                return;

            lock (_gate)
                _handshakes.Remove(ticket);
        }

        /// <summary>释放一个活跃槽（减到 0 为止，可重复调用）</summary>
        public void ReleaseActive()
        {
            lock (_gate)
            {
                if (_active > 0)
                    _active--;
            }
        }

        /// <summary>清掉过期握手槽，返回清掉的个数（在线列表 2 秒循环每拍调用）</summary>
        public int SweepExpired(DateTime nowUtc)
        {
            lock (_gate)
                return PruneLocked(nowUtc);
        }

        /// <summary>停服清表：活跃与握手一起归零</summary>
        public void Clear()
        {
            lock (_gate)
            {
                _handshakes.Clear();
                _active = 0;
            }
        }

        private int PruneLocked(DateTime nowUtc)
        {
            if (_handshakes.Count == 0)
                return 0;

            List<string>? expired = null;

            foreach (var pair in _handshakes)
            {
                if (nowUtc >= pair.Value)
                    (expired ??= new List<string>()).Add(pair.Key);
            }

            if (expired == null)
                return 0;

            foreach (var key in expired)
                _handshakes.Remove(key);

            return expired.Count;
        }
    }
}
