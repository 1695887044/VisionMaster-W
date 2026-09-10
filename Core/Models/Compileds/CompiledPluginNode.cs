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

        /// <summary>
        /// 本节点需要绑定 context 的代理端口集合
        /// 由 FlowCompiler.LinkPorts 填充：当某 InputPort 引用了运行时变量
        /// （RuntimeVariableProxyPort），编译期被加入此集合
        /// 执行插件前会逐个 BindContext(context)，触发 ValueChanged，
        /// 让对应 InputPort 重新 RefreshLinkedCache 读取最新变量值
        /// </summary>
        public List<IContextAwareOutputPort> ContextAwareBindings { get; } = new();

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
                // 在执行插件前，把 context 注入所有代理端口
                // 这样引用了运行时变量的 InputPort 在插件内 GetTypedValue() 时能取到最新值
                if (ContextAwareBindings.Count > 0)
                {
                    foreach (var binding in ContextAwareBindings)
                        binding.BindContext(context);
                }

                ExternalPlugin.Execute(context);
                UpdateStepRuntimeState(context, StepRuntimeState.Success);
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
