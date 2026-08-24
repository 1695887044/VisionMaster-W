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
        /// </summary>
        public override List<CompiledNode> RunAndGetNext(IExecutionContext context)
        {
            context.CurrentNodeId = Id;

            if (context.CancellationToken.IsCancellationRequested)
                return null;

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
                    if (isTrue) return branch.ExecutionSteps;
                }
                catch (Exception ex)
                {
                    context.Logger.Error($"分支执行异常: {ex.Message}");
                }
            }
            return null;
        }
    }
}
