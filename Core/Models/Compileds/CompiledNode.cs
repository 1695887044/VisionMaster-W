using Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using VisionMaster.Services;

namespace VisionMaster.Models
{
    /// <summary>
    /// 编译节点基类
    /// 所有编译后节点的抽象基类
    /// </summary>
    public abstract class CompiledNode
    {
        /// <summary>
        /// 节点唯一标识
        /// </summary>
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// 节点名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 步骤名称（用于UI显示）
        /// </summary>
        public string StepName { get; set; }

        /// <summary>
        /// 本节点需要在执行期绑定 context 的代理端口集合（如引用运行时变量的 RuntimeVariableProxyPort）。
        /// 由 FlowCompiler.LinkPorts 填充；节点执行体前先调 BindContextAwarePorts 注入 context，
        /// 代理端口才能从 context.LocalVariables 取到最新值。
        /// （原为 CompiledPluginNode 独有，上移基类后条件/For 节点也能引用运行时变量）
        /// </summary>
        public List<IContextAwareOutputPort> ContextAwareBindings { get; } = new();

        /// <summary>
        /// 执行体前统一注入上下文：把 context 交给所有代理端口并触发下游缓存刷新
        /// </summary>
        protected void BindContextAwarePorts(IExecutionContext context)
        {
            if (ContextAwareBindings.Count > 0)
            {
                foreach (var binding in ContextAwareBindings)
                    binding.BindContext(context);
            }
        }

        /// <summary>
        /// 执行节点并获取下一个要执行的节点列表
        /// </summary>
        public abstract List<CompiledNode> RunAndGetNext(IExecutionContext context);

        /// <summary>
        /// 更新步骤运行时状态
        /// 高亮采用"执行指针"语义：新步骤 Running 时从上一个焦点步骤平滑接管，
        /// Success/Failed 不清焦点（保留最近执行高亮，避免毫秒级步骤的高亮闪变不可见）
        /// </summary>
        protected void UpdateStepRuntimeState(IExecutionContext context, StepRuntimeState state)
        {
            if (context is VisionMaster.Services.ExecutionContext execContext && execContext.CurrentSession != null)
            {
                var session = execContext.CurrentSession;
                var step = session.Blueprints
                    .FirstOrDefault(s => s.StepID == Id);

                if (step != null)
                {
                    step.State = state switch
                    {
                        StepRuntimeState.Idle => StepState.Idle,
                        StepRuntimeState.Running => StepState.Running,
                        StepRuntimeState.Success => StepState.Success,
                        StepRuntimeState.Failed => StepState.Failed,
                        StepRuntimeState.Skipped => StepState.Skipped,
                        _ => StepState.Idle
                    };

                    if (state == StepRuntimeState.Running)
                    {
                        // 执行指针：焦点从上一个步骤移交，恒定单行高亮
                        if (session.FocusedStep != null && !ReferenceEquals(session.FocusedStep, step))
                            session.FocusedStep.IsRunningFocus = false;
                        step.IsRunningFocus = true;
                        step.BeginTiming();
                        session.FocusedStep = step;
                    }
                    else if (state == StepRuntimeState.Success || state == StepRuntimeState.Failed)
                    {
                        // 完成不清焦点（执行指针停留）；耗时冻结在最终值，防止 UI 定时器继续累加
                        step.EndTiming();
                    }
                    else if (state == StepRuntimeState.Skipped && ReferenceEquals(session.FocusedStep, step))
                    {
                        // 取消导致的跳过：释放焦点
                        step.IsRunningFocus = false;
                        session.FocusedStep = null;
                    }
                }
            }
        }
    }

    /// <summary>
    /// 步骤运行时状态枚举
    /// </summary>
    public enum StepRuntimeState
    {
        Idle,
        Running,
        Success,
        Failed,
        Skipped
    }


}
