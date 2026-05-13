using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Core.Interfaces;
using GongSolutions.Wpf.DragDrop;
using UI.CustomControl;
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
                Workspace.SwitchStep(CurrentSelectedStepModel);
            }
        }
        StepModel CurrentSelectedStepModel;

        public ProcessViewModel(IWorkspaceManager workspace, IDialogService dialogService)
        {
            this.Workspace = workspace;
            this.dialogService = dialogService;
            ModuleActionCommand = new(ModuleActionAsync);
        }

        private async Task ModuleActionAsync(ModuleCommandAction? action)
        {
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
                        dialogService.ShowDialog("DataBindView");
                    }
                    else
                    {
                        var parameters = new DialogParameters();
                        parameters.Add("Node", SelectStep); // 传入整个 IF 容器
                        //parameters.Add("Branch", clickedBranch); // 传入双击的具体分支 (可选)
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
                    CurrentSelectedStepModel.IsEnabled = false;
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
            else if (dropInfo.TargetItem is ConditionStep condition)
            {
                destinationName = $"容器: {condition.StepName}";
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

            if (args.Effects != DragDropEffects.Copy && args.Effects != DragDropEffects.Move)
                return;

            // ==========================================
            // 第一步：智能解析要放进哪个“口袋” (彻底告别反射)
            // ==========================================
            IList targetList = args.TargetCollection as IList;
            int insertIndex = args.InsertIndex;

            // 如果 WPF 拖拽库没有直接命中 ObservableCollection<StepModel>，
            // 说明鼠标可能悬停在了“大容器的头部”或“背景空白处”，我们需要手动纠偏。
            if (targetList == null || !(targetList is IEnumerable<StepModel>))
            {
                switch (args.TargetItem)
                {
                    // 🎯 命中情况 1：鼠标悬停在“If 分支”这个容器上
                    case StepCollection branch:
                        targetList = branch.Steps;
                        insertIndex = branch.Steps.Count; // 默认追加到分支末尾
                        break;

                    // 🎯 命中情况 2：鼠标悬停在“If 算子”的大头上
                    case ConditionStep condition when condition.Children.Count > 0:
                        targetList = condition.Children[0].Steps; // 默认丢进它的第一个分支 (If)
                        insertIndex = condition.Children[0].Steps.Count;
                        break;

                    // 🎯 兜底情况：统统丢进主流程
                    default:
                        targetList = Workspace.CurrentFlow.Steps;
                        insertIndex = Workspace.CurrentFlow.Steps.Count;
                        break;
                }
            }

            // 防止因为 UI 虚拟化导致的索引越界
            if (insertIndex < 0)
                insertIndex = 0;
            if (insertIndex > targetList.Count)
                insertIndex = targetList.Count;

            // ==========================================
            // 第二步：场景 A - 从工具箱【新增】算子 (Copy)
            // ==========================================
            if (args.Effects == DragDropEffects.Copy && args.Data is ToolItemModel node)
            {
                var totalIndex = CountStepsDeep(Workspace.CurrentFlow.Steps, node.ModuleTypeName);

                StepModel newStep = node.IsContainer
                    ? new ConditionStep(
                        node.Icon,
                        node.Name,
                        node.ModuleTypeName,
                        $"{node.Name}_{totalIndex}"
                    )
                    : new ActionStep(
                        node.Icon,
                        node.Name,
                        node.ModuleTypeName,
                        $"{node.Name}_{totalIndex}"
                    );

                targetList.Insert(insertIndex, newStep);
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
