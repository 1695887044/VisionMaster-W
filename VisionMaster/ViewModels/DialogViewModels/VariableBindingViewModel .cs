using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Xml.Linq;
using Core.Interfaces;
using UI.CustomControl;
using VisionMaster.Helpers;
using VisionMaster.Models;
using VisionMaster.Services;

namespace VisionMaster.ViewModels
{
    public class VariableBindingViewModel : BindableBase, IDialogAware
    {
        private readonly IPluginProvider pluginProvider;

        public string Title => "变量绑定";
        public IWorkspaceManager Workspace { get; init; }

        public ObservableCollection<InputPortUIModel> DisplayDataPort { get; } = new();

        public ObservableCollection<ToolItemModel> TreeNodes { get; } = new();

        public DelegateCommand ConfirmCommand { get; set; }
        public DelegateCommand CancelCommand { get; set; }


        public DelegateCommand<InputPortUIModel> UnbindCommand => new(port =>
        {
            if (port == null) return;
            Workspace.CurrentStep.LinkedSources.Remove(port.Definition.Name);
            port.LinkedAddress = null;
        });

        public DelegateCommand<PortDefinition> DoublockClickBindCommand { get; set; }

        public ObservableCollection<PortDefinition> DisplayPorts
        {
            get => field;
            set => SetProperty(ref field, value);
        }

        public InputPortUIModel SelectedInputPort
        {
            get => field;
            set => SetProperty(ref field, value);
        }
        public ToolItemModel SelectedNode
        {
            get => field;
            set
            {
                SetProperty(ref field, value);
                if (value != null && value.OutputDefinitions != null)
                {
                    DisplayPorts = new ObservableCollection<PortDefinition>(value.OutputDefinitions);
                }
                else
                {
                    DisplayPorts = new ObservableCollection<PortDefinition>();
                }
            }
        }

        public DialogCloseListener RequestClose { get; set; }

        public VariableBindingViewModel(IWorkspaceManager workspace,IPluginProvider pluginProvider)
        {
            this.Workspace = workspace;
            this.pluginProvider = pluginProvider;
            CancelCommand = new DelegateCommand(() =>
            {
                RequestClose.Invoke( ButtonResult.Cancel);
            });
            ConfirmCommand = new DelegateCommand(Confirm);
            DoublockClickBindCommand = new DelegateCommand<PortDefinition>(DoublockClickBind);
        }

        private void DoublockClickBind(PortDefinition outputSchema)
        {
            if (SelectedInputPort == null)
            {
                EasyDialog.ShowSync("提示", "请先在左侧选择需要绑定的输入端口！");
                return;
            }
           
            Type inputType = Type.GetType(SelectedInputPort.Definition.DataTypeName);
            Type outputType = Type.GetType(outputSchema.DataTypeName);
            bool isIndexing = !inputType.IsArray && outputType.IsArray;

            if (isIndexing)
            {
                Type outputElementType = outputType.GetElementType();
                if (outputElementType == null || !inputType.IsAssignableFrom(outputElementType))
                {
                    EasyDialog.ShowSync("类型不匹配",
                        $"变量集合中的元素类型是 {outputElementType?.Name}，\n无法赋值给 {inputType.Name} 类型的端口！");
                    return;
                }

                var (isConfirmed, indexStr) = EasyDialog.ShowTextInputSync("索引选择", "0");
                if (!isConfirmed) return;

                if (!int.TryParse(indexStr, out int index) || index < 0)
                {
                    EasyDialog.ShowSync("格式错误", "索引必须是非负整数！");
                    return;
                }
                DoFinalBind(outputSchema, index);
            }
            else
            {
                if (!inputType.IsAssignableFrom(outputType))
                {
                    EasyDialog.ShowSync("类型不匹配",
                        $"无法将 {outputType.Name} 绑定到 {inputType.Name} 端口！");
                    return;
                }

                DoFinalBind(outputSchema);
            }

        }
        private void DoFinalBind(PortDefinition port)
        {
            Guid targetId = SelectedNode.Id;
            string targetPort = port.Name;
            string displayName = $"{SelectedNode.Name}.{port.Name}";
            var linkRef = new LinkReference(targetId, targetPort, displayName);
            Workspace.CurrentStep.LinkedSources[SelectedInputPort.Definition.Name] = linkRef;
            SelectedInputPort.LinkedAddress = displayName;
        }
        private void DoFinalBind(PortDefinition port,int index)
        {
            Guid targetId = SelectedNode.Id;
            string targetPort = port.Name;
            string displayName = $"{SelectedNode.Name}.{port.Name}[{index}]";
            var linkRef = new LinkReference(targetId, targetPort, displayName);
            Workspace.CurrentStep.LinkedSources[SelectedInputPort.Definition.Name] = linkRef;
            SelectedInputPort.LinkedAddress = displayName;
        }
        private void Confirm()=> RequestClose.Invoke(ButtonResult.OK);
        /// <summary>
        /// 拿变量的时候 要检查是判断类型还是算子类型
        /// </summary>
        /// <param name="type"></param>
        public void BuildTree(Type type = null)
        {
            if (DisplayDataPort == null || TreeNodes == null || Workspace?.CurrentStep == null)
                return;
            DisplayDataPort.Clear();
            TreeNodes.Clear();
            var currentSchema = pluginProvider.ModulePlugins[Workspace.CurrentStep.PluginTypeName].InputDefinitions;
            if (currentSchema != null)
            {
                foreach (var schema in currentSchema)
                {
                    Workspace.CurrentStep.LinkedSources.TryGetValue(schema.Name, out var existingLink);

                    DisplayDataPort.Add(new InputPortUIModel(schema, existingLink?.DisplayAddress));
                }
            }
            //判断是不是判断类型
            //if (Workspace.CurrentStep is ConditionStep condition)
            //{
            //    foreach (var varName in condition.LocalVariableNames)
            //    {
            //        // 尝试找回已绑定的上游地址
            //        condition.LinkedSources.TryGetValue(varName, out var existingLink);


            //    }
            //}
            var dat1a =FlowQueryHelper.GetAvailableVariablesTree(Workspace.GlobalVariables, Workspace.CurrentFlow.Steps,Workspace.CurrentStep);
            foreach (var item in dat1a)
            {
                TreeNodes.Add(item);
            }
        }
        public bool CanCloseDialog() => true;

        public void OnDialogClosed() { }

        public void OnDialogOpened(IDialogParameters parameters)
        {
            BuildTree();
        }
    }
}
