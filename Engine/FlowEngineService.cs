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
            IResourceLockService resourceLocks)
        {
            _runtimeManager = runtimeManager ?? throw new ArgumentNullException(nameof(runtimeManager));
            _logService = logService ?? throw new ArgumentNullException(nameof(logService));
            _workspaceManager = workspaceManager ?? throw new ArgumentNullException(nameof(workspaceManager));
            _performanceMonitor = performanceMonitor;
            _resourceLocks = resourceLocks ?? throw new ArgumentNullException(nameof(resourceLocks));
        }

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
        /// 启动会话连续执行
        /// </summary>
        /// <param name="session">要执行的会话</param>
        /// <returns>异步任务</returns>
        public async Task RunSessionAsync(FlowSession session)
        {
            if (session == null || session.ExecutionEngine == null)
                throw new ArgumentException("Session 或底层执行引擎不能为空，请先编译！");

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
                session.IsRunning = true;
                session.PauseLock.Set();
                session.CancellationTokenSource = new CancellationTokenSource();
                var token = session.CancellationTokenSource.Token;

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

                        var context = new ExecutionContext(_logService, session, _workspaceManager, token);
                        session.ExecutionEngine.Run(context);

                        // 用令牌等待替代 Thread.Sleep：空闲节流 10ms，但停止请求会立即唤醒退出
                        token.WaitHandle.WaitOne(10);
                    }
                }, token);
            }
            catch (OperationCanceledException)
            {
                // 正常取消，无需处理
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
        /// <returns>异步任务</returns>
        public async Task RunSessionOnceAsync(FlowSession session)
        {
            if (session == null || session.ExecutionEngine == null) return;

            // A3：抢到锁才继续，抢不到说明这个会话已经在跑
            var sessionLock = TryOccupySession(session);
            if (sessionLock == null)
            {
                _logService.Warn($"流程 {session.FlowName} 已在运行中，忽略重复的单次执行请求");
                return;
            }

            try
            {
                // 单次执行不经过暂停点（没有循环去等 PauseLock），显式标记为"不可暂停"，
                // 让 PauseSession 能拒绝这个请求而不是造出"UI 显示已暂停、流程照跑"的假象
                session.IsContinuousRun = false;
                session.IsRunning = true;

                // 单次执行同样需要取消令牌：否则 StopSession 因 CTS 为 null 而无法停止
                session.CancellationTokenSource = new CancellationTokenSource();
                var token = session.CancellationTokenSource.Token;

                // 复位所有步序状态
                foreach (var step in session.Blueprints)
                {
                    step.ResetState();
                }

                NotifyStateChanged(session, SessionState.Running);
                _performanceMonitor?.RecordSessionStart(session.SessionID, session.FlowName);

                await Task.Run(() =>
                {
                    var context = new ExecutionContext(_logService, session, _workspaceManager, token);
                    session.ExecutionEngine.Run(context);
                }, token);
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
        }

        /// <summary>
        /// 暂停会话执行（**仅对连续执行有效**）
        ///
        /// 【为什么单次执行要显式拒绝（#7）】
        /// 暂停生效的前提是执行体在循环里等 PauseLock —— 只有连续执行有这个循环。
        /// 单次执行是一把跑完整图，中间没有可插入等待的位置；而它运行期间 State 同样是 Running，
        /// 所以旧实现只按 State == Running 判断，会把它也置成 Paused ——
        /// UI 显示"已暂停"、流程却一路跑到底，结束时直接落 Stopped，前后矛盾。
        /// 现在按 IsContinuousRun 显式区分，并对无效请求写 Warn（不静默吞掉）。
        /// </summary>
        /// <param name="session">要暂停的会话</param>
        public void PauseSession(FlowSession session)
        {
            if (session == null || !session.IsRunning || session.State != SessionState.Running)
                return;

            if (!session.IsContinuousRun)
            {
                // 明确拒绝而不是静默返回：用户点了暂停，总得有个说法
                _logService.Warn(
                    $"流程 {session.FlowName} 单次执行不支持暂停（它不经过暂停点），本次暂停请求已忽略"
                );
                return;
            }

            session.PauseLock.Reset();
            // State 由 NotifyStateChanged 统一赋值（唯一赋值点）
            NotifyStateChanged(session, SessionState.Paused);
        }

        /// <summary>
        /// 恢复会话执行
        /// </summary>
        /// <param name="session">要恢复的会话</param>
        public void ResumeSession(FlowSession session)
        {
            // 只有连续执行会被置成 Paused（见 PauseSession），所以这里不必再判 IsContinuousRun
            if (session != null && session.IsRunning && session.State == SessionState.Paused)
            {
                session.PauseLock.Set();
                // State 由 NotifyStateChanged 统一赋值（唯一赋值点）
                NotifyStateChanged(session, SessionState.Running);
            }
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
                // 循环线程正在收尾（令牌已释放）：它本来就在退出，无需再停
            }
        }

        /// <summary>
        /// 停止所有会话
        /// </summary>
        public void StopAll()
        {
            // 走快照而不是裸枚举 ActiveSessions：本方法是退出任务的一环，
            // 而 HTTP 请求线程此时仍可能 RegisterSession/UnregisterSession，
            // 裸枚举撞上增删会抛 "Collection was modified"，把整条退出链拖住。
            foreach (var session in _runtimeManager.SnapshotSessions())
            {
                StopSession(session);
            }
        }
    }
}