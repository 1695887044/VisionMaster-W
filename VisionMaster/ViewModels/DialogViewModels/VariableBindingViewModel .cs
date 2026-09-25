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

        /// <summary>
        /// 本次绑定的目标步骤 —— 候选树的"我在给谁连线"锚点，也是回写 LinkedSources 的对象。
        ///
        /// 取值优先级：弹窗参数 TargetStep 显式传入 > Workspace.CurrentStep。
        /// 为什么不能只靠 CurrentStep：下钻进容器语义下它指向的是外层容器而不是被双击的步骤
        /// （画布上双击 For 里的节点，CurrentStep 仍是那个 For），
        /// 只认它就永远算不出"For/While 子层里某步骤"的上游候选。
        /// 老调用方不传 TargetStep 时行为与原来完全一致。
        /// </summary>
        private StepModel _targetStep;

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
                _targetStep?.LinkedSources.Remove(port.Definition.Name);
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
                LoadExistingConstant();
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

        /// <summary>
        /// 把「当前选中端口」已经绑定的常量值回显到常量输入框。
        ///
        /// 为什么必须在每次换端口时都做，而不是打开弹窗时做一次：
        /// Confirm 的规则是「常量框非空就写常量」，而换端口并不会清空这个框。
        /// 于是"选中 A 端口填了值 → 再切到 B 端口 → 点确定"会把 A 的值写到 B 上 ——
        /// 静默写坏另一个端口的绑定，且界面上看不出任何异常。
        /// 所以换端口要么回显该端口自己的值，要么清空，绝不能留着上一个端口的残留。
        /// </summary>
        private void LoadExistingConstant()
        {
            ConstantValue = string.Empty;

            if (_targetStep == null || SelectedInputPort?.Definition == null)
                return;

            string key = ResolveBindKey(SelectedInputPort.Definition);

            // 情形一：端口有「连线」来源（在变量绑定窗口选过常量/上游端口，或画布上连了线）。
            // 有连线时以连线为准 —— 这一点与运行期 ApplyConfigValues 的取舍一致（IsLinked 就跳过 InputValues）。
            if (_targetStep.LinkedSources != null
                && _targetStep.LinkedSources.TryGetValue(key, out var link) && link != null)
            {
                // 只有常量来源才回显进常量框；上游端口/全局变量/运行时变量由左栏以地址展示，
                // 塞进常量框会让"点确定"把连线改写成常量 —— 等于悄悄把线拆了
                if (link.NormalizeKind() == LinkKind.Constant)
                    ConstantValue = ExtractConstantText(link);
                return;
            }

            // 情形二：没有连线，值就是「手填常量」—— 它存在 InputValues 里（插件 OnConfirm 写回的那份）。
            // 早先只查了 LinkedSources，于是手填的值永远回显不出来，常量框一直是空的。
            if (_targetStep.InputValues != null
                && _targetStep.InputValues.TryGetValue(key, out var raw) && raw != null)
            {
                // 布尔按提示里的写法回显（true/false），别露出 C# 的 True/False
                ConstantValue = raw is bool flag ? (flag ? "true" : "false") : raw.ToString();
            }
        }

        /// <summary>
        /// 端口在 LinkedSources 里的键：条件节点用的是端口的描述（中文），其余用端口名。
        /// 与 DoFinalBind / Confirm 里的取法保持一致，三处必须是同一口径。
        /// </summary>
        private string ResolveBindKey(PortDefinition definition)
            => _targetStep is ConditionStep ? definition.Description : definition.Name;

        /// <summary>
        /// 从常量连线里取回原始文本。
        /// Confirm 写入时把原值放在 TargetPortName，显示地址则是「前缀 + 原值」；
        /// 优先取前者，取不到再退回剥前缀，兼容更早存下的方案。
        /// </summary>
        private static string ExtractConstantText(LinkReference link)
        {
            if (!string.IsNullOrEmpty(link.TargetPortName)
                && !string.Equals(link.TargetPortName, link.DisplayAddress, StringComparison.Ordinal))
                return link.TargetPortName;

            var address = link.DisplayAddress ?? string.Empty;
            return address.StartsWith(LinkProtocol.ConstantDisplayPrefix, StringComparison.Ordinal)
                ? address.Substring(LinkProtocol.ConstantDisplayPrefix.Length)
                : address;
        }

        private void UpdatePresetOptions()
        {
            PresetOptions.Clear();
            HasFunctionalEnumPort = false;

            if (SelectedInputPort == null || SelectedInputPort.Definition == null)
                return;

            var portDef = SelectedInputPort.Definition;

            if (
                portDef.IsFunctionalEnum
                && portDef.PresetOptions != null
                && portDef.PresetOptions.Any()
            )
            {
                // 已有值（上面回显来的）优先，只有在空着的时候才退到第一个预设项 ——
                // 无条件取 First() 会把刚回显出来的值冲掉，用户看到的就不是实际生效的值了
                if (string.IsNullOrWhiteSpace(ConstantValue))
                    ConstantValue = portDef.PresetOptions.First();
                HasFunctionalEnumPort = true;
                foreach (var option in portDef.PresetOptions)
                {
                    PresetOptions.Add(
                        new PresetOptionItem
                        {
                            Option = option,
                            IsSelected = option == ConstantValue,
                        }
                    );
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

            Type targetType =
                Type.GetType(SelectedInputPort.Definition.DataTypeName) ?? typeof(object);
            Type outputType = Type.GetType(outputSchema.DataTypeName) ?? typeof(object);

            bool isIndexing = outputType.IsArray && !targetType.IsArray;

            if (isIndexing)
            {
                Type elementType = outputType.GetElementType();
                if (elementType != null && !TypeHelper.IsTypeCompatible(elementType, targetType))
                {
                    EasyDialog.ShowSync(
                        "类型不匹配",
                        $"变量集合中的元素类型是 [{elementType.Name}]，\n无法赋值给 [{targetType.Name}] 类型的端口！"
                    );
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
                    EasyDialog.ShowSync(
                        "类型不匹配",
                        $"无法将 [{outputType.Name}] 直接绑定到 [{targetType.Name}] 端口！"
                    );
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
            // 连线类型由候选节点自带（FlowQueryHelper 构造时指定），不再靠 Id 是否为空反推
            LinkKind kind = SelectedNode.DefaultLinkKind;
            string displayName;

            // 运行时变量引用：使用 Runtime. 前缀，与协议层 marker Guid 约定一致
            if (kind == LinkKind.RuntimeVariable)
            {
                displayName = $"Runtime.{port.Name}";
            }
            else
            {
                displayName =
                    index >= 0
                        ? $"{SelectedNode.Name}.{port.Name}[{index}]"
                        : $"{SelectedNode.Name}.{port.Name}";
            }
            var linkRef = new LinkReference(kind, targetId, targetPort, displayName)
            {
                // 全局变量连线带上稳定身份：变量改名后仍能命中，不必依赖 TargetPortName 里的旧名字。
                // 非变量来源的端口这里是 Guid.Empty，解析器按"无身份"处理并退回按名查找。
                TargetVariableId = port.VariableId
            };

            // P0-③：单绑模式下弹窗只负责"把用户选的变量还给出题人"（经 BoundLink 回传），
            // 绝不能直写目标步骤的 LinkedSources——此时目标可能是条件节点，
            // bindKey 会落到中文描述上（如"当前准备绑定的变量"），在活模型里留下垃圾键
            if (!_isSingleBindMode && _targetStep != null)
            {
                string bindKey;
                if (_targetStep is ConditionStep)
                {
                    bindKey = SelectedInputPort.Definition.Description;
                }
                else
                {
                    bindKey = SelectedInputPort.Definition.Name;
                }

                _targetStep.LinkedSources[bindKey] = linkRef;
            }
            SelectedInputPort.LinkedAddress = displayName;

            _lastBoundLink = linkRef;
            _lastBoundPort = port;
        }

        private void Confirm()
        {
            if (!string.IsNullOrWhiteSpace(ConstantValue) && SelectedInputPort != null)
            {
                string bindKey = SelectedInputPort.Definition.Name;
                string displayName = $"{LinkProtocol.ConstantDisplayPrefix}{ConstantValue}";
                var linkRef = new LinkReference(LinkKind.Constant, Guid.Empty, ConstantValue, displayName);
                // P0-③：同 DoFinalBind，单绑模式下常量也只回传、不写活模型
                if (!_isSingleBindMode && _targetStep != null)
                    _targetStep.LinkedSources[bindKey] = linkRef;
                SelectedInputPort.LinkedAddress = displayName;
                _lastBoundLink = linkRef;
                _lastBoundPort = new PortDefinition
                {
                    Name = bindKey,
                    DataTypeName = "System.String",
                };
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

        public void BuildTree(IDialogParameters parameters = null)
        {
            if (DisplayDataPort == null || TreeNodes == null || _targetStep == null)
                return;

            DisplayDataPort.Clear();
            TreeNodes.Clear();

            if (!_isSingleBindMode)
            {
                if (
                    pluginProvider.ModulePlugins.TryGetValue(
                        _targetStep.PluginTypeName,
                        out var pluginInfo
                    )
                )
                {
                    if (pluginInfo.InputDefinitions != null)
                    {
                        foreach (var schema in pluginInfo.InputDefinitions)
                        {
                            _targetStep.LinkedSources.TryGetValue(
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
                // 单绑定模式：优先使用外部指定的目标端口（插件自定义视图发来的链接请求）
                string portName = "目标变量";
                string typeName = "System.Object";
                string desc = "当前准备绑定的变量";
                if (
                    parameters != null
                    && parameters.TryGetValue<string>("TargetPortName", out var targetPort)
                    && !string.IsNullOrEmpty(targetPort)
                )
                {
                    portName = targetPort;
                    desc = "当前准备绑定的输入端口";
                    typeName =
                        parameters.TryGetValue<string>("TargetTypeName", out var tn)
                        && !string.IsNullOrEmpty(tn)
                            ? tn
                            : "System.Object";
                }

                var mockNode = new InputPortUIModel(
                    new PortDefinition
                    {
                        Name = portName,
                        DataTypeName = typeName,
                        Description = desc,
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
                _targetStep
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

            // 目标步骤：外部显式传入优先（For/条件弹窗的下钻场景），否则退回 Workspace.CurrentStep。
            // 单靠 CurrentStep 会在"下钻进容器"时指错人：它指向的是容器本身，不是被双击的那个步骤。
            _targetStep =
                parameters != null
                && parameters.TryGetValue<StepModel>("TargetStep", out var explicitTarget)
                && explicitTarget != null
                    ? explicitTarget
                    : Workspace?.CurrentStep;

            BuildTree(parameters);
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
