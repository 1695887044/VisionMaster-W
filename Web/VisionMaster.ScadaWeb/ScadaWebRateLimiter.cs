using System;
using System.Collections.Generic;
using VisionMaster.Models;

namespace VisionMaster.ScadaWeb
{
    /// <summary>
    /// Web 命令的<b>双层速率限制</b>（方案 §8.4 监管手段 5）。
    ///
    /// <b>两层各自挡什么（不是同一件事的两种写法）</b>
    /// ---------
    ///   · <b>会话级</b> <see cref="CommandRatePerSecond"/>（默认 5/s，<b>每会话独立</b>）：
    ///     挡"一个人连点"——鼠标按住不放、触屏抖动、脚本刷命令；
    ///   · <b>图元级</b> <see cref="ElementRatePerSecond"/>（默认 2/s，<b>同一 elementId 跨会话共享</b>）：
    ///     挡"一百个浏览器同时按同一个按钮"——那种情况下每会话 5/s 全都不超，但那个图元的动作
    ///     会被执行上百次（写变量 / 启停流程 / 让轴动）。两层缺一层，另一种事故就没人管。
    ///
    /// <b>为什么是滑动窗口而不是固定秒桶</b>
    /// ---------
    /// 固定桶在整秒边界上允许"上一秒末尾 5 条 + 这一秒开头 5 条"，瞬间 10 条；
    /// 滑动窗口没有这个边界（与 <c>ScadaWebSecurityGate</c> 的取票限速同一口径）。
    ///
    /// <b>判定与记账是原子的</b>
    /// ---------
    /// 两层在<b>同一把锁</b>内先判后记：任一层超限就<b>两层都不记账</b>。
    /// 分开做的话，"会话层通过、图元层拒绝"的那一次会白白消耗会话额度，
    /// 表现为"我什么都没干成，却已经被限流了"。
    ///
    /// <b>线程</b>：Watson 的接收循环是多线程的（每条连接一条），本类自带锁。
    /// </summary>
    public sealed class ScadaWebRateLimiter
    {
        /// <summary>每会话默认命令速率（条/秒）。与 <see cref="ScadaWebSettings.DefaultCommandRatePerSecond"/> 同源</summary>
        public const int DefaultCommandRatePerSecond = ScadaWebSettings.DefaultCommandRatePerSecond;

        /// <summary>每图元默认命令速率（条/秒）</summary>
        public const int DefaultElementRatePerSecond = 2;

        /// <summary>滑动窗口长度（1 秒）</summary>
        private static readonly TimeSpan Window = TimeSpan.FromSeconds(1);

        /// <summary>单键窗口队列的硬上限（防止"速率配成 100000"把内存撑爆）</summary>
        private const int MaxTrackedHits = 4096;

        private readonly object _gate = new();
        private readonly Dictionary<string, WindowQueue> _sessions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, WindowQueue> _elements = new(StringComparer.Ordinal);
        private readonly Func<DateTime> _utcNow;

        /// <summary>每会话上限（条/秒）；0 或负数按 1 处理——<b>不许关掉限流</b></summary>
        public int CommandRatePerSecond { get; }

        /// <summary>每图元上限（条/秒，跨会话共享）；0 或负数按 1 处理</summary>
        public int ElementRatePerSecond { get; }

        /// <param name="settings">配置快照（读 <c>CommandRatePerSecond</c> / <c>ElementRatePerSecond</c>）</param>
        public ScadaWebRateLimiter(ScadaWebSettings settings)
            : this(
                (settings ?? throw new ArgumentNullException(nameof(settings))).CommandRatePerSecond,
                settings.ElementRatePerSecond,
                utcNow: null)
        {
        }

        /// <param name="commandRatePerSecond">每会话上限（条/秒）</param>
        /// <param name="elementRatePerSecond">每图元上限（条/秒）</param>
        /// <param name="utcNow">
        /// 时钟（断言用来把 1 秒窗口快进过去，产品路径传 <c>null</c> = <see cref="DateTime.UtcNow"/>）。
        /// 不注入的话，"限速窗口恢复"只能靠真等 1 秒来验，连跑几次就会变成慢断言。
        /// </param>
        public ScadaWebRateLimiter(int commandRatePerSecond, int elementRatePerSecond, Func<DateTime>? utcNow = null)
        {
            CommandRatePerSecond = Math.Clamp(commandRatePerSecond, 1, MaxTrackedHits);
            ElementRatePerSecond = Math.Clamp(elementRatePerSecond, 1, MaxTrackedHits);
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        /// <summary>当前被跟踪的会话数（诊断 / 断言核对"停服后表已清"）</summary>
        public int TrackedSessions
        {
            get { lock (_gate) return _sessions.Count; }
        }

        /// <summary>当前被跟踪的图元数</summary>
        public int TrackedElements
        {
            get { lock (_gate) return _elements.Count; }
        }

        /// <summary>
        /// 尝试为一条命令取得配额。返回 false 时 <paramref name="error"/> 是可直接上屏的原因。
        /// </summary>
        /// <param name="sessionId">服务端会话 Id（<see cref="ScadaWebSession.SessionId"/>）</param>
        /// <param name="elementId">目标图元 Id；<c>null</c>/空白 = 只记会话层（如"确认全部报警"）</param>
        /// <param name="error">超限原因；通过时为 <c>null</c></param>
        public bool TryAcquire(string? sessionId, string? elementId, out string? error)
            => TryAcquire(sessionId, elementId, out error, out _);

        /// <summary>
        /// 同 <see cref="TryAcquire(string?, string?, out string?)"/>，另给 <paramref name="retryAfterMs"/>
        /// （回执里带 <c>retryAfterMs</c>，前端据此决定"稍后重试"而不是立刻重发）。
        /// </summary>
        public bool TryAcquire(
            string? sessionId,
            string? elementId,
            out string? error,
            out int retryAfterMs)
        {
            error = null;
            retryAfterMs = 0;

            var now = _utcNow();
            var sessionKey = string.IsNullOrWhiteSpace(sessionId) ? "<unknown>" : sessionId!;
            var elementKey = string.IsNullOrWhiteSpace(elementId) ? null : elementId;

            lock (_gate)
            {
                var sessionWindow = GetWindow(_sessions, sessionKey);
                sessionWindow.Trim(now, Window);

                WindowQueue? elementWindow = null;
                if (elementKey != null)
                {
                    elementWindow = GetWindow(_elements, elementKey);
                    elementWindow.Trim(now, Window);
                }

                if (sessionWindow.Count >= CommandRatePerSecond)
                {
                    retryAfterMs = sessionWindow.RetryAfterMs(now, Window);
                    error = $"命令过快：每会话上限 {CommandRatePerSecond} 条/秒，请 {retryAfterMs}ms 后重试";
                    return false;
                }

                if (elementWindow != null && elementWindow.Count >= ElementRatePerSecond)
                {
                    retryAfterMs = elementWindow.RetryAfterMs(now, Window);
                    error = $"该图元操作过快：每秒最多 {ElementRatePerSecond} 次（所有会话合计），"
                            + $"请 {retryAfterMs}ms 后重试";
                    return false;
                }

                // 两层都通过才一起记账（理由见类注释）。
                sessionWindow.Add(now);
                elementWindow?.Add(now);

                return true;
            }
        }

        /// <summary>会话断开时摘掉它的窗口（否则"每来一个新连接就多一个键"，长期运行会慢慢涨）</summary>
        public void RemoveSession(string? sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId)) return;

            lock (_gate)
                _sessions.Remove(sessionId!);
        }

        /// <summary>停服清空（宿主 <c>Stop()</c> 调用；下次启动从零开始）</summary>
        public void Reset()
        {
            lock (_gate)
            {
                _sessions.Clear();
                _elements.Clear();
            }
        }

        private static WindowQueue GetWindow(Dictionary<string, WindowQueue> table, string key)
        {
            if (!table.TryGetValue(key, out var window))
            {
                window = new WindowQueue();
                table[key] = window;
            }

            return window;
        }

        /// <summary>一个键的滑动窗口：只保留窗口内的时刻，队列长度天然不超过额度</summary>
        private sealed class WindowQueue
        {
            private readonly Queue<DateTime> _hits = new();

            public int Count => _hits.Count;

            public void Trim(DateTime now, TimeSpan window)
            {
                var cutoff = now - window;

                while (_hits.Count > 0 && _hits.Peek() <= cutoff)
                    _hits.Dequeue();
            }

            public void Add(DateTime now)
            {
                // 额度被配得极大时也不许无限增长（正常配置下永远走不到）。
                while (_hits.Count >= MaxTrackedHits)
                    _hits.Dequeue();

                _hits.Enqueue(now);
            }

            /// <summary>最早那一条还要多久才滑出窗口（毫秒，至少 1）</summary>
            public int RetryAfterMs(DateTime now, TimeSpan window)
            {
                if (_hits.Count == 0) return 1;

                var releaseAt = _hits.Peek() + window;
                var remaining = (releaseAt - now).TotalMilliseconds;

                return remaining <= 0 ? 1 : (int)Math.Ceiling(remaining);
            }
        }
    }
}
