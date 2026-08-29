using Prism.Dialogs;
using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UI.CustomControl;
using VisionMaster.Models;
using VisionMaster.Services;

namespace VisionMaster.ViewModels
{
    /// <summary>
    /// 程序面板（FlowListView）ViewModel：
    /// 流程列表的显示、选中切换（驱动流程栏）与右键管理操作
    /// </summary>
    public class FlowListViewModel : BindableBase
    {
        private readonly IDialogService dialogService;

        public IWorkspaceManager Workspace { get; init; }

        /// <summary>
        /// 选中的流程：切换时驱动流程栏（Workspace.SwitchFlow）
        /// </summary>
        public FlowModel SelectFlow
        {
            get { return field; }
            set { field = value; Workspace.SwitchFlow(value); }
        }

        public AsyncDelegateCommand<FlowAction?> FlowCommand { get; }

        public FlowListViewModel(IWorkspaceManager workspace, IDialogService dialogService)
        {
            Workspace = workspace;
            this.dialogService = dialogService;
            FlowCommand = new AsyncDelegateCommand<FlowAction?>(FlowCommandExecute);
        }

        private async Task FlowCommandExecute(FlowAction? action)
        {
            switch (action)
            {
                case FlowAction.Create:
                    Workspace.CurrentSolution.Flows.Insert(Workspace.CurrentSolution.Flows.IndexOf(SelectFlow) + 1, new FlowModel() { FlowName = "新建流程" });
                    break;
                case FlowAction.Delete:
                    Workspace.CurrentSolution.Flows.Remove(SelectFlow);
                    break;
                case FlowAction.Rename:
                    var data = await EasyDialog.ShowTextInputAsync("流程重命名", SelectFlow.FlowName);
                    if (data.IsConfirmed) SelectFlow.FlowName = data.Value;
                    break;
                case FlowAction.EditComment:
                    var data1 = await EasyDialog.ShowTextInputAsync("流程注释修改", SelectFlow.Description);
                    if (data1.IsConfirmed) SelectFlow.Description = data1.Value;
                    break;
                case FlowAction.Manager:
                    ShowFlowManager();
                    break;
            }
        }

        /// <summary>
        /// 显示流程管理对话框
        /// </summary>
        private void ShowFlowManager()
        {
            if (Workspace.CurrentSolution == null) return;

            var parameters = new DialogParameters();
            parameters.Add("Flows", Workspace.CurrentSolution.Flows);

            dialogService.ShowDialog("FlowManagerView", parameters, result =>
            {
                // 可以在这里处理对话框关闭后的逻辑
            });
        }
    }
}
