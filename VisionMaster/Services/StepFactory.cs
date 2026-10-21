using System.Collections.Generic;
using System.Linq;
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
    ///  · BuiltIn_Wait → WaitStep（**非容器**，必须先于下面的非容器兜底判定——
    ///    它没有内部步骤，但也不是反射加载的算子；落进 ActionStep 会在编译期报"找不到插件"）；
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
            // 等待节点：非容器，但也不是算子（必须放在 !IsContainer 兜底之前，否则会静默变成 ActionStep）
            if (tool.ModuleTypeName == "BuiltIn_Wait")
                return new WaitStep(tool.Icon, tool.Name, tool.ModuleTypeName, stepName)
                {
                    Category = tool.Category,
                };

            if (!tool.IsContainer)
                return new ActionStep(tool.Icon, tool.Name, tool.ModuleTypeName, stepName)
                {
                    Category = tool.Category,
                };

            switch (tool.ModuleTypeName)
            {
                case "BuiltIn_While":
                    return new WhileStep(tool.Icon, tool.Name, tool.ModuleTypeName, stepName)
                    {
                        Category = tool.Category,
                    };

                case "BuiltIn_For":
                    return new ForStep(tool.Icon, tool.Name, tool.ModuleTypeName, stepName)
                    {
                        Category = tool.Category,
                    };

                case "BuiltIn_Case":
                    // 分支匹配：与 If 同族但"判据 + 匹配值"语义（编译成同一个 CompiledIfNode）
                    return new CaseStep(tool.Icon, tool.Name, tool.ModuleTypeName, stepName)
                    {
                        Category = tool.Category,
                    };

                case "BuiltIn_Parallel":
                    return new ParallelStep(tool.Icon, tool.Name, tool.ModuleTypeName, stepName)
                    {
                        Category = tool.Category,
                        // 全限定名：System.Linq 也有个 ParallelExecutionMode（隐式 using），不限定则 CS0104
                        ExecutionMode = VisionMaster.Models.ParallelExecutionMode.Parallel,
                    };

                default:
                    return new ConditionStep(tool.Icon, tool.Name, tool.ModuleTypeName, stepName)
                    {
                        Category = tool.Category,
                    };
            }
        }

        /// <summary>
        /// 自动命名：{模板名}_{同型步骤计数}。计数口径与抽取前一致——数的是整棵树里
        /// PluginTypeName 相同的步骤，而不是"当前容器里的下标"。
        /// </summary>
        public static string NextStepName(FlowModel flow, ToolItemModel tool)
            => NextStepName(flow, tool.Name, tool.ModuleTypeName);

        /// <summary>
        /// 自动命名的薄适配：不经过工具箱模板，直接按显示名与类型名走同一条计数口径。
        /// 画布复制/粘贴（FlowClipboard.Take）用——粘贴件手里没有 ToolItemModel 可给
        ///（只有源步骤的 PluginName / PluginTypeName），但命名形状必须与拖入一致。
        /// </summary>
        public static string NextStepName(FlowModel flow, string displayName, string pluginTypeName)
            => $"{displayName}_{CountStepsDeep(flow.Steps, pluginTypeName)}";

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

        // ==================================================================
        //  循环控制指令（Break / Continue）的落点闸门
        //
        //  运行期对"无循环归属"的 Break/Continue 是警告 + 忽略（CompiledNode.RunSequence
        //  与 CompiledParallelNode 的兜底，B2/B4/P17 断言钉着这条语义），所以这里不做
        //  编译期硬错误，只在**新增落点**上拦住：拖入与粘贴都不许把 Break/Continue 放到
        //  没有 For/While 包着的位置，免得用户以为它生效了。
        //
        //  判据与运行期一致：由近及远找第一个 For/While/Parallel——For/While 消费它，
        //  合法；Parallel 分支不把控制流传给外层循环，与"到顶"同属无归属。
        //  If/Case 只透传，不算边界。
        // ==================================================================

        /// <summary>是不是循环控制指令（Break / Continue）。按 PluginTypeName 判——
        /// 工具箱拖入的是 ActionStep，模型类 BreakStep/ContinueStep 只在旧图纸里出现。</summary>
        public static bool IsLoopControlType(string? pluginTypeName)
            => pluginTypeName is "BuiltIn_Break" or "BuiltIn_Continue";

        /// <summary>落点被拒时的统一提示（画布状态栏 / 流程栏回执共用一份文案）</summary>
        public const string LoopControlPlacementMessage =
            "Break / Continue 只能放在 For / While 循环体内（顶层与并行分支不会执行它）";

        /// <summary>
        /// 插入点是否允许放循环控制指令。<paramref name="chainNearestFirst"/> 是插入点
        /// 所在容器链（由近及远；顶层传空集合）。非 Break/Continue 一律放行。
        /// </summary>
        public static bool AllowsLoopControl(string? pluginTypeName, IReadOnlyList<StepModel>? chainNearestFirst)
        {
            if (!IsLoopControlType(pluginTypeName))
                return true;

            if (chainNearestFirst != null)
            {
                foreach (var ancestor in chainNearestFirst)
                {
                    if (ancestor is ForStep or WhileStep)
                        return true;

                    if (ancestor is ParallelStep)
                        return false;
                }
            }

            return false;
        }

        /// <summary>
        /// 找落点所在容器链（由近及远）。<paramref name="target"/> 可以是
        ///  · 容器步骤（落点在它的第一条分支里 → 它自己也算一层）；
        ///  · 普通步骤（返回包住它的容器链）；
        ///  · StepCollection 分支（返回它的属主容器链）。
        /// 找不到 / 顶层 → 空数组（顶层放 Break/Continue 正是要被拒的情况）。
        /// </summary>
        public static IReadOnlyList<StepModel> ContainerChainOf(IEnumerable<StepModel>? roots, object? target)
        {
            if (roots == null || target == null)
                return System.Array.Empty<StepModel>();

            var chain = new List<StepModel>();   // 由外及里，最后反转成由近及远
            return Find(roots, target, chain)
                ? chain.AsEnumerable().Reverse().ToArray()
                : System.Array.Empty<StepModel>();

            static bool Find(IEnumerable<StepModel>? steps, object target, List<StepModel> chain)
            {
                if (steps == null)
                    return false;

                foreach (var step in steps)
                {
                    if (step == null)
                        continue;

                    if (ReferenceEquals(step, target))
                    {
                        // 落在容器头上 = 插进它的第一条分支，容器自己也算一层
                        if (step is IContainerStep)
                            chain.Add(step);
                        return true;
                    }

                    if (step is not IContainerStep container || container.Children == null)
                        continue;

                    foreach (var branch in container.Children)
                    {
                        if (branch == null)
                            continue;

                        if (ReferenceEquals(branch, target))
                        {
                            chain.Add(step);
                            return true;
                        }

                        chain.Add(step);
                        if (Find(branch.Steps, target, chain))
                            return true;
                        chain.RemoveAt(chain.Count - 1);
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// 在一棵待插入的子树里找第一个"无循环归属"的 Break/Continue（返回步骤名；全合法返回 null）。
        /// <paramref name="outerChainNearestFirst"/> = 插入点所在容器链（由近及远）。
        /// 禁用的步骤跳过（不参与编译）。
        /// </summary>
        public static string? FindMisplacedLoopControl(
            IEnumerable<StepModel>? steps,
            IReadOnlyList<StepModel>? outerChainNearestFirst)
        {
            if (steps == null)
                return null;

            foreach (var step in steps)
            {
                if (step == null || step.IsDisEnable)
                    continue;

                if (IsLoopControlType(step.PluginTypeName)
                    && !AllowsLoopControl(step.PluginTypeName, outerChainNearestFirst))
                    return step.StepName;

                if (step is not IContainerStep container || container.Children == null)
                    continue;

                var innerChain = new List<StepModel> { step };
                if (outerChainNearestFirst != null)
                    innerChain.AddRange(outerChainNearestFirst);

                foreach (var branch in container.Children)
                {
                    var hit = FindMisplacedLoopControl(branch?.Steps, innerChain);
                    if (hit != null)
                        return hit;
                }
            }

            return null;
        }
    }
}
