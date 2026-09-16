using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using Core.Interfaces;
using VisionMaster.Models;
using VisionMaster.Services;
using VisionMaster.ViewModels;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 断言用的最小插件提供者。
    ///
    /// 画布的端口表有两个来源：插件静态定义（输出端口）与图纸快照（动态输出端口），
    /// 因此桩插件的输出口会直接决定断言的期望值，三条约束必须守住：
    ///   1. 叶子算子给一个 Out:System.Double —— 连线断言用它当生产端；
    ///   2. If 算子必须给 Cond —— [C] 断言「层根锚点带容器自身输出口」时数到它；
    ///   3. For 算子的 OutputDefinitions 必须是 null —— 否则 [C] 里
    ///      「层根 Outputs[0] == Index」会被静态端口抢在前面（画布是静态口优先、再去重）。
    ///
    /// 注意类型名里的 If/For 大小写：ConditionStep 的构造函数用的是
    /// <c>pluginTypeName.Contains("If")</c>（序数比较、区分大小写），写成 "IF" 会得到
    /// 「只有一条兜底分支」的容器，整个分支建模就变了。
    /// </summary>
    internal sealed class StubPluginProvider : IPluginProvider
    {
        /// <summary>叶子算子类型名（一个输入端口由 InputValues 决定，一个静态输出口 Out）</summary>
        public const string LeafPlugin = "VM.CanvasStub.Leaf";

        /// <summary>If 容器类型名（必须含 "If"，且提供 Cond 输出口）</summary>
        public const string IfPlugin = "VM.CanvasStub.If";

        /// <summary>For 容器类型名（不能有静态输出口，Index 由画布对 ForStep 特判注入）</summary>
        public const string ForPlugin = "VM.CanvasStub.For";

        private readonly Dictionary<string, ToolItemModel> _modules = new()
        {
            [LeafPlugin] = new ToolItemModel
            {
                Id = Guid.NewGuid(),
                Name = "桩算子",
                Icon = "\uE700",
                Category = "断言",
                Description = "画布断言用桩算子",
                ModuleTypeName = LeafPlugin,
                InputDefinitions = new List<PortDefinition>
                {
                    new() { Name = "In", Description = "入参", DataTypeName = "System.Double" },
                },
                OutputDefinitions = new List<PortDefinition>
                {
                    new() { Name = "Out", Description = "出参", DataTypeName = "System.Double" },
                },
            },
            [IfPlugin] = new ToolItemModel
            {
                Id = Guid.NewGuid(),
                Name = "桩If",
                Icon = "\uE700",
                Category = "逻辑",
                Description = "画布断言用条件容器",
                IsContainer = true,
                ModuleTypeName = IfPlugin,
                InputDefinitions = null,
                OutputDefinitions = new List<PortDefinition>
                {
                    new() { Name = "Cond", Description = "条件值", DataTypeName = "System.Boolean" },
                },
            },
            [ForPlugin] = new ToolItemModel
            {
                Id = Guid.NewGuid(),
                Name = "桩For",
                Icon = "\uE700",
                Category = "逻辑",
                Description = "画布断言用循环容器",
                IsContainer = true,
                ModuleTypeName = ForPlugin,
                InputDefinitions = null,
                // 刻意留 null：见类型注释第 3 条
                OutputDefinitions = null,
            },
        };

        public IReadOnlyDictionary<string, ToolItemModel> ModulePlugins => _modules;
        public IReadOnlyDictionary<string, ToolItemModel> CameraPlugins { get; } =
            new Dictionary<string, ToolItemModel>();
        public IReadOnlyDictionary<string, ToolItemModel> LaserPlugins { get; } =
            new Dictionary<string, ToolItemModel>();
        public IReadOnlyDictionary<string, ToolItemModel> MotionPlugins { get; } =
            new Dictionary<string, ToolItemModel>();

        public void RegisterModule(ToolItemModel plugin) => _modules[plugin.ModuleTypeName] = plugin;
        public void RegisterCamera(ToolItemModel plugin) { }
        public void RegisterLaser(ToolItemModel plugin) { }
        public void RegisterMotion(ToolItemModel plugin) { }

        public ToolItemModel GetModule(string name) => _modules.TryGetValue(name, out var m) ? m : null!;
        public ToolItemModel GetCamera(string name) => null!;
        public ToolItemModel GetLaser(string name) => null!;
        public ToolItemModel GetMotion(string name) => null!;
    }

    /// <summary>
    /// 画布断言夹具：一个自建的「方案 → 流程 → 步骤」栈 + 一层查询封装。
    ///
    /// 只走 public 面：WorkspaceContext 的构造函数不碰 Application.Current / Dispatcher / DI，
    /// 因此控制台进程里可以直接 new；同理，连线/导航/端口断言全部通过 FlowCanvasViewModel 的
    /// public 命令与集合间接驱动 internal/private 实现，不为测试放宽任何可见性。
    /// </summary>
    internal sealed class Harness
    {
        private FlowCanvasViewModel? _canvas;

        public WorkspaceContext Workspace { get; } = new();

        public SolutionModel Solution { get; } = new();

        public FlowModel Flow { get; }

        public Harness()
        {
            Flow = new FlowModel { FlowName = "画布断言流程" };
            Solution.Flows.Add(Flow);
            Workspace.SwitchSolution(Solution);
            Workspace.SwitchFlow(Flow);
        }

        /// <summary>
        /// 画布懒建。必须「先建画布再改图纸」之外的顺序无关：
        /// 所有查询都走这个属性，避免某些断言在画布尚未附加流程时读到空 Nodes。
        /// </summary>
        public FlowCanvasViewModel Canvas => _canvas ??= new FlowCanvasViewModel(Workspace, new StubPluginProvider());

        // ------------------------------------------------------------------
        //  建模
        // ------------------------------------------------------------------

        public ActionStep Leaf(string name) => new("\uE700", "桩算子", StubPluginProvider.LeafPlugin, name);

        public ConditionStep If(string name) => new("\uE700", "桩If", StubPluginProvider.IfPlugin, name);

        public ForStep For(string name) => new("\uE700", "桩For", StubPluginProvider.ForPlugin, name);

        /// <summary>往图纸主列追加一个步骤</summary>
        public T Add<T>(T step) where T : StepModel
        {
            Flow.Steps.Add(step);
            return step;
        }

        /// <summary>给叶子步骤登记一个输入参数（画布的输入端口来自 InputValues 的键）</summary>
        public static T WithInput<T>(T step, string port) where T : StepModel
        {
            step.SetInputValue(port, 0d);
            return step;
        }

        // ------------------------------------------------------------------
        //  画布节点查询
        // ------------------------------------------------------------------

        /// <summary>本层全部节点（含层根、泳道两类装饰节点），顺序 = 层根 → 泳道 → 主列 → 各泳道内步骤</summary>
        public IReadOnlyList<CanvasNodeViewModel> AllNodes => Canvas.Nodes;

        /// <summary>本层所有 Kind==Step 的节点。注意：泳道里的步骤同样是 Step 节点！</summary>
        public List<CanvasNodeViewModel> Steps() =>
            Canvas.Nodes.Where(n => n.Kind == CanvasNodeKind.Step).ToList();

        /// <summary>
        /// 本层「主列」步骤（= _layerList 的直接成员）。
        /// 判据：拓扑里 Owner 引用等于任一泳道的 Branch.Steps → 归泳道，否则归主列。
        /// </summary>
        public List<CanvasNodeViewModel> MainSteps()
        {
            var branchOwners = new HashSet<ObservableCollection<StepModel>>(
                Canvas.Nodes
                    .Where(n => n.Kind == CanvasNodeKind.Lane && n.Branch != null)
                    .Select(n => n.Branch!.Steps));

            var topo = Topology();
            var result = new List<CanvasNodeViewModel>();
            foreach (var node in Steps())
            {
                if (topo.TryGet(node.StepId, out var pos)
                    && pos!.Owner != null
                    && branchOwners.Contains(pos.Owner))
                    continue;

                result.Add(node);
            }

            return result;
        }

        public List<CanvasNodeViewModel> Lanes() =>
            Canvas.Nodes.Where(n => n.Kind == CanvasNodeKind.Lane).ToList();

        public List<CanvasNodeViewModel> Roots() =>
            Canvas.Nodes.Where(n => n.Kind == CanvasNodeKind.LayerRoot).ToList();

        /// <summary>按分支集合实例取泳道节点（找不到返回 null，断言里显式判空）</summary>
        public CanvasNodeViewModel? Lane(StepCollection branch) =>
            Canvas.Nodes.FirstOrDefault(n =>
                n.Kind == CanvasNodeKind.Lane && ReferenceEquals(n.Branch, branch));

        /// <summary>
        /// 按步骤实例取节点。查的是 Canvas.Nodes（本层可见集合），
        /// 刻意不查内部节点池——否则「删除步骤后画布是否摘掉节点」会被池复用掩盖成假绿。
        /// </summary>
        public CanvasNodeViewModel? Node(StepModel step) =>
            Canvas.Nodes.FirstOrDefault(n =>
                n.Kind == CanvasNodeKind.Step && n.Model == step);

        public CanvasNodeViewModel? Node(Guid stepId) =>
            Canvas.Nodes.FirstOrDefault(n => n.Kind == CanvasNodeKind.Step && n.StepId == stepId);

        /// <summary>取输出端口（按 PortName 匹配，找不到返回 null 让断言显式失败）</summary>
        public CanvasConnectorViewModel? Out(CanvasNodeViewModel? node, string port)
            => node?.Outputs.FirstOrDefault(p => p.PortName == port);

        /// <summary>取输入端口</summary>
        public CanvasConnectorViewModel? In(CanvasNodeViewModel? node, string port)
            => node?.Inputs.FirstOrDefault(p => p.PortName == port);

        // ------------------------------------------------------------------
        //  拓扑
        // ------------------------------------------------------------------

        /// <summary>每次现取一份拓扑快照（拓扑不跟踪图纸）</summary>
        public FlowTopology Topology() => FlowTopology.Build(Flow);

        /// <summary>取某条分支在本层拓扑里的位置（用 Owner 引用匹配），分支为空返回 null</summary>
        public static StepPosition? LanePosition(FlowTopology topo, StepCollection branch)
        {
            foreach (var pos in topo.Positions)
            {
                if (ReferenceEquals(pos.Owner, branch.Steps))
                    return pos;
            }

            return null;
        }

        // ------------------------------------------------------------------
        //  典型图式
        // ------------------------------------------------------------------

        /// <summary>
        /// 「外层步骤 ⇒ 内层消费方」图式：容器带一条分支，分支里放消费方，外层步骤排在容器之前或之后。
        ///   producerBefore = true  → Classify == SameListBefore（合法）
        ///   producerBefore = false → Classify == ProducerAfterEnclosingContainer（非法，编译致命错）
        /// 返回图纸根序列，可直接喂给 FlowCompiler.CheckLinkOrder。
        /// </summary>
        public static List<StepModel> OuterBeforeInner(
            bool producerBefore,
            out ConditionStep container,
            out StepModel consumer,
            out StepModel producer)
        {
            container = new ConditionStep("\uE700", "桩If", StubPluginProvider.IfPlugin, "容器");
            consumer = new ActionStep("\uE700", "桩算子", StubPluginProvider.LeafPlugin, "内层消费")
            {
                InputValues = new Dictionary<string, object> { ["In"] = 0d },
            };
            producer = new ActionStep("\uE700", "桩算子", StubPluginProvider.LeafPlugin, "外层生产");

            container.Children[0].Steps.Add(consumer);

            var roots = producerBefore
                ? new List<StepModel> { producer, container }
                : new List<StepModel> { container, producer };

            RawLink(consumer, "In", producer, "Out");
            return roots;
        }

        /// <summary>
        /// 直接按「引用某步骤的某个输出口」写一条连线（绕过画布，用于构造编译期/拓扑的输入）。
        /// </summary>
        public static void RawLink(StepModel consumer, string inPort, StepModel producer, string outPort)
        {
            consumer.SetLink(inPort, new LinkReference(
                LinkKind.StepPort,
                producer.StepID,
                outPort,
                $"{producer.StepName}.{outPort}"));
        }
    }

    /// <summary>
    /// 画布连线入口封装：走 public 的 CompleteConnectionCommand，
    /// 从而真实经过 internal static CanConnect 的合法性判定（与用户拖线同一条路径）。
    /// 参数用 Tuple&lt;object,object&gt; 是因为命令签名是 DelegateCommand&lt;object&gt;，
    /// 具体两端顺序由 OnCompleteConnection 的取值约定决定（Item1 = 源、Item2 = 目标）。
    /// </summary>
    internal static class FlowCanvasExtensions
    {
        public static void Connect(
            this FlowCanvasViewModel canvas,
            CanvasConnectorViewModel? output,
            CanvasConnectorViewModel? input)
        {
            canvas.CompleteConnectionCommand.Execute(
                new Tuple<object, object>(output!, input!));
        }

        /// <summary>
        /// 段3 测试辅助：用反射调用 internal PushUndo，
        /// 因为撤销命令类是 internal，且 PushUndo 也是 internal——
        /// 不为测试在产品代码里改可见性，反射入口与既有 CheckLinkOrder 一致。
        /// </summary>
        public static void PushUndoReflection(this FlowCanvasViewModel canvas, object command)
        {
            var m = typeof(FlowCanvasViewModel).GetMethod(
                "PushUndo", BindingFlags.Instance | BindingFlags.NonPublic);
            if (m == null)
                throw new InvalidOperationException("PushUndo 方法未找到（已改名或被删除）");
            m.Invoke(canvas, new[] { command });
        }

        /// <summary>反射构造 VisionMaster.ViewModels 内部的撤销命令类实例</summary>
        public static object CreateReorderCommand(
            ObservableCollection<StepModel> owner, int from, int to)
        {
            var t = typeof(FlowCanvasViewModel).Assembly.GetType(
                "VisionMaster.ViewModels.ReorderCommand")
                ?? throw new InvalidOperationException("ReorderCommand 类型未找到");
            return Activator.CreateInstance(t, owner, from, to)!;
        }

        public static object CreateMoveBranchCommand(
            StepModel step,
            ObservableCollection<StepModel> fromOwner, int fromIndex,
            ObservableCollection<StepModel> toOwner, int toIndex)
        {
            var t = typeof(FlowCanvasViewModel).Assembly.GetType(
                "VisionMaster.ViewModels.MoveBranchCommand")
                ?? throw new InvalidOperationException("MoveBranchCommand 类型未找到");
            return Activator.CreateInstance(t, step, fromOwner, fromIndex, toOwner, toIndex)!;
        }

        /// <summary>反射执行 IUndoCommand.Redo（产品接口未暴露，但段3 调用方就是用 Redo 入栈）</summary>
        public static void InvokeRedo(object command)
        {
            var m = command.GetType().GetMethod("Redo")
                ?? throw new InvalidOperationException("Redo 方法未找到");
            m.Invoke(command, null);
        }

        public static void InvokeUndo(object command)
        {
            var m = command.GetType().GetMethod("Undo")
                ?? throw new InvalidOperationException("Undo 方法未找到");
            m.Invoke(command, null);
        }
    }
}
