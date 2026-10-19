using System;
using System.Collections.Generic;
using System.Linq;
using Core.Interfaces.Io;

namespace VisionMaster.Services
{
    /// <summary>
    /// IO 输出写租约：同一个输出点在同一时刻只允许一个 owner 持有。
    ///
    /// 为什么需要它：并行流程的两个分支同时写同一个 DO 是现实场景（一个伸出、一个缩回），
    /// 没有租约就是"最后写入者赢"，表现为设备偶尔动作错、且无法复现。
    ///
    /// 口径：
    /// - 默认严格单写者；点位显式开启 AllowSharedWrite 时才允许共享；
    /// - 手动写用独立的 manual owner；流程持有时手动写返回明确冲突；
    /// - 租约按 pointId 排序获取（调用方一次只租一个点，不涉及多锁顺序问题）；
    /// - 租约带超时回收，防止分支异常退出后永久占用。
    /// </summary>
    public sealed class IoOutputLeaseRegistry
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
        public string? GetOwner(string pointId)
        {
            lock (_gate)
            {
                if (!_leases.TryGetValue(pointId, out var lease)) return null;
                if (IsExpired(lease)) { _leases.Remove(pointId); return null; }
                return lease.Owner;
            }
        }

        /// <summary>
        /// 获取写租约。成功返回 true；被别人持有返回 false 并给出当前 owner。
        /// allowShared = 点位配置允许共享写（此时不互斥，仅记录最后 owner）。
        /// </summary>
        public bool TryAcquire(string pointId, string ownerId, int timeoutMs, bool allowShared, out string? error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(pointId)) { error = "点位为空"; return false; }
            if (string.IsNullOrWhiteSpace(ownerId)) { error = "owner 为空"; return false; }

            lock (_gate)
            {
                if (allowShared)
                {
                    // 共享写：不互斥，但仍记录最后的 owner，供界面与审计展示。
                    _leases[pointId] = new Lease
                    {
                        Owner = ownerId,
                        AcquiredAt = DateTime.Now,
                        TimeoutMs = Math.Max(1000, timeoutMs)
                    };
                    return true;
                }

                if (_leases.TryGetValue(pointId, out var current) && !IsExpired(current))
                {
                    if (string.Equals(current.Owner, ownerId, StringComparison.OrdinalIgnoreCase))
                        return true;

                    error = $"点位 {pointId} 正被「{current.Owner}」占用";
                    return false;
                }

                _leases[pointId] = new Lease
                {
                    Owner = ownerId,
                    AcquiredAt = DateTime.Now,
                    TimeoutMs = Math.Max(1000, timeoutMs)
                };
                return true;
            }
        }

        /// <summary>释放租约（只有 owner 自己能释放；幂等）。</summary>
        public void Release(string pointId, string ownerId)
        {
            lock (_gate)
            {
                if (!_leases.TryGetValue(pointId, out var lease)) return;
                if (!string.Equals(lease.Owner, ownerId, StringComparison.OrdinalIgnoreCase)) return;
                _leases.Remove(pointId);
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
