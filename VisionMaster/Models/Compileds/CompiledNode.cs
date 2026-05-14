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
        /// 执行节点并获取下一个要执行的节点列表
        /// </summary>
        public abstract List<CompiledNode> RunAndGetNext(IExecutionContext context);

        /// <summary>
        /// 更新步骤运行时状态
        /// </summary>
        protected void UpdateStepRuntimeState(IExecutionContext context, StepRuntimeState state)
        {
            if (context is VisionMaster.Services.ExecutionContext execContext && execContext.CurrentSession != null)
            {
                var step = execContext.CurrentSession.Blueprints
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
                        step.IsRunningFocus = true;
                        step.LastRunStartTime = DateTime.Now;
                    }
                    else if (state == StepRuntimeState.Success || state == StepRuntimeState.Failed)
                    {
                        step.IsRunningFocus = false;
                        if (step.LastRunStartTime.HasValue)
                        {
                            step.LastRunTimeMs = (long)(DateTime.Now - step.LastRunStartTime.Value).TotalMilliseconds;
                        }
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
