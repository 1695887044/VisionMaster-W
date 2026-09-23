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

            foreach (var branch in Branches)
            {
                if (branch.ConditionLambda == null) continue;

                // args 数组：先局部变量（LocalVarIds），再运行时变量（RuntimeVarNames）
                int totalArgs = branch.LocalVarIds.Count + branch.RuntimeVarNames.Count;
                var args = new object[totalArgs];

                // 局部变量：从 UpstreamLinks 取值
                for (int i = 0; i < branch.LocalVarIds.Count; i++)
                {
                    Guid varId = branch.LocalVarIds[i];
                    Type expectedType = branch.VarTypes.ContainsKey(varId) ? branch.VarTypes[varId] : typeof(double);

                    if (UpstreamLinks.TryGetValue(varId, out var sourcePort) && sourcePort?.Value != null)
                    {
                        args[i] = sourcePort.Value;
                    }
                    else
                    {
                        if (expectedType == typeof(string)) args[i] = string.Empty;
                        else if (expectedType == typeof(bool)) args[i] = false;
                        else args[i] = 0.0;
                    }
                }

                // 运行时变量：从 context.LocalVariables 取值
                for (int i = 0; i < branch.RuntimeVarNames.Count; i++)
                {
                    string varName = branch.RuntimeVarNames[i];
                    int argIndex = branch.LocalVarIds.Count + i;
                    Type expectedType = branch.RuntimeVarTypes.ContainsKey(varName) ? branch.RuntimeVarTypes[varName] : typeof(object);

                    if (context.LocalVariables.TryGetValue(varName, out var v))
                    {
                        args[argIndex] = v;
                    }
                    else
                    {
                        // 变量未定义时返回类型默认值
                        args[argIndex] = expectedType.IsValueType ? Activator.CreateInstance(expectedType) : null;
                    }
                }

                try
                {
                    bool isTrue = (bool)branch.ConditionLambda.Invoke(args);
                    if (isTrue)
                    {
                        // 本节点的活儿到"选出分支"为止，分支里各算子的状态由算子自己上报
                        UpdateStepRuntimeState(context, StepRuntimeState.Success);
                        return branch.ExecutionSteps;
                    }
                }
                catch (Exception ex)
                {
                    context.Logger.Error($"分支执行异常: {ex.Message}");
                }
            }

            // 一条分支都没命中（没有 Else 的 If）：判断本身是成功的，只是无事可做
            UpdateStepRuntimeState(context, StepRuntimeState.Success);
            return null;
        }
    }
}
