using Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.Models
{
    public class CompiledIfNode : CompiledNode
    {
        public List<CompiledBranch> Branches { get; set; } = new();

        public Dictionary<Guid, IOutputPort> UpstreamLinks { get; set; } = new();

        public override List<CompiledNode> RunAndGetNext(IExecutionContext context)
        {
            foreach (var branch in Branches)
            {
                if (branch.ConditionLambda == null) continue;

                var args = new object[branch.LocalVarIds.Count];
                for (int i = 0; i < branch.LocalVarIds.Count; i++)
                {
                    Guid varId = branch.LocalVarIds[i]; // 🌟 用 ID 取值
                    Type expectedType = branch.VarTypes.ContainsKey(varId) ? branch.VarTypes[varId] : typeof(double);

                    // 极速匹配上游数据
                    if (UpstreamLinks.TryGetValue(varId, out var sourcePort) && sourcePort?.Value != null)
                    {
                        args[i] = sourcePort.Value;
                    }
                    else
                    {
                        // 断线兜底机制
                        if (expectedType == typeof(string)) args[i] = string.Empty;
                        else if (expectedType == typeof(bool)) args[i] = false;
                        else args[i] = 0.0;
                    }
                }

                try
                {
                    bool isTrue = (bool)branch.ConditionLambda.Invoke(args);
                    if (isTrue) return branch.ExecutionSteps;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"分支执行异常: {ex.Message}");
                }
            }
            return null;
        }
    }
}
