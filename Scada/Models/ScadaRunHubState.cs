namespace VisionMaster.Scada
{
    /// <summary>
    /// 运行轮次的<b>纯状态机</b>：只回答两个事实——"这一轮起没起"与"这是第几轮"，不碰 WPF、不碰会话。
    ///
    /// 为什么单独抽出来
    /// ---------
    /// 启停广播的门禁必须<b>只有一份实现</b>。<see cref="IScadaRunHub"/> 承诺"同一轮只发一次
    /// <c>RunStarted</c>、一次 <c>RunStopped</c>"，这条承诺如果由宿主里散落的几个 if 拼出来，
    /// 早晚出现"重复点运行发了两次 Started"或"启动失败却发了 Stopped"这类现场偶发、
    /// 日志里又看不出的错。抽成状态机之后，宿主只剩一句
    /// <c>if (_hubState.TryStarted(out var g)) PublishStarted(...)</c>，
    /// 断言工程也能对<b>同一份实现</b>跑满启停序列（见 ScadaChecks 的 [RH] 段）。
    ///
    /// <b>线程</b>：只在 UI 线程上读写（与 <see cref="ScadaRuntime"/>、宿主的 <c>ScadaNavigator</c>
    /// 同一条口径：运行态会话的生命周期完全由 UI 线程驱动），所以<b>不加锁</b>。
    /// 将来若要在后台线程读 <see cref="Generation"/>，必须先在这里补同步，
    /// 而不是让每个消费者各自复制一份状态。
    /// </summary>
    public sealed class ScadaRunHubState
    {
        private bool _started;
        private long _generation;

        /// <summary>
        /// 这一轮是否已经启动过：<c>true</c> = 已发过 <c>RunStarted</c>、尚未发 <c>RunStopped</c>。
        /// </summary>
        public bool IsStarted => _started;

        /// <summary>
        /// 最近一次成功启动的轮次。一轮都没起过时为 0，且<b>永不回退</b>
        /// （<see cref="Reset"/> 也不清它——理由见 Reset 的注释）。
        /// </summary>
        public long Generation => _generation;

        /// <summary>
        /// 标记一轮启动。<b>已启动时返回 false 且不递增</b>：重复点「运行」不该被算成新一轮，
        /// 否则命令信封里的 generation 会凭空跳一格，"旧命令已过期"的判定跟着错位。
        /// </summary>
        /// <param name="generation">本轮轮次号（成功时是新的，失败时是当前值）</param>
        /// <returns>是否真的启动了新的一轮（= 该不该发 <c>RunStarted</c>）</returns>
        public bool TryStarted(out long generation)
        {
            if (_started)
            {
                generation = _generation;
                return false;
            }

            _started = true;
            generation = ++_generation;
            return true;
        }

        /// <summary>
        /// 标记一轮结束。<b>未启动时返回 false</b>：从没起过就没有可结束的一轮，
        /// "启动失败 → 关窗口"那条路因此天然不会发出 <c>RunStopped</c>。
        /// </summary>
        /// <param name="generation">结束的这一轮的轮次号（失败时是当前值，供日志/诊断用）</param>
        /// <returns>是否真的结束了一轮（= 该不该发 <c>RunStopped</c>）</returns>
        public bool TryStopped(out long generation)
        {
            generation = _generation;

            if (!_started)
                return false;

            _started = false;
            return true;
        }

        /// <summary>
        /// 清掉"这一轮起没起"（测试与收尾兜底用）。<b>不递增、也不清零 generation</b>：
        /// 轮次是单调时钟，把它重置回 0 等于让上一轮发出去的命令信封重新变成"当前轮"。
        /// </summary>
        public void Reset() => _started = false;
    }
}
