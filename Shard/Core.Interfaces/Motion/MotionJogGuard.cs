using System;

namespace Core.Interfaces
{
    /// <summary>
    /// 点动心跳兜底的安全判断（**唯一**实现）。
    ///
    /// 为什么从 VM 里抽出来：点动没有终点，"松手即停"完全依赖界面事件一定送达。
    /// 而在真实产线上，一次丢事件（失焦、卡顿、行为失效）就意味着机器一直走 ——
    /// 安全不能建立在"事件不会丢"的假设上，所以必须有心跳看门狗；
    /// 而这条判断恰恰是最后一道闸，必须能被无 WPF 消息泵的测试宿主直接断言
    /// （计时器只负责"什么时候问"，不负责"怎么判"）。
    ///
    /// 【迁移说明】原先放在 <c>MotionDebugViewModel</c> 上，导致新的调试页签
    /// 为了复用一个静态函数去依赖一个**将被淘汰的旧 VM**（耦合方向反了）。
    /// 现在两边都引用这里。
    /// </summary>
    public static class MotionJogGuard
    {
        /// <summary>
        /// 点动心跳超时（ms）。按住期间界面每 150ms 重复一次脉冲，这里给 3 倍余量。
        /// </summary>
        public const int PulseTimeoutMs = 500;

        /// <summary>
        /// 心跳是否已超时（没有收到过任何脉冲时不算超时 ——
        /// 那是"从未开始点动"，不是"按着按着断了"）。
        /// </summary>
        public static bool IsPulseTimedOut(DateTime nowUtc, DateTime lastPulseUtc, int timeoutMs)
            => lastPulseUtc != DateTime.MinValue
               && (nowUtc - lastPulseUtc).TotalMilliseconds > Math.Max(1, timeoutMs);
    }
}
