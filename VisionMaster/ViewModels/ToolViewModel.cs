using GongSolutions.Wpf.DragDrop;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using UI.CustomControl;
using VisionMaster.Models;
using VisionMaster.Services;

namespace VisionMaster.ViewModels
{
    public class ToolViewModel : BindableBase

    {
        private readonly SolutionService solutionService;
        private readonly IPluginProvider pluginProvider;

        public IWorkspaceManager Workspace {  get; init; }


        public FlowModel SelectFlow
        {
            get { return field; }
            set { field = value; Workspace.SwitchFlow(value); }
        }

        public List<ToolGroupModel> ToolBarSource { get; set; } = new();

        public AsyncDelegateCommand<FlowAction?> FlowCommand { get; set; }
        public ToolViewModel(SolutionService solutionService, IWorkspaceManager Workspace,IPluginProvider pluginProvider)
        {
            this.solutionService = solutionService;
            this.Workspace = Workspace;
            this.pluginProvider = pluginProvider;
            FlowCommand =new (FlowCommandExecute);
            loadTools();

        }

        private async Task FlowCommandExecute(FlowAction? action)
        {
            switch (action) { 
                case FlowAction.Create:
                    Workspace.CurrentSolution.Flows.Insert(Workspace.CurrentSolution.Flows.IndexOf(SelectFlow)+1, new FlowModel() { FlowName = "新建流程" });
                    break;
                case FlowAction.Delete:
                    Workspace.CurrentSolution.Flows.Remove(SelectFlow);
                    break;
                case FlowAction.Rename:
                 var data =await  EasyDialog.ShowTextInputAsync("流程重命名", SelectFlow.FlowName);
                if (data.IsConfirmed) SelectFlow.FlowName = data.Value;
                    break;
                case FlowAction.EditComment:
                    var data1 = await EasyDialog.ShowTextInputAsync("流程注释修改", SelectFlow.Description);
                    if (data1.IsConfirmed) SelectFlow.Description = data1.Value;
                    break;
            }
        }

        void loadTools()
        {
            pluginProvider.ModulePlugins.GroupBy(t => t.Value.Category).ToList().ForEach(g =>
            {
                ToolGroupModel toolGroup = new ToolGroupModel() { Name = g.Key };
                g.ToList().ForEach(p =>
                {
                    toolGroup.Children.Add(p.Value);
                });
                ToolBarSource.Add(toolGroup);
            });

        }
    }
}
