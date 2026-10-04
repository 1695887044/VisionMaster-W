using System;
using System.Collections.Generic;

namespace VisionMaster.Lifetime
{
    /// <summary>一次异常出现的落盘判定（由 <see cref="ExceptionThrottle.Decide"/> 给出）</summary>
    public readonly struct ThrottleDecision
    {
        /// <summary>本次是否要落盘。false = 已被合并，只计数</summary>
        public bool ShouldLog { get; init; }

        /// <summary>true = 这行是"同类异常合并汇总"，不是原始异常</summary>
        public bool IsSummary { get; init; }

        /// <summary>该异常累计出现过多少次（含被合并的）</summary>
        public long TotalCount { get; init; }

        /// <summary>本窗口内被合并掉、没有逐条记录的条数</summary>
        public long SuppressedSinceLastLog { get; init; }

        /// <summary>本次是否该给一次"疑似自激"提示（同一次风暴只给一次）</summary>
        public bool StormHint { get; init; }
    }

    /// <summary>
    /// 重复异常日志限流。
    ///
    /// 【为什么必须有它】
    /// 现场一次真实事故：Fluent 样式里把 <c>Foreground</c> 写成了 Color 令牌（不是 Brush），
    /// TextBlock 每次测量都抛同一个异常；全局处理器把它降级成 WARN 吞掉 → 布局重新失效 → 再抛 →
    /// 再记一条……一次会话写下 **29141 条**同样的 WARN（3MB 日志），界面同时**永久无响应**。
    /// 逐条落盘在这里不只是"日志变脏"：它本身就在拖慢现场，还把真正的根因埋在几万行重复里。
    ///
    /// 【口径】同一个异常（key 由调用方给，通常是"类型 + 上下文 + 消息"）在窗口期内：
    ///   · 前 <see cref="MaxVerbatimPerWindow"/> 条**原样记录**（保留现场细节）；
    ///   · 其余只计数，每 <see cref="SummaryInterval"/> 落一条**汇总**（带累计次数）；
    ///   · 窗口内出现次数超过 <see cref="StormThreshold"/> 时，额外给一次"疑似自激"提示。
    /// 窗口一过自动复位，所以"偶发重复"照旧看得到，不会把正常信息吃掉。
    ///
    /// 【为什么单独一个类】纯逻辑、时间由调用方传入（<c>now</c>），可以脱离界面与真实时钟做断言，
    /// 见 <c>UIThemeSmokeTest --controls</c>。
    /// </summary>
    public sealed class ExceptionThrottle
    {
        private sealed class KeyState
        {
            public DateTime WindowStart;
            public DateTime LastSeen;
            public DateTime LastSummaryAt;
            public int CountInWindow;
            public int VerbatimLogged;
            public long TotalCount;
            public bool StormHinted;
        }

        private readonly object _gate = new();
        private readonly Dictionary<string, KeyState> _states = new(StringComparer.Ordinal);

        /// <summary>每个窗口期最多原样记录几条</summary>
        public int MaxVerbatimPerWindow { get; }

        /// <summary>窗口期长度（过后复位，重新允许原样记录）</summary>
        public TimeSpan Window { get; }

        /// <summary>汇总行的最小间隔（风暴期间每隔这么久留一条"还在刷"的痕迹）</summary>
        public TimeSpan SummaryInterval { get; }

        /// <summary>窗口内超过这个次数就判定"疑似自激"</summary>
        public int StormThreshold { get; }

        /// <summary>同时跟踪的异常种类上限（防止异常种类本身爆炸导致内存无界）</summary>
        public int MaxKeys { get; }

        /// <summary>同一异常安静这么久之后，允许再给一次"疑似自激"提示（避免一次风暴后永久失声）</summary>
        public TimeSpan StormHintIdleReset { get; }

        public ExceptionThrottle(
            int maxVerbatimPerWindow = 3,
            TimeSpan? window = null,
            TimeSpan? summaryInterval = null,
            int stormThreshold = 50,
            int maxKeys = 128,
            TimeSpan? stormHintIdleReset = null)
        {
            MaxVerbatimPerWindow = Math.Max(1, maxVerbatimPerWindow);
            Window = window ?? TimeSpan.FromSeconds(5);
            SummaryInterval = summaryInterval ?? TimeSpan.FromSeconds(5);
            StormThreshold = Math.Max(MaxVerbatimPerWindow + 1, stormThreshold);
            MaxKeys = Math.Max(16, maxKeys);
            StormHintIdleReset = stormHintIdleReset ?? TimeSpan.FromSeconds(60);
        }

        /// <summary>当前跟踪的异常种类数（恒 &lt;= <see cref="MaxKeys"/>；供断言与现场自查）</summary>
        public int TrackedKeyCount
        {
            get { lock (_gate) return _states.Count; }
        }

        /// <summary>登记一次异常出现，并给出"这次该怎么记"</summary>
        /// <param name="key">异常身份（同一条异常必须给出同一个 key）</param>
        /// <param name="now">当前时间（由调用方传入，便于断言）</param>
        public ThrottleDecision Decide(string key, DateTime now)
        {
            lock (_gate)
            {
                if (!_states.TryGetValue(key, out var st))
                {
                    if (_states.Count >= MaxKeys) EvictOldest();

                    st = new KeyState { WindowStart = now, LastSeen = now, LastSummaryAt = now };
                    _states[key] = st;
                }
                else if (now - st.LastSeen >= StormHintIdleReset)
                {
                    // 安静够久：当作新的一次风暴，允许再提示一次
                    st.StormHinted = false;
                }

                if (now - st.WindowStart >= Window)
                {
                    // 注意：这里**不重置** LastSummaryAt —— 它与窗口是两个独立节奏。
                    // 一起重置过（窗口 == 汇总间隔）会让汇总行永远等不到时机，
                    // 风暴里就剩几条原始行，谁也看不出"它还在刷、已经刷了多少次"。
                    st.WindowStart = now;
                    st.CountInWindow = 0;
                    st.VerbatimLogged = 0;
                }

                st.CountInWindow++;
                st.TotalCount++;
                st.LastSeen = now;

                bool stormHint = false;
                if (!st.StormHinted && st.CountInWindow > StormThreshold)
                {
                    stormHint = true;
                    st.StormHinted = true;
                }

                // ① 窗口内前几条：原样落盘
                if (st.VerbatimLogged < MaxVerbatimPerWindow)
                {
                    st.VerbatimLogged++;
                    return new ThrottleDecision
                    {
                        ShouldLog = true,
                        TotalCount = st.TotalCount,
                        StormHint = stormHint,
                    };
                }

                // ② 其余：只计数，隔一段时间补一条汇总
                if (now - st.LastSummaryAt >= SummaryInterval)
                {
                    long suppressed = st.CountInWindow - st.VerbatimLogged;
                    st.LastSummaryAt = now;
                    return new ThrottleDecision
                    {
                        ShouldLog = true,
                        IsSummary = true,
                        TotalCount = st.TotalCount,
                        SuppressedSinceLastLog = suppressed,
                        StormHint = stormHint,
                    };
                }

                // ③ 正在合并中：不落盘
                return new ThrottleDecision { TotalCount = st.TotalCount, StormHint = stormHint };
            }
        }

        /// <summary>丢弃最久没出现的一个 key（优先丢"窗口早过了"的）</summary>
        private void EvictOldest()
        {
            string? victim = null;
            var oldest = DateTime.MaxValue;

            foreach (var kv in _states)
            {
                if (kv.Value.LastSeen >= oldest) continue;
                oldest = kv.Value.LastSeen;
                victim = kv.Key;
            }

            if (victim != null) _states.Remove(victim);
        }
    }
}
