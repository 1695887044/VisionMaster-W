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
        public string Title => "变量绑定";
        public IWorkspaceManager Workspace { get; init; }

        /// <summary>
        /// 当前可选的变量
        /// </summary>
        public ObservableCollection<InputPortUIModel> DisplayDataPort { get; } = new();
        public ObservableCollection<NodeLinkViewModel> TreeNodes { get; } = new();

        public DelegateCommand ConfirmCommand { get; set; }
        public DelegateCommand CancelCommand { get; set; }


        public DelegateCommand<InputPortUIModel> UnbindCommand => new(port =>
        {
            if (port == null) return;
            // 底层字典移除
            Workspace.CurrentStep.LinkedSources.Remove(port.Schema.Name);
            // UI 状态清空
            port.LinkedAddress = null;
        });

        public DelegateCommand<PortSchema> DoublockClickBindCommand { get; set; }

        public ObservableCollection<PortSchema> DisplayPorts
        {
            get => field;
            set => SetProperty(ref field, value);
        }

        public InputPortUIModel SelectedInputPort
        {
            get => field;
            set => SetProperty(ref field, value);
        }
        public NodeLinkViewModel SelectedNode
        {
            get => field;
            set
            {
                SetProperty(ref field, value);
                DisplayPorts = new(value.OutputSchemas);
            }
        }

        public DialogCloseListener RequestClose { get; set; }

        public VariableBindingViewModel(IWorkspaceManager workspace)
        {
            this.Workspace = workspace;
            CancelCommand = new DelegateCommand(() =>
            {
                RequestClose.Invoke();
            });
            ConfirmCommand = new DelegateCommand(Confirm);
            DoublockClickBindCommand = new DelegateCommand<PortSchema>(DoublockClickBind);
        }

        private void DoublockClickBind(PortSchema outputSchema)
        {
            if (SelectedInputPort == null)
            {
                EasyDialog.ShowSync("提示", "请先在左侧选择需要绑定的输入端口！");
                return;
            }

            Type inputType = SelectedInputPort.Schema.DataType;
            Type outputType = outputSchema.DataType;
            bool isIndexing = !inputType.IsArray && outputType.IsArray;

            if (isIndexing)
            {
                Type outputElementType = outputType.GetElementType();

                // 校验：数组里的东西，能不能塞进输入端口？
                if (outputElementType == null || !inputType.IsAssignableFrom(outputElementType))
                {
                    EasyDialog.ShowSync("类型不匹配",
                        $"变量集合中的元素类型是 {outputElementType?.Name}，\n无法赋值给 {inputType.Name} 类型的端口！");
                    return;
                }

                // 校验通过，进入索引输入流程
                var (isConfirmed, indexStr) = EasyDialog.ShowTextInputSync("索引选择", "0");
                if (!isConfirmed) return;

                if (!int.TryParse(indexStr, out int index) || index < 0)
                {
                    EasyDialog.ShowSync("格式错误", "索引必须是非负整数！");
                    return;
                }

                // 最终地址：StepID.PortName[Index]
                DoFinalBind($"{SelectedNode.StepID}.{outputSchema.Name}[{index}]");
            }
            else
            {
                // 2. 普通绑定（标量对标量，或数组对数组）
                if (!inputType.IsAssignableFrom(outputType))
                {
                    EasyDialog.ShowSync("类型不匹配",
                        $"无法将 {outputType.Name} 绑定到 {inputType.Name} 端口！");
                    return;
                }

                // 最终地址：StepID.PortName
                DoFinalBind($"{SelectedNode.StepID}.{outputSchema.Name}");
            }

        }
        private void DoFinalBind(string address)
        {
            Workspace.CurrentStep.LinkedSources[SelectedInputPort.Schema.Name] = address;
            SelectedInputPort.LinkedAddress = address;
        }
        private void Confirm()=> RequestClose.Invoke();

        public void BuildTree(Type type = null)
        {
            if (DisplayDataPort == null || TreeNodes == null || Workspace?.CurrentStep == null)
                return;
            DisplayDataPort.Clear();
            TreeNodes.Clear();
            //加载当前算子需要的【输入端口】 (左侧栏)
            var currentSchema = PluginRegistry.GetSchema(Workspace.CurrentStep.PluginTypeName);
            if (currentSchema != null)
            {
                foreach (var schema in currentSchema.InputSchemas)
                {
                    Workspace.CurrentStep.LinkedSources.TryGetValue(schema.Name, out string existingLink);
                    DisplayDataPort.Add(new InputPortUIModel(schema, existingLink));
                }
            }
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
