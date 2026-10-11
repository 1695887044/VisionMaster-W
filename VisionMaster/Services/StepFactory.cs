using System.Collections.Generic;
using VisionMaster.Models;

namespace VisionMaster.Services
{
    /// <summary>
    /// 工具箱模板 → 步骤模型 的共享工厂。
    ///
    /// 为什么要抽出来：流程栏 Drop（ProcessViewModel）与画布 Drop（FlowCanvasViewModel）
    /// 都要"按 ToolItemModel 造一个步骤"，两处各抄一份必然漂移（改了一处忘另一处）。
    /// 本文件只做两件纯计算：类型分派与自动命名；落点命中、运行锁、版本推进留在各自调用方
    ///（流程栏落点看 TreeView 命中项，画布落点看画布坐标）。
    ///
    /// 类型分派口径（与抽取前的 ProcessViewModel.Drop 逐字一致）：
    ///  · 容器 + BuiltIn_While → WhileStep；
    ///  · 容器 + BuiltIn_For → ForStep；
    ///  · 容器 + BuiltIn_Parallel → ParallelStep，且显式 ExecutionMode=Parallel（只在此处置——
    ///    ParallelStep 构造器保持 Sequential，存量 .vms 反序列化默认兼容）；
    ///  · 容器 + BuiltIn_Case → CaseStep（分支匹配：判据 + 匹配值）；
    ///  · 其余容器（BuiltIn_If 与未知名）→ ConditionStep 兜底；
    ///  · 非容器 → ActionStep。
    /// </summary>
    public static class StepFactory
    {
        /// <summary>
        /// 按工具箱模板造一个步骤。stepName 由调用方给（「模板名_同型计数」见 <see cref="NextStepName"/>）。
        /// </summary>
        public static StepModel CreateFromTool(ToolItemModel tool, string stepName)
        {
            if (!tool.IsContainer)
                return new ActionStep(tool.Icon, tool.Name, tool.ModuleTypeName, stepName);

            switch (tool.ModuleTypeName)
            {
                case "BuiltIn_While":
                    return new WhileStep(tool.Icon, tool.Name, tool.ModuleTypeName, stepName);

                case "BuiltIn_For":
                    return new ForStep(tool.Icon, tool.Name, tool.ModuleTypeName, stepName);

                case "BuiltIn_Case":
                    // 分支匹配：与 If 同族但"判据 + 匹配值"语义（编译成同一个 CompiledIfNode）
                    return new CaseStep(tool.Icon, tool.Name, tool.ModuleTypeName, stepName);

                case "BuiltIn_Parallel":
                    return new ParallelStep(tool.Icon, tool.Name, tool.ModuleTypeName, stepName)
                    {
                        // 全限定名：System.Linq 也有个 ParallelExecutionMode（隐式 using），不限定则 CS0104
                        ExecutionMode = VisionMaster.Models.ParallelExecutionMode.Parallel,
                    };

                default:
                    return new ConditionStep(tool.Icon, tool.Name, tool.ModuleTypeName, stepName);
            }
        }

        /// <summary>
        /// 自动命名：{模板名}_{同型步骤计数}。计数口径与抽取前一致——数的是整棵树里
        /// PluginTypeName 相同的步骤，而不是"当前容器里的下标"。
        /// </summary>
        public static string NextStepName(FlowModel flow, ToolItemModel tool)
            => $"{tool.Name}_{CountStepsDeep(flow.Steps, tool.ModuleTypeName)}";

        /// <summary>
        /// 递归计数：本层命中 PluginTypeName 的步骤 + 每条分支子层继续数。
        /// 判据统一走 IContainerStep（If/While=ConditionStep、For、并行分组都实现它）——
        /// 2026-10-09 前只认 ConditionStep，For 与并行分支里的算子数不到，自动命名会重号。
        /// </summary>
        public static int CountStepsDeep(IEnumerable<StepModel> steps, string pluginTypeName)
        {
            int count = 0;
            if (steps == null)
                return count;

            foreach (var step in steps)
            {
                // 1. 如果名字匹配，计数 +1
                if (step.PluginTypeName == pluginTypeName)
                {
                    count++;
                }

                // 2. 如果遇到容器节点，钻进它的每一个分支里继续找。
                if (step is IContainerStep container)
                {
                    foreach (var branch in container.Children)
                    {
                        count += CountStepsDeep(branch.Steps, pluginTypeName);
                    }
                }
            }

            return count;
        }
    }
}
