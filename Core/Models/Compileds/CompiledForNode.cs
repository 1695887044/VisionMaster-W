using Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;

namespace VisionMaster.Models
{
    /// <summary>
    /// 编译后的 For 循环节点
    /// 支持指定次数的循环执行，并向外暴露循环索引
    /// </summary>
    public class CompiledForNode : CompiledNode
    {
        /// <summary>
        /// 索引输出端口（供下游算子绑定）
        /// </summary>
        public OutputPort<int> IndexPort { get; } = new OutputPort<int>("Index");

        /// <summary>
        /// 循环次数输入连线
        /// </summary>
        public IOutputPort LoopCountLink { get; set; }

        /// <summary>
        /// 默认循环次数（未连接时使用）
        /// </summary>
        public int DefaultLoopCount { get; set; }

        /// <summary>
        /// 循环体步骤列表
        /// </summary>
        public List<CompiledNode> LoopBody { get; set; } = new();

        /// <summary>
        /// 执行 For 循环
        /// 根据指定次数执行循环体，支持 Break/Continue/Return 控制流
        ///
        /// A2：容器节点也要上报运行状态，否则画布上循环永不高亮、永无耗时，
        /// 现场排查时看不出"循环进没进、跑了几圈、卡在哪一圈"
        /// </summary>
        public override List<CompiledNode> RunAndGetNext(IExecutionContext context)
        {
            context.CurrentNodeId = Id;

            UpdateStepRuntimeState(context, StepRuntimeState.Running);
            try
            {
                RunLoop(context);
                UpdateStepRuntimeState(
                    context,
                    context.CancellationToken.IsCancellationRequested
                        ? StepRuntimeState.Skipped
                        : StepRuntimeState.Success);
            }
            catch
            {
                UpdateStepRuntimeState(context, StepRuntimeState.Failed);
                throw;
            }

            return null;
        }

        /// <summary>
        /// 循环主体：正常跑完次数、或遇到 Break/Return/取消时提前结束
        /// </summary>
        private void RunLoop(IExecutionContext context)
        {
            // 断桥修复：允许 LoopCount 端口绑定运行时变量（如"检测数量"由上游算子定义），
            // 代理端口先注入 context，下面读 LoopCountLink.Value 即为最新变量值
            BindContextAwarePorts(context);

            int targetCount = DefaultLoopCount;
            if (LoopCountLink != null && LoopCountLink.Value != null)
            {
                // A6：上游值脏（字符串/NaN/越界）时不再抛穿整个流程——回落默认次数并留痕
                try { targetCount = Convert.ToInt32(LoopCountLink.Value); }
                catch (Exception ex)
                {
                    context.Logger.Warn($"For节点 '{Name}' 的 LoopCount 输入值 [{LoopCountLink.Value}] 无法转为整数，回落默认次数 {DefaultLoopCount}：{ex.Message}");
                }
            }
            if (targetCount < 0)
            {
                context.Logger.Warn($"For节点 '{Name}' 循环次数为负 ({targetCount})，按 0 次处理");
                targetCount = 0;
            }

            for (int i = 0; i < targetCount; i++)
            {
                if (context.CancellationToken.IsCancellationRequested) break;

                IndexPort.Value = i;

                // A1：循环体交给全引擎统一的序列执行器。
                // 旧实现是 foreach 挨个 step.RunAndGetNext(context) 并丢弃返回值，
                // 而 CompiledIfNode 靠返回值交出选中分支 —— 于是循环体里的 If 一步都不执行（静默失效）。
                // 循环体用 yieldToControlFlow: true，Break/Continue 会被立刻交回来由本循环裁决。
                CompiledNode.RunSequence(LoopBody, context, yieldToControlFlow: true);

                if (context.CancellationToken.IsCancellationRequested) return;

                if (context.CurrentFlowState == FlowControlState.Continue)
                {
                    context.CurrentFlowState = FlowControlState.Normal;
                    continue; // 进入下一圈
                }
                if (context.CurrentFlowState == FlowControlState.Break)
                {
                    context.CurrentFlowState = FlowControlState.Normal;
                    return; // 跳出整个循环
                }
                if (context.CurrentFlowState == FlowControlState.Return)
                {
                    return; // 终止流程，状态保留给上层执行器识别
                }
            }
        }
    }
}
