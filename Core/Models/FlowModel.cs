using System;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.Models
{
    /// <summary>
    /// 流程调用方式：哪一种"自动/外部"力量可以把这条流程跑起来。
    ///
    /// 【为什么是 [Flags]（多选位集）】一条流程完全可以"定时跑"又"允许 HTTP 触发"，
    /// 单选枚举表达不了；列表界面按多选勾选，落盘就是一个整数位集。
    ///
    /// 【为什么位值从 8 起跳、Manual 取 0】老文件里这个字段曾是**单选**枚举：
    /// 1=定时、2=变量、3=子程序。若把新位值定成 1/2/4，老值 3（子程序）会与
    /// "定时|变量"撞成同一个数，读回来就变成另一种调用方式（静默地干错事）。
    /// 把位值抬到 8/16/32/64 后，1/2/3 全部落在位区间之外，加载时一眼可辨"这是老值"，
    /// 按 <see cref="FlowInvokeTypeExtensions.MigrateLegacy"/> 映射迁移。
    /// Manual=0（不占位）表达"没有任何自动触发"，是新建流程的默认态；界面显示为「手动」。
    ///
    /// 落盘纪律同 <c>ScadaActionType</c>：数值一旦发布只许在末尾追加，不许改、不许回收——
    /// 否则老工程里那条调用方式会被读成另一个方式。
    /// </summary>
    [Flags]
    public enum FlowInvokeType
    {
        /// <summary>手动调用（默认）：只由本机界面的运行入口触发，不接受任何自动触发</summary>
        Manual = 0,

        /// <summary>定时调用：按 <see cref="FlowModel.TimerIntervalMs"/> 周期自动执行（位值 8，理由见上）</summary>
        Timer = 8,

        /// <summary>变量触发：<see cref="FlowModel.TriggerVariable"/> 指定的变量变化时执行（位值 16）</summary>
        Variable = 16,

        /// <summary>子程序调用：允许被其它流程以「调用流程」步骤调用（位值 32）</summary>
        Subroutine = 32,

        /// <summary>HTTP 外部调用：允许经 /flow/{流程名} 接口触发（位值 64；唯一带门禁的位）</summary>
        Http = 64,
    }

    /// <summary>
    /// 流程在方案里的<b>固定角色</b>：Home / Main / End 三条流程是每个方案的强制骨架
    /// （用户决策：所有程序都要有这三个流程）。
    ///
    /// 【为什么是角色而不是"按名字管"】名字是给现场看的，可以被改名（"Home"改叫"回原点"），
    /// 而"这条流程是不是骨架"必须有个不随改名漂移的判据——落盘的 Role 字段就是它。
    /// 删除 / 新建的约束、加载时补齐骨架，全部按 Role 判定，名字只作新建时的默认名。
    ///
    /// 不是 [Flags]：一条流程只能有一个角色。数值一旦发布同样只许在末尾追加（理由同上）。
    /// </summary>
    public enum FlowRole
    {
        /// <summary>普通流程：无角色，可自由增删</summary>
        None = 0,

        /// <summary>回原 / 回零：开机与换产时的安全位姿流程</summary>
        Home = 1,

        /// <summary>主任务：生产主流程（连续或按触发运行）</summary>
        Main = 2,

        /// <summary>收尾：停机 / 换产前把机构与状态收干净</summary>
        End = 3,
    }

    /// <summary>
    /// 调用方式的展示与迁移（领域层只写一份，属性面板 / 流程管理列表 / 日志共用）。
    /// </summary>
    public static class FlowInvokeTypeExtensions
    {
        /// <summary>是否允许被 HTTP 接口触发（/flow/{流程名} 的唯一门禁位）</summary>
        public static bool IsHttpCallable(this FlowInvokeType type) => (type & FlowInvokeType.Http) != 0;

        /// <summary>
        /// 老单选值 → 新位集的迁移映射。
        ///
        /// 只有加载 .vms 时走这里：位区间（1..7）之外的值要么是老单选（1/2/3），
        /// 要么是未知数据；老值按"继续按原名工作"映射到对应位，
        /// 未知值<b>原样保留</b>（高版本软件存下的位集，读进低版本不该被清掉）。
        /// </summary>
        public static FlowInvokeType MigrateLegacy(int raw) => raw switch
        {
            1 => FlowInvokeType.Timer,        // 老「定时」
            2 => FlowInvokeType.Variable,     // 老「变量」
            3 => FlowInvokeType.Subroutine,   // 老「子程序」
            _ => (FlowInvokeType)raw,         // 0 = 手动；其它按位集原样保留
        };

        /// <summary>
        /// 调用方式的人话文本：无任何自动触发 → 「手动」；否则按勾选的位拼（"定时 / HTTP外部调用"）。
        /// 与流程列表和日志共用一个口径，避免"面板里叫『HTTP外部调用』、日志里叫『Http』"。
        ///
        /// 认不出的位（人工编辑 .vms 塞进 4/5/6/7 这类不在位表里的值）显示成「未知(4)」而不是
        /// 回落"手动"：把损坏数据显示成合法状态，用户永远查不出为什么"勾了 HTTP 还是 403"。
        /// </summary>
        public static string DisplayText(this FlowInvokeType type)
        {
            if (type == FlowInvokeType.Manual) return "手动";

            var parts = new List<string>(4);
            if ((type & FlowInvokeType.Timer) != 0) parts.Add("定时");
            if ((type & FlowInvokeType.Variable) != 0) parts.Add("变量");
            if ((type & FlowInvokeType.Subroutine) != 0) parts.Add("子程序");
            if ((type & FlowInvokeType.Http) != 0) parts.Add("HTTP外部调用");

            return parts.Count > 0 ? string.Join(" / ", parts) : $"未知({(int)type})";
        }

        /// <summary>
        /// 某个调用位"还没接通运行侧"的一句话原因；<c>null</c> = 已接通。
        ///
        /// 为什么要有它（与 <c>ScadaActionType.PendingReason</c> 同一条理由）：界面上勾了却
        /// 什么都不发生，用户只会以为是软件坏了。把"哪一位已接通"写在领域层，
        /// 列表提示与文档共用同一句，接一个删一句。
        /// </summary>
        public static string PendingReason(this FlowInvokeType flag) => flag switch
        {
            // 四位全部接通（2026-10-05 第二批）：
            //  · Http        → HttpImageServer 会话获取前的门禁（404/403/403/409）；
            //  · Timer       → FlowTimerScheduler 按 TimerIntervalMs 周期触发（下限 100ms）；
            //  · Variable    → FlowVariableTriggerService 在 TriggerVariable 上升沿触发；
            //  · Subroutine  → FlowInvoker + 「调用流程」插件（Plugin.RunFlow），宿主侧查这一位放行。
            // 将来再加位、而运行侧没跟上时：在这里给它一句话，接上了把这句删掉（界面自动跟着变）。
            _ => null,
        };
    }

    /// <summary>
    /// 流程运行时状态
    /// </summary>
    public enum FlowRunState
    {
        /// <summary>停止</summary>
        Stopped = 0,
        /// <summary>运行中</summary>
        Running = 1,
        /// <summary>暂停</summary>
        Paused = 2
    }

    /// <summary>
    /// 流程模型
    /// 表示一个完整的视觉检测流程
    /// </summary>
    public class FlowModel : BindableBase
    {
        /// <summary>
        /// 流程唯一标识
        /// </summary>
        public string FlowID { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// 流程名称
        /// </summary>
        public string FlowName
        {
            get { return field; }
            set { SetProperty(ref field, value); }
        }

        /// <summary>
        /// 流程描述
        /// </summary>
        public string Description
        {
            get { return field; }
            set { SetProperty(ref field, value); }
        }

        /// <summary>
        /// 版本号（自动递增）
        /// </summary>
        public int Version
        {
            get { return field; }
            set { SetProperty(ref field, value); }
        }

        /// <summary>
        /// 流程是否启用
        /// </summary>
        public bool IsEnabled
        {
            get { return field; }
            set { SetProperty(ref field, value); }
        } = true;

        /// <summary>
        /// 流程角色：Home / Main / End 三条骨架流程在方案里各占一角（见 <see cref="FlowRole"/>）。
        /// 普通流程为 None。加载老方案时由 MandatoryFlows.Ensure 按名字补齐缺的角色；
        /// 带角色的流程不允许删除（FlowListViewModel 拦截），改名不受限（判据是角色不是名字）。
        /// </summary>
        public FlowRole Role
        {
            get { return field; }
            set { SetProperty(ref field, value); }
        }

        /// <summary>
        /// 调用类型
        /// </summary>
        public FlowInvokeType InvokeType
        {
            get { return field; }
            set { SetProperty(ref field, value); }
        }

        /// <summary>
        /// 触发变量名称（当InvokeType为Variable时使用）
        /// </summary>
        public string TriggerVariable
        {
            get { return field; }
            set { SetProperty(ref field, value); }
        }

        /// <summary>
        /// 定时调用间隔（毫秒）
        /// </summary>
        public int TimerIntervalMs
        {
            get { return field; }
            set { SetProperty(ref field, value); }
        }

        /// <summary>
        /// 步序内容是否加密
        /// </summary>
        public bool StepsEncrypted
        {
            get { return field; }
            set { SetProperty(ref field, value); }
        }

        /// <summary>
        /// 加密密钥（加密后的密钥由系统生成）
        /// </summary>
        public string? EncryptedKey { get; set; }

        /// <summary>
        /// 触发模式（单次/连续/外部/定时）
        /// </summary>
        public FlowTriggerMode TriggerMode { get; set; } = FlowTriggerMode.Continuous;

        /// <summary>
        /// 运行时状态
        /// </summary>
        [JsonIgnore]
        public FlowRunState RunState
        {
            get { return field; }
            set { SetProperty(ref field, value); }
        }

        /// <summary>
        /// 当前运行步骤索引
        /// </summary>
        [JsonIgnore]
        public int CurrentStepIndex
        {
            get { return field; }
            set { SetProperty(ref field, value); }
        }

        /// <summary>
        /// 开始运行时间
        /// </summary>
        [JsonIgnore]
        public DateTime? StartRunTime
        {
            get { return field; }
            set { SetProperty(ref field, value); }
        }

        /// <summary>
        /// 画布布局（视图元数据，StepID → 坐标/折叠态）。
        ///
        /// 刻意写成「不通知」的普通属性：它不在 Steps 的 PropertyChanged 订阅链上，
        /// 也不调用 SetProperty，因此改动布局不会递增 Version、不会触发重编译。
        /// setter 挡 null 是为了让画布侧可以无脑使用 Layout 而不必判空。
        /// </summary>
        public FlowLayoutStore Layout
        {
            get => _layout;
            set => _layout = value ?? new FlowLayoutStore();
        }

        private FlowLayoutStore _layout = new();

        private ObservableCollection<StepModel> _steps = new();

        /// <summary>
        /// 步骤列表
        /// ObjectCreationHandling.Replace 确保 Newtonsoft 反序列化时调用 setter 整体替换集合，
        /// 使 setter 内的订阅重挂逻辑（CollectionChanged + 每个步骤的 PropertyChanged）生效，
        /// 否则默认追加行为会绕过 setter，导致步骤属性变更通知丢失 → 流程版本不递增 → 改参数后不触发自动重编译
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public ObservableCollection<StepModel> Steps
        {
            get => _steps;
            set
            {
                _steps = value ?? new ObservableCollection<StepModel>();

                // 反序列化也会走到这里：新集合（及其子树）在赋值时已填好内容，
                // 一次重扫即完成挂接；旧集合与旧步骤因不再可达被摘除。
                // 刻意不递增 Version——版本号由 JSON 反序列化赋值，内容变更由集合事件负责
                RebuildSubscriptions();
            }
        }

        /// <summary>
        /// 初始化流程模型（为初始空集合挂上订阅）
        /// </summary>
        [JsonConstructor]
        public FlowModel()
        {
            _watchedCollections.Add(_steps);
            _steps.CollectionChanged += OnStructureChanged;
        }

        /// <summary>
        /// 已订阅 PropertyChanged 的步骤（全树，含嵌套分支/循环体内的步骤）
        /// </summary>
        private readonly HashSet<StepModel> _watchedSteps = new();

        /// <summary>
        /// 已订阅 CollectionChanged 的集合：顶层 Steps + 每个分支/循环体的 Steps + 每个容器的 Children。
        ///
        /// 【为什么连 Children 也要盯】新增 ElseIf/Else 分支只动 Children 集合，
        /// 不盯它就收不到事件 → 既不递增版本，也不会重扫订阅 → 之后往这个新分支里
        /// 拖入的步骤永远挂不上 PropertyChanged（禁用/改参数静默不生效）。
        /// </summary>
        private readonly HashSet<INotifyCollectionChanged> _watchedCollections = new();

        /// <summary>
        /// 按当前流程树重扫订阅：从顶层集合出发收集所有可达的步骤集合、分支集合与步骤，
        /// 与已订阅名单求差后摘挂。
        ///
        /// 【为什么不按事件的 New/OldItems 逐个摘挂】Move（拖动改序）会同时带上
        /// OldItems 与 NewItems，Reset（Clear）则不带任何明细——按明细摘挂会把还在树上的
        /// 步骤订阅摘掉（后续禁用/改参数不再递增版本），或漏摘已删除的步骤（假版本递增 → 白重编译）。
        /// 重扫的代价是 O(全树步骤数)，而结构变更由用户操作驱动、不在运行热路径上。
        /// </summary>
        private void RebuildSubscriptions()
        {
            var wantedCollections = new HashSet<INotifyCollectionChanged>();
            var wantedSteps = new HashSet<StepModel>();
            CollectSubscriptions(_steps, wantedCollections, wantedSteps);

            foreach (var collection in _watchedCollections.ToList())
            {
                if (wantedCollections.Contains(collection)) continue;
                collection.CollectionChanged -= OnStructureChanged;
                _watchedCollections.Remove(collection);
            }

            foreach (var step in _watchedSteps.ToList())
            {
                if (wantedSteps.Contains(step)) continue;
                step.PropertyChanged -= OnStepPropertyChanged;
                _watchedSteps.Remove(step);
            }

            foreach (var collection in wantedCollections)
            {
                if (_watchedCollections.Add(collection))
                    collection.CollectionChanged += OnStructureChanged;
            }

            foreach (var step in wantedSteps)
            {
                if (_watchedSteps.Add(step))
                    step.PropertyChanged += OnStepPropertyChanged;
            }
        }

        /// <summary>
        /// 深度优先收集子树里的全部集合与步骤（迭代实现，避免深层嵌套递归爆栈）。
        /// collections 兼作访问标记与环检测：容器 Children 若被手工编辑成环，不会死循环。
        /// </summary>
        private static void CollectSubscriptions(
            ObservableCollection<StepModel> root,
            HashSet<INotifyCollectionChanged> collections,
            HashSet<StepModel> steps)
        {
            var pending = new Stack<ObservableCollection<StepModel>>();
            pending.Push(root);

            while (pending.Count > 0)
            {
                var collection = pending.Pop();
                if (collection == null || !collections.Add(collection)) continue;

                foreach (var step in collection)
                {
                    if (step == null) continue;

                    steps.Add(step);

                    if (step is not IContainerStep container || container.Children == null) continue;

                    collections.Add(container.Children);

                    foreach (var branch in container.Children)
                    {
                        if (branch?.Steps != null)
                            pending.Push(branch.Steps);
                    }
                }
            }
        }

        /// <summary>
        /// 任意一处结构变更（增删步骤、增删分支、拖动改序、整体清空）的统一入口：
        /// 递增版本 + 重扫订阅 + 清理已移出流程树的步骤的布局。
        /// </summary>
        private void OnStructureChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            Version++;

            // 先快照，重扫后不在名单里的就是被移出流程树的步骤，顺带清掉它们保存的坐标。
            // 判据用"是否仍可达"而不是"是否出现在 OldItems 里"：Move（拖动改序）同样带
            // OldItems，但步骤还在树上，误删会让它的位置丢失（下次渲染被迫重新自动布局）。
            var before = _watchedSteps.ToList();
            RebuildSubscriptions();

            foreach (var step in before)
            {
                if (!_watchedSteps.Contains(step))
                    Layout.Remove(step.StepID);
            }
        }

        /// <summary>
        /// 纯运行时属性名集合（由 [RuntimeState] 标记），静态构建一次。
        /// 这些属性变化不改变流程语义，因此不递增 Version。
        /// </summary>
        private static readonly HashSet<string> s_runtimePropertyNames = CollectRuntimePropertyNames();

        private static HashSet<string> CollectRuntimePropertyNames()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);

            foreach (var type in typeof(StepModel).Assembly.GetTypes())
            {
                if (!typeof(StepModel).IsAssignableFrom(type)) continue;

                // DeclaredOnly：只看各类型自己声明的属性，避免基类属性被重复扫描
                foreach (var prop in type.GetProperties(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (prop.GetCustomAttribute<RuntimeStateAttribute>() != null)
                        names.Add(prop.Name);
                }
            }

            return names;
        }

        /// <summary>
        /// 步骤属性变更处理：仅语义属性递增版本号。
        ///
        /// 排除名单由 [RuntimeState] 特性反射得出，不再手写字符串——
        /// 旧实现写死 "IsSelected" / "LastRunTime"，前者 StepModel 从未拥有，
        /// 后者在耗时 Stopwatch 改造后已更名为 LastRunTimeMs，名单静默失效，
        /// 导致 BeginTiming/EndTiming/ResetState 每步合计约 8 次 Version++，
        /// 每次运行前都被迫全量重编译。
        /// </summary>
        private void OnStepPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != null && s_runtimePropertyNames.Contains(e.PropertyName))
                return;

            Version++;
        }

        /// <summary>
        /// 添加步骤并自动生成唯一名称
        /// </summary>
        public void AddStepWithAutoName(StepModel newStep)
        {
            if (newStep == null) return;
            if (string.IsNullOrWhiteSpace(newStep.PluginName)) newStep.PluginName = "未知工具";

            var existingNames = new HashSet<string>(Steps.Select(s => s.StepName));

            int nextId = 1;
            string targetName = $"{newStep.PluginName}_{nextId}";

            while (existingNames.Contains(targetName))
            {
                nextId++;
                targetName = $"{newStep.PluginName}_{nextId}";
            }

            newStep.StepName = targetName;
            Steps.Add(newStep);
        }
    }
}
