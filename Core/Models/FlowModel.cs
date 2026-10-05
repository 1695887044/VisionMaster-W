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
    /// 流程调用类型枚举
    /// </summary>
    public enum FlowInvokeType
    {
        /// <summary>手动调用</summary>
        Manual = 0,
        /// <summary>定时调用</summary>
        Timer = 1,
        /// <summary>变量触发调用</summary>
        Variable = 2,
        /// <summary>作为子程序调用</summary>
        Subroutine = 3
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
