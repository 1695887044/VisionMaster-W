using Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.Models
{
    /// <summary>
    /// 编译后的插件节点
    /// 封装外部视觉插件的执行
    /// </summary>
    public class CompiledPluginNode : CompiledNode
    {
        /// <summary>
        /// 外部插件实例
        /// </summary>
        public IVisionPlugin ExternalPlugin { get; set; }

        // ContextAwareBindings 已上移至 CompiledNode 基类（条件/For 节点同样需要引用运行时变量）

        /// <summary>
        /// 执行插件节点
        /// </summary>
        public override List<CompiledNode> RunAndGetNext(IExecutionContext context)
        {
            context.CurrentNodeId = Id;
            UpdateStepRuntimeState(context, StepRuntimeState.Running);

            if (context.CancellationToken.IsCancellationRequested)
            {
                UpdateStepRuntimeState(context, StepRuntimeState.Skipped);
                return null;
            }

            try
            {
                // 在执行插件前，把 context 注入所有代理端口（基类统一实现）
                // 这样引用了运行时变量的 InputPort 在插件内 GetTypedValue() 时能取到最新值
                BindContextAwarePorts(context);

                bool ok = ExternalPlugin.Execute(context);
                if (!ok)
                {
                    // 插件业务失败（RunAlgorithm 不抛异常、写 Success=false）：
                    // 标记步骤失败 + 落日志；不抛异常，流程按既有语义继续，软件不崩
                    UpdateStepRuntimeState(context, StepRuntimeState.Failed);
                    context.Logger?.Error($"步骤[{ExternalPlugin.InstanceName}]执行失败: {ExternalPlugin.LastError}");
                    return null;
                }

                UpdateStepRuntimeState(context, StepRuntimeState.Success);
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
            {
                // 取消（停止 / 急停）不是失败（A9）：插件是被"叫停"的，不是算错了。
                // 标 Skipped（StepState 没有 Cancel 值，Skipped 是"本轮没跑完但不算错"的既有口径），
                // 吞掉异常让外层序列照常退栈 —— RunSequence 下一次循环就会读到令牌已取消并返回。
                // 判据带 when 令牌已取消：插件内部自己抛的 OCE（例如它自己的超时）仍按失败处理。
                UpdateStepRuntimeState(context, StepRuntimeState.Skipped);
                return null;
            }
            catch (Exception)
            {
                UpdateStepRuntimeState(context, StepRuntimeState.Failed);
                throw;
            }

            return null;
        }
    }
}
