using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Core.Interfaces;
using VisionMaster.Models;

namespace VisionMaster.Services
{
    /// <summary>
    /// 流程调用器：<see cref="IFlowInvoker"/> 在宿主侧的实现（「调用流程」子程序步骤的能力面）。
    ///
    /// 门禁与失败口径（全部在此一处判定，插件不做二次判定）：
    ///  1. 目标流程必须存在、启用、未加密；
    ///  2. **调用方式必须勾选「子程序调用」**——与 HTTP 门禁同一条纪律（用户决策：
    ///     "只有选择了这个的才能被调用"扩展到子程序）。没勾就是把流程当"私有块"，
    ///     不允许被别的流程顺手拖起来；
    ///  3. 目标正在运行 → 直接失败（**不排队**：排队会把"流程 A 等 B、B 又在等 A"
    ///     这种成环误配变成挂死现场，直接报错让人去改）；
    ///  4. 超时**不打断**目标（取消归引擎自己的 CTS 管，这里抢它的令牌会污染会话状态）——
    ///     与 HTTP 收图的超时口径逐字一致，只把"没等到"如实报给父流程。
    ///
    /// 为什么结果里带耗时与"失败步骤数"：父流程的输出端口要把这两件事带回界面上，
    /// 工厂现场排查"子流程到底跑没跑、跑错了哪一步"时，只看一个 bool 是查不动的。
    /// </summary>
    public sealed class FlowInvoker : IFlowInvoker
    {
        private readonly IWorkspaceManager _workspace;
        private readonly IRuntimeManager _runtime;
        private readonly IFlowEngine _engine;
        private readonly FlowCompiler _compiler;
        private readonly ILogService _log;

        public FlowInvoker(
            IWorkspaceManager workspace,
            IRuntimeManager runtime,
            IFlowEngine engine,
            FlowCompiler compiler,
            ILogService log)
        {
            _workspace = workspace;
            _runtime = runtime;
            _engine = engine;
            _compiler = compiler;
            _log = log;
        }

        /// <inheritdoc />
        public FlowInvokeResult Invoke(string flowName, int timeoutMs, CancellationToken cancellationToken)
        {
            var sw = Stopwatch.StartNew();
            var name = flowName?.Trim();

            if (string.IsNullOrEmpty(name))
                return FlowInvokeResult.Fail("目标流程名不能为空（在「调用流程」步骤里选择或填写流程名）", (int)sw.ElapsedMilliseconds);

            if (cancellationToken.IsCancellationRequested)
                return FlowInvokeResult.Fail("调用前父流程已被取消（急停 / 停止）", (int)sw.ElapsedMilliseconds);

            var flow = FindFlow(name);
            if (flow == null)
                return FlowInvokeResult.Fail($"当前方案中没有名为「{name}」的流程", (int)sw.ElapsedMilliseconds);

            if (!flow.IsEnabled)
                return FlowInvokeResult.Fail($"流程「{name}」已被禁用，不能作为子程序调用", (int)sw.ElapsedMilliseconds);

            if ((flow.InvokeType & FlowInvokeType.Subroutine) == 0)
                return FlowInvokeResult.Fail(
                    $"流程「{name}」未开放「子程序调用」（当前调用方式：{flow.InvokeType.DisplayText()}）。"
                    + "请在「流程管理」里勾选「子程序」后重试",
                    (int)sw.ElapsedMilliseconds);

            if (flow.StepsEncrypted)
                return FlowInvokeResult.Fail($"流程「{name}」步序已加密，无法编译调用", (int)sw.ElapsedMilliseconds);

            // 目标正在跑 → 直接失败（门禁的第一道，语义与文案都是"调用侧"的；重建守卫在会话准备单点里）
            if (_runtime.GetSessionByName(name)?.IsRunning == true)
                return FlowInvokeResult.Fail($"流程「{name}」正在运行中，本次子程序调用未执行（避免成环等待）", (int)sw.ElapsedMilliseconds);

            // 会话准备：单点收口（查会话 → 判新鲜度[FlowID+Version] → 必要时重编译 → 注册）
            if (!FlowSessionFactory.TryEnsureSession(_runtime, _compiler, flow, out var session, out var sessionError, _log))
                return FlowInvokeResult.Fail(sessionError, (int)sw.ElapsedMilliseconds);

            var task = _engine.TryRunSessionOnceAsync(session);

            // 等待完成：轮询式等待同时兼顾"父流程被急停"与"超时"两件事——
            // task.Wait(timeout) 一次性阻塞既打断不了也对不齐取消令牌
            var timeout = TimeSpan.FromMilliseconds(Math.Max(1000, timeoutMs));
            while (!task.IsCompleted)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    ObserveFault(task);
                    return FlowInvokeResult.Fail(
                        $"流程「{name}」执行中父流程被取消（急停），目标仍在后台收尾", (int)sw.ElapsedMilliseconds);
                }

                if (sw.Elapsed >= timeout)
                {
                    ObserveFault(task);
                    return FlowInvokeResult.Fail(
                        $"等待流程「{name}」执行超时（>{timeout.TotalMilliseconds:F0}ms），目标仍在后台继续",
                        (int)sw.ElapsedMilliseconds);
                }

                Thread.Sleep(20);
            }

            bool ran;
            try
            {
                ran = task.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                var reason = task.IsCanceled ? "执行被取消（急停 / 停止）" : $"执行异常：{ex.GetBaseException().Message}";
                return FlowInvokeResult.Fail($"流程「{name}」{reason}", (int)sw.ElapsedMilliseconds);
            }

            // ★ 抢不到会话锁时引擎返回 false（不抛不静默）：**必须当场如实报错**。
            // 旧签名在这里只记一条 Warn 就正常返回，于是并发调用同一子流程时，
            // 后到的这一单会把"我没跑上"读成"跑完了"（父流程带着未落地的数据继续走）
            if (!ran)
                return FlowInvokeResult.Fail(
                    $"流程「{name}」正在运行中（被其它执行占用），本次子程序调用未执行", (int)sw.ElapsedMilliseconds);

            var failedSteps = session.Blueprints.Count(s => s.State == StepState.Failed);
            var elapsed = (int)sw.ElapsedMilliseconds;

            if (failedSteps > 0)
            {
                _log.Warn($"[调用流程] 流程「{name}」跑完但有 {failedSteps} 个步骤失败（详见运行日志）");
                return FlowInvokeResult.Fail($"流程「{name}」跑完但有 {failedSteps} 个步骤失败（详见运行日志）", elapsed);
            }

            _log.Info($"[调用流程] 流程「{name}」执行完成，耗时 {elapsed}ms");
            return FlowInvokeResult.Ok($"流程「{name}」执行完成", elapsed);
        }

        private FlowModel FindFlow(string flowName)
        {
            var flows = _workspace?.CurrentSolution?.Flows;
            if (flows == null) return null;

            // 与引擎补编译 / 状态镜像 / HTTP 路由同口径（Ordinal）：
            // 判据不一致会出现"找到了流程却找不到会话"这种自己跟自己打架的状态
            return flows.FirstOrDefault(f => string.Equals(f.FlowName, flowName, StringComparison.Ordinal));
        }

        /// <summary>超时/取消返回后目标可能仍会炸：挂个观察者把异常吃掉，避免变成未观察异常</summary>
        private static void ObserveFault(Task task)
        {
            _ = task.ContinueWith(
                t => { _ = t.Exception; },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }
    }
}
