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
        /// 反向指针：本节点由哪张图纸（StepModel）编译而来。由 FlowCompiler 在创建节点时一次性挂上。
        ///
        /// 为什么要有它（B1）：运行状态要写回图纸（高亮、耗时），而旧实现是每上报一次状态就
        /// <c>session.Blueprints.FirstOrDefault(s => s.StepID == Id)</c> —— Blueprints 是
        /// ObservableCollection，FirstOrDefault 是线性扫描，于是"每个节点每轮至少扫两遍全集"，
        /// 100 步的流程一轮就是 2 万次比较，300 步 18 万次，纯浪费在找东西上。
        /// 编译期 nodeLookup 里两边引用都齐着，顺手挂一下，运行时就是零查找。
        /// </summary>
        public StepModel Blueprint { get; set; }

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
        /// 【全引擎唯一的节点序列执行器】—— 顶层序列、If 选中分支、While/For 循环体都走这里。
        ///
        /// 为什么必须统一（A1）：引擎原先并存两套遍历机制。
        ///   机制一（CompiledFlow.Run）：栈式深度优先，把节点 <c>RunAndGetNext</c> 的返回值当"下一段要跑的清单"压栈；
        ///   机制二（CompiledWhileNode / CompiledForNode）：<c>foreach</c> 直接挨个调用，<b>返回值丢弃</b>。
        /// 而 CompiledIfNode 自己不执行分支，只把选中分支的清单<b>当返回值交出去</b>。
        /// 两下叠加的结果是：If 放在流程顶层能正常进分支，放进 While/For 循环体里，
        /// 条件照算、不报错、不写日志，分支里的算子却一个都不执行 —— 典型的静默失效。
        /// 统一成一个执行器后，"节点在哪儿跑"与"节点怎么跑"再无关系，任何层级都能承接分支。
        ///
        /// <paramref name="yieldToControlFlow"/> 的取舍：
        ///   true  —— 调用方是循环体。遇到 Break/Continue 必须立刻把控制权交回循环，
        ///            由循环决定"下一圈"还是"跳出"，所以这里不就地消化，直接返回。
        ///   false —— 调用方是顶层序列。顶层没有循环可跳，Break/Continue 属于用户摆错位置，
        ///            就地清零并继续跑完剩余节点：既不中断主流程，也不让脏状态泄漏给后面某个循环
        ///            （旧实现里脏状态会让后续第一个循环一进门就自杀退出）。
        /// </summary>
        /// <param name="nodes">要执行的节点序列</param>
        /// <param name="context">执行上下文</param>
        /// <param name="yieldToControlFlow">true=循环体（Break/Continue 交回上层循环处理）；false=顶层（就地消化）</param>
        public static void RunSequence(
            List<CompiledNode> nodes,
            IExecutionContext context,
            bool yieldToControlFlow)
        {
            if (nodes == null || nodes.Count == 0)
                return;

            var stack = new Stack<IEnumerator<CompiledNode>>();
            stack.Push(nodes.GetEnumerator());

            while (stack.Count > 0)
            {
                if (context.CancellationToken.IsCancellationRequested)
                    return;

                var currentEnumerator = stack.Peek();
                if (!currentEnumerator.MoveNext())
                {
                    stack.Pop();
                    continue;
                }

                var currentNode = currentEnumerator.Current;
                context.CurrentNodeId = currentNode.Id;

                if (context.CancellationToken.IsCancellationRequested)
                    return;

                // 返回值 = 本节点选中的子清单（If 分支 / 循环体由节点自己交出）
                var nextBranchToRun = currentNode.RunAndGetNext(context);
                if (nextBranchToRun != null && nextBranchToRun.Count > 0)
                    stack.Push(nextBranchToRun.GetEnumerator());

                if (context.CurrentFlowState == FlowControlState.Return)
                    return; // Return 终止整个流程，与层级无关

                if (context.CurrentFlowState != FlowControlState.Normal)
                {
                    if (yieldToControlFlow)
                        return; // 交回循环处理：Break 跳出 / Continue 下一圈

                    // 顶层的孤儿 Break/Continue：清零继续，别污染后面的循环
                    context.Logger?.Warn(
                        $"流程顶层出现无循环归属的 {context.CurrentFlowState} 指令，已忽略（请把 Break/Continue 放进循环体内）");
                    context.CurrentFlowState = FlowControlState.Normal;
                }
            }
        }

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
                // B1：编译期已挂好反向指针，这里直接用引用，不再线性扫描 Blueprints
                var step = Blueprint;

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
