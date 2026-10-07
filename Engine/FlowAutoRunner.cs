using System;
using System.Linq;
using System.Threading.Tasks;
using Core.Interfaces;
using VisionMaster.Models;

namespace VisionMaster.Services
{
    /// <summary>
    /// 自动触发的公共执行段：把"某条流程该跑一次了"变成"真的跑了一次"。
    ///
    /// 定时调度器与变量触发服务共用它——两处的准备步骤一字不差：
    ///  ① 目标在跑 → 跳过（定时/变量触发都不抢占；HTTP 与手动运行返回明确错误，此处只是"错过这一拍"）；
    ///  ② 没有会话或图纸改过 → 编译并注册（与"编译本流程"/HTTP 补编译同口径）；
    ///  ③ 单次执行（非调试：断点不生效，自动触发不允许被遗留断点卡住产线）。
    ///
    /// 为什么单独一个类而不是各自复制一遍：这两条链路的行为口径必须一致
    /// （错一次就是"定时跑的流程不带调试豁免"这类隐蔽差异），口径只写一份。
    /// </summary>
    internal static class FlowAutoRunner
    {
        /// <summary>
        /// 尝试把流程跑一次。<paramref name="reason"/> 只用于日志前缀（"定时" / "变量触发"），
        /// 便于现场一眼分清这一轮是谁触发的。
        /// </summary>
        internal static void RunOnce(
            IWorkspaceManager workspace,
            IRuntimeManager runtime,
            IFlowEngine engine,
            FlowCompiler compiler,
            ILogService log,
            FlowModel flow,
            string reason)
        {
            if (workspace?.CurrentSolution == null || flow == null) return;

            // 加密流程不参与自动触发：与定时判定（ShouldFire）/ 子程序调用同一口径——
            // 加密的意义就是"内容不给看、也不许被跑"，只在界面侧拦会让这条链上出现例外
            if (flow.StepsEncrypted)
            {
                log?.Warn($"[{reason}] 流程「{flow.FlowName}」步序已加密，本次触发跳过");
                return;
            }

            try
            {
                var session = runtime.GetSessionByName(flow.FlowName);
                if (session?.IsRunning == true)
                {
                    log?.Warn($"[{reason}] 流程「{flow.FlowName}」正在运行，本次触发跳过");
                    return;
                }

                // 会话准备：单点收口（查会话 → 判新鲜度[FlowID+Version] → 必要时重编译 → 注册）
                if (!FlowSessionFactory.TryEnsureSession(runtime, compiler, flow, out session, out var sessionError, log))
                {
                    log?.Error($"[{reason}] {sessionError}");
                    return;
                }

                _ = RunObservedAsync(engine, session, flow.FlowName, reason, log);
            }
            catch (Exception ex)
            {
                log?.Warn($"[{reason}] 流程「{flow.FlowName}」触发失败：{ex.Message}");
            }
        }

        private static async Task RunObservedAsync(IFlowEngine engine, FlowSession session, string flowName, string reason, ILogService log)
        {
            try
            {
                // 用 Try... 版本：自动触发与"并发调用"撞上时（定时拍 + 手动运行同时到），
                // 引擎会返回 false；如实记一条 Warn，现场排查"这拍到底跑没跑"时有确凿依据
                var ran = await engine.TryRunSessionOnceAsync(session);
                if (!ran)
                    log?.Warn($"[{reason}] 流程「{flowName}」正被其它执行占用，本次触发未执行");
            }
            catch (Exception ex)
            {
                // 结局由引擎侧反映（日志 / 状态事件）；这里只负责消化任务异常，
                // 顺手留一条带触发来源的 Warn，现场"流程莫名跑了/没跑"时有抓手
                log?.Warn($"[{reason}] 流程「{flowName}」执行任务异常结束：{ex.Message}");
            }
        }
    }
}
