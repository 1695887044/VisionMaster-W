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

        public Dictionary<string, IOutputPort> UpstreamLinks { get; set; } = new();

        public override List<CompiledNode> RunAndGetNext(IExecutionContext context)
        {
            foreach (var branch in Branches)
            {
                if (branch.ConditionLambda == null) continue;

                var args = new object[branch.LocalVarNames.Count];
                for (int i = 0; i < branch.LocalVarNames.Count; i++)
                {
                    string varName = branch.LocalVarNames[i];

                    Type expectedType = branch.VarTypes.ContainsKey(varName) ? branch.VarTypes[varName] : typeof(double);

                    // 🌟 直接提取原始 Value，不再做 ToDouble 强转！
                    if (UpstreamLinks.TryGetValue(varName, out var sourcePort) && sourcePort?.Value != null)
                    {
                        args[i] = sourcePort.Value;
                    }
                    else
                    {
                        if (expectedType == typeof(string))
                            args[i] = string.Empty;
                        else if (expectedType == typeof(bool))
                            args[i] = false;
                        else
                            args[i] = 0.0;
                    }
                }
                try
                {
                    bool isTrue = (bool)branch.ConditionLambda.Invoke(args);
                    if (isTrue) return branch.ExecutionSteps;
                }
                catch (Exception ex)
                {
                    // 记录运行期崩溃日志
                    Console.WriteLine($"分支执行异常: {ex.Message}");
                }
            }
            return null;
        }
    }
}
