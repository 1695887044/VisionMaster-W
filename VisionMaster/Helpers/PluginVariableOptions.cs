using System;
using System.Collections.Generic;
using System.Linq;
using Core.Interfaces;
using VisionMaster.Models;
using VisionMaster.Services;

namespace VisionMaster.Helpers
{
    /// <summary>
    /// 「插件配置界面里能选哪些变量名」的快照来源（宿主侧）。
    ///
    /// 为什么要有这一层（而不是让插件自己找）
    /// ---------
    /// 插件 DLL 只引用 Core.Interfaces，够不到"变量管理"所在的程序集，也读不到流程图纸 ——
    /// 而"名字"这一行的两个正确来源恰恰都在宿主侧：
    ///   · 作用域=全局变量 → 变量管理里的变量名（<see cref="IWorkspaceManager.GlobalVariables"/>）；
    ///   · 作用域=运行时变量 → 上游「变量定义」节点声明的名字（流程图纸上的信息）。
    /// 没有这份快照，用户只能手打名字 —— 打错名字的两种后果都很难倒查：
    /// 运行期报"找不到全局变量"，或者（名字恰好命中另一个变量时）**值写进了别的变量**。
    ///
    /// 口径与变量绑定弹窗保持一致
    /// ---------
    /// 运行时变量名一律走 <see cref="FlowQueryHelper.TryGetDefinedVariable"/>（同一个"什么算变量定义、
    /// 名字与类型怎么取"的判据），不在这里另写一套 —— 两处各写一遍迟早漂移，
    /// 漂移的表现是"弹窗里选得到、这里选不到"这种没人想查的差别。
    /// </summary>
    public static class PluginVariableOptions
    {
        /// <summary>作用域取值：变量管理里的全局变量（与「变量赋值」插件 Scope 端口的字面量一致）</summary>
        public const string ScopeGlobal = "Global";

        /// <summary>作用域取值：流程内运行时变量</summary>
        public const string ScopeRuntime = "Runtime";

        /// <summary>
        /// 组装候选快照：全局变量 + 目标步骤上游声明的运行时变量。
        ///
        /// 找不到当前流程 / 目标步骤时只给全局那一半（运行时候选依赖图纸，取不到就当没有）。
        /// </summary>
        /// <param name="workspace">工作区（提供变量集合与当前流程）</param>
        /// <param name="targetStepId">正在配置的步骤 Id：运行时候选只取它**上游**声明过的名字</param>
        public static List<VariableOption> Build(IWorkspaceManager workspace, Guid targetStepId)
        {
            var result = new List<VariableOption>();
            if (workspace == null)
                return result;

            // 去重键是「名字 + 作用域」而不是只按名字：全局与运行时允许同名，那是两个不同的东西
            //（作用域本身就是为了把同名歧义显式化才加的）。只按名字去重会让"运行时那个同名变量"
            // 在候选里凭空消失，而界面上看起来"名字明明建好了"。
            var seen = new HashSet<string>(StringComparer.Ordinal);

            // ① 变量管理里的那批（含网络变量）：作用域=全局时唯一合法的名字来源
            foreach (var variable in workspace.GlobalVariables ?? Enumerable.Empty<IVariable>())
            {
                if (variable == null || string.IsNullOrWhiteSpace(variable.Name))
                    continue;
                if (!seen.Add(Key(variable.Name, ScopeGlobal)))
                    continue;   // 同名重复：以先出现者为准，与变量索引 Rebuild 的口径一致

                result.Add(new VariableOption
                {
                    Name = variable.Name,
                    Scope = ScopeGlobal,
                    TypeName = ShortTypeName(variable.DataType),
                    Description = string.IsNullOrWhiteSpace(variable.Description)
                        ? "全局变量"
                        : variable.Description,
                });
            }

            // ② 上游「变量定义」声明的运行时变量名
            var flow = workspace.CurrentFlow;
            var target = FindStepById(flow?.Steps, targetStepId);
            if (flow != null && target != null)
            {
                foreach (var step in FlowQueryHelper.GetUpstreamNodes(flow.Steps, target))
                {
                    if (step == null || !FlowQueryHelper.TryGetDefinedVariable(step, out var name, out var type))
                        continue;
                    if (string.IsNullOrWhiteSpace(name) || !seen.Add(Key(name, ScopeRuntime)))
                        continue;

                    result.Add(new VariableOption
                    {
                        Name = name,
                        Scope = ScopeRuntime,
                        TypeName = ShortTypeName(type),
                        Description = $"由上游「{step.StepName}」声明",
                    });
                }
            }

            return result;
        }

        private static string Key(string name, string scope) => scope + "\n" + name;

        /// <summary>按 Id 在图纸里找步骤（含嵌套容器的分支；找不到返回 null）</summary>
        private static StepModel? FindStepById(IEnumerable<StepModel>? steps, Guid stepId)
        {
            if (steps == null || stepId == Guid.Empty)
                return null;

            foreach (var step in steps)
            {
                if (step == null)
                    continue;

                if (step.StepID == stepId)
                    return step;

                // 容器（If/While/For/并行分组）的内部步骤同样要能命中：
                // 下钻进去配参数的步骤就是这些，漏了它们 → 运行时候选整批为空
                if (step is IContainerStep container && container.Children != null)
                {
                    foreach (var branch in container.Children)
                    {
                        var hit = FindStepById(branch?.Steps, stepId);
                        if (hit != null)
                            return hit;
                    }
                }
            }

            return null;
        }

        /// <summary>声明类型的短名（Nullable 先剥掉，否则界面会显示 Nullable`1 这种东西）</summary>
        private static string ShortTypeName(Type? type)
        {
            var resolved = type ?? typeof(object);
            return (Nullable.GetUnderlyingType(resolved) ?? resolved).Name;
        }
    }
}
