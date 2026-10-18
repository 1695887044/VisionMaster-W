using Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;

namespace VisionMaster.Models
{
    /// <summary>
    /// 编译后的 If 节点
    /// 支持 If-ElseIf-Else 分支结构
    /// </summary>
    public class CompiledIfNode : CompiledNode
    {
        /// <summary>
        /// 分支列表
        /// </summary>
        public List<CompiledBranch> Branches { get; set; } = new();

        /// <summary>
        /// 上游连线映射（变量ID -> 输出端口）
        /// </summary>
        public Dictionary<Guid, IOutputPort> UpstreamLinks { get; set; } = new();

        /// <summary>
        /// 执行 If 节点
        /// 按顺序计算条件，执行第一个条件为真的分支
        ///
        /// 注意本节点只负责"选出分支"，把分支清单当返回值交出去，
        /// 真正执行分支的是上层序列执行器 CompiledNode.RunSequence（A1 统一后任何层级都能承接）
        /// </summary>
        public override List<CompiledNode> RunAndGetNext(IExecutionContext context)
        {
            context.CurrentNodeId = Id;

            // A2：分支容器也要上报状态与耗时，否则画布上看不出 If 走没走、判断本身花了几毫秒
            UpdateStepRuntimeState(context, StepRuntimeState.Running);

            // 断桥修复：引脚绑定"运行时变量"的代理端口需先注入 context，
            // UpstreamLinks[varId].Value 才能从 context.LocalVariables 现取最新值
            BindContextAwarePorts(context);

            if (context.CancellationToken.IsCancellationRequested)
            {
                UpdateStepRuntimeState(context, StepRuntimeState.Skipped);
                return null;
            }

            for (int branchIndex = 0; branchIndex < Branches.Count; branchIndex++)
            {
                var branch = Branches[branchIndex];
                if (branch.ConditionLambda == null) continue;

                bool isTrue;
                try
                {
                    // 实参构建统一走基类 BuildConditionArgs（与 While / 等待共用同一份类型归一）
                    isTrue = (bool)branch.ConditionLambda.Invoke(
                        BuildConditionArgs(branch, UpstreamLinks, context));
                }
                catch (Exception ex)
                {
                    // P0-1：条件求值抛异常 = 判断结果未知，绝不能再往下试分支。
                    // 旧实现只记一条日志就 continue，于是 Else 的 Parse("true") 恒真兜底命中 ——
                    // 本该走 If 实际走了 Else，步骤状态还是绿的。对工业视觉而言，
                    // "绿的但走了错分支"比"红的崩溃"危险得多：崩溃会停机报警，走错分支会一路放行不良品。
                    // 口径与 CompiledPluginNode 的程序异常一致：标 Failed + 上抛，
                    // 由 FlowEngineService 把会话置 Faulted 并上报（不静默、不继续）。
                    UpdateStepRuntimeState(context, StepRuntimeState.Failed);
                    context.Logger?.Error(
                        $"If节点 '{Name}' 第 {branchIndex + 1} 个分支条件求值失败，已中断流程: {ex.Message}"
                    );
                    throw;
                }

                if (isTrue)
                {
                    // 本节点的活儿到"选出分支"为止，分支里各算子的状态由算子自己上报
                    UpdateStepRuntimeState(context, StepRuntimeState.Success);
                    return branch.ExecutionSteps;
                }
            }

            // 一条分支都没命中（没有 Else 的 If）：判断本身是成功的，只是无事可做
            UpdateStepRuntimeState(context, StepRuntimeState.Success);
            return null;
        }
    }
}
