﻿﻿﻿﻿﻿﻿﻿﻿﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Core.Events;
using Core.Interfaces;
using VisionMaster.Binding;
using VisionMaster.EventModel;
using VisionMaster.Models;

namespace VisionMaster.Services
{
    /// <summary>
    /// 只读工作区上下文接口
    /// 提供对当前工作区状态的只读访问
    /// </summary>
    public interface IReadOnlyWorkspaceContext
    {
        /// <summary>
        /// 当前方案
        /// </summary>
        SolutionModel CurrentSolution { get; }

        /// <summary>
        /// 当前流程
        /// </summary>
        FlowModel CurrentFlow { get; }

        /// <summary>
        /// 当前步骤
        /// </summary>
        StepModel CurrentStep { get; }

        /// <summary>
        /// 监视项集合（用于调试时查看变量值）
        /// </summary>
        ObservableCollection<WatchItemModel> WatchItems { get; }
    }

    /// <summary>
    /// 工作区管理器接口
    /// 继承自只读接口，增加状态切换能力
    /// </summary>
    public interface IWorkspaceManager : IReadOnlyWorkspaceContext
    {
        /// <summary>
        /// 全局变量集合
        /// </summary>
        public ObservableCollection<IVariable> GlobalVariables { get; set; }

        /// <summary>
        /// 变量解析索引：与 <see cref="GlobalVariables"/> 是同一份数据，额外提供 Id/Name 双路查找，
        /// 并把"连线引用 → 变量对象"的解析收敛到一处。
        ///
        /// 为什么挂在工作区上而不是各自 new：索引必须跟着变量集合的增删改变，
        /// 每处消费者各建一个索引，就会出现"一个索引知道新变量、另一个不知道"的分裂。
        /// 连线编译、监视项、HMI 画面绑定一律走这里，不要再自行遍历 GlobalVariables。
        /// </summary>
        IVariableRegistry VariableRegistry { get; }

        /// <summary>
        /// 切换当前方案
        /// </summary>
        void SwitchSolution(SolutionModel solution);

        /// <summary>
        /// 切换当前流程
        /// </summary>
        void SwitchFlow(FlowModel flow);

        /// <summary>
        /// 切换当前步骤
        /// </summary>
        void SwitchStep(StepModel step);
    }

    /// <summary>
    /// 工作区上下文实现类
    /// 管理方案、流程、步骤的切换，并维护全局变量
    /// </summary>
    public class WorkspaceContext : BindableBase, IWorkspaceManager
    {
        private ObservableCollection<IVariable> _globalVariables = new();

        /// <summary>
        /// 全局变量集合。
        ///
        /// setter 不是摆设：集合实例一旦被整体替换（如 InitializeCommonVariables、
        /// 方案重载换容器），索引必须同步重挂到新实例上，否则会"看得见幽灵变量、看不见新变量"。
        /// 这条同步在 setter 内完成，任何替换路径都绕不过去。
        /// </summary>
        public ObservableCollection<IVariable> GlobalVariables
        {
            get => _globalVariables;
            set
            {
                var next = value ?? new ObservableCollection<IVariable>();
                if (ReferenceEquals(_globalVariables, next))
                    return;

                _globalVariables = next;
                _variableRegistry?.Attach(next);
            }
        }

        private readonly VariableRegistry _variableRegistry;

        /// <summary>
        /// 变量解析索引（Id/Name 双路查找）。实现类型固定为本工程的 VariableRegistry，
        /// 对外只暴露接口，消费者不依赖具体实现
        /// </summary>
        public IVariableRegistry VariableRegistry => _variableRegistry;

        private SolutionModel _currentSolution;
        /// <summary>
        /// 当前方案（只读）
        /// </summary>
        public SolutionModel CurrentSolution => _currentSolution;

        private FlowModel _currentFlow;
        /// <summary>
        /// 当前流程（只读）
        /// </summary>
        public FlowModel CurrentFlow => _currentFlow;

        private StepModel _currentStep;
        /// <summary>
        /// 当前步骤（只读）
        /// </summary>
        public StepModel CurrentStep => _currentStep;

        /// <summary>
        /// 监视项集合
        /// </summary>
        public ObservableCollection<WatchItemModel> WatchItems => CurrentSolution?.WatchItems;

        /// <summary>
        /// 初始化工作区上下文
        /// </summary>
        public WorkspaceContext()
        {
            InitializeCommonVariables();
            // 索引必须在变量初始化之后建立：InitializeCommonVariables 会整体替换集合实例，
            // 先建索引就会挂在一个随即被丢弃的旧集合上（看得见 12 个演示变量，看不见真实变量）
            _variableRegistry = new VariableRegistry(_globalVariables);
            GlobalEventBus.Subscribe<StepRenamedMessage>(OnStepRenamed);

            // 变量改名的级联入口。为什么订阅注册表的实例事件而不是走 GlobalEventBus：
            // 改名不是"广播给所有关心的人"的公告，而是"注册表改了自己的索引后必须通知依赖它的引用"——
            // 事件源头就是索引本身，订阅者与事件源同生共死（本类是单例，注册表字段只读），无需退订。
            // 这一条订阅是整个 S0-b 的落点：少了它，改完名连线/监视项全部静默失联。
            _variableRegistry.VariableRenamed += OnVariableRenamed;
        }

        /// <summary>
        /// 切换当前方案
        /// </summary>
        public void SwitchSolution(SolutionModel solution)
        {
            SetProperty(ref _currentSolution, solution, nameof(CurrentSolution));
            SwitchFlow(null);
        }

        /// <summary>
        /// 切换当前流程
        /// </summary>
        public void SwitchFlow(FlowModel flow)
        {
            if (flow != null)
            {
                if (_currentSolution == null)
                    throw new InvalidOperationException("必须在当前 Solution 存在时才能设置 Flow");

                if (!_currentSolution.Flows.Contains(flow))
                    throw new InvalidOperationException("指定的 Flow 不属于当前 Solution");
            }
            SetProperty(ref _currentFlow, flow, nameof(CurrentFlow));
            SwitchStep(null);
        }

        /// <summary>
        /// 切换当前步骤
        /// </summary>
        public void SwitchStep(StepModel step)
        {
            if (step != null)
            {
                if (_currentFlow == null)
                    throw new InvalidOperationException("必须在当前 Flow 存在时才能设置 Step");

                if (!ContainsStepRecursively(_currentFlow.Steps, step))
                    throw new InvalidOperationException($"指定的 Step [{step.StepID}] 不属于当前 Flow，或已被删除！");
            }

            SetProperty(ref _currentStep, step, nameof(CurrentStep));
        }

        /// <summary>
        /// 处理步骤重命名：按 StepId 精确定位引用，重算连线显示地址。
        ///
        /// 相对旧实现的三点修正：
        /// 1. 遍历整个方案的所有流程，而不是只 CurrentFlow —— 跨流程引用同样需要更新；
        /// 2. 判据由「DisplayAddress 前缀等于旧名」改为「Kind==StepPort 且 TargetStepId==被改名步骤」，
        ///    彻底消除同名步骤误伤（步骤名在不同流程里可以合法重复）；
        /// 3. 不再改写分支条件表达式 —— 表达式里的标识符是用户自起的变量别名
        ///    （LocalVariableItem.Name），与步骤名无关。旧实现用 \b旧名\b 正则替换，
        ///    一旦别名恰好等于步骤名，就会把表达式改成编译不过的样子，属于自造语法错误。
        /// </summary>
        private void OnStepRenamed(StepRenamedMessage args)
        {
            if (args == null || CurrentSolution?.Flows == null) return;

            foreach (var flow in CurrentSolution.Flows)
                RefreshStepPortDisplayAddresses(flow.Steps, args);
        }

        /// <summary>
        /// 递归刷新引用了被改名步骤的连线显示地址（含所有嵌套容器分支）
        /// </summary>
        private static void RefreshStepPortDisplayAddresses(IEnumerable<StepModel> steps, StepRenamedMessage args)
        {
            foreach (var step in steps)
            {
                foreach (var link in step.LinkedSources.Values)
                {
                    if (link == null) continue;

                    // 只有真实步骤输出的地址串里含步骤名；
                    // 全局变量 / 运行时变量 / 常量的地址与步骤名无关，一律不碰。
                    // NormalizeKind 顺带把旧工程缺失的 Kind 补齐，避免按前缀误判
                    if (link.NormalizeKind() != LinkKind.StepPort) continue;
                    if (link.TargetStepId != args.StepId) continue;

                    link.DisplayAddress = $"{args.NewName}.{link.TargetPortName}";
                }

                if (step is IContainerStep container && container.Children != null)
                {
                    foreach (var branch in container.Children)
                        RefreshStepPortDisplayAddresses(branch.Steps, args);
                }
            }
        }

        /// <summary>
        /// 变量改名的级联：把"引用侧"所有按旧名挂着的显示文案与兜底键改成新名。
        ///
        /// 与步骤改名的区别（这是 S0-b 的核心认识）：
        /// 步骤改名的引用判据是 TargetStepId，改完名引用天然还成立，只需要刷新显示串；
        /// 而变量引用在 Id 落地之前是"名字即地址"（TargetVariableId 为空、TargetPortName 存名字），
        /// 改名就等于换地址——光刷新显示串不够，必须把寻址键一起改写，否则编译期直接断链。
        /// 所以这里对老连线做一次性自愈（补 Id + 换名字），对新连线只刷新显示串。
        /// </summary>
        private void OnVariableRenamed(object? sender, VariableRenamedEventArgs args)
        {
            var variable = args?.Variable;
            if (variable == null || variable.VariableId == Guid.Empty)
                return;

            var variableId = variable.VariableId;
            var newName = variable.Name;

            // ① 连线：显示串 + 老连线的兜底寻址键
            if (CurrentSolution?.Flows != null)
            {
                foreach (var flow in CurrentSolution.Flows)
                    RefreshGlobalVariableLinks(flow.Steps, variableId, args!.OldName, newName);
            }

            // ② 监视项：改 GlobalVariableName（展示）并按需补 VariableId（自愈）
            RefreshWatchItems(variableId, args!.OldName, newName);

            // ③ 画面绑定：口径与连线完全一致（Id 优先、名字兜底），
            //    老绑定按名命中时顺带把 Id 补回来，一次改名即完成迁移。
            //    少这一步的后果很隐蔽：改完名当场看不出问题，直到下次打开方案
            //    才发现画面上的数值控件全空——因为绑定还按旧名找变量。
            CurrentSolution?.Scada.RefreshVariableReferences(variableId, args.OldName, newName);
        }

        /// <summary>
        /// 递归刷新引用了被改名变量的全局变量连线（含所有嵌套容器分支）。
        /// </summary>
        private static void RefreshGlobalVariableLinks(
            IEnumerable<StepModel> steps,
            Guid variableId,
            string? oldName,
            string newName)
        {
            foreach (var step in steps)
            {
                foreach (var link in step.LinkedSources.Values)
                {
                    if (link == null) continue;

                    // 只看全局变量连线：步骤端口/运行时变量/常量的寻址与变量名无关
                    // 注意必须判 Kind 而不是判 TargetStepId 是否为空 —— 全局变量与常量的
                    // TargetStepId 都是 Guid.Empty，只能靠 Kind 区分（历史教训见 LinkKind 落地记录）
                    if (link.NormalizeKind() != LinkKind.GlobalVariable) continue;

                    // 命中判据有两路：
                    // ① 已自愈/新绑定的连线：TargetVariableId 就是权威键，改名不受影响，只需刷新显示；
                    // ② 老连线（Id 为空、只有名字）：用旧名兜底认领，顺手把 Id 补上完成自愈。
                    //    这是唯一一处"按名字认领引用"的地方——改名动作本身保证了新名唯一，
                    //    且此刻名字已经从索引里摘掉，认领不会与别的变量混淆。
                    bool byId = link.TargetVariableId == variableId;
                    bool byLegacyName =
                        link.TargetVariableId == Guid.Empty
                        && !string.IsNullOrEmpty(oldName)
                        && string.Equals(link.TargetPortName, oldName, StringComparison.OrdinalIgnoreCase);

                    if (!byId && !byLegacyName) continue;

                    if (byLegacyName)
                    {
                        // 自愈：老连线的 TargetPortName 存的就是变量名，改成新名并把稳定身份补上。
                        // 不补的话，下次编译 ResolveGlobalLink 会按新名……也命中不了（它还拿着旧名），直接断链
                        link.TargetPortName = newName;
                        link.TargetVariableId = variableId;
                    }

                    link.DisplayAddress = RebuildVariableDisplayAddress(link.DisplayAddress, newName);
                }

                if (step is IContainerStep container && container.Children != null)
                {
                    foreach (var branch in container.Children)
                        RefreshGlobalVariableLinks(branch.Steps, variableId, oldName, newName);
                }
            }
        }

        /// <summary>
        /// 重算全局变量连线的显示串：只把"变量名那一段"换成新名，前缀与下标后缀照搬。
        ///
        /// 为什么不按 <c>{变量名}</c> 直接拼一个新串：显示串是绑定弹窗按
        /// <c>{来源节点名}.{变量名}</c> 拼的，来源节点名历史上有 "Global" 与 "全局变量 (Global)"
        /// 两种写法（旧方案文件里两种都存着），数组元素绑定还带 <c>[i]</c> 后缀。
        /// 只换尾段既不用在 Core 里硬编码任何展示文案，也能一次性覆盖所有历史格式。
        /// （DisplayAddress 是纯展示字段，编译器不读它做判定，改错也不会影响寻址。）
        /// </summary>
        private static string RebuildVariableDisplayAddress(string? displayAddress, string newName)
        {
            if (string.IsNullOrEmpty(displayAddress))
                return newName;

            var display = displayAddress!;

            // 摘下数组下标后缀（形如 "[0]"），只对名字段动手
            var suffix = string.Empty;
            if (display.EndsWith("]", StringComparison.Ordinal))
            {
                var open = display.LastIndexOf('[');
                if (open > display.LastIndexOf('.'))
                {
                    suffix = display.Substring(open);
                    display = display.Substring(0, open);
                }
            }

            var dot = display.LastIndexOf('.');
            var prefix = dot >= 0 ? display.Substring(0, dot + 1) : string.Empty;
            return prefix + newName + suffix;
        }

        /// <summary>
        /// 刷新监视项里的全局变量引用：改展示名，并给老数据补上稳定身份。
        /// </summary>
        private void RefreshWatchItems(Guid variableId, string? oldName, string newName)
        {
            var watchItems = WatchItems;
            if (watchItems == null) return;

            foreach (var item in watchItems)
            {
                if (item == null || item.ItemType != WatchItemType.GlobalVariable) continue;

                bool byId = item.VariableId == variableId;

                // 老监视项只存了变量名（VariableId 为空），按旧名认领并补 Id ——
                // 不补的话它下次仍会按旧名去找变量，改名后永远显示"(未知变量)"
                bool byLegacyName =
                    item.VariableId == Guid.Empty
                    && !string.IsNullOrEmpty(oldName)
                    && string.Equals(item.GlobalVariableName, oldName, StringComparison.OrdinalIgnoreCase);

                if (!byId && !byLegacyName) continue;

                if (byLegacyName)
                    item.VariableId = variableId;

                item.GlobalVariableName = newName;
            }
        }

        /// <summary>
        /// 递归检查步骤是否在步骤集合中（包括嵌套容器）
        /// </summary>
        private bool ContainsStepRecursively(IEnumerable<StepModel> steps, StepModel targetStep)
        {
            if (steps == null) return false;

            foreach (var step in steps)
            {
                if (step == targetStep)
                    return true;
                if (step is IContainerStep container && container.Children != null)
                {
                    foreach (var branch in container.Children)
                    {
                        if (ContainsStepRecursively(branch.Steps, targetStep))
                            return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 初始化常用全局变量
        /// </summary>
        public void InitializeCommonVariables()
        {
            GlobalVariables = new ObservableCollection<IVariable>
            {
                VariableFactory.CreateLocal("RecipeName", typeof(string), "当前加载的产品模型配方名称", "Product_Type_A"),
                VariableFactory.CreateLocal("ProductCode", typeof(string),  "当前识别到的条码或二维码信息", "QR202310240001"),
                VariableFactory.CreateLocal("TotalCount", typeof(int),"设备运行以来的累计生产总数", 1500),
                VariableFactory.CreateLocal("OKCount", typeof(int),"累计检测良品总数", 1485),
                VariableFactory.CreateLocal("NGCount", typeof(int),"累计检测不良品总数", 15),
                VariableFactory.CreateLocal("ScoreThreshold", typeof(double),"模板匹配的最小及格分数 (0-100)", 85.5),
                VariableFactory.CreateLocal("Exposure_Time", typeof(double),"主相机的曝光时间 (ms)", 25.0),
                VariableFactory.CreateLocal("YieldRate", typeof(double), "当前的实时良率 (%)",99.0),
                VariableFactory.CreateLocal("PLC_Ready", typeof(bool), "外部PLC通讯握手信号", true),
                VariableFactory.CreateLocal("BarcodeResults", typeof(string[]), "单次触发读取到的所有条码集合",new string[] { "SN2026-A01", "SN2026-A02", "SN2026-B01" }),
                VariableFactory.CreateLocal("HoleCoordinatesX", typeof(double[]),"所有定位孔的 X 坐标集合 (mm)", new double[] { 12.5, 45.2, 88.9, 120.0 }),
                VariableFactory.CreateLocal("CameraROI", typeof(int[]),"相机的动态检测区域 [X, Y, Width, Height]", new int[] { 100, 100, 800, 600 }),
            };
        }
    }
}
