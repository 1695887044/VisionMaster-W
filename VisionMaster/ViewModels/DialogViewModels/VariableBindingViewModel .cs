using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Core.Interfaces;
using Prism.Dialogs;
using UI.CustomControl;
using VisionMaster.Helpers;
using VisionMaster.Models;
using VisionMaster.Services;

namespace VisionMaster.ViewModels
{
    public class VariableBindingViewModel : BindableBase, IDialogAware
    {
        private PortDefinition _lastBoundPort;
        private readonly IPluginProvider pluginProvider;
        private LinkReference _lastBoundLink;
        private bool _isSingleBindMode = false; // 🌟 区分模式开关

        public string Title => "变量绑定";
        public IWorkspaceManager Workspace { get; init; }

        public ObservableCollection<InputPortUIModel> DisplayDataPort { get; } = new();
        public ObservableCollection<ToolItemModel> TreeNodes { get; } = new();

        public DelegateCommand ConfirmCommand { get; set; }
        public DelegateCommand CancelCommand { get; set; }

        public DelegateCommand<InputPortUIModel> UnbindCommand =>
            new(port =>
            {
                if (port == null || _isSingleBindMode)
                    return; // 单绑模式下不许解绑左侧
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

        // 新增右侧选中的端口，为了让用户单选后点“确定”按钮也能生效
        public PortDefinition SelectedOutputPort
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
                DisplayPorts =
                    value?.OutputDefinitions != null
                        ? new ObservableCollection<PortDefinition>(value.OutputDefinitions)
                        : new ObservableCollection<PortDefinition>();
            }
        }

        public DialogCloseListener RequestClose { get; set; }

        public VariableBindingViewModel(IWorkspaceManager workspace, IPluginProvider pluginProvider)
        {
            this.Workspace = workspace;
            this.pluginProvider = pluginProvider;

            CancelCommand = new DelegateCommand(() =>
                RequestClose.Invoke(new DialogResult(ButtonResult.Cancel))
            );
            ConfirmCommand = new DelegateCommand(Confirm);
            DoublockClickBindCommand = new DelegateCommand<PortDefinition>(DoublockClickBind);
        }

        private void DoublockClickBind(PortDefinition outputSchema)
        {
            // 1. 普通模式下防呆拦截
            // 1. 防呆：普通模式下必须先选左侧输入端口
            if (!_isSingleBindMode && SelectedInputPort == null)
            {
                EasyDialog.ShowSync("提示", "请先在左侧选择需要绑定的输入端口！");
                return;
            }

            // 2. 解析类型
            Type targetType = Type.GetType(SelectedInputPort.Definition.DataTypeName) ?? typeof(object);
            Type outputType = Type.GetType(outputSchema.DataTypeName) ?? typeof(object);

            // 判断是否为“降维打击”（上游是数组，下游是单个元素）
            bool isIndexing = outputType.IsArray && !targetType.IsArray;

            if (isIndexing)
            {
                // 🌟 补全：降维绑定时，必须检查数组的“内部元素类型”是否与目标匹配
                Type elementType = outputType.GetElementType();
                if (elementType != null && !TypeHelper.IsTypeCompatible(elementType, targetType))
                {
                    EasyDialog.ShowSync("类型不匹配",
                        $"变量集合中的元素类型是 [{elementType.Name}]，\n无法赋值给 [{targetType.Name}] 类型的端口！");
                    return;
                }

                var (isConfirmed, indexStr) = EasyDialog.ShowTextInputSync("索引选择", "0");
                if (!isConfirmed)
                    return;

                if (!int.TryParse(indexStr, out int index) || index < 0)
                {
                    EasyDialog.ShowSync("格式错误", "索引必须是非负整数！");
                    return;
                }

                DoFinalBind(outputSchema, index);
            }
            else
            {
                // 🌟 补全：直接绑定时，检查两个类型是否兼容
                if (!TypeHelper.IsTypeCompatible(outputType, targetType))
                {
                    EasyDialog.ShowSync("类型不匹配",
                        $"无法将 [{outputType.Name}] 直接绑定到 [{targetType.Name}] 端口！");
                    return;
                }

                DoFinalBind(outputSchema);
            }

            // 3. 如果是条件节点弹出的单选框，双击后自动确认关闭
            if (_isSingleBindMode)
            {
                Confirm();
            }
        }

        private void DoFinalBind(PortDefinition port, int index = -1)
        {
            Guid targetId = SelectedNode.Id;
            string targetPort = port.Name;
            string displayName =
                index >= 0
                    ? $"{SelectedNode.Name}.{port.Name}[{index}]"
                    : $"{SelectedNode.Name}.{port.Name}";
            var linkRef = new LinkReference(targetId, targetPort, displayName);

            // 🌟 核心改进：根据节点类型决定 Key
            string bindKey;
            if (Workspace.CurrentStep is ConditionStep)
            {
                // 从我们刚才埋下的 Description 中取回 Guid 字符串
                bindKey = SelectedInputPort.Definition.Description;
            }
            else
            {
                bindKey = SelectedInputPort.Definition.Name;
            }

            // 这样就能确保同一个 Guid Key 被新的 linkRef 覆盖，实现真正的“换绑”
            Workspace.CurrentStep.LinkedSources[bindKey] = linkRef;
            SelectedInputPort.LinkedAddress = displayName;

            _lastBoundLink = linkRef;
            _lastBoundPort = port; // 🌟 记录当前绑定的端口信息
        }

        private void Confirm()
        {
            // 防御：如果用户没双击，而是单击选中了右边某个变量，直接点了"确定"按钮
            if (_lastBoundLink == null && SelectedOutputPort != null && _isSingleBindMode)
            {
                DoFinalBind(SelectedOutputPort);
            }

            var p = new DialogParameters();
            if (_lastBoundLink != null)
                p.Add("BoundLink", _lastBoundLink);
            p.Add("DataTypeName", _lastBoundPort.DataTypeName);
            RequestClose.Invoke(p, ButtonResult.OK);
        }

        public void BuildTree()
        {
            if (DisplayDataPort == null || TreeNodes == null || Workspace?.CurrentStep == null)
                return;

            DisplayDataPort.Clear();
            TreeNodes.Clear();

            // 🌟 核心逻辑：如果是单绑模式（条件节点发起的），直接跳过左侧列表的加载！
            if (!_isSingleBindMode)
            {
                if (
                    pluginProvider.ModulePlugins.TryGetValue(
                        Workspace.CurrentStep.PluginTypeName,
                        out var pluginInfo
                    )
                )
                {
                    if (pluginInfo.InputDefinitions != null)
                    {
                        foreach (var schema in pluginInfo.InputDefinitions)
                        {
                            Workspace.CurrentStep.LinkedSources.TryGetValue(
                                schema.Name,
                                out var existingLink
                            );
                            DisplayDataPort.Add(
                                new InputPortUIModel(schema, existingLink?.DisplayAddress)
                            );
                        }
                    }
                }
            }
            else
            {
                var mockNode = new InputPortUIModel(
                    new PortDefinition
                    {
                        Name = "目标变量",
                        DataTypeName = "System.Object",
                        Description = "当前准备绑定的变量",
                    },
                    ""
                );

                DisplayDataPort.Add(mockNode);
            }
            if (DisplayDataPort.Count > 0)
            {
                SelectedInputPort = DisplayDataPort[0];
            }
            // 右侧的“全局变量”和“上游算子输出”，不管是哪种模式都要正常加载
            var availableVars = FlowQueryHelper.GetAvailableVariablesTree(
                Workspace.GlobalVariables,
                Workspace.CurrentFlow.Steps,
                Workspace.CurrentStep
            );
            foreach (var item in availableVars)
            {
                TreeNodes.Add(item);
            }
        }

        public bool CanCloseDialog() => true;

        public void OnDialogClosed() { }

        public void OnDialogOpened(IDialogParameters parameters)
        {
            // 接收启动模式
            _isSingleBindMode = parameters.GetValue<bool>("IsSingleBindMode");
            BuildTree();
        }
    }
}
