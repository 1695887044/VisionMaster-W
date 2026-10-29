using Prism.Commands;
using Prism.Mvvm;
using Prism.Dialogs;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Windows;
using VisionMaster.Helpers;
using VisionMaster.Models;
using VisionMaster.Services;
using Core.Events;
using UI.CustomControl;

namespace VisionMaster.ViewModels.DialogViewModels
{
    /// <summary>
    /// 流程管理ViewModel
    ///
    /// 设计基线：<c>docs/UI设计基线/flow-manager/</c> v1.1（2026-10-28 用户裁定 Q1~Q5 + A 方案）。
    /// 本版相对旧实现的结构性改动（判据都在基线 README 里）：
    ///  · 调用方式 5 个复选框 → 5 个可点胶囊（绑定仍是 5 个独立 bool，位集与落盘不变）；
    ///  · 删除「调用关系」列（取值完全由 InvokeType 位集派生，信息并入胶囊颜色 + 角色徽标）；
    ///  · 操作列按状态收敛：锁定/解锁合成一个图标键，运行中两个键都不可点（Q1）；
    ///  · 说明面板折叠（Q2）；搜索无结果给空态；
    ///  · 新增「未配齐」提示 + A 方案配置入口（勾了定时/变量却没配参数 → 琥珀提示 + 齿轮小窗）。
    /// </summary>
    public class FlowManagerViewModel : BindableBase, IDialogAware
    {
        private ObservableCollection<FlowItem> _flows = new();
        private FlowItem? _selectedFlow;
        private string _searchText = string.Empty;
        private bool _isLegendExpanded;
        private ObservableCollection<FlowModel>? _originalFlows;

        public DialogCloseListener RequestClose { get; set; }

        /// <summary>
        /// 流程列表
        /// </summary>
        public ObservableCollection<FlowItem> Flows
        {
            get => _flows;
            set
            {
                if (_flows != null) _flows.CollectionChanged -= OnFlowsCollectionChanged;
                if (SetProperty(ref _flows, value) && _flows != null)
                {
                    _flows.CollectionChanged += OnFlowsCollectionChanged;
                    RaiseListStateChanged();
                }
            }
        }

        /// <summary>
        /// 选中的流程
        /// </summary>
        public FlowItem? SelectedFlow
        {
            get => _selectedFlow;
            set => SetProperty(ref _selectedFlow, value);
        }

        /// <summary>
        /// 搜索文本
        /// </summary>
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    FilterFlows();
                    RaisePropertyChanged(nameof(HasSearchText));
                }
            }
        }

        /// <summary>搜索框右侧清除键是否显示（空串时收起）</summary>
        public bool HasSearchText => !string.IsNullOrEmpty(SearchText);

        /// <summary>列表为空（搜索无结果时可达；无流程不可达，见基线 §6）</summary>
        public bool IsEmpty => Flows.Count == 0;

        /// <summary>空态标题：带关键词，用户才知道是"搜错了"而不是"列表坏了"</summary>
        public string EmptyStateTitle => string.IsNullOrWhiteSpace(SearchText)
            ? "当前方案没有流程"
            : $"没有匹配「{SearchText.Trim()}」的流程";

        /// <summary>工具栏计数</summary>
        public string FlowCountText => $"{Flows.Count} 条";

        /// <summary>说明面板是否展开（默认收起，Q2）</summary>
        public bool IsLegendExpanded
        {
            get => _isLegendExpanded;
            set
            {
                if (SetProperty(ref _isLegendExpanded, value))
                    RaisePropertyChanged(nameof(LegendToggleText));
            }
        }

        /// <summary>说明面板展开键文案</summary>
        public string LegendToggleText => IsLegendExpanded ? "收起" : "展开";

        /// <summary>
        /// 所有流程（原始数据）
        /// </summary>
        private ObservableCollection<FlowItem> _allFlows = new();

        /// <summary>
        /// 加密命令（锁定：禁止运行，不改变文件内容）
        /// </summary>
        public DelegateCommand<FlowItem> EncryptFlowCommand { get; }

        /// <summary>
        /// 解密命令（解锁：恢复可运行）
        /// </summary>
        public DelegateCommand<FlowItem> DecryptFlowCommand { get; }

        /// <summary>
        /// 启用/禁用流程命令
        /// </summary>
        public DelegateCommand<FlowItem> ToggleEnabledCommand { get; }

        /// <summary>
        /// 调用参数配置命令（A 方案：行内齿轮 → EasyDialog 属性网格小窗）
        /// 参数是 FlowItem；用 object 型并按 R27 首行判型（绑定来源是 DataGrid 的 DataContext，
        /// 编译期无法证明类型）。
        /// </summary>
        public DelegateCommand<object?> ConfigureInvokeCommand { get; }

        /// <summary>
        /// 展开/收起说明面板
        /// </summary>
        public DelegateCommand ToggleLegendCommand { get; }

        /// <summary>
        /// 清空搜索
        /// </summary>
        public DelegateCommand ClearSearchCommand { get; }

        /// <summary>
        /// 关闭命令
        /// </summary>
        public DelegateCommand CloseCommand { get; }

        public string Title => "流程管理";

        /// <summary>
        /// 构造函数
        /// </summary>
        public FlowManagerViewModel(WorkspaceContext workspace)
        {
            _workspace = workspace;
            EncryptFlowCommand = new DelegateCommand<FlowItem>(ExecuteEncryptFlow);
            DecryptFlowCommand = new DelegateCommand<FlowItem>(ExecuteDecryptFlow);
            ToggleEnabledCommand = new DelegateCommand<FlowItem>(ExecuteToggleEnabled);
            ConfigureInvokeCommand = new DelegateCommand<object?>(ExecuteConfigureInvoke);
            ToggleLegendCommand = new DelegateCommand(() => IsLegendExpanded = !IsLegendExpanded);
            ClearSearchCommand = new DelegateCommand(() => SearchText = string.Empty);
            CloseCommand = new DelegateCommand(ExecuteClose);

            _flows.CollectionChanged += OnFlowsCollectionChanged;

            // 常驻面板模式：跟随工作区当前方案自动加载流程列表
            _workspace.PropertyChanged += OnWorkspacePropertyChanged;
            LoadFlowsFromWorkspace();
        }

        private readonly WorkspaceContext _workspace;

        private void OnWorkspacePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(WorkspaceContext.CurrentSolution))
            {
                LoadFlowsFromWorkspace();
            }
        }

        private void OnFlowsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
            => RaiseListStateChanged();

        /// <summary>列表级派生状态一起重发（计数 / 空态 / 空态标题）</summary>
        private void RaiseListStateChanged()
        {
            RaisePropertyChanged(nameof(IsEmpty));
            RaisePropertyChanged(nameof(FlowCountText));
            RaisePropertyChanged(nameof(EmptyStateTitle));
        }

        /// <summary>
        /// 从当前方案加载流程列表（方案未加载时清空列表）
        /// </summary>
        private void LoadFlowsFromWorkspace()
        {
            var flows = _workspace.CurrentSolution?.Flows;
            if (flows != null)
            {
                LoadFlows(flows);
            }
            else
            {
                _allFlows.Clear();
                Flows.Clear();
            }
        }

        public bool CanCloseDialog() => true;

        public void OnDialogClosed()
        {
        }

        public void OnDialogOpened(IDialogParameters parameters)
        {
            if (parameters.ContainsKey("Flows"))
            {
                _originalFlows = parameters.GetValue<ObservableCollection<FlowModel>>("Flows");
                LoadFlows(_originalFlows);
            }
        }

        /// <summary>
        /// 加载流程列表
        /// </summary>
        /// <param name="flows">流程集合</param>
        public void LoadFlows(ObservableCollection<FlowModel> flows)
        {
            _allFlows.Clear();
            Flows.Clear();

            foreach (var flow in flows)
            {
                var item = new FlowItem(flow);
                _allFlows.Add(item);
                Flows.Add(item);
            }
        }

        /// <summary>
        /// 过滤流程列表
        /// </summary>
        private void FilterFlows()
        {
            Flows.Clear();

            foreach (var flow in _allFlows)
            {
                if (string.IsNullOrWhiteSpace(SearchText) ||
                    flow.Flow.FlowName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                    (flow.Flow.Description ?? string.Empty).Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                {
                    Flows.Add(flow);
                }
            }
        }

        // ==================================================================
        //  运行中守卫（Q1：用户裁定"要补上对应的行为"）
        //  R136 口径：按下前判（行内 IsEnabled 绑 CanToggle/CanLock）+ 命令执行前再判一次。
        //  为什么执行前还要判：按钮 IsEnabled 是绑定的，从"按下"到"命令执行"之间流程可能刚被
        //  定时器拉起来——只靠绑定会漏掉这个窗口。
        // ==================================================================

        /// <summary>运行中禁止变更锁定/启用状态；返回 true = 已拦截并提示</summary>
        private bool RejectWhileRunning(FlowItem item, string action)
        {
            if (item.Flow.RunState != FlowRunState.Running) return false;
            Notifier.ShowWarning($"流程 [{item.Flow.FlowName}] 正在运行，无法{action}；请先停止");
            return true;
        }

        /// <summary>
        /// 加密流程（锁定：禁止运行）
        /// </summary>
        private void ExecuteEncryptFlow(FlowItem? item)
        {
            if (item == null) return;
            if (RejectWhileRunning(item, "锁定")) return;

            if (item.Flow.StepsEncrypted)
            {
                Notifier.ShowWarning("该流程已锁定（禁止运行）");
                return;
            }

            try
            {
                string key = EncryptionHelper.GenerateKey();
                item.Flow.EncryptedKey = key;
                item.Flow.StepsEncrypted = true;
                item.Refresh();

                // 如实说明：这里的"锁定"只表达"禁止运行"，步序内容仍以明文随方案落盘
                // （真正的机密性保护另立一期：落盘加密 + 密钥管理）
                Notifier.ShowSuccess($"流程 [{item.Flow.FlowName}] 已锁定：禁止运行（注意：文件内容仍是明文）");
            }
            catch (Exception ex)
            {
                Notifier.ShowError($"加密失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 解密流程（解锁：恢复可运行）
        /// </summary>
        private void ExecuteDecryptFlow(FlowItem? item)
        {
            if (item == null) return;
            if (RejectWhileRunning(item, "解锁")) return;

            if (!item.Flow.StepsEncrypted)
            {
                Notifier.ShowWarning("该流程未锁定");
                return;
            }

            try
            {
                item.Flow.EncryptedKey = null;
                item.Flow.StepsEncrypted = false;
                item.Refresh();
                
                Notifier.ShowSuccess($"流程 [{item.Flow.FlowName}] 已解锁：恢复可运行");
            }
            catch (Exception ex)
            {
                Notifier.ShowError($"解密失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 切换启用状态
        /// </summary>
        private void ExecuteToggleEnabled(FlowItem? item)
        {
            if (item == null) return;
            if (RejectWhileRunning(item, "启用/禁用")) return;

            item.Flow.IsEnabled = !item.Flow.IsEnabled;
            item.Refresh();
        }

        /// <summary>
        /// 调用参数配置（A 方案）。
        ///
        /// 【为什么这里才有意义】「定时」读 FlowModel.TimerIntervalMs、「变量」读 TriggerVariable，
        /// 两者此前**没有任何界面入口**（全仓 XAML 零绑定），而 ShouldFire 把 0 判成"没配间隔"
        /// 直接不触发、EvaluateCore 的空串永不匹配变量名——勾了等于没勾，且界面不提示。
        /// 基线 §1.8 / §9.2 的 Q6 即此。
        ///
        /// 【为什么用 EasyDialog.ShowPropertyGridSync】与并行分组参数同一条路（R26 / 基线 A 方案）：
        /// 弹窗自己建窗口、自己收口成 bool，不需要新的 Prism 视图注册名，也不需要新 XAML。
        /// </summary>
        private void ExecuteConfigureInvoke(object? parameter)
        {
            if (parameter is not FlowItem item) return;
            if (RejectWhileRunning(item, "修改调用参数")) return;

            var candidates = _workspace.CurrentSolution?.GlobalVariables?
                .Select(v => v.Name) ?? Enumerable.Empty<string>();

            var draft = new InvokeParamEditModel(item.Flow, candidates);
            if (!EasyDialog.ShowPropertyGridSync($"调用参数 · {item.Flow.FlowName}", draft))
                return;   // 取消 = 草稿整份丢弃，活模型一个字节都不动

            draft.ApplyToModel();
            item.Refresh();
        }

        /// <summary>
        /// 关闭
        /// </summary>
        private void ExecuteClose()
        {
            var parameters = new DialogParameters();
            RequestClose.Invoke(parameters, ButtonResult.OK);
        }
    }

    /// <summary>
    /// 流程列表项
    /// </summary>
    public class FlowItem : BindableBase
    {
        private readonly FlowModel _flow;

        /// <summary>
        /// 流程模型
        /// </summary>
        public FlowModel Flow => _flow;

        /// <summary>
        /// 调用方式文本（领域层单一口径：无自动触发显示「手动」，否则按勾选拼接）
        /// 基线 v1.1 起表格不再显示这一行（与胶囊重复），保留给日志/其它消费方复用。
        /// </summary>
        public string InvokeTypeText => _flow.InvokeType.DisplayText();

        // ------------------------------------------------------------------
        //  调用方式：多选胶囊（直接改流程模型，落盘为位集）
        //  五位全部接通：HTTP（/flow/ 门禁）、定时（FlowTimerScheduler）、变量（上升沿触发）、
        //  子程序（「调用流程」插件 + FlowInvoker）、整线（FlowBatchSelector）。
        //  PendingReasonText 仍保留：将来加位而运行侧没跟上时，它把"哪一位还没接通"显示出来，
        //  避免"勾了没反应 = 软件坏了"（当前五位全接通 → 返回空串，界面自动收起那一行）。
        // ------------------------------------------------------------------

        public bool IsTimerInvoke
        {
            get => HasFlag(FlowInvokeType.Timer);
            set => SetFlag(FlowInvokeType.Timer, value);
        }

        public bool IsVariableInvoke
        {
            get => HasFlag(FlowInvokeType.Variable);
            set => SetFlag(FlowInvokeType.Variable, value);
        }

        public bool IsSubroutineInvoke
        {
            get => HasFlag(FlowInvokeType.Subroutine);
            set => SetFlag(FlowInvokeType.Subroutine, value);
        }

        /// <summary>HTTP 外部调用：唯一带门禁的位——没勾这里，/flow/{流程名} 一律 403</summary>
        public bool IsHttpInvoke
        {
            get => HasFlag(FlowInvokeType.Http);
            set => SetFlag(FlowInvokeType.Http, value);
        }

        /// <summary>
        /// 随整线启动：工具栏「启动整线」把它和其它同样勾了本位的流程各起各的会话一起启动。
        /// 工位与主流程平级——一条出问题只停它自己，其余照常循环（2026-10-28 用户裁决）。
        /// </summary>
        public bool IsLineStartInvoke
        {
            get => HasFlag(FlowInvokeType.LineStart);
            set => SetFlag(FlowInvokeType.LineStart, value);
        }

        /// <summary>
        /// 已勾选但运行侧还没接通的调用位的一句话原因（全部接通或没勾时为"[]"）。
        /// 界面把它挂在胶囊的行尾，用户看到"待接通"就不会误以为勾了没生效是坏了。
        /// </summary>
        public string PendingReasonText
        {
            get
            {
                var reasons = new List<string>(3);
                foreach (var flag in new[] { FlowInvokeType.Timer, FlowInvokeType.Variable, FlowInvokeType.Subroutine, FlowInvokeType.LineStart })
                {
                    if (HasFlag(flag) && flag.PendingReason() is { } reason)
                        reasons.Add(reason);
                }
                return reasons.Count == 0 ? string.Empty : string.Join("；", reasons);
            }
        }

        /// <summary>
        /// 历史数据里的矛盾配置：整线 + 子程序同时勾。
        ///
        /// 2026-10-29 起界面互斥（<see cref="SetFlag"/> 勾一个清另一个），新配的数据不会再出现；
        /// 但旧 .vms 可能带着这个组合，必须显性说出来——否则两条胶囊都亮着，用户看不出
        /// "为什么整线在跑时 Main 调不动它"。修正方式就是点一下任一胶囊（互斥逻辑会清掉另一位）。
        /// </summary>
        public string InvokeConflictNote
            => HasFlag(FlowInvokeType.LineStart) && HasFlag(FlowInvokeType.Subroutine)
                ? "整线与子程序互斥：整线成员自己起会话循环，不能再被「调用流程」调用（点一下任一胶囊即可修正）"
                : string.Empty;

        /// <summary>
        /// 调用方式那一行显示的警告文本：矛盾配置优先于"未配齐"。
        /// 两者都是"配了但不生效"，但矛盾会让另一条流程也连带失败，先说它。
        /// </summary>
        public string InvokeWarningText
            => InvokeConflictNote.Length > 0 ? InvokeConflictNote : InvokeConfigNote;

        /// <summary>
        /// 勾了「定时」/「变量」但缺参数 → 这一行永远不会触发（基线 §1.8）。
        ///
        /// 与 <see cref="PendingReasonText"/> 的分工：那个回答"这一位有没有接通运行侧"，
        /// 这个回答"这一条流程有没有把参数配齐"。两者都为空才是真的能跑。
        /// 判据与运行侧严格同源：ShouldFire 的 TimerIntervalMs &lt;= 0、EvaluateCore 的
        /// TriggerVariable 空串比较——这里不另发明一套。
        /// </summary>
        public string InvokeConfigNote
        {
            get
            {
                var missing = new List<string>(2);
                if (HasFlag(FlowInvokeType.Timer) && _flow.TimerIntervalMs <= 0)
                    missing.Add("定时未设间隔");
                if (HasFlag(FlowInvokeType.Variable) && string.IsNullOrWhiteSpace(_flow.TriggerVariable))
                    missing.Add("变量未选触发源");
                return missing.Count == 0 ? string.Empty : string.Join(" · ", missing) + "，不会触发";
            }
        }

        /// <summary>
        /// 派生小字（中性色）：删掉「调用关系」列之后，"仅子程序"与"手动"这两种情况
        /// 没有别的表达——它们不是异常，只是提醒用户"这条流程自己不会跑起来"。
        ///
        /// 判据全部来自领域层，不在视图里重算：
        ///  · <see cref="FlowModel.IsSubroutineOnly"/>：非主流程 + 勾了子程序 + 无外部触发 + 非整线成员；
        ///  · <c>InvokeType == Manual</c>：一个位都没勾。
        /// </summary>
        public string DerivedNote
        {
            get
            {
                // 有警告时让位（矛盾配置 / 未配齐）：同一时刻只出一行，行高不被撑成两行
                if (InvokeWarningText.Length > 0) return string.Empty;
                if (_flow.IsSubroutineOnly) return "仅子程序";
                if (_flow.InvokeType == FlowInvokeType.Manual) return "手动";
                return string.Empty;
            }
        }

        /// <summary>是否需要显示配置入口齿轮（勾了定时或变量才显示）</summary>
        public bool NeedsInvokeParams =>
            HasFlag(FlowInvokeType.Timer) || HasFlag(FlowInvokeType.Variable);

        private bool HasFlag(FlowInvokeType flag) => (_flow.InvokeType & flag) != 0;

        /// <summary>
        /// 设置一位调用方式。**「整线」与「子程序」互斥**（2026-10-29 用户裁决）：
        /// 勾整线自动清子程序，勾子程序自动清整线。
        ///
        /// 【为什么必须互斥】运行期一个流程名只有一个会话（RuntimeManager 按名唯一），
        /// 执行权是一把会话锁（FlowEngineService.TryOccupySession）。两个入口同时勾时：
        /// 整线在跑 → 「调用流程」必然失败（FlowInvoker 见 IsRunning 直接拒）；
        /// Main 正在调它 → 点「启动整线」又被 ShellViewModel 的 IsRunning 判据静默跳过。
        /// 即"两条路都配上了，但任何时刻只有一条能用，另一条要么报错要么无声跳过"——
        /// 现场不会去看这段推理，只会说"软件时好时坏"，所以矛盾配置根本不该配得出来。
        /// </summary>
        private void SetFlag(FlowInvokeType flag, bool on)
        {
            var next = on
                ? (_flow.InvokeType | flag)
                : (_flow.InvokeType & ~flag);

            if (on && flag == FlowInvokeType.LineStart)
                next &= ~FlowInvokeType.Subroutine;
            else if (on && flag == FlowInvokeType.Subroutine)
                next &= ~FlowInvokeType.LineStart;

            _flow.InvokeType = next;
            Refresh();
            // 五个胶囊互相独立，但显示文本 / 待接通 / 未配齐提示由整体位集派生：一次改动一并重发
            RaisePropertyChanged(nameof(IsTimerInvoke));
            RaisePropertyChanged(nameof(IsVariableInvoke));
            RaisePropertyChanged(nameof(IsSubroutineInvoke));
            RaisePropertyChanged(nameof(IsHttpInvoke));
            RaisePropertyChanged(nameof(IsLineStartInvoke));
            RaisePropertyChanged(nameof(PendingReasonText));
            RaisePropertyChanged(nameof(InvokeConfigNote));
            RaisePropertyChanged(nameof(InvokeConflictNote));
            RaisePropertyChanged(nameof(InvokeWarningText));
            RaisePropertyChanged(nameof(DerivedNote));
            RaisePropertyChanged(nameof(NeedsInvokeParams));
        }

        // ------------------------------------------------------------------
        //  角色徽标（基线 §1.2：删掉「调用关系」列后，骨架角色由名称旁徽标表达）
        //  文案来自 FlowRole 的 [Display(ShortName=…)]，不在视图里写死中文（R40）。
        //  2026-10-28 起没有"主流程"角色：生产主任务由「调用方式」里的「整线」表达。
        // ------------------------------------------------------------------

        /// <summary>角色徽标短名（回零 / 收尾）；无角色时为空串，徽标整体收起</summary>
        public string RoleBadgeText =>
            FlowRoleNames.TryGetValue(_flow.Role, out var name) ? name.Short : string.Empty;

        /// <summary>角色徽标的样式键后缀（home / end）；无角色时为空串</summary>
        public string RoleBadgeKey => _flow.Role switch
        {
            FlowRole.Home => "home",
            FlowRole.End => "end",
            _ => string.Empty,
        };

        /// <summary>角色徽标 ToolTip：完整名 + 该角色的约束</summary>
        public string RoleBadgeToolTip => _flow.Role switch
        {
            FlowRole.Home => "回零（Home）：开机与换产时的安全位姿流程；每个方案必须保留，不允许删除（可改名）",
            FlowRole.End => "收尾（End）：停机 / 换产前把机构与状态收干净；每个方案必须保留，不允许删除（可改名）",
            _ => string.Empty,
        };

        private static readonly Dictionary<FlowRole, (string Short, string Full)> FlowRoleNames = BuildRoleNames();

        private static Dictionary<FlowRole, (string Short, string Full)> BuildRoleNames()
        {
            var map = new Dictionary<FlowRole, (string, string)>();
            foreach (FlowRole role in Enum.GetValues(typeof(FlowRole)))
            {
                if (role == FlowRole.None) continue;
                var attr = typeof(FlowRole)
                    .GetField(role.ToString(), BindingFlags.Public | BindingFlags.Static)
                    ?.GetCustomAttribute<DisplayAttribute>();
                var shortName = attr?.ShortName;
                if (string.IsNullOrWhiteSpace(shortName)) continue;
                map[role] = (shortName!, attr!.Name ?? role.ToString());
            }
            return map;
        }

        // ------------------------------------------------------------------
        //  状态（基线 §1.3）：四态，只有异常态给底色
        // ------------------------------------------------------------------

        /// <summary>已锁定（步序加密 = 禁止运行）；锁定优先级最高，压过运行/启用</summary>
        public bool IsLocked => _flow.StepsEncrypted;

        /// <summary>正在运行</summary>
        public bool IsRunning => _flow.RunState == FlowRunState.Running;

        /// <summary>状态文本（基线四态，比旧版短：旧的"🔒 已锁定（禁止运行）"在 120px 列里放不下）</summary>
        public string StatusText
        {
            get
            {
                if (_flow.StepsEncrypted) return "已锁定";
                if (_flow.RunState == FlowRunState.Running) return "运行中";
                return _flow.IsEnabled ? "已启用" : "已禁用";
            }
        }

        /// <summary>
        /// 状态样式键：lock / run / on / off。
        /// 用独立属性而不是拿 StatusText 做 DataTrigger 比较——文案会改（这一版就改了），
        /// 样式不该跟着文案漂。
        /// </summary>
        public string StatusKind
        {
            get
            {
                if (_flow.StepsEncrypted) return "lock";
                if (_flow.RunState == FlowRunState.Running) return "run";
                return _flow.IsEnabled ? "on" : "off";
            }
        }

        // ------------------------------------------------------------------
        //  操作列可用性（Q1：运行中禁止变更锁定/启用）
        // ------------------------------------------------------------------

        /// <summary>启用/禁用键是否可点</summary>
        public bool CanToggle => !IsRunning;

        /// <summary>锁定/解锁键是否可点</summary>
        public bool CanLock => !IsRunning;

        /// <summary>
        /// 行内操作区 ToolTip。
        /// 不返回空串：空串 ToolTip 在 WPF 里仍会弹一个空框。运行中解释"为什么点不了"，
        /// 平时解释"这两个键各改什么"——两条都有人会问。
        /// </summary>
        public string OpsToolTip => IsRunning
            ? "流程正在运行，锁定 / 启用状态不可变更；请先停止"
            : "锁定 = 该流程禁止运行（不改文件内容）；启用 / 禁用 = 是否参与自动调用";

        /// <summary>
        /// 启用状态文本
        /// </summary>
        public string EnabledText => _flow.IsEnabled ? "禁用" : "启用";

        /// <summary>启用/禁用键的语义（true = 恢复动作，用主色）</summary>
        public bool IsEnableAction => !_flow.IsEnabled;

        /// <summary>
        /// 构造函数
        /// </summary>
        public FlowItem(FlowModel flow)
        {
            _flow = flow;
            _flow.PropertyChanged += (s, e) => Refresh();
        }

        /// <summary>刷新显示（流程模型任何一处变更都会走到这里：胶囊 / 状态 / 角色 / 派生提示一起重发）</summary>
        public void Refresh()
        {
            RaisePropertyChanged(nameof(InvokeTypeText));
            RaisePropertyChanged(nameof(StatusText));
            RaisePropertyChanged(nameof(StatusKind));
            RaisePropertyChanged(nameof(EnabledText));
            RaisePropertyChanged(nameof(IsEnableAction));
            RaisePropertyChanged(nameof(IsLocked));
            RaisePropertyChanged(nameof(IsRunning));
            RaisePropertyChanged(nameof(CanToggle));
            RaisePropertyChanged(nameof(CanLock));
            RaisePropertyChanged(nameof(OpsToolTip));
            RaisePropertyChanged(nameof(IsTimerInvoke));
            RaisePropertyChanged(nameof(IsVariableInvoke));
            RaisePropertyChanged(nameof(IsSubroutineInvoke));
            RaisePropertyChanged(nameof(IsHttpInvoke));
            RaisePropertyChanged(nameof(IsLineStartInvoke));
            RaisePropertyChanged(nameof(PendingReasonText));
            RaisePropertyChanged(nameof(InvokeConfigNote));
            RaisePropertyChanged(nameof(InvokeConflictNote));
            RaisePropertyChanged(nameof(InvokeWarningText));
            RaisePropertyChanged(nameof(DerivedNote));
            RaisePropertyChanged(nameof(NeedsInvokeParams));
            RaisePropertyChanged(nameof(RoleBadgeText));
            RaisePropertyChanged(nameof(RoleBadgeKey));
            RaisePropertyChanged(nameof(RoleBadgeToolTip));
        }
    }
}
