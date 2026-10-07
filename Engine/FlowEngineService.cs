using Core.Interfaces;
using System;
using System.Threading;
using System.Threading.Tasks;
using VisionMaster.EventModel;
using VisionMaster.Models;
using VisionMaster.Core;

namespace VisionMaster.Services
{
    /// <summary>
    /// 流程执行引擎服务实现
    /// 负责管理多个流程会话的并发执行、状态管理和生命周期控制
    /// </summary>
    public class FlowEngineService : IFlowEngine
    {
        /// <summary>
        /// 运行时管理器，管理所有活动会话
        /// </summary>
        private readonly IRuntimeManager _runtimeManager;

        /// <summary>
        /// 日志服务
        /// </summary>
        private readonly ILogService _logService;

        /// <summary>
        /// 工作空间管理器，提供全局变量访问
        /// </summary>
        private readonly IWorkspaceManager _workspaceManager;

        /// <summary>
        /// 性能监控服务
        /// </summary>
        private readonly IPerformanceMonitor _performanceMonitor;

        /// <summary>
        /// 资源锁服务：会话启动靠它做原子互斥（A3）
        /// </summary>
        private readonly IResourceLockService _resourceLocks;

        /// <summary>
        /// 会话状态变更事件
        /// 当会话状态发生变化时触发
        /// </summary>
        public event EventHandler<SessionStateChangedEventArgs> SessionStateChanged;

        /// <summary>
        /// 一轮流程执行完成（<c>ExecutionEngine.Run</c> 返回之后触发）。
        ///
        /// 连续运行**每轮**触发一次、单次运行触发一次：图像集采集要的正是"每跑完一轮，
        /// 就把这一轮所有 HImage 输出端口收一遍"，所以语义定在"轮"而不是"会话结束"。
        ///
        /// 参数直接给 <see cref="FlowSession"/> 本体（而不像 <see cref="SessionStateChanged"/> 只给 ID）：
        /// 订阅方要遍历 <c>session.ExecutionEngine.PluginLookup</c> 才能枚举输出端口，
        /// 只给 ID 还得自己经 <see cref="IRuntimeManager"/> 反查，多一跳且可能查不到。
        ///
        /// 触发点包了 try/catch：订阅者（图像采集）出问题绝不能影响流程执行本身。
        /// </summary>
        public event Action<FlowSession> FlowRunCompleted;

        /// <summary>
        /// 即将开始执行一轮流程（<c>ExecutionEngine.Run</c> 之前触发）。
        ///
        /// 与 <see cref="FlowRunCompleted"/> 成对：连续运行**每轮**各触发一次、单次运行一次。
        /// 存在的理由是画布要"每轮开始时按设置决定清空还是覆盖上一轮的图"——必须在这轮出图
        /// **之前**收到通知，否则新图和旧图会同时挂在列表里。
        ///
        /// 同样把 <see cref="FlowSession"/> 本体交给订阅方（订阅方要用 <c>FlowName</c> 定位本轮图），
        /// 并且订阅者异常就地隔离，绝不影响流程执行。
        /// </summary>
        public event Action<FlowSession> FlowRunStarted;

        /// <summary>
        /// 流程调用器：由「调用流程」步骤经执行上下文取用（子程序调用）。
        ///
        /// 为什么是"可写属性 + 装配时注入"而不是构造参数：调用器自己要用引擎跑子流程，
        /// 与引擎互相引用（环）没法在构造期解；默认 <see cref="NullFlowInvoker"/>（调用即失败并说明原因），
        /// FlowEngineModule 在装配时把它换成真实现（与 ServiceLocator.CommunicationManager 同一手法）。
        /// </summary>
        public IFlowInvoker FlowInvoker { get; set; } = NullFlowInvoker.Instance;

        /// <summary>
        /// 统一门禁：被锁定（<see cref="FlowModel.StepsEncrypted"/>）的流程一律不许运行。
        ///
        /// 为什么把这道门禁放到引擎这一层（而不是各入口自查）：此前的检查散在定时/变量/子程序/单流程
        /// 四条链上，HTTP 与界面"运行全部"两条链漏判——"同一语义两套口径"正是审查点名的结构性问题。
        /// 放在引擎入口后，任何触发源（含将来新增的）自动受同一道闸。
        /// </summary>
        private bool IsFlowLocked(string flowName)
        {
            var flows = _workspaceManager?.CurrentSolution?.Flows;
            if (flows == null) return false;

            foreach (var flow in flows)
            {
                if (flow == null || !flow.StepsEncrypted) continue;
                if (string.Equals(flow.FlowName, flowName, StringComparison.Ordinal)) return true;
            }

            return false;
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="runtimeManager">运行时管理器</param>
        /// <param name="logService">日志服务</param>
        /// <param name="workspaceManager">工作空间管理器</param>
        /// <param name="performanceMonitor">性能监控服务</param>
        /// <param name="resourceLocks">资源锁服务</param>
        public FlowEngineService(
            IRuntimeManager runtimeManager,
            ILogService logService,
            IWorkspaceManager workspaceManager,
            IPerformanceMonitor performanceMonitor,
            IResourceLockService resourceLocks,
            ICameraProvider cameras,
            IMotionProvider motions = null)   // 夹具可省略：插件侧拿 NullMotionProvider（取卡即失败并说明原因）
        {
            _runtimeManager = runtimeManager ?? throw new ArgumentNullException(nameof(runtimeManager));
            _logService = logService ?? throw new ArgumentNullException(nameof(logService));
            _workspaceManager = workspaceManager ?? throw new ArgumentNullException(nameof(workspaceManager));
            _performanceMonitor = performanceMonitor;
            _resourceLocks = resourceLocks ?? throw new ArgumentNullException(nameof(resourceLocks));
            // 允许为 null：检查/测试夹具直接 new 本类时给 null，插件侧会拿到 NullCameraProvider
            //（"取相机即失败并说明原因"），不必在每个夹具里都造一个假的相机仓库。
            _cameras = cameras ?? NullCameraProvider.Instance;
            // 运动设备同上 —— 这里不给，运动步骤的 context.Motions 永远是 NullMotionProvider，
            // 现象是"明明选了卡、执行时却报找不到运动卡"（此前正是这个缺口）。
            _motions = motions ?? NullMotionProvider.Instance;
        }

        /// <summary>相机仓库：每次执行都递给 ExecutionContext，插件据此取相机（见 IExecutionContext.Cameras）</summary>
        private readonly ICameraProvider _cameras;

        /// <summary>运动设备仓库：同上（见 IExecutionContext.Motions）。运动步骤按地址从这里取卡</summary>
        private readonly IMotionProvider _motions;

        /// <summary>
        /// 会话启动锁的资源名
        /// 以会话为粒度互斥；将来若图纸上能声明"本流程占用某台相机/某组轴"，
        /// 就在同一个服务上按设备名再叠一层锁，这里保持不变。
        /// </summary>
        private static string StartLockKey(FlowSession session) => $"FlowSession:{session.SessionID}";

        /// <summary>
        /// 原子地占用会话的执行权
        ///
        /// 为什么不能沿用 if (session.IsRunning) return; 紧接 session.IsRunning = true;：
        /// IsRunning 的 getter 和 setter 各自单独 lock，"读—判—写"不是一步，
        /// 连点两次启动按钮时两个线程能双双穿过判断。后果不是简单的跑两遍——
        /// 第二次 new CancellationTokenSource() 会把第一次的覆盖掉，
        /// 那个循环线程的令牌从此没人拿得到，变成点"停止"也停不掉的僵尸循环；
        /// 同一批插件实例（Halcon 句柄、相机连接）还会被两个线程同时驱动。
        /// 信号量的 Wait(0) 由系统保证原子，抢不到就是抢不到，缝没有了。
        ///
        /// 【这里不再往 session.LockedResources 记一笔】
        /// 原来抢到锁后会把锁名 Add 进会话自己的 HashSet，归还时 Remove。
        /// 但"释放句柄后 Remove、移除前别人已抢到并 Add"这个交接窗口里，
        /// 两个线程会同时改同一个 HashSet（裸 HashSet 非线程安全）→ 集合可能损坏。
        /// 而这份记录生产代码里一个消费者都没有（锁状态本来就能从
        /// IResourceLockService.IsLocked 查），属于纯粹的第二份账本 ——
        /// 删掉比给它加锁更彻底：没有共享可变状态，就没有竞态。
        /// </summary>
        /// <returns>抢到则返回释放句柄，抢不到返回 null（调用方直接放弃启动）</returns>
        private IDisposable TryOccupySession(FlowSession session)
        {
            if (_resourceLocks.TryAcquireLock(StartLockKey(session), out var handle, session.SessionID))
                return handle;

            return null;
        }

        /// <summary>
        /// 归还会话执行权（句柄可能为 null：从未抢到的那条早退路径）
        /// </summary>
        private void ReleaseSession(IDisposable handle)
        {
            handle?.Dispose();
        }

        /// <summary>
        /// 获取当前活跃会话数量
        /// </summary>
        public int ActiveSessionCount =>
            _runtimeManager.SnapshotSessions().Count(s => s.State == SessionState.Running);

        /// <summary>
        /// 通知会话状态变更 —— **全引擎唯一的 session.State 赋值点**。
        ///
        /// 【为什么必须是唯一赋值点（#6）】
        /// 原来 8 处调用点都先自己写一句 session.State = newState，再调这个方法；
        /// 而方法内部又读一次 session.State 当 oldState —— 于是读到的永远是刚写进去的新值，
        /// OldState 恒等于 NewState，字段彻底失效（CommStress 压测程序正是靠它统计状态转换的）。
        /// 现在赋值收进方法内部：先取旧值、再写新值，事件里的 OldState 才有意义。
        ///
        /// 【为什么订阅者的异常必须在这里吃掉（A 组 #2）】
        /// 这个方法会在 finally 收尾里被调用（置 Stopped）。订阅者是外部代码，
        /// UI 那份还会同步 Dispatcher.Invoke 回主线程 —— 它一旦抛异常，
        /// 异常会顺着 finally 往上爬，把"放会话锁"那一句整个跳过，
        /// 于是这个会话永久停在"已在运行中"，除了重启软件再也启不来。
        /// 订阅者自己的 bug 不该有这种杀伤力：就地隔离 + 记日志。
        /// </summary>
        /// <param name="session">会话实例</param>
        /// <param name="newState">新状态</param>
        /// <param name="message">附加消息（可选，会随事件一起送达）</param>
        private void NotifyStateChanged(FlowSession session, SessionState newState, string message = null)
        {
            var oldState = session.State;
            session.State = newState;

            try
            {
                SessionStateChanged?.Invoke(
                    this,
                    new SessionStateChangedEventArgs(
                        session.SessionID,
                        session.FlowName,
                        oldState,
                        newState,
                        message
                    )
                );
            }
            catch (Exception ex)
            {
                _logService.Error(
                    $"流程 {session.FlowName} 的状态变更订阅者抛出异常（已隔离，不影响流程收尾）: {ex.Message}"
                );
            }
        }

        /// <summary>
        /// 广播"这一轮跑完了"。订阅者异常就地隔离并记日志：
        /// 图像采集失败不该让流程执行报错，更不该影响会话锁的归还。
        /// </summary>
        private void NotifyFlowRunCompleted(FlowSession session)
        {
            try
            {
                FlowRunCompleted?.Invoke(session);
            }
            catch (Exception ex)
            {
                _logService.Error(
                    $"流程 {session.FlowName} 的运行完成订阅者抛出异常（已隔离，不影响流程执行）: {ex.Message}"
                );
            }
        }

        /// <summary>
        /// 广播"这一轮要开始了"。与 <see cref="NotifyFlowRunCompleted"/> 同样就地隔离订阅者异常：
        /// 清图失败不该让流程跑不起来。
        /// </summary>
        private void NotifyFlowRunStarted(FlowSession session)
        {
            try
            {
                FlowRunStarted?.Invoke(session);
            }
            catch (Exception ex)
            {
                _logService.Error(
                    $"流程 {session.FlowName} 的运行开始订阅者抛出异常（已隔离，不影响流程执行）: {ex.Message}"
                );
            }
        }

        /// <summary>
        /// 调试门命中（断点 / 单步）处理：把会话状态归到 Paused，并让暂停原因随消息送达
        /// （SessionStateChangedEventArgs.Message 的首次启用，评审结论 4）。
        /// 由运行前订阅、收尾退订（成对，见 RunSessionAsync / RunSessionOnceAsync）——
        /// 会话对象比引擎实例活得久，不退订就是悬挂订阅。
        /// </summary>
        private void OnDebugStopped(FlowSession session)
        {
            var message = session.PauseReason == SessionPauseReason.Step
                ? "调试暂停：单步"
                : "调试暂停：断点命中";

            // State 由 NotifyStateChanged 统一赋值（唯一赋值点）；订阅者异常在内部隔离
            NotifyStateChanged(session, SessionState.Paused, message);
        }

        /// <summary>
        /// 启动会话连续执行
        /// </summary>
        /// <param name="session">要执行的会话</param>
        /// <param name="debugSession">
        /// 本次运行是否为调试会话（DWV 第 1 期）。置位动作在抢到会话锁之后、按本参数执行
        /// （收尾统一清回 false，见 finally）：调用方先置位的写法把调试态与"谁抢到锁"解耦了——
        /// 界面置 true 后被 HTTP 抢到锁时，true 会泄漏给那次非界面运行（P2 竞态）。
        /// </param>
        /// <returns>异步任务</returns>
        public async Task RunSessionAsync(FlowSession session, bool debugSession = false)
        {
            if (session == null || session.ExecutionEngine == null)
                throw new ArgumentException("Session 或底层执行引擎不能为空，请先编译！");

            if (IsFlowLocked(session.FlowName))
            {
                _logService.Warn($"流程 {session.FlowName} 已被锁定（禁止运行），本次连续运行请求被拒绝");
                return;
            }

            // A3：抢锁即"检查+置位"，抢到才继续；抢不到说明这个会话已经在跑
            var sessionLock = TryOccupySession(session);
            if (sessionLock == null)
            {
                _logService.Warn($"流程 {session.FlowName} 已在运行中，忽略重复的启动请求");
                return;
            }

            // 从抢到锁的那一刻起，后面所有语句都必须在 try 内：
            // 中途抛异常若落不到 finally，这把锁就永久泄漏，该会话除了重启再也启不来
            try
            {
                // 先记"这一轮可暂停"，再置 IsRunning：PauseSession 的守卫先读 IsRunning，
                // 所以它能通过守卫时，读到的 IsContinuousRun 必定是本轮的值
                session.IsContinuousRun = true;
                // 清掉上一轮可能残留的调试状态：单步请求（否则本轮第一个节点就莫名停住）、
                // 暂停原因（否则运行中 UI 还显示着上一轮的暂停理由）
                session.DebugStepPending = false;
                session.PauseReason = SessionPauseReason.None;
                // 调试态按本次调用的显式参数置位 —— 只在这里（抢到锁之后）赋值：
                // 调用方先置位的话，本次若被拒（HTTP 抢到锁），true 会残留给那次 HTTP 运行（P2）
                session.DebugEnabled = debugSession;
                session.IsRunning = true;
                session.PauseLock.Set();
                session.CancellationTokenSource = new CancellationTokenSource();
                var token = session.CancellationTokenSource.Token;

                // 订阅调试门命中通知（断点 / 单步在节点执行前 raise）：本订阅负责把 State 归到
                // Paused（State 唯一赋值点在 NotifyStateChanged）。与收尾的退订成对。
                session.DebugStopped += OnDebugStopped;

                // 复位所有步序状态
                foreach (var step in session.Blueprints)
                {
                    step.ResetState();
                }

                NotifyStateChanged(session, SessionState.Running);
                _performanceMonitor?.RecordSessionStart(session.SessionID, session.FlowName);

                await Task.Run(() =>
                {
                    while (!token.IsCancellationRequested)
                    {
                        session.PauseLock.Wait(token);

                        if (token.IsCancellationRequested) break;

                        // 每次循环前再次复位所有步序状态
                        foreach (var step in session.Blueprints)
                        {
                            step.ResetState();
                        }

                        var context = new ExecutionContext(_logService, session, _workspaceManager, token)
                        {
                            Cameras = _cameras,
                            Motions = _motions,
                            FlowInvoker = FlowInvoker
                        };
                        // 这一轮开始即广播（画布据此清空/覆盖上一轮的图，见 FlowRunStarted 的注释）
                        NotifyFlowRunStarted(session);

                        session.ExecutionEngine.Run(context);

                        // 容器状态上浮（理由见单次执行路径的同名调用）
                        session.EscalateContainerFailures();

                        // 这一轮跑完即广播（图像集按"每轮"采集，见 FlowRunCompleted 的注释）
                        NotifyFlowRunCompleted(session);

                        // 用令牌等待替代 Thread.Sleep：空闲节流 10ms，但停止请求会立即唤醒退出
                        token.WaitHandle.WaitOne(10);
                    }
                }, token);
            }
            catch (OperationCanceledException)
            {
                // 正常取消，无需处理
            }
            catch (ObjectDisposedException)
            {
                // 与单次执行同一口径：启动准备期间会话被并发释放 = 本次连续运行根本没开始
                // （不置 Faulted——这不是流程的错，界面上不该出现一条"执行失败"）
                _logService.Warn($"流程 {session.FlowName} 的会话在启动准备期间被释放（并发替换），本次连续运行未开始");
            }
            catch (Exception ex)
            {
                _logService.Error($"流程执行异常 {session.FlowName}: {ex.Message}");
                // State 由 NotifyStateChanged 统一赋值；ex.Message 也随之送达订阅者
                NotifyStateChanged(session, SessionState.Faulted, ex.Message);
            }
            finally
            {
                // 【为什么收尾要套一层 try/catch/finally，而不是平铺】
                // IsRunning = false 是"本轮跑完了"的信号，RuntimeManager.RemoveAndDispose 正是
                // 靠它决定去 session.Dispose()（内含 PauseLock.Dispose()）。也就是说从这一行起，
                // 我们脚下这块内存随时可能被别人释放；而通知订阅者还可能同步阻塞很久
                // （UI 订阅者会 Dispatcher.Invoke），把窗口拉得更宽。
                // 收尾里任何一句抛异常，只要让执行流跳过了 ReleaseSession，
                // 这把会话锁就永久泄漏 —— 该会话此后永远"已在运行中"，只能重启软件。
                // 所以放锁必须落在最外层 finally：前面无论怎么炸，锁都还回去。
                // （[E9] ① ② 两条断言就是靠"订阅者抛异常 / 提前释放 PauseLock"把旧结构的漏还现场做出来的）
                try
                {
                    // 调试标志随本轮结束归零：DebugEnabled 表达的是"这一轮是不是界面发起的调试运行"，
                    // 绝不能泄漏给同会话的下一次非界面触发（HTTP 收图复用界面会话，残留 true 就会被
                    // 断点卡住产线链路——硬约束）；退订与运行前的订阅成对（会话比引擎活得久）
                    session.DebugEnabled = false;
                    session.DebugStopped -= OnDebugStopped;

                    session.IsRunning = false;

                    // 循环退出后清除所有步骤的运行焦点：
                    // 停止时机可能落在步骤"置 Running 之后、置 Success 之前"，不清除会导致高亮永久卡住
                    foreach (var step in session.Blueprints)
                        step.IsRunningFocus = false;
                    session.FocusedStep = null;

                    if (session.State != SessionState.Faulted)
                    {
                        // State 由 NotifyStateChanged 统一赋值（唯一赋值点）
                        NotifyStateChanged(session, SessionState.Stopped);
                    }

                    _performanceMonitor?.RecordSessionEnd(session.SessionID);

                    // 会话可能已被 RemoveAndDispose 释放，Set 会抛 ObjectDisposedException。
                    // 那种情况下锁已随会话一起消失，本来就不需要再置位。
                    try { session.PauseLock.Set(); }
                    catch (ObjectDisposedException) { }

                    session.CancellationTokenSource?.Dispose();
                    session.CancellationTokenSource = null;
                }
                catch (Exception ex)
                {
                    // 收尾本身失败不改变"这一轮结束了"这个事实，也不该再往外抛
                    // （生产里这是 Task.Run 的 fire-and-forget 任务，抛出去只会变成未观察异常）
                    _logService.Error(
                        $"流程 {session.FlowName} 收尾时发生异常（已忽略，会话锁照常归还）: {ex.Message}"
                    );
                }
                finally
                {
                    // 最后一步才放锁：IsRunning 已置 false，后来者抢到锁时不会看见半死的旧会话
                    ReleaseSession(sessionLock);
                }
            }
        }

        /// <summary>
        /// 启动会话单次执行
        /// </summary>
        /// <param name="session">要执行的会话</param>
        /// <param name="debugSession">本次运行是否为调试会话（语义与置位时机同 RunSessionAsync）</param>
        /// <returns>异步任务</returns>
        public async Task RunSessionOnceAsync(FlowSession session, bool debugSession = false)
            => await TryRunSessionOnceAsync(session, debugSession);

        /// <summary>
        /// 启动会话单次执行，并**如实回答"这一单到底跑了没有"**。
        ///
        /// 为什么要有返回值：「抢不到会话锁」在旧签名里只记一条 Warn 就正常返回——
        /// 对"触发一次就完事"的调用方（HTTP 收图 / 定时触发）没问题，但「调用流程」这类
        /// **必须知道自己有没有跑上**的调用方会把"我没跑"误读成"跑完了"，
        /// 父流程于是带着尚未落地的子流程数据继续往下走（假成功比报错危险得多）。
        /// 返回 false = 目标正被别的执行占着，本次**没有执行**。
        /// </summary>
        /// <param name="session">要执行的会话</param>
        /// <param name="debugSession">本次运行是否为调试会话（语义与置位时机同 RunSessionAsync）</param>
        /// <returns>true = 抢到会话锁并跑完（含被停止打断）；false = 未执行</returns>
        public async Task<bool> TryRunSessionOnceAsync(FlowSession session, bool debugSession = false)
        {
            if (session == null || session.ExecutionEngine == null) return false;

            if (IsFlowLocked(session.FlowName))
            {
                _logService.Warn($"流程 {session.FlowName} 已被锁定（禁止运行），本次单次执行请求被拒绝");
                return false;
            }

            // A3：抢到锁才继续，抢不到说明这个会话已经在跑
            var sessionLock = TryOccupySession(session);
            if (sessionLock == null)
            {
                _logService.Warn($"流程 {session.FlowName} 已在运行中，忽略重复的单次执行请求");
                return false;
            }

            try
            {
                // 非调试单次执行不经过暂停点（没有循环去等 PauseLock），显式标记为"不可暂停"，
                // 让 PauseSession 能拒绝这种请求而不是造出"UI 显示已暂停、流程照跑"的假象；
                // 调试会话（DebugEnabled）例外——它经调试门逐节点等待，PauseSession 守卫放行
                session.IsContinuousRun = false;
                // 兜底清掉上一轮"停在暂停点被停止"可能留下的合锁（调试单次执行才可能出现）
                // 与调试残留：单步请求 / 暂停原因的理由同 RunSessionAsync 开轮清理
                session.PauseLock.Set();
                session.DebugStepPending = false;
                session.PauseReason = SessionPauseReason.None;
                // 调试态按本次调用的显式参数置位（抢到锁之后），理由见 RunSessionAsync 的 P2 注释
                session.DebugEnabled = debugSession;
                session.IsRunning = true;

                // 单次执行同样需要取消令牌：否则 StopSession 因 CTS 为 null 而无法停止
                session.CancellationTokenSource = new CancellationTokenSource();
                var token = session.CancellationTokenSource.Token;

                // 与连续执行同一口径：订阅调试门命中，退订在收尾（成对）
                session.DebugStopped += OnDebugStopped;

                // 复位所有步序状态
                foreach (var step in session.Blueprints)
                {
                    step.ResetState();
                }

                NotifyStateChanged(session, SessionState.Running);
                _performanceMonitor?.RecordSessionStart(session.SessionID, session.FlowName);

                await Task.Run(() =>
                {
                    var context = new ExecutionContext(_logService, session, _workspaceManager, token)
                    {
                        Cameras = _cameras,
                        Motions = _motions,
                        FlowInvoker = FlowInvoker
                    };
                    // 与连续运行同一语义：一轮开始/结束各广播一次
                    NotifyFlowRunStarted(session);

                    session.ExecutionEngine.Run(context);

                    // 容器状态上浮：子步骤有失败 → 容器如实标 Failed（只改状态报告，不动控制流）。
                    // 放在广播之前：UI / 图像集 / HTTP 收集到的状态才是自洽的
                    session.EscalateContainerFailures();

                    // 单次运行跑完即广播（与连续运行同一语义：一轮结束）
                    NotifyFlowRunCompleted(session);
                }, token);
            }
            catch (ObjectDisposedException)
            {
                // 会话在本轮启动准备期间被并发替换/释放（RegisterSession / ClearAll 的 RemoveAndDispose）：
                // 这一单**一个节点都没跑**，不能算"跑过"——如实返回 false，让调用方（HTTP 收图 / 子程序调用）
                // 按"未执行"回错，而不是拿着别人/上一轮的端口值报成功。
                // finally 照常执行（ReleaseSession 归还这把锁定）
                _logService.Warn($"流程 {session.FlowName} 的会话在启动准备期间被释放（并发替换），本次执行未开始");
                return false;
            }
            catch (OperationCanceledException)
            {
                // 用户主动停止，正常路径
            }
            catch (Exception ex)
            {
                _logService.Error($"流程单次执行异常 {session.FlowName}: {ex.Message}");
                // State 由 NotifyStateChanged 统一赋值；ex.Message 也随之送达订阅者
                NotifyStateChanged(session, SessionState.Faulted, ex.Message);
            }
            finally
            {
                // 与连续执行同一结构、同一理由：收尾失败可以忽略，会话锁绝不能漏还
                try
                {
                    // 与连续执行同一口径：调试标志/订阅随本轮结束归零（理由见 RunSessionAsync 收尾）
                    session.DebugEnabled = false;
                    session.DebugStopped -= OnDebugStopped;

                    session.IsRunning = false;

                    // 单次执行结束同样清除运行焦点，避免最终行高亮卡住
                    foreach (var step in session.Blueprints)
                        step.IsRunningFocus = false;
                    session.FocusedStep = null;

                    _performanceMonitor?.RecordSessionEnd(session.SessionID);

                    if (session.State != SessionState.Faulted)
                    {
                        // State 由 NotifyStateChanged 统一赋值（唯一赋值点）
                        NotifyStateChanged(session, SessionState.Stopped);
                    }

                    // 兜底（同连续执行）：调试单次执行可能"停在暂停点"时被停止，
                    // 保证 PauseLock 不留在合上的状态；会话可能已被 RemoveAndDispose 释放
                    try { session.PauseLock.Set(); }
                    catch (ObjectDisposedException) { }

                    session.CancellationTokenSource?.Dispose();
                    session.CancellationTokenSource = null;
                }
                catch (Exception ex)
                {
                    _logService.Error(
                        $"流程 {session.FlowName} 收尾时发生异常（已忽略，会话锁照常归还）: {ex.Message}"
                    );
                }
                finally
                {
                    // 与连续执行同理：锁留到最后一步归还
                    ReleaseSession(sessionLock);
                }
            }

            // 走到这里说明这一单确实抢到锁执行过了（正常跑完 / 被停止 / 抛异常都算"跑过"）
            return true;
        }

        /// <summary>
        /// 暂停会话执行（连续执行全量有效；单次执行仅调试会话有效）
        ///
        /// 【为什么非调试单次执行要显式拒绝（#7）】
        /// 暂停生效的前提是执行体有等待 PauseLock 的位置 —— 连续执行在轮顶等；
        /// 单次执行只有"调试会话（DebugEnabled）"才经调试门逐节点等（DWV 第 1 期）。
        /// 非调试单次执行是一把跑完整图，中间没有等待位置；而它运行期间 State 同样是 Running，
        /// 所以旧实现只按 State == Running 判断，会把它也置成 Paused ——
        /// UI 显示"已暂停"、流程却一路跑到底，结束时直接落 Stopped，前后矛盾。
        /// 现在按 "IsContinuousRun || DebugEnabled" 显式区分，并对无效请求写 Warn（不静默吞掉）。
        /// </summary>
        /// <param name="session">要暂停的会话</param>
        public void PauseSession(FlowSession session)
        {
            if (session == null || !session.IsRunning || session.State != SessionState.Running)
                return;

            if (!session.IsContinuousRun && !session.DebugEnabled)
            {
                // 明确拒绝而不是静默返回：用户点了暂停，总得有个说法
                _logService.Warn(
                    $"流程 {session.FlowName} 单次执行不支持暂停（它不经过暂停点），本次暂停请求已忽略"
                );
                return;
            }

            // 记原因供 UI / 日志区分（调试门命中的 Breakpoint/Step 由门自己置位）
            session.PauseReason = SessionPauseReason.User;
            session.PauseLock.Reset();
            // State 由 NotifyStateChanged 统一赋值（唯一赋值点）
            NotifyStateChanged(session, SessionState.Paused);
        }

        /// <summary>
        /// 恢复会话执行（对"用户暂停 / 断点命中 / 单步停点"三种暂停统一适用）
        /// </summary>
        /// <param name="session">要恢复的会话</param>
        public void ResumeSession(FlowSession session)
        {
            // 会进入 Paused 的通路：连续执行（PauseSession）、调试会话的调试门（断点/单步）——
            // 两者都满足 IsRunning && State==Paused，所以这里不必再判 IsContinuousRun/DebugEnabled
            if (session != null && session.IsRunning && session.State == SessionState.Paused)
            {
                session.PauseReason = SessionPauseReason.None; // 已放行，暂停原因随之清空
                session.PauseLock.Set();
                // State 由 NotifyStateChanged 统一赋值（唯一赋值点）
                NotifyStateChanged(session, SessionState.Running);
            }
        }

        /// <summary>
        /// 单步执行：从调试停点放行，恰好执行一个节点后再次停住（DWV 第 1 期）。
        ///
        /// 机制：置"单步待停"标志再放行 —— 等待中的调试门醒来先执行当前节点，
        /// 下一个节点边界读到标志即消费并再次停住（容器算一步：它的子节点同样经过调试门，
        /// 从容器停点单步会停到容器内第一个子节点，天然"步入"）。
        /// 仅当"运行中且已暂停"时有效：其余情况没有等待中的调试门可唤醒，置了标志也只是
        /// 下一轮开始被清（见 RunSessionAsync 开轮清理）。
        /// </summary>
        /// <param name="session">要单步的流程会话</param>
        public void StepSession(FlowSession session)
        {
            if (session == null || !session.IsRunning || session.State != SessionState.Paused)
                return;

            // 置位先于放行：等待中的门醒来后必然读到（Set/Wait 之间的栅栏由 ManualResetEventSlim 提供）
            session.DebugStepPending = true;
            session.PauseReason = SessionPauseReason.None;
            session.PauseLock.Set();
            // State 由 NotifyStateChanged 统一赋值（唯一赋值点）
            NotifyStateChanged(session, SessionState.Running);
        }

        /// <summary>
        /// 停止会话执行
        ///
        /// 【为什么要把令牌取到局部、还要 catch ObjectDisposedException】
        /// 执行循环的 finally 里有 session.CancellationTokenSource?.Dispose() 紧接 = null。
        /// 原来的写法是 if (… != null) { … .Cancel(); } —— 属性读两次，
        /// "判空时非空、调用 Cancel 时已被 Dispose"这个窗口是真实存在的。
        /// 对已释放的 CTS 调 Cancel 会抛 ObjectDisposedException，而本方法是公共 API
        /// （退出任务、UI 停止按钮都走它），让它炸出去毫无意义：循环本来就在退出。
        /// 这里与 RuntimeManager.StopSessionGracefully 保持同一口径（那边早就这么防了）。
        /// </summary>
        /// <param name="session">要停止的会话</param>
        public void StopSession(FlowSession session)
        {
            if (session == null || !session.IsRunning) return;

            try
            {
                var cts = session.CancellationTokenSource;
                cts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // 循环线程已退出并释放了 CTS，取消动作已无意义，忽略即可
            }
        }

        /// <summary>
        /// 紧急停止所有正在运行的会话（急停开关）。
        ///
        /// 【为什么遍历必须走快照，而不是直接 foreach ActiveSessions】
        /// 那是运行期可变集合：HTTP 请求线程可能正在 RegisterSession，而本方法常跑在退出链上。
        /// 裸枚举撞上增删即抛 "Collection was modified"，而 StopAll 是退出任务的一环 ——
        /// 抛出去会拖住整条退出链（现场表现：软件关不掉）。SnapshotSessions 在集合锁内复制一份，
        /// 几微秒且不等待，拿到手以后再逐个停。
        ///
        /// 【单个会话停不下来不能影响其余会话】急停要的是"尽可能多停几个"，
        /// 所以每个 StopSession 各自 try/catch，失败只记日志。
        /// </summary>
        public void StopAll()
        {
            IReadOnlyList<FlowSession> snapshot;
            try
            {
                snapshot = _runtimeManager.SnapshotSessions();
            }
            catch (Exception ex)
            {
                _logService.Error($"急停：获取活动会话快照失败（已隔离，不拖累退出链）: {ex.Message}");
                return;
            }

            foreach (var session in snapshot)
            {
                try
                {
                    StopSession(session);
                }
                catch (Exception ex)
                {
                    _logService.Warn($"急停：停止流程 {session?.FlowName} 失败（已隔离，继续停其余会话）: {ex.Message}");
                }
            }
        }
    }
}
