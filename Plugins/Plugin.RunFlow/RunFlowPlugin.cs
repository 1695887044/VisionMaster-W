using Core.Interfaces;
using System;
using System.ComponentModel.DataAnnotations;

namespace VisionMaster.Plugins.RunFlow
{
    /// <summary>
    /// 调用流程（子程序调用）：把另一条流程当子程序跑一遍，等它跑完再把结果带回父流程。
    ///
    /// 能力来源：<see cref="IExecutionContext.FlowInvoker"/>——插件物理上够不到宿主引擎，
    /// 调用能力由执行上下文递送（与全局变量写入口、相机仓库同一范式；没装配时拿到的是
    /// NullFlowInvoker，"调用即失败并说明原因"，本插件因此不必判空）。
    ///
    /// 门禁全在宿主侧一处判定（本插件不做二次判定，避免"两处各判一半"的口径漂移）：
    ///  · 目标流程必须存在 / 启用 / 未加密，且**调用方式勾选了「子程序」**；
    ///  · 目标正在运行 → 直接失败（不排队，防止流程互等挂死；自身递归调用也由此拦住）。
    ///
    /// 为什么"等待"是默认且唯一的模式：引擎按节点顺序执行，父流程在等子流程时本来就没法往下走；
    /// 不做"不等待"分支，是为了避免出现"子流程还在往回写全局变量、父流程已经往下跑"的竞态
    /// （真要并行，应该用两条独立流程 + 各自的调用方式，而不是一个"不等待的调用"开关）。
    /// </summary>
    [Display(
        Name = "调用流程",
        GroupName = "流程控制",
        Description = "把另一条流程当子程序调用（目标流程需勾选「子程序」调用方式），等待其跑完并把结果带回",
        ShortName = "\uf0e8"
    )]
    public class RunFlowPlugin : VisionPluginBase
    {
        /// <summary>目标流程名（必填；按名查找，与 HTTP 路由 / 运行状态镜像同口径）</summary>
        public InputPort<string> TargetFlowPort { get; } =
            new InputPort<string>("FlowName", "", "目标流程名") { IsRequired = true };

        /// <summary>等待超时（毫秒，下限 1000）：超时只报错、不打断目标</summary>
        public InputPort<int> TimeoutPort { get; } =
            new InputPort<int>("TimeoutMs", 60000, "等待超时(ms)");

        /// <summary>是否成功执行（含"跑完但有失败步骤" = false）</summary>
        public OutputPort<bool> Invoked { get; } = new OutputPort<bool>("Invoked", "是否成功执行");

        /// <summary>从开始调用到返回的耗时（毫秒）</summary>
        public OutputPort<int> ElapsedMs { get; } = new OutputPort<int>("ElapsedMs", "执行耗时(ms)");

        /// <summary>调用结果说明（失败时是可直接展示的中文原因）</summary>
        public OutputPort<string> InvokeMessage { get; } = new OutputPort<string>("Message", "调用结果说明");

        public override void RunAlgorithm(IExecutionContext context)
        {
            Invoked.Value = false;
            ElapsedMs.Value = 0;
            InvokeMessage.Value = string.Empty;
            // 框架契约：VisionPluginBase.Execute 以 Success.Value is true 判定业务成功
            Success.Value = false;

            var target = TargetFlowPort.GetTypedValue()?.Trim();
            if (string.IsNullOrEmpty(target))
            {
                InvokeMessage.Value = "目标流程名不能为空";
                context.Logger.Error($"{InstanceName} 未指定目标流程名（请在「目标流程名」里选择或填写）");
                return;
            }

            var result = context.FlowInvoker.Invoke(target, TimeoutPort.GetTypedValue(), context.CancellationToken);

            Invoked.Value = result.Success;
            ElapsedMs.Value = result.ElapsedMs;
            InvokeMessage.Value = result.Message;
            Success.Value = result.Success;

            if (result.Success)
                context.Logger.Info($"{InstanceName} {result.Message}（{result.ElapsedMs}ms）");
            else
                context.Logger.Error($"{InstanceName} 调用流程「{target}」失败：{result.Message}");
        }

        public override void Initialize() { }

        public override void Dispose() { }
    }
}
