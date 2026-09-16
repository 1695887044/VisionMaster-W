using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace VisionMaster.Models
{
    /// <summary>
    /// 一个步骤在图纸结构中的位置快照。
    ///
    /// 为什么需要它：流程的执行顺序既不是 <see cref="StepModel.SortId"/>，也不是节点在画布上的坐标，
    /// 而是「步骤所属步骤集合里的下标」+「容器的嵌套层级」。
    /// SortId 全项目只有 FlowLayoutStore.AutoLayout 读取，FlowCompiler 完全不认它，
    /// 拿它判执行顺序会得到一个"看起来有序、运行时无人遵守"的假依据，所以本类刻意不暴露 SortId。
    ///
    /// 本类是纯数据快照，由 <see cref="FlowTopology.Build"/> 一次性构造，构造后不订阅任何事件；
    /// 图纸变更后必须由调用方重建，不能指望它自动跟踪集合变化。
    /// </summary>
    public sealed class StepPosition
    {
        /// <summary>
        /// 步骤 Id。构造时从 Step.StepID 快照下来：
        /// 拓扑字典以它为键，若每次读取都回到 Step 上取，一旦 Id 被改写就会出现"字典里有却查不到"的鬼影。
        /// </summary>
        public Guid StepId { get; }

        /// <summary>步骤本体（用于取名、判禁用、读连线，绝不可用于反推位置）</summary>
        public StepModel Step { get; }

        /// <summary>
        /// 所在步骤列表：顶层是 FlowModel.Steps，子层是某个 StepCollection.Steps。
        /// 判"是否同一层"必须用引用相等（ReferenceEquals）——
        /// 同名的分支集合可能有多个（例如两个 If 都有 Else 分支），只有集合实例才是唯一身份。
        /// </summary>
        public ObservableCollection<StepModel> Owner { get; }

        /// <summary>在 Owner 里的 0 基下标，即本层执行序号</summary>
        public int IndexInOwner { get; }

        /// <summary>包住本步骤的容器步骤位置；顶层步骤为 null</summary>
        public StepPosition? ParentContainer { get; }

        /// <summary>嵌套深度：顶层 0，容器内的每一层 +1</summary>
        public int Depth { get; }

        /// <summary>
        /// 所在分支集合（顶层为 null）。
        /// 报错文案要告诉操作人员"这个步骤在哪个容器的哪个分支下"，
        /// 而 ParentContainer 只能给出容器步骤本身——一个 ConditionStep 有 If/Else 多个分支，
        /// 光有容器无法定位分支，所以这里额外记下 StepCollection 引用。
        /// </summary>
        public StepCollection? Branch { get; }

        /// <summary>
        /// 由近及远的祖先容器位置数组（不含自己）。构建时算好，长度恒等于 Depth，
        /// 避免每次判定都重新沿 ParentContainer 走一遍并分配新列表。
        /// </summary>
        internal readonly StepPosition[] Ancestors;

        internal StepPosition(
            StepModel step,
            ObservableCollection<StepModel> owner,
            int indexInOwner,
            StepPosition? parentContainer,
            StepCollection? branch)
        {
            Step = step;
            StepId = step.StepID;
            Owner = owner;
            IndexInOwner = indexInOwner;
            ParentContainer = parentContainer;
            Branch = branch;
            Depth = parentContainer == null ? 0 : parentContainer.Depth + 1;
            Ancestors = BuildAncestors(parentContainer, Depth);
        }

        /// <summary>
        /// 生成由近及远的祖先数组，长度必须等于「本步骤」的 Depth（父 + 祖父 + …），
        /// 所以传入的是子辈 depth 而不是 parent.Depth：
        /// 用 parent.Depth 开数组会短一格，chain[0] 直接越界（嵌套图纸一上来就崩）。
        /// </summary>
        private static StepPosition[] BuildAncestors(StepPosition? parent, int depth)
        {
            if (parent == null)
                return Array.Empty<StepPosition>();

            // 由近及远：[父, 祖父, ...]，直接复用父辈已算好的数组，整棵图纸构建仍是 O(n)
            var chain = new StepPosition[depth];
            chain[0] = parent;
            Array.Copy(parent.Ancestors, 0, chain, 1, parent.Ancestors.Length);
            return chain;
        }
    }

    /// <summary>
    /// 一条「消费步骤取产出步骤输出」的连线在图纸结构上的合法性分类。
    ///
    /// 判定只看结构位置，不看端口是否存在、类型是否匹配——那些由 FlowCompiler 的其他检查负责。
    /// </summary>
    public enum LinkLegality
    {
        /// <summary>producer 与 consumer 在同一列表，且 producer 在前 —— 合法</summary>
        SameListBefore,

        /// <summary>producer 是 consumer 的祖先容器（例如循环体里取 For 的 Index 输出）—— 合法</summary>
        ProducerIsAncestor,

        /// <summary>producer 与 consumer 在同一列表，但 producer 在后 —— 非法：执行顺序倒序</summary>
        SameListReversed,

        /// <summary>
        /// producer 在 consumer 所在层的更外层列表里，但排在"包住 consumer 的那个容器"之后 —— 非法：producer 还没执行
        /// </summary>
        ProducerAfterEnclosingContainer,

        /// <summary>
        /// producer 与 consumer 分属同一容器的不同分支（兄弟分支），或 producer 在 consumer 的子树里 —— 非法：跨分支取数，运行时可能未执行/陈旧
        /// </summary>
        CrossBranch,

        /// <summary>任一 Id 在图纸里找不到 —— 不做判定</summary>
        Unknown,
    }

    /// <summary>
    /// 流程图纸的结构拓扑快照：画布与编译器共用的「位置 / 取数合法性」唯一真相源。
    ///
    /// 为什么要抽这一层：画布要在拉线时实时拦非法连线，编译器要在编译期挡致命图纸，
    /// 两边各写一套"谁是第几步、能不能取"的规则必然漂移（漂移的后果是画布放行、编译报错，
    /// 或者反过来），所以语义只在此处实现一次。
    ///
    /// 不可变性：Build 之后不订阅任何 CollectionChanged / PropertyChanged，
    /// 图纸一变调用方必须重新 Build。流程编译本来就是全量重编译，画布也可以在图纸版本变化时重建，
    /// 换来的是"查询期间图纸不会动"的确定语义，比增量维护一套事件同步要可靠得多。
    /// </summary>
    public sealed class FlowTopology
    {
        private readonly Dictionary<Guid, StepPosition> _positions;

        /// <summary>
        /// 图纸里全部步骤的位置，顺序为深度优先（父容器先于其分支内步骤）。
        /// 编译器做全量结构检查时遍历它即可，无需自己再写一套递归——
        /// 少写一套递归就少犯一次"漏了 For 循环体"这类错。
        /// </summary>
        public IReadOnlyList<StepPosition> Positions { get; }

        private FlowTopology(Dictionary<Guid, StepPosition> positions)
        {
            _positions = positions;
            Positions = new List<StepPosition>(positions.Values);
        }

        /// <summary>
        /// 从流程模型构建拓扑快照
        /// </summary>
        public static FlowTopology Build(FlowModel flow)
        {
            if (flow == null)
                return new FlowTopology(new Dictionary<Guid, StepPosition>());

            return Build(flow.Steps);
        }

        /// <summary>
        /// 从顶层步骤列表构建拓扑快照。
        /// FlowCompiler.Compile 只拿到 IEnumerable&lt;StepModel&gt;（没有 FlowModel），故提供此重载。
        /// </summary>
        public static FlowTopology Build(IEnumerable<StepModel> rootSteps)
        {
            var positions = new Dictionary<Guid, StepPosition>();

            // 顶层若不是 ObservableCollection（例如传了 LINQ 投影），包装一份只用于"同层引用相等"判定。
            // 同一次 Build 内顶层步骤共享同一个包装实例，因此判同层仍然成立；该包装不参与任何执行语义。
            var rootList = rootSteps as ObservableCollection<StepModel>
                ?? new ObservableCollection<StepModel>(SafeEnumerate(rootSteps));

            Walk(rootList, positions, parentContainer: null, branch: null);

            return new FlowTopology(positions);
        }

        private static IEnumerable<StepModel> SafeEnumerate(IEnumerable<StepModel> rootSteps)
        {
            return rootSteps ?? Array.Empty<StepModel>();
        }

        private static void Walk(
            ObservableCollection<StepModel> steps,
            Dictionary<Guid, StepPosition> positions,
            StepPosition? parentContainer,
            StepCollection? branch)
        {
            for (int i = 0; i < steps.Count; i++)
            {
                StepModel step = steps[i];
                if (step == null)
                    continue;

                var position = new StepPosition(step, steps, i, parentContainer, branch);

                // 索引用赋值而非 Add：复制粘贴产生的重复 Id 属于图纸畸形，
                // 此处保留最后一个位置即可（宁可少报一条结构错，也不能让 Build 抛异常打断整次编译）
                positions[position.StepId] = position;

                // ⚠⚠ 容器判定必须用 `is IContainerStep`，绝不能写成 `is ConditionStep`：
                // WhileStep : ConditionStep（看起来能被 ConditionStep 覆盖），但 ForStep 是直接继承 StepModel 的，
                // 只判 ConditionStep 会把 For 循环体整层漏掉——本项目就犯过这个错（For 循环体里的步骤
                // 既不参与拓扑也不被连线检查覆盖，表现为"循环体里的连线不报错"）。
                if (step is IContainerStep container && container.Children != null)
                {
                    foreach (var childCollection in container.Children)
                    {
                        if (childCollection?.Steps != null)
                            Walk(childCollection.Steps, positions, parentContainer: position, branch: childCollection);
                    }
                }
            }
        }

        /// <summary>
        /// 按步骤 Id 取位置。返回 false 表示该 Id 不在图纸里（步骤被删除，或连线指向野 Id）。
        /// </summary>
        public bool TryGet(Guid stepId, [NotNullWhen(true)] out StepPosition? position)
        {
            position = null;
            return stepId != Guid.Empty && _positions.TryGetValue(stepId, out position);
        }

        /// <summary>
        /// 由近及远的祖先容器位置链（不含自己）。顶层步骤返回空链。
        /// </summary>
        public IReadOnlyList<StepPosition> AncestorChain(StepPosition position)
        {
            if (position == null)
                return Array.Empty<StepPosition>();

            return position.Ancestors;
        }

        /// <summary>
        /// 判定「consumer 取 producer 输出」这条连线在结构上是否合法。
        ///
        /// 判定顺序是设计决策，逐条都有原因，改动前先读注释：
        /// 1) 查不到 → Unknown；自连 → SameListReversed（自连由上层拦截，这里不给合法）
        /// 2) producer 在 consumer 的祖先容器链上 → ProducerIsAncestor
        /// 3) producer 与 consumer 同列表 → 比下标
        /// 4) 在 consumer 的祖先链里找与 producer 同列表的容器 K → 比 producer 与 K 的下标
        /// 5) 其余 → CrossBranch
        /// </summary>
        public LinkLegality Classify(Guid producerStepId, Guid consumerStepId)
        {
            if (!TryGet(producerStepId, out var producer) || !TryGet(consumerStepId, out var consumer))
                return LinkLegality.Unknown;

            // 1) 自连：结构上"自己取自己"永远不可能有序，返回非法值让上层决定怎么报
            if (ReferenceEquals(producer, consumer))
                return LinkLegality.SameListReversed;

            // consumer 的祖先容器链（由近及远），第 2 步与第 4 步共用
            var consumerAncestors = consumer.Ancestors;

            // 2) producer 是否就是包住 consumer 的某一层容器本身（沿祖先链一路向外找）
            //    必须排在与第 4 步之前！
            //    坑在这里：producer（例如 For 步骤）与"包住 consumer 的那个容器 K"往往就在同一个 Owner 列表里，
            //    甚至 K 就是 producer 自己。若先走第 4 步，会拿 producer 的下标和它自己的下标比，
            //    `Index < Index` 恒为 false，于是"循环体里取 For 的 Index"这条完全合法的连线会被误判成非法。
            //    For.Index 是流程里最高频的一类连线，误判等于把功能打死。
            for (int i = 0; i < consumerAncestors.Length; i++)
            {
                if (ReferenceEquals(consumerAncestors[i], producer))
                    return LinkLegality.ProducerIsAncestor;
            }

            // 3) 同一列表：下标小者先执行
            if (ReferenceEquals(producer.Owner, consumer.Owner))
            {
                return producer.IndexInOwner < consumer.IndexInOwner
                    ? LinkLegality.SameListBefore
                    : LinkLegality.SameListReversed;
            }

            // 4) producer 位于包住 consumer 的某层容器的同一列表里（即"跨层但同列表"）：
            //    此时只要 producer 排在该容器之前，producer 就一定已经执行完，取数合法。
            //    合法时复用 SameListBefore 而不新增枚举值——调用方只关心合法/非法两态，
            //    多一个"外层更早"的枚举值只会让每个调用点都多写一次判断。
            for (int i = 0; i < consumerAncestors.Length; i++)
            {
                var enclosing = consumerAncestors[i];
                if (!ReferenceEquals(enclosing.Owner, producer.Owner))
                    continue;

                return producer.IndexInOwner < enclosing.IndexInOwner
                    ? LinkLegality.SameListBefore
                    : LinkLegality.ProducerAfterEnclosingContainer;
            }

            // 5) 兜底：兄弟分支互取、producer 藏在 consumer 的子树里、producer 在别人家分支深处
            //    共同点是"这一轮根本轮不到它执行"，运行时取到的是 null 或上一轮陈旧值
            return LinkLegality.CrossBranch;
        }
    }
}
