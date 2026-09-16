using Core.Interfaces;
using GongSolutions.Wpf.DragDrop;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using UI.CustomControl;
using Core.Events;
using VisionMaster.Models;
using VisionMaster.Services;

namespace VisionMaster.ViewModels
{
    public class ProcessViewModel : BindableBase, IDropTarget
    {
        private readonly IDialogService dialogService;
        public bool IsIfNodeSelected =>
            SelectStep is ConditionStep step && step.PluginName.Contains("If");

        public bool IsSwitchNodeSelected =>
            SelectStep is ConditionStep step && step.PluginName.Contains("Switch");
        public IWorkspaceManager Workspace { get; init; }

        /// <summary>
        /// 构造时的 Workspace INotifyPropertyChanged 引用，Dispose 时用于解绑。
        /// IWorkspaceManager 是业务接口不继承 INotifyPropertyChanged，只有运行时实例才是 INPC。
        /// </summary>
        private readonly INotifyPropertyChanged? _workspaceChanged;

        public AsyncDelegateCommand<ModuleCommandAction?> ModuleActionCommand { get; init; }
        public object SelectStep
        {
            get => field;
            set
            {
                if (value is StepCollection)
                {
                    return;
                }

                SetProperty(ref field, value);
                CurrentSelectedStepModel = value as StepModel;
                RaisePropertyChanged(nameof(IsIfNodeSelected));
                RaisePropertyChanged(nameof(IsSwitchNodeSelected));

                // 反向推送来的值本来就是 Workspace 自己发出来的，再 SwitchStep 一次纯属回环
                if (!_syncingFromWorkspace)
                    Workspace.SwitchStep(CurrentSelectedStepModel);
            }
        }
        StepModel CurrentSelectedStepModel;

        /// <summary>
        /// true = 正在把 Workspace.CurrentStep 推给 SelectStep。
        /// 联动只有一条总线：Workspace.CurrentStep。画布（或任何编辑端）改当前步骤 → 这里收到通知
        /// → 更新 SelectStep → TreeViewBehavior 的双向绑定把流程树选中并逐级展开过去。
        /// 反向（流程树点选 → SwitchStep）走 SelectStep 的 setter，两条路径靠这个标志互相隔断。
        /// </summary>
        private bool _syncingFromWorkspace;

        /// <summary>
        /// 运行状态镜像：由 ShellViewModel 经 GlobalEventBus 广播同步。
        /// 运行中锁住流程编辑（增删/改名/参数/拖放），防止"边跑边换轮胎"。
        /// </summary>
        private MainRunState _runState = MainRunState.NotStarted;
        private bool IsRunLocked => _runState != MainRunState.NotStarted;

        /// <summary>
        /// 是否属于"编辑"动作：复制、查看类放行，其余全部受运行锁管控
        /// </summary>
        private static bool IsEditingAction(ModuleCommandAction action)
            => action is not (ModuleCommandAction.Copy or ModuleCommandAction.ShowAll);

        /// <summary>
        /// 运行时间实时刷新定时器：
        /// 引擎只记录步骤起始时间戳（LastRunStartTimestamp），运行中耗时由 UI 定时器计算写入 CurrentRunTimeMs，
        /// 否则毫秒级步骤的耗时显示永远停在初始值 0
        /// </summary>
        private readonly System.Windows.Threading.DispatcherTimer _runTimeTimer;

        public ProcessViewModel(IWorkspaceManager workspace, IDialogService dialogService)
        {
            this.Workspace = workspace;
            this.dialogService = dialogService;
            ModuleActionCommand = new(ModuleActionAsync);

            _workspaceChanged = workspace as INotifyPropertyChanged;

            _runTimeTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(200),
            };
            _runTimeTimer.Tick += OnRunTimeTimerTick;

            // 订阅与计时器统一由 Activate 挂接；View 的 Loaded / Unloaded 会成对调用
            // Activate / Deactivate。构造即视为"已入树"，所以这里先挂一次。
            Activate();
        }

        private bool _subscribed;

        /// <summary>
        /// 挂接全局订阅与运行耗时计时器（幂等）。
        ///
        /// 为什么不做成一次性的 Dispose：AvalonDock 切换标签页、隐藏面板都会让 View 触发
        /// Unloaded（之后不会自动 Loaded），一次性清理会让面板恢复显示后失去流程联动。
        /// 因此清理必须是**可逆**的挂/摘——离树时摘干净让旧 VM 可被 GC，入树时重新挂上。
        /// </summary>
        public void Activate()
        {
            if (_subscribed) return;
            _subscribed = true;

            GlobalEventBus.Subscribe<LinkPathEvent>(OnLinkPathEvent);
            // 用命名方法而非 lambda：GlobalEventBus.Unsubscribe 依赖委托的 Target+Method 匹配
            GlobalEventBus.Subscribe<MainRunState>(OnMainRunStateChanged);

            if (_workspaceChanged != null)
                _workspaceChanged.PropertyChanged += OnWorkspacePropertyChanged;

            _runTimeTimer.Start();
        }

        /// <summary>
        /// 摘除全部长生命周期订阅并停表（幂等）。
        /// 摘干净后本 VM 不再被 GlobalEventBus 静态表 / Workspace 单例 / 计时器泵引用，可被 GC。
        /// </summary>
        public void Deactivate()
        {
            if (!_subscribed) return;
            _subscribed = false;

            _runTimeTimer.Stop();

            GlobalEventBus.Unsubscribe<LinkPathEvent>(OnLinkPathEvent);
            GlobalEventBus.Unsubscribe<MainRunState>(OnMainRunStateChanged);

            if (_workspaceChanged != null)
                _workspaceChanged.PropertyChanged -= OnWorkspacePropertyChanged;
        }

        private void OnMainRunStateChanged(MainRunState state) => _runState = state;

        private void OnRunTimeTimerTick(object? sender, EventArgs e)
            => TickRunningTimes(Workspace.CurrentFlow?.Steps);

        private void OnWorkspacePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(IWorkspaceManager.CurrentStep)) return;

            var step = Workspace.CurrentStep;
            if (ReferenceEquals(step, SelectStep)) return;

            _syncingFromWorkspace = true;
            try
            {
                SelectStep = step;
            }
            finally
            {
                _syncingFromWorkspace = false;
            }
        }

        /// <summary>
        /// 递归刷新运行中步骤的实时耗时（含容器内嵌套步骤）
        /// 只更新 IsRunningFocus 且 State==Running 的步骤：
        /// 完成步骤的耗时已在引擎侧冻结为最终值，此处不覆盖
        /// </summary>
        private static void TickRunningTimes(IEnumerable<StepModel> steps)
        {
            if (steps == null) return;

            foreach (var step in steps)
            {
                if (step.IsRunningFocus
                    && step.State == StepState.Running
                    && step.LastRunStartTimestamp.HasValue)
                {
                    step.CurrentRunTimeMs = step.LiveElapsedMs();
                }

                if (step is IContainerStep container && container.Children != null)
                {
                    foreach (var branch in container.Children)
                        TickRunningTimes(branch.Steps);
                }
            }
        }

        private void OnLinkPathEvent(LinkPathEvent @event)
        {
            // 未携带目标端口：兼容旧行为，打开完整绑定视图
            if (string.IsNullOrEmpty(@event?.InputPortName))
            {
                dialogService.ShowDialog("DataBindView");
                return;
            }

            // 单绑定模式：弹窗左侧目标端口 = 事件携带的插件输入端口
            var p = new DialogParameters
            {
                { "IsSingleBindMode", true },
                { "TargetPortName", @event.InputPortName },
            };
            if (@event.TargetType != null)
                p.Add("TargetTypeName", @event.TargetType.AssemblyQualifiedName);

            dialogService.ShowDialog("DataBindView", p, result =>
            {
                if (result.Result != ButtonResult.OK)
                    return;
                if (result.Parameters.TryGetValue<LinkReference>("BoundLink", out var link))
                    @event.OnBound?.Invoke(link);
            });
        }

        /// <summary>
        /// 根据 StepModel 的 PluginTypeName 反射创建插件实例
        /// 用于检查插件是否实现 IPluginCustomViewProvider
        /// </summary>
        private static object ResolvePluginInstance(ActionStep step)
        {
            if (step == null || string.IsNullOrWhiteSpace(step.PluginTypeName))
                return null;

            try
            {
                var type = Type.GetType(step.PluginTypeName);
                if (type == null)
                    return null;

                // 检查类型是否实现 IPluginCustomViewProvider
                if (!typeof(IPluginCustomViewProvider).IsAssignableFrom(type))
                    return null;

                return Activator.CreateInstance(type);
            }
            catch
            {
                return null;
            }
        }

        private async Task ModuleActionAsync(ModuleCommandAction? action)
        {
            // 运行锁：双击卡片、右键菜单（重命名/删除/禁用/模块参数…）都汇聚到本命令，单点拦截
            if (action.HasValue && IsRunLocked && IsEditingAction(action.Value))
            {
                Notifier.ShowWarning("流程运行中，禁止编辑；如需修改请先点击“停止”");
                return;
            }

            switch (action)
            {
                case ModuleCommandAction.Rename:
                    var data = await EasyDialog.ShowTextInputAsync(
                        "步序重命名",
                        CurrentSelectedStepModel.StepName
                    );
                    if (data.IsConfirmed)
                        CurrentSelectedStepModel.StepName = data.Value;
                    break;
                case ModuleCommandAction.EditComment:
                    var data1 = await EasyDialog.ShowTextInputAsync(
                        "注释重命名",
                        CurrentSelectedStepModel.Description
                    );
                    if (data1.IsConfirmed)
                        CurrentSelectedStepModel.Description = data1.Value;
                    break;
                case ModuleCommandAction.ExecuteSelected:
                    break;
                case ModuleCommandAction.ExecuteFromHere:
                    break;
                case ModuleCommandAction.ShowAll:
                    break;
                case ModuleCommandAction.EnableSuperTool:
                    break;
                case ModuleCommandAction.SetBreakpoint:
                    break;
                case ModuleCommandAction.ModuleParameters:
                    if(SelectStep is ActionStep stepModel)
                    {
                        // 尝试获取插件实例，检查是否实现 IPluginCustomViewProvider
                        var pluginInstance = ResolvePluginInstance(stepModel);
                        if (pluginInstance is IPluginCustomViewProvider viewProvider)
                        {
                            // 有自定义视图：插件直接返回视图对象，注入 PluginConfigShellView  
                            var stepData = (IStepConfigData)stepModel;
                            var view = viewProvider.GetConfigView(stepData);
                            if (view != null)
                            {
                                var parameters = new DialogParameters();
                                parameters.Add("StepData", stepData);
                                parameters.Add("PluginView", view);
                                parameters.Add("Plugin", pluginInstance);
                                dialogService.ShowDialog("PluginConfigShell", parameters);
                                break;
                            }
                        }
                        // 回退到通用 DataBindView
                        dialogService.ShowDialog("DataBindView");
                    }
                    else
                    {
                        var parameters = new DialogParameters();
                        parameters.Add("Node", SelectStep);
                        dialogService.ShowDialog("ConditionEditor", parameters);
                    }
                   
                    break;
                case ModuleCommandAction.Cut:
                    break;
                case ModuleCommandAction.Copy:
                    break;
                case ModuleCommandAction.Paste:
                    break;
                case ModuleCommandAction.Disable:
                    CurrentSelectedStepModel.IsDisEnable = false;
                    break;
                case ModuleCommandAction.Delete:
                    if (CurrentSelectedStepModel == null || Workspace?.CurrentFlow == null)
                        break;
                    if (
                        RemoveStepRecursively(Workspace.CurrentFlow.Steps, CurrentSelectedStepModel)
                    )
                    {
                        CurrentSelectedStepModel = null;
                    }

                    break;
                case ModuleCommandAction.AddElseIf:
                {
                    if (SelectStep is ConditionStep ifNode)
                    {
                        int insertIndex = ifNode.Children.Count;
                        var lastBranch = ifNode.Children.LastOrDefault();

                        if (lastBranch != null && lastBranch.BranchType == BranchType.Else)
                        {
                            insertIndex = ifNode.Children.Count - 1;
                        }

                        // 3. 插入数据
                        ifNode.Children.Insert(
                            insertIndex,
                            new StepCollection
                            {
                                BranchType = BranchType.ElseIf,
                                StepName = "ElseIf 分支",
                            }
                        );
                    }
                    break;
                }
                case ModuleCommandAction.AddElse:
                {
                    if (SelectStep is ConditionStep ifNode)
                    {
                        if (ifNode.Children.Any(c => c.BranchType == BranchType.Else))
                        {
                            Notifier.ShowWarning("该算子已经包含了 Else 分支！");
                            break;
                        }
                        ifNode.Children.Add(
                            new StepCollection
                            {
                                BranchType = BranchType.Else,
                                StepName = "Else 分支",
                            }
                        );
                    }
                    break;
                }
                case ModuleCommandAction.AddCase:
                {
                    if (SelectStep is ConditionStep switchNode)
                    {
                        int caseCount = switchNode.Children.Count(c =>
                            c.BranchType == BranchType.Case
                        );
                        switchNode.Children.Add(
                            new StepCollection
                            {
                                BranchType = BranchType.Case,
                                StepName = $"Case {caseCount + 1}",
                            }
                        );
                    }
                    break;
                }
            }
        }

        #region 控件拖拽
        /// <summary>
        /// 控件拖动
        /// </summary>
        /// <param name="args"></param>
        public void DragOver(IDropInfo dropInfo)
        {
            // 1. 基础防线：没有抓到数据，或者没加载流程，直接拒绝
            if (dropInfo.Data == null || Workspace?.CurrentFlow == null)
            {
                dropInfo.Effects = DragDropEffects.None;
                return;
            }

            // 2. 核心判定：拖的是什么？(直接决定了是复制还是移动)
            bool isFromToolbox = dropInfo.Data is ToolItemModel; // 从左侧工具箱拖来的图纸模板
            bool isFromCanvas = dropInfo.Data is StepModel; // 从画布上拖起来的旧算子

            if (!isFromToolbox && !isFromCanvas)
            {
                dropInfo.Effects = DragDropEffects.None;
                return;
            }
            // 3. 智能推断“落地点名称”和“UI 框选效果”
            string destinationName = "主流程";
            bool isHoveringContainer = false;

            if (dropInfo.TargetItem is StepCollection branch)
            {
                destinationName = $"分支: {branch.StepName}";
                isHoveringContainer = true;
            }
            else if (dropInfo.TargetItem is ConditionStep condition) // 💡 自动包含 If 和 While
            {
                destinationName = $"容器: {condition.StepName}";
                isHoveringContainer = true;
            }
            else if (dropInfo.TargetItem is ForStep forStep) // 🌟 新增：识别 For 循环容器
            {
                destinationName = $"循环: {forStep.StepName}";
                isHoveringContainer = true;
            }

            // 4. 设置 UI 样式：悬停在容器头上显示高亮框，悬停在算子之间显示插入线条
            dropInfo.DropTargetAdorner = isHoveringContainer
                ? DropTargetAdorners.Highlight
                : DropTargetAdorners.Insert;

            // 5. 根据来源设置终极动作
            if (isFromToolbox)
            {
                // 场景 A：从工具箱拖来的 -> 永远是添加 (Copy)
                dropInfo.Effects = DragDropEffects.Copy;
                dropInfo.EffectText = "添加算子";
                dropInfo.DestinationText = destinationName;
            }
            else if (isFromCanvas)
            {
                // 场景 B：在画布内部拖的 -> 无论是同级排序还是跨分支，永远是移动 (Move)

                // 🛡️ 防御性编程：防止用户把一个大容器拖进自己的肚子里造成无限死循环死锁
                if (dropInfo.Data == dropInfo.TargetItem)
                {
                    dropInfo.Effects = DragDropEffects.None;
                    return;
                }

                dropInfo.Effects = DragDropEffects.Move;
                dropInfo.EffectText = "移动算子";
                dropInfo.DestinationText = destinationName;
            }
        }

        public void Drop(IDropInfo args)
        {
            if (Workspace.CurrentFlow == null)
            {
                Notifier.ShowError("请先加载流程");
                return;
            }

            // 运行锁：从工具箱拖入、卡片间拖拽排序，都会走到这里，一并拦下
            if (IsRunLocked)
            {
                Notifier.ShowWarning("流程运行中，禁止编辑；如需修改请先点击“停止”");
                return;
            }

            if (args.Effects != DragDropEffects.Copy && args.Effects != DragDropEffects.Move)
                return;

            // ==========================================
            // 第一步：智能解析要放进哪个“口袋”
            // ==========================================
            IList targetList = args.TargetCollection as IList;
            int insertIndex = args.InsertIndex;

            if (targetList == null || !(targetList is IEnumerable<StepModel>))
            {
                switch (args.TargetItem)
                {
                    case StepCollection branch:
                        targetList = branch.Steps;
                        insertIndex = branch.Steps.Count;
                        break;

                    case ConditionStep condition when condition.Children.Count > 0: // 💡 包含 If 和 While
                        targetList = condition.Children[0].Steps;
                        insertIndex = condition.Children[0].Steps.Count;
                        break;

                    case ForStep forStep when forStep.Children.Count > 0: // 🌟 新增：把算子丢进 For 循环肚子里
                        targetList = forStep.Children[0].Steps;
                        insertIndex = forStep.Children[0].Steps.Count;
                        break;

                    default:
                        targetList = Workspace.CurrentFlow.Steps;
                        insertIndex = Workspace.CurrentFlow.Steps.Count;
                        break;
                }
            }

            if (insertIndex < 0) insertIndex = 0;
            if (insertIndex > targetList.Count) insertIndex = targetList.Count;

            // ==========================================
            // 第二步：场景 A - 从工具箱【新增】算子 (Copy)
            // ==========================================
            if (args.Effects == DragDropEffects.Copy && args.Data is ToolItemModel node)
            {
                var totalIndex = CountStepsDeep(Workspace.CurrentFlow.Steps, node.ModuleTypeName);
                string stepName = $"{node.Name}_{totalIndex}";

                StepModel newStep = null;

                // 🌟 核心修改：根据 ModuleTypeName 精准实例化原生节点
                if (node.IsContainer)
                {
                    if (node.ModuleTypeName == "BuiltIn_While")
                    {
                        newStep = new WhileStep(node.Icon, node.Name, node.ModuleTypeName, stepName);
                    }
                    else if (node.ModuleTypeName == "BuiltIn_For")
                    {
                        newStep = new ForStep(node.Icon, node.Name, node.ModuleTypeName, stepName);
                    }
                    else // 默认兜底是 If
                    {
                        newStep = new ConditionStep(node.Icon, node.Name, node.ModuleTypeName, stepName);
                    }
                }
                else
                {
                    // 普通算子
                    newStep = new ActionStep(node.Icon, node.Name, node.ModuleTypeName, stepName);
                }

                targetList.Insert(insertIndex, newStep);

                // 🌟 别忘了通知图纸：结构改变了，需要重新编译！
                Workspace.CurrentFlow.Version++;
                return;
            }
            // ==========================================
            // 第三步：场景 B - 在画布内部【拖拽移动】 (Move)
            // ==========================================
            if (args.Effects == DragDropEffects.Move && args.Data is StepModel sourceItem)
            {
                // 🚨 致命 Bug 修复处：必须从 DragInfo 里拿数据的来源集合！
                IList sourceList = args.DragInfo?.SourceCollection as IList;

                if (sourceList != null && targetList != null)
                {
                    int oldIndex = sourceList.IndexOf(sourceItem);
                    int newIndex = insertIndex;

                    if (oldIndex == -1)
                        return; // 防御性编程

                    // 【情况 B-1】：同容器内移动（上下排序）
                    if (sourceList == targetList)
                    {
                        // 如果位置没变，直接跳过
                        if (oldIndex == newIndex || oldIndex == newIndex - 1)
                            return;

                        // 核心算法：因为元素被移除后，后面的元素会整体往前挤1位，所以新索引需要修正
                        if (newIndex > oldIndex)
                            newIndex--;

                        sourceList.RemoveAt(oldIndex);
                        sourceList.Insert(newIndex, sourceItem);
                    }
                    // 【情况 B-2】：跨容器移动（主流程 <-> 逻辑分支，或者 分支A <-> 分支B）
                    else
                    {
                        // 1. 先从老口袋里拿出来
                        sourceList.RemoveAt(oldIndex);

                        // 2. 重新校验目标口袋的容量上限 (因为拿出来一个，总数可能变了)
                        if (newIndex > targetList.Count)
                            newIndex = targetList.Count;

                        // 3. 塞进新口袋
                        targetList.Insert(newIndex, sourceItem);
                    }
                }
            }
        }

        private int CountStepsDeep(IEnumerable<StepModel> steps, string pluginTypeName)
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

                // 2. 如果遇到容器节点，钻进它的每一个分支里继续找
                if (step is ConditionStep conditionNode)
                {
                    foreach (var branch in conditionNode.Children)
                    {
                        count += CountStepsDeep(branch.Steps, pluginTypeName);
                    }
                }
            }

            return count;
        }

        private bool RemoveStepRecursively(
            ObservableCollection<StepModel> steps,
            StepModel targetToRemove
        )
        {
            if (steps == null || steps.Count == 0 || targetToRemove == null)
                return false;

            // 1. 第一层拦截：如果这个节点就在当前集合里，直接斩杀！
            if (steps.Contains(targetToRemove))
            {
                steps.Remove(targetToRemove);
                return true;
            }

            // 2. 如果不在当前层，遍历当前层里的所有“容器算子”（比如 If/While）
            foreach (var step in steps)
            {
                if (step is ConditionStep conditionNode)
                {
                    // 钻进容器的每一个分支里去找（比如 If分支、Else分支）
                    foreach (var branch in conditionNode.Children)
                    {
                        // 递归调用！如果在深层找到了并删除了，立刻顺着调用栈返回 true 终止搜索
                        if (RemoveStepRecursively(branch.Steps, targetToRemove))
                        {
                            return true;
                        }
                    }
                }
            }

            // 到底了都没找到
            return false;
        }
        #endregion
    }
}
