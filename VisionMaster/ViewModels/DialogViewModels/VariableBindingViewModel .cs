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
        private bool _isSingleBindMode = false;

        public string Title => "变量绑定";
        public IWorkspaceManager Workspace { get; init; }

        public ObservableCollection<InputPortUIModel> DisplayDataPort { get; } = new();
        public ObservableCollection<ToolItemModel> TreeNodes { get; } = new();

        public ObservableCollection<PresetOptionItem> PresetOptions { get; } = new();

        private string _constantValue;
        public string ConstantValue
        {
            get => _constantValue;
            set => SetProperty(ref _constantValue, value);
        }

        private bool _hasFunctionalEnumPort;
        public bool HasFunctionalEnumPort
        {
            get => _hasFunctionalEnumPort;
            set => SetProperty(ref _hasFunctionalEnumPort, value);
        }

        public DelegateCommand ConfirmCommand { get; set; }
        public DelegateCommand CancelCommand { get; set; }
        public DelegateCommand<string> SelectPresetCommand { get; set; }

        public DelegateCommand<InputPortUIModel> UnbindCommand =>
            new(port =>
            {
                if (port == null || _isSingleBindMode)
                    return;
                Workspace.CurrentStep.LinkedSources.Remove(port.Definition.Name);
                port.LinkedAddress = null;
            });

        public DelegateCommand<PortDefinition> DoublockClickBindCommand { get; set; }

        private ObservableCollection<PortDefinition> _displayPorts = new();
        public ObservableCollection<PortDefinition> DisplayPorts
        {
            get => _displayPorts;
            set => SetProperty(ref _displayPorts, value);
        }

        private InputPortUIModel _selectedInputPort;
        public InputPortUIModel SelectedInputPort
        {
            get => _selectedInputPort;
            set
            {
                SetProperty(ref _selectedInputPort, value);
                UpdatePresetOptions();
            }
        }

        private PortDefinition _selectedOutputPort;
        public PortDefinition SelectedOutputPort
        {
            get => _selectedOutputPort;
            set => SetProperty(ref _selectedOutputPort, value);
        }

        private ToolItemModel _selectedNode;
        public ToolItemModel SelectedNode
        {
            get => _selectedNode;
            set
            {
                SetProperty(ref _selectedNode, value);
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
            SelectPresetCommand = new DelegateCommand<string>(SelectPresetOption);
        }

        private void SelectPresetOption(string option)
        {
            ConstantValue = option;
            
            foreach (var preset in PresetOptions)
            {
                preset.IsSelected = preset.Option == option;
            }
        }

        private void UpdatePresetOptions()
        {
            PresetOptions.Clear();
            HasFunctionalEnumPort = false;

            if (SelectedInputPort == null || SelectedInputPort.Definition == null)
                return;

            var portDef = SelectedInputPort.Definition;

            if (portDef.IsFunctionalEnum && portDef.PresetOptions != null && portDef.PresetOptions.Any())
            {
                ConstantValue = portDef.PresetOptions.First();
                HasFunctionalEnumPort = true;
                foreach (var option in portDef.PresetOptions)
                {
                    PresetOptions.Add(new PresetOptionItem
                    {
                        Option = option,
                        IsSelected = option == ConstantValue
                    });
                }
              
            }
        }

        private void DoublockClickBind(PortDefinition outputSchema)
        {
            if (!_isSingleBindMode && SelectedInputPort == null)
            {
                EasyDialog.ShowSync("提示", "请先在左侧选择需要绑定的输入端口！");
                return;
            }

            Type targetType = Type.GetType(SelectedInputPort.Definition.DataTypeName) ?? typeof(object);
            Type outputType = Type.GetType(outputSchema.DataTypeName) ?? typeof(object);

            bool isIndexing = outputType.IsArray && !targetType.IsArray;

            if (isIndexing)
            {
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
                if (!TypeHelper.IsTypeCompatible(outputType, targetType))
                {
                    EasyDialog.ShowSync("类型不匹配",
                        $"无法将 [{outputType.Name}] 直接绑定到 [{targetType.Name}] 端口！");
                    return;
                }

                DoFinalBind(outputSchema);
            }

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

            string bindKey;
            if (Workspace.CurrentStep is ConditionStep)
            {
                bindKey = SelectedInputPort.Definition.Description;
            }
            else
            {
                bindKey = SelectedInputPort.Definition.Name;
            }

            Workspace.CurrentStep.LinkedSources[bindKey] = linkRef;
            SelectedInputPort.LinkedAddress = displayName;

            _lastBoundLink = linkRef;
            _lastBoundPort = port;
        }

        private void Confirm()
        {
            if (!string.IsNullOrWhiteSpace(ConstantValue) && SelectedInputPort != null)
            {
                string bindKey = SelectedInputPort.Definition.Name;
                string displayName = $"常量值: {ConstantValue}";
                var linkRef = new LinkReference(Guid.Empty, ConstantValue, displayName);
                Workspace.CurrentStep.LinkedSources[bindKey] = linkRef;
                SelectedInputPort.LinkedAddress = displayName;
                _lastBoundLink = linkRef;
                _lastBoundPort = new PortDefinition { Name = bindKey, DataTypeName = "System.String" };
            }

            if (_lastBoundLink == null && SelectedOutputPort != null && _isSingleBindMode)
            {
                DoFinalBind(SelectedOutputPort);
            }

            var p = new DialogParameters();
            if (_lastBoundLink != null)
                p.Add("BoundLink", _lastBoundLink);
            if (_lastBoundPort != null)
                p.Add("DataTypeName", _lastBoundPort.DataTypeName);
            RequestClose.Invoke(p, ButtonResult.OK);
        }

        public void BuildTree()
        {
            if (DisplayDataPort == null || TreeNodes == null || Workspace?.CurrentStep == null)
                return;

            DisplayDataPort.Clear();
            TreeNodes.Clear();

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
            _isSingleBindMode = parameters.GetValue<bool>("IsSingleBindMode");
            BuildTree();
        }

        public class PresetOptionItem : BindableBase
        {
            private string _option;
            public string Option
            {
                get => _option;
                set => SetProperty(ref _option, value);
            }

            private bool _isSelected;
            public bool IsSelected
            {
                get => _isSelected;
                set => SetProperty(ref _isSelected, value);
            }
        }
    }
}
