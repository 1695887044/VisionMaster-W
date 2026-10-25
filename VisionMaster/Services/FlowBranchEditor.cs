using System.Linq;
using VisionMaster.Models;

namespace VisionMaster.Services
{
    /// <summary>
    /// 分支结构动作的共享实现：加并行分支 / 加 ElseIf / 加 Else / 加 Case。
    ///
    /// 为什么抽出来：这两个动作此前只挂在流程栏右键（ProcessViewModel），画布节点右键
    /// 完全没有入口——用户只能在流程栏点。补画布入口时若各写一份判型、上限、自动命名、
    /// 插入位置，两处必然漂移（改了一处忘另一处，且症状是"某个入口静默少一条分支"）。
    /// 本类只做纯结构改动与文案，**不碰 Notifier / 弹窗**：反馈出口由调用方给
    ///（流程栏走 ShowFeedback 弹泡、画布走状态栏回执），运行锁也由调用方先判。
    ///
    /// 版本号口径（两处一致，别在这里加 Version++）：
    ///  · Children 增删由 FlowModel 的集合监听自动递增 Version；
    ///  · 只有"分支改名"（StepCollection.StepName）不在监听面里，必须调用方手动推。
    /// </summary>
    public static class FlowBranchEditor
    {
        /// <summary>结构动作的结果：是否改了结构 + 给用户的回执文案</summary>
        public readonly record struct Result(bool Success, string Message)
        {
            public static Result Ok(string message) => new(true, message);

            public static Result Rejected(string message) => new(false, message);
        }

        /// <summary>
        /// 给并行分组追加一条并行分支。
        /// 名字在"分支 N"里挑第一个未占用的 N：用户可能已经改过名（"左工位"），
        /// 按"当前条数 + 1"命名会撞出两条"分支 3"。
        /// </summary>
        public static Result AddParallelBranch(ParallelStep? group)
        {
            if (group == null)
                return Result.Rejected(string.Empty);   // 类型不对：静默空跑（菜单常驻，不弹泡）

            if (group.Children.Count >= ParallelStep.RecommendedMaxBranches)
            {
                return Result.Rejected(
                    $"并行分支已达上限 {ParallelStep.RecommendedMaxBranches} 条（并列泳道再宽一屏就放不下）");
            }

            string name = NextBranchName(group);
            group.Children.Add(new StepCollection { BranchType = BranchType.Default, StepName = name });

            return Result.Ok($"已添加「{name}」到「{group.StepName}」（可在分支上右键改名）");
        }

        /// <summary>
        /// 给 If 容器追加一条 ElseIf 分支：插在兜底 Else 之前（Else 之后的分支编译器按
        /// "不可达分支"报错，插在前面才是有意义的顺序）。
        /// </summary>
        public static Result AddElseIfBranch(ConditionStep? ifNode)
        {
            if (ifNode == null)
                return Result.Rejected(string.Empty);

            int insertIndex = ifNode.Children.Count;
            var lastBranch = ifNode.Children.LastOrDefault();

            if (lastBranch != null && lastBranch.BranchType == BranchType.Else)
                insertIndex = ifNode.Children.Count - 1;

            ifNode.Children.Insert(
                insertIndex,
                new StepCollection
                {
                    BranchType = BranchType.ElseIf,
                    StepName = "ElseIf 分支",
                });

            return Result.Ok($"已为「{ifNode.StepName}」添加 ElseIf 分支");
        }

        /// <summary>给 If 容器补一条兜底 Else 分支（已存在则拒绝，不重复加）</summary>
        public static Result AddElseBranch(ConditionStep? ifNode)
        {
            if (ifNode == null)
                return Result.Rejected(string.Empty);

            if (ifNode.Children.Any(c => c.BranchType == BranchType.Else))
                return Result.Rejected("该算子已经包含了 Else 分支！");

            ifNode.Children.Add(
                new StepCollection
                {
                    BranchType = BranchType.Else,
                    StepName = "Else 分支",
                });

            return Result.Ok($"已为「{ifNode.StepName}」添加 Else 分支");
        }

        /// <summary>
        /// 给分支匹配容器追加一条 Case 分支：同样插在兜底分支（Else）之前，
        /// 自动名取"Case N"里第一个未占用号（用户改过名的空位可复用）。
        /// </summary>
        public static Result AddCaseBranch(CaseStep? caseNode)
        {
            if (caseNode == null)
                return Result.Rejected(string.Empty);

            int insertIndex = caseNode.Children.Count;
            var lastBranch = caseNode.Children.LastOrDefault();

            if (lastBranch != null && lastBranch.BranchType == BranchType.Else)
                insertIndex = caseNode.Children.Count - 1;

            string name = caseNode.NextCaseBranchName();
            caseNode.Children.Insert(
                insertIndex,
                new StepCollection
                {
                    BranchType = BranchType.Case,
                    StepName = name,
                });

            return Result.Ok($"已为「{caseNode.StepName}」添加「{name}」分支");
        }

        /// <summary>在"分支 N"里挑第一个未被占用的 N（分支数有上限、名字空间无限，循环必然找到空位）</summary>
        private static string NextBranchName(ParallelStep group)
        {
            for (int n = 1; ; n++)
            {
                string candidate = $"分支 {n}";
                if (!group.Children.Any(c => c.StepName == candidate))
                    return candidate;
            }
        }
    }
}
