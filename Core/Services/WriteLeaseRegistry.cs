using System;
using System.Collections.Generic;
using System.Linq;

namespace VisionMaster.Services
{
    /// <summary>
    /// 写租约：同一个可写目标（点位/变量）在同一时刻只允许一个 owner 持有。
    ///
    /// <para><b>为什么需要它</b>：并行流程的两个分支同时写同一个输出是现实场景（一个伸出、一个缩回），
    /// 没有租约就是"最后写入者赢"，表现为设备偶尔动作错、且无法复现。</para>
    ///
    /// <para><b>口径</b>：</para>
    /// <para>· 默认严格单写者；目标显式开启"允许共享写"时才不互斥（仅记录最后 owner 供审计）；</para>
    /// <para>· 手动写用独立的 manual owner；流程持有时手动写返回明确冲突；</para>
    /// <para>· 租约按目标键独立获取（调用方一次只租一个目标，不涉及多锁顺序问题）；</para>
    /// <para>· 租约带超时回收，防止分支异常退出后永久占用。</para>
    ///
    /// <para><b>与具体协议无关</b>：键是字符串（<c>{连接名}.{变量名}</c> 或 <c>{设备}.{点位}</c>），
    /// 所以 Modbus / S7 / 自定义驱动的写路径共用同一套仲裁，不需要各写一份。</para>
    /// </summary>
    public sealed class WriteLeaseRegistry
    {
        private sealed class Lease
        {
            public string Owner = string.Empty;
            public DateTime AcquiredAt;
            public int TimeoutMs;
        }

        private readonly object _gate = new();
        private readonly Dictionary<string, Lease> _leases = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>手动写的固定 owner，便于审计与冲突提示。</summary>
        public const string ManualOwner = "manual";

        /// <summary>当前 owner（诊断用）；未持有时为 null。</summary>
        public string? GetOwner(string targetKey)
        {
            lock (_gate)
            {
                if (!_leases.TryGetValue(targetKey, out var lease)) return null;
                if (IsExpired(lease)) { _leases.Remove(targetKey); return null; }
                return lease.Owner;
            }
        }

        /// <summary>
        /// 获取写租约。成功返回 true；被别人持有返回 false 并给出当前 owner。
        /// <paramref name="allowShared"/> = true 时该目标不互斥（仅记录最后 owner）。
        /// </summary>
        public bool TryAcquire(string targetKey, string ownerId, int timeoutMs, bool allowShared, out string? error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(targetKey)) { error = "目标为空"; return false; }
            if (string.IsNullOrWhiteSpace(ownerId)) { error = "owner 为空"; return false; }

            lock (_gate)
            {
                if (allowShared)
                {
                    // 共享写：不互斥，但仍记录最后的 owner，供界面与审计展示。
                    _leases[targetKey] = new Lease
                    {
                        Owner = ownerId,
                        AcquiredAt = DateTime.Now,
                        TimeoutMs = Math.Max(1000, timeoutMs)
                    };
                    return true;
                }

                if (_leases.TryGetValue(targetKey, out var current) && !IsExpired(current))
                {
                    if (string.Equals(current.Owner, ownerId, StringComparison.OrdinalIgnoreCase))
                        return true;

                    error = $"目标 {targetKey} 正被「{current.Owner}」占用";
                    return false;
                }

                _leases[targetKey] = new Lease
                {
                    Owner = ownerId,
                    AcquiredAt = DateTime.Now,
                    TimeoutMs = Math.Max(1000, timeoutMs)
                };
                return true;
            }
        }

        /// <summary>释放租约（只有 owner 自己能释放；幂等）。</summary>
        public void Release(string targetKey, string ownerId)
        {
            lock (_gate)
            {
                if (!_leases.TryGetValue(targetKey, out var lease)) return;
                if (!string.Equals(lease.Owner, ownerId, StringComparison.OrdinalIgnoreCase)) return;
                _leases.Remove(targetKey);
            }
        }

        /// <summary>释放某个 owner 持有的全部租约（分支/步骤结束时的兜底）。</summary>
        public void ReleaseAll(string ownerId)
        {
            lock (_gate)
            {
                foreach (var key in _leases.Where(p =>
                             string.Equals(p.Value.Owner, ownerId, StringComparison.OrdinalIgnoreCase))
                         .Select(p => p.Key).ToList())
                {
                    _leases.Remove(key);
                }
            }
        }

        private static bool IsExpired(Lease lease)
            => (DateTime.Now - lease.AcquiredAt).TotalMilliseconds > lease.TimeoutMs;
    }
}
