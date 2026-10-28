using System;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
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

        /// <summary>
        /// 随整线启动（位值 128）：工具栏「启动整线」把它和其它同样勾了本位的流程
        /// <b>各起各的会话</b>一起启动。
        ///
        /// 【为什么不叫"主流程"】工位是<b>平级</b>的：某条流程失败 / 卡死只收尾它自己，
        /// 其余照常循环（用户裁决 2026-10-28）。把工位做成主流程的子调用（串行或并行都一样）
        /// 都会让父流程必须"管"子流程，一有故障整线就停——那正是要避免的形态。
        /// </summary>
        LineStart = 128,
    }

    /// <summary>
    /// 流程在方案里的<b>固定角色</b>：Home / End 两条流程是每个方案的强制骨架
    /// （用户决策：所有程序都要有回零与收尾）。
    ///
    /// 【为什么没有"主流程"】2026-10-28 用户裁决：生产主任务不再是"角色"，而是
    /// <see cref="FlowInvokeType.LineStart"/> 这一个调用位——任何流程（可多条）都能随
    /// 「启动整线」各起各的会话，工位与主任务平级。原 <c>Main = 2</c> 已退役，
    /// 数值 2 永久保留不复用；老方案加载时由 <see cref="MandatoryFlows.MigrateRetiredRoles"/>
    /// 迁移成"无角色 + 勾整线"。
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
        [Display(Name = "回零（Home）", ShortName = "回零")]
        Home = 1,

        /// <summary>
        /// 保留位：原「主任务（Main）」。**已退役，不许复用**——
        /// 老 .vms 里 <c>Role: 2</c> 靠这个数值被识别并迁移，回收它就等于把老文件读成另一条流程。
        /// 运行期不会出现该值：加载时已迁移成 <see cref="None"/> + <see cref="FlowInvokeType.LineStart"/>。
        /// </summary>
        [Obsolete("主流程角色已退役（2026-10-28）：请改用 FlowInvokeType.LineStart")]
        RetiredMain = 2,

        /// <summary>收尾：停机 / 换产前把机构与状态收干净</summary>
        [Display(Name = "收尾（End）", ShortName = "收尾")]
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
            if ((type & FlowInvokeType.LineStart) != 0) parts.Add("整线");

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
            //  · LineStart   → FlowBatchSelector（工具栏「启动整线」按这一位选流程，2026-10-28）。
            // 将来再加位、而运行侧没跟上时：在这里给它一句话，接上了把这句删掉（界面自动跟着变）。
            _ => null,
        };
    }

    /// <summary>
    /// 流程结果源：决定宿主从哪读本流程的 OK/NG（L2 `FlowVerdictResolver`）。
    ///
    /// Auto 的优先级：结果输出步骤 &gt; 配置的结果变量 &gt; 配置的步骤端口 &gt; 约定变量
    /// <c>&lt;流程名&gt;_Result</c> &gt; 未判定。显式模式只认该来源，读不到就记未判定。
    /// </summary>
    public enum FlowResultSourceKind
    {
        Auto = 0,
        ResultStep = 1,
        Variable = 2,
        StepPort = 3,
        None = 4,
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
        /// 流程角色：Home / End 两条骨架流程在方案里各占一角（见 <see cref="FlowRole"/>）。
        /// 普通流程为 None。加载老方案时由 MandatoryFlows.Ensure 按名字补齐缺的角色；
        /// 带角色的流程不允许删除（FlowListViewModel 拦截），改名不受限（判据是角色不是名字）。
        /// </summary>
        public FlowRole Role
        {
            get { return field; }
            set
            {
                if (SetProperty(ref field, value))
                    RaiseCallVisualChanged();
            }
        }

        /// <summary>
        /// 调用类型
        /// </summary>
        public FlowInvokeType InvokeType
        {
            get { return field; }
            set
            {
                if (SetProperty(ref field, value))
                    RaiseCallVisualChanged();
            }
        }

        /// <summary>结果源模式（见 <see cref="FlowResultSourceKind"/>）；默认自动。</summary>
        public FlowResultSourceKind ResultSource
        {
            get => field;
            set => SetProperty(ref field, value);
        } = FlowResultSourceKind.Auto;

        /// <summary>Variable 模式的结果变量 Id（优先于名字；改名不丢）</summary>
        public Guid ResultVariableId
        {
            get => field;
            set => SetProperty(ref field, value);
        } = Guid.Empty;

        /// <summary>Variable 模式的结果变量名（Id 为空时按名解析）</summary>
        public string ResultVariableName
        {
            get => field;
            set => SetProperty(ref field, value ?? string.Empty);
        } = string.Empty;

        /// <summary>StepPort 模式的判定步骤 Id</summary>
        public Guid ResultStepId
        {
            get => field;
            set => SetProperty(ref field, value);
        } = Guid.Empty;

        /// <summary>StepPort 模式的 bool 输出端口名（如 IsOk）</summary>
        public string ResultPortName
        {
            get => field;
            set => SetProperty(ref field, value ?? string.Empty);
        } = string.Empty;

        /// <summary>
        /// 是否可由工具栏「启动整线 / 整线循环」直接启动。
        /// 判据只有一条：勾了「随整线启动」（<see cref="FlowInvokeType.LineStart"/>）。
        /// 2026-10-28 起不再有"主流程角色也算"这条——任何流程（可多条）平级启动。
        /// </summary>
        [JsonIgnore]
        public bool IsDirectlyLoopable => IsLineStartMember;

        /// <summary>
        /// 是否勾了「随整线启动」：工具栏那一个按钮会把它和其它同样勾选的流程各起各的会话。
        /// 与 <see cref="HasExternalTrigger"/> 分开：整线启动是本机按钮，不是外部触发。
        /// </summary>
        [JsonIgnore]
        public bool IsLineStartMember => (InvokeType & FlowInvokeType.LineStart) != 0;

        /// <summary>是否有除「子程序」以外的直接触发方式（定时 / 变量 / HTTP）。</summary>
        [JsonIgnore]
        public bool HasExternalTrigger
            => (InvokeType & (FlowInvokeType.Timer | FlowInvokeType.Variable | FlowInvokeType.Http)) != 0;

        /// <summary>
        /// 是否只能作为子程序被其它流程调用：勾了子程序、且没有外部触发、
        /// 也不是整线成员。最后一条必须有：工位流程通常同时勾「子程序」（供「调用流程」复用）
        /// 与「随整线启动」，漏判会让它在列表里被标成"只能被调用"，用户就不敢按整线启动了。
        /// </summary>
        [JsonIgnore]
        public bool IsSubroutineOnly
            => (InvokeType & FlowInvokeType.Subroutine) != 0
               && !HasExternalTrigger
               && !IsLineStartMember;

        /// <summary>流程列表用的短徽标：循环 / 外部 / 子；空串表示仅手动。</summary>
        [JsonIgnore]
        public string CallBadgeText
        {
            get
            {
                if (IsDirectlyLoopable) return "循环";
                if (HasExternalTrigger) return "外部";
                if (IsSubroutineOnly) return "子";
                return string.Empty;
            }
        }

        /// <summary>短徽标的完整解释（ToolTip）。</summary>
        [JsonIgnore]
        public string CallBadgeToolTip
        {
            get
            {
                if (IsDirectlyLoopable)
                    return "整线成员：工具栏「启动整线」会把它和其它勾选本位的流程一起启动（各起各的会话）";
                if (HasExternalTrigger)
                    return "可直接触发：" + InvokeType.DisplayText();
                if (IsSubroutineOnly)
                    return "子程序：只能由其它流程的「调用流程」拉起";
                return "手动：仅在本机界面手动运行";
            }
        }

        /// <summary>流程管理表格用的完整说明。</summary>
        [JsonIgnore]
        public string CallSummaryText
        {
            get
            {
                if (IsDirectlyLoopable) return "整线成员 · 随整线启动";
                if (HasExternalTrigger) return "外部触发";
                if (IsSubroutineOnly) return "子程序 · 由其它流程调用";
                return "手动";
            }
        }

        private void RaiseCallVisualChanged()
        {
            RaisePropertyChanged(nameof(IsDirectlyLoopable));
            RaisePropertyChanged(nameof(IsLineStartMember));
            RaisePropertyChanged(nameof(HasExternalTrigger));
            RaisePropertyChanged(nameof(IsSubroutineOnly));
            RaisePropertyChanged(nameof(CallBadgeText));
            RaisePropertyChanged(nameof(CallBadgeToolTip));
            RaisePropertyChanged(nameof(CallSummaryText));
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
        /// 深度判定本流程是否存在断点（含并行分支 / 循环体 / If 分支里的嵌套步骤）。
        ///
        /// 用途：界面"运行"入口据此决定是否按调试会话启动（方案 A，2026-10-10）——
        /// 没有断点 = 普通运行（并行组真并发）；有断点 = 自动按调试运行（装调试门，
        /// 断点/单步可用，代价是并行组就地退化顺序执行，见 FlowSession.DebugDegradedParallel）。
        /// 容器判定一律走 IContainerStep（与 FlowTopology 同一口径，别退化成判具体容器类型）。
        /// </summary>
        public bool HasBreakpoint() => HasBreakpointDeep(_steps);

        private static bool HasBreakpointDeep(IEnumerable<StepModel> steps)
        {
            if (steps == null)
                return false;

            foreach (var step in steps)
            {
                if (step == null)
                    continue;

                if (step.IsBreakpoint)
                    return true;

                if (step is IContainerStep container && container.Children != null)
                {
                    foreach (var branch in container.Children)
                    {
                        if (branch?.Steps != null && HasBreakpointDeep(branch.Steps))
                            return true;
                    }
                }
            }

            return false;
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
        /// </summary>
        private readonly HashSet<INotifyCollectionChanged> _watchedCollections = new();

        /// <summary>
        /// 按当前流程树重扫订阅：从顶层集合出发收集所有可达的步骤集合、分支集合与步骤，
        /// 与已订阅名单求差后摘挂。
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
    }
}
