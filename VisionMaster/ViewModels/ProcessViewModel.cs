using Core.Interfaces;
using GongSolutions.Wpf.DragDrop;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using UI.CustomControl;
using VisionMaster.Models;
using VisionMaster.Services;

namespace VisionMaster.ViewModels
{
    public class ProcessViewModel : BindableBase, IDropTarget
    {
        public bool IsIfNodeSelected => SelectStep is ConditionStep step && step.PluginName.Contains("If");

        public bool IsSwitchNodeSelected => SelectStep is ConditionStep step && step.PluginName.Contains("Switch");
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
                CurrentSelectedStepModel= value as StepModel;
                RaisePropertyChanged(nameof(IsIfNodeSelected));
                RaisePropertyChanged(nameof(IsSwitchNodeSelected));
                Workspace.SwitchStep(CurrentSelectedStepModel);
            }
        }
         StepModel CurrentSelectedStepModel;

        public ProcessViewModel(IWorkspaceManager workspace)
        {
            this.Workspace = workspace;
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
                    var dialogService = ContainerLocator.Container.Resolve<IDialogService>();
                    dialogService.ShowDialog("DataBindView");
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
                    Workspace.CurrentFlow.Steps.Remove(CurrentSelectedStepModel);
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
                            ifNode.Children.Insert(insertIndex, new StepCollection
                            {
                                BranchType = BranchType.ElseIf,
                                StepName = "ElseIf 分支"
                            });
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
                            ifNode.Children.Add(new StepCollection
                            {
                                BranchType = BranchType.Else,
                                StepName = "Else 分支"
                            });
                        }
                        break;
                    }
                case ModuleCommandAction.AddCase:
                    {
                        if (SelectStep is ConditionStep switchNode)
                        {
                            int caseCount = switchNode.Children.Count(c => c.BranchType == BranchType.Case);
                            switchNode.Children.Add(new StepCollection
                            {
                                BranchType = BranchType.Case,
                                StepName = $"Case {caseCount + 1}"
                            });
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
            // 如果没有抓取到数据，或者没有落点集合，直接拒绝
            if (dropInfo.Data == null || dropInfo.TargetCollection == null)
                return;

            bool isInternalMove = dropInfo.DragInfo.SourceCollection == dropInfo.TargetCollection;

            if (isInternalMove)
            {
                dropInfo.Effects = DragDropEffects.Move;
                // 允许在项之间插入
                dropInfo.DropTargetAdorner = DropTargetAdorners.Insert;
            }
            else
            {
                dropInfo.Effects = DragDropEffects.Copy;
                dropInfo.DropTargetAdorner = DropTargetAdorners.Highlight;
                dropInfo.EffectText = "添加算子";

                dropInfo.DestinationText = "视觉主流程";
            }
        }

        /// <summary>
        /// 控件落下
        /// </summary>
        /// <param name="dropInfo"></param>
        public void Drop(IDropInfo args)
        {
            if (Workspace.CurrentFlow == null)
            {
                Notifier.ShowError("请先加载流程");
                return;
            }
            if (args.Effects != DragDropEffects.Copy && args.Effects != DragDropEffects.Move)
                return;
            var targetCollection = args.TargetCollection as IList;

            // 3. 兜底逻辑：如果没找到目标集合，默认给主流程
            if (targetCollection == null)
            {
                targetCollection = Workspace.CurrentFlow.Steps;
            }

            if (targetCollection.GetType().IsGenericType)
            {
                var genericType = targetCollection.GetType().GetGenericArguments()[0];
                if (genericType != typeof(StepModel) && !genericType.IsSubclassOf(typeof(StepModel)))
                {
                    // 🌟 修复 1：如果鼠标精准落在了某个分支上（如 If 分支）
                    if (args.TargetItem is StepCollection branch)
                    {
                        targetCollection = branch.Steps; // 重定向到该分支的口袋里
                    }
                    // 如果鼠标落在了外层的大 If 容器头上
                    else if (args.TargetItem is ConditionStep condition)
                    {
                        targetCollection = condition.Children[0].Steps; // 默认给它的第一个分支
                    }
                    else
                    {
                        targetCollection = Workspace.CurrentFlow.Steps;
                    }
                }
            }

            // 2. 场景 A：从工具箱【新增】算子 (Copy)
            if (args.Effects == DragDropEffects.Copy && args.Data is ToolItemModel node)
            {
                // 🌟 重点：计算新算子的索引位置
                var totalIndex = CountStepsDeep(Workspace.CurrentFlow.Steps, node.ModuleTypeName);

                StepModel stepData = node.IsContainer
                    ? new ConditionStep(node.Icon, node.Name, node.ModuleTypeName, node.Name + "_" + totalIndex)
                    : new ActionStep(node.Icon, node.Name, node.ModuleTypeName, node.Name + "_" + totalIndex);
                int insertIndex = args.InsertIndex;
                if (insertIndex > targetCollection.Count) insertIndex = targetCollection.Count;

                targetCollection.Insert(insertIndex, stepData);
                return;
            }

            if (args.Effects == DragDropEffects.Move && args.Data is StepModel sourceItem)
            {
                var sourceCollection = args.TargetCollection as IList;
                if (sourceCollection != null)
                {
                    int oldIndex = sourceCollection.IndexOf(sourceItem);
                    int newIndex = args.InsertIndex;

                    // 如果是同一个集合内移动（排序）
                    if (sourceCollection == targetCollection)
                    {
                        if (oldIndex < newIndex) newIndex--; // 修正删除元素后的索引偏移
                        if (oldIndex != newIndex && newIndex >= 0 && newIndex < sourceCollection.Count)
                        {
                            sourceCollection.RemoveAt(oldIndex);
                            sourceCollection.Insert(newIndex, sourceItem);
                        }
                    }
                    else
                    {
                        // 🌟 跨容器移动：从主流程移入容器，或从容器移出到主流程
                        sourceCollection.Remove(sourceItem);
                        if (newIndex > targetCollection.Count) newIndex = targetCollection.Count;
                        targetCollection.Insert(newIndex, sourceItem);
                    }
                }
            }
            #endregion
        }
        private int CountStepsDeep(IEnumerable<StepModel> steps, string pluginTypeName)
        {
            int count = 0;
            if (steps == null) return count;

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
    }
}
