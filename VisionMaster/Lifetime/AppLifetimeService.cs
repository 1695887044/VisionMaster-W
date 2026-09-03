using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Core.Interfaces;

namespace VisionMaster.Lifetime
{
    /// <summary>启动链执行结果</summary>
    public record StartupChainResult(bool Success, string? BlockingReason, IReadOnlyList<string> Warnings);

    /// <summary>单项自检完成事件参数（SplashScreen 订阅刷新）</summary>
    public record CheckProgressEventArgs(string Name, bool Passed, CheckResult Result, int Done, int Total);

    /// <summary>
    /// 应用生命周期宿主：启动自检链 / 运行期异常分级 / 退出资源释放链。
    /// 退出任务通过委托注册（ExitTask），业务服务无需依赖本模块。
    /// </summary>
    public class AppLifetimeService
    {
        private readonly ILogService _log;
        private readonly ExceptionGrading _grading;
        private readonly List<IStartupCheck> _checks = new();
        private readonly List<ExitTask> _exitTasks = new();
        private int _shutdownExecuted;

        /// <summary>注册的自检项（Splash 初始化用）</summary>
        public IReadOnlyList<IStartupCheck> Checks => _checks;

        /// <summary>单项自检完成（name, passed, result, done, total）</summary>
        public event Action<CheckProgressEventArgs>? CheckCompleted;

        /// <summary>警告级异常抬升（主界面状态栏可订阅做非阻断提示）</summary>
        public event Action<string>? WarningRaised;

        public AppLifetimeService(ILogService log)
        {
            _log = log;
            _grading = new ExceptionGrading(log);
            _grading.WarningRaised += m => WarningRaised?.Invoke(m);
            _grading.CriticalOccurred += ex => OnCriticalAsync(ex);
        }

        #region 注册

        /// <summary>注册启动自检项（按调用顺序执行）</summary>
        public void RegisterCheck(IStartupCheck check) => _checks.Add(check);

        /// <summary>注册退出任务（执行时按注册相反顺序）</summary>
        public void RegisterExitTask(ExitTask task) => _exitTasks.Add(task);

        /// <summary>接线三处全局异常入口（应在 OnStartup 尽早调用）</summary>
        public void RegisterGlobalExceptionHandlers()
        {
            Application.Current.DispatcherUnhandledException += (s, e) =>
            {
                Handle(e.Exception, ExceptionGrading.ClassifyGlobal(ExceptionGrading.UnhandledExceptionSource.Dispatcher), "UI");
                e.Handled = true; // 分级器已处置：提示/降级/退出
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                    Handle(ex, ExceptionGrading.ClassifyGlobal(ExceptionGrading.UnhandledExceptionSource.AppDomain), "AppDomain");
            };
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                Handle(e.Exception, ExceptionGrading.ClassifyGlobal(ExceptionGrading.UnhandledExceptionSource.UnobservedTask), "Task");
                e.SetObserved();
            };
        }

        /// <summary>显式上报异常（业务代码推荐入口）</summary>
        public void Handle(Exception ex, ExceptionSeverity severity, string context = "")
            => _grading.Handle(ex, severity, context);

        #endregion

        #region 启动链

        /// <summary>
        /// 顺序执行启动自检链。Error 级失败立即返回失败；Warning 级记录后继续。
        /// </summary>
        public async Task<StartupChainResult> RunStartupChecksAsync(CancellationToken ct = default)
        {
            var warnings = new List<string>();
            int total = _checks.Count, done = 0;

            foreach (var check in _checks)
            {
                CheckResult result;
                try
                {
                    // 单项兜底超时 30 秒（检查项内部通常已有更短超时）
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    linked.CancelAfter(30_000);
                    var execTask = check.ExecuteAsync(linked.Token);
                    var winner = await Task.WhenAny(execTask, Task.Delay(Timeout.Infinite, linked.Token));
                    if (winner != execTask)
                        result = CheckResult.Fail(CheckLevel.Error, "检查超时（30 秒）");
                    else
                        result = execTask.Result;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw; // 外部取消（窗口关闭等）
                }
                catch (Exception ex)
                {
                    result = CheckResult.Fail(CheckLevel.Error, "检查异常：" + ex.Message);
                }

                done++;
                CheckCompleted?.Invoke(new CheckProgressEventArgs(check.Name, result.Passed, result, done, total));

                if (result.Passed)
                {
                    if (!string.IsNullOrEmpty(result.Message))
                        _log.Info($"[启动自检] {check.Name}: {result.Message}");
                }
                else if (result.Level == CheckLevel.Error)
                {
                    _log.Error($"[启动自检] {check.Name} 失败：{result.Message}");
                    return new StartupChainResult(false, $"{check.Name}：{result.Message}", warnings);
                }
                else
                {
                    warnings.Add($"{check.Name}：{result.Message}");
                    _log.Warn($"[启动自检] {check.Name} 警告：{result.Message}");
                }
            }

            return new StartupChainResult(true, null, warnings);
        }

        #endregion

        #region 退出链

        /// <summary>严重级异常处置：安全停机 → 有序退出（任意线程触发，自动回 UI 线程）</summary>
        private void OnCriticalAsync(Exception ex)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return;
            dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    await ExecuteExitChainAsync("严重异常");
                }
                catch { /* 退出链异常不抛出 */ }
                finally
                {
                    Environment.Exit(1); // 严重异常下不依赖优雅 Shutdown
                }
            });
        }

        /// <summary>
        /// 逆序执行退出任务（后注册先释放）。
        /// 单项超时保护；整体看门狗 30 秒后强制终止进程。
        /// 幂等：多次调用只执行一次。
        /// </summary>
        public async Task ExecuteExitChainAsync(string trigger)
        {
            if (Interlocked.Exchange(ref _shutdownExecuted, 1) == 1) return;

            _log.Info($"[退出链] 开始执行（触发：{trigger}），共 {_exitTasks.Count} 项");
            using var watchdog = new CancellationTokenSource(30_000);

            for (int i = _exitTasks.Count - 1; i >= 0; i--)
            {
                var task = _exitTasks[i];
                try
                {
                    using var per = CancellationTokenSource.CreateLinkedTokenSource(watchdog.Token);
                    per.CancelAfter(task.TimeoutMs);
                    var exec = task.Execute();
                    var winner = await Task.WhenAny(exec, Task.Delay(Timeout.Infinite, per.Token));
                    _log.Info(winner == exec
                        ? $"[退出链] ✓ {task.Name}"
                        : $"[退出链] ✗ {task.Name} 超时（{task.TimeoutMs}ms），跳过");
                }
                catch (OperationCanceledException) when (watchdog.Token.IsCancellationRequested)
                {
                    _log.Error("[退出链] 总看门狗超时（30s），剩余任务放弃");
                    return;
                }
                catch (Exception ex)
                {
                    _log.Warn($"[退出链] ✗ {task.Name} 异常：{ex.Message}");
                }
            }
            _log.Info("[退出链] 全部完成");
        }

        /// <summary>OnExit 用的阻塞版本（退出阶段已无 UI 需要响应）</summary>
        public void ExecuteExitChainBlocking(string trigger)
            => ExecuteExitChainAsync(trigger).GetAwaiter().GetResult();

        #endregion
    }
}
