using Prism.Commands;
using Prism.Mvvm;
using Prism.Dialogs;
using System;
using System.Collections.ObjectModel;
using System.Windows;
using VisionMaster.Helpers;
using VisionMaster.Models;
using UI.Events;
using UI.CustomControl;

namespace VisionMaster.ViewModels.DialogViewModels
{
    /// <summary>
    /// 流程管理ViewModel
    /// </summary>
    public class FlowManagerViewModel : BindableBase, IDialogAware
    {
        private ObservableCollection<FlowItem> _flows = new();
        private FlowItem? _selectedFlow;
        private string _searchText = string.Empty;
        private ObservableCollection<FlowModel>? _originalFlows;

        public DialogCloseListener RequestClose { get; set; }

        /// <summary>
        /// 流程列表
        /// </summary>
        public ObservableCollection<FlowItem> Flows
        {
            get => _flows;
            set => SetProperty(ref _flows, value);
        }

        /// <summary>
        /// 选中的流程
        /// </summary>
        public FlowItem? SelectedFlow
        {
            get => _selectedFlow;
            set => SetProperty(ref _selectedFlow, value);
        }

        /// <summary>
        /// 搜索文本
        /// </summary>
        public string SearchText
        {
            get => _searchText;
            set
            {
                SetProperty(ref _searchText, value);
                FilterFlows();
            }
        }

        /// <summary>
        /// 所有流程（原始数据）
        /// </summary>
        private ObservableCollection<FlowItem> _allFlows = new();

        /// <summary>
        /// 加密命令
        /// </summary>
        public DelegateCommand<FlowItem> EncryptFlowCommand { get; }

        /// <summary>
        /// 解密命令
        /// </summary>
        public DelegateCommand<FlowItem> DecryptFlowCommand { get; }

        /// <summary>
        /// 启用/禁用流程命令
        /// </summary>
        public DelegateCommand<FlowItem> ToggleEnabledCommand { get; }

        /// <summary>
        /// 关闭命令
        /// </summary>
        public DelegateCommand CloseCommand { get; }

        public string Title => "流程管理";

        /// <summary>
        /// 构造函数
        /// </summary>
        public FlowManagerViewModel()
        {
            EncryptFlowCommand = new DelegateCommand<FlowItem>(ExecuteEncryptFlow);
            DecryptFlowCommand = new DelegateCommand<FlowItem>(ExecuteDecryptFlow);
            ToggleEnabledCommand = new DelegateCommand<FlowItem>(ExecuteToggleEnabled);
            CloseCommand = new DelegateCommand(ExecuteClose);
        }

        public bool CanCloseDialog() => true;

        public void OnDialogClosed()
        {
        }

        public void OnDialogOpened(IDialogParameters parameters)
        {
            if (parameters.ContainsKey("Flows"))
            {
                _originalFlows = parameters.GetValue<ObservableCollection<FlowModel>>("Flows");
                LoadFlows(_originalFlows);
            }
        }

        /// <summary>
        /// 加载流程列表
        /// </summary>
        /// <param name="flows">流程集合</param>
        public void LoadFlows(ObservableCollection<FlowModel> flows)
        {
            _allFlows.Clear();
            Flows.Clear();

            foreach (var flow in flows)
            {
                var item = new FlowItem(flow);
                _allFlows.Add(item);
                Flows.Add(item);
            }
        }

        /// <summary>
        /// 过滤流程列表
        /// </summary>
        private void FilterFlows()
        {
            Flows.Clear();

            foreach (var flow in _allFlows)
            {
                if (string.IsNullOrWhiteSpace(SearchText) ||
                    flow.Flow.FlowName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                    flow.Flow.Description.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                {
                    Flows.Add(flow);
                }
            }
        }

        /// <summary>
        /// 加密流程
        /// </summary>
        private void ExecuteEncryptFlow(FlowItem? item)
        {
            if (item == null) return;

            if (item.Flow.StepsEncrypted)
            {
                Notifier.ShowWarning("该流程步序已加密");
                return;
            }

            try
            {
                string key = EncryptionHelper.GenerateKey();
                item.Flow.EncryptedKey = key;
                item.Flow.StepsEncrypted = true;
                item.Refresh();
                
                Notifier.ShowSuccess($"流程 [{item.Flow.FlowName}] 已加密");
            }
            catch (Exception ex)
            {
                Notifier.ShowError($"加密失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 解密流程
        /// </summary>
        private void ExecuteDecryptFlow(FlowItem? item)
        {
            if (item == null) return;

            if (!item.Flow.StepsEncrypted)
            {
                Notifier.ShowWarning("该流程步序未加密");
                return;
            }

            try
            {
                item.Flow.EncryptedKey = null;
                item.Flow.StepsEncrypted = false;
                item.Refresh();
                
                Notifier.ShowSuccess($"流程 [{item.Flow.FlowName}] 已解密");
            }
            catch (Exception ex)
            {
                Notifier.ShowError($"解密失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 切换启用状态
        /// </summary>
        private void ExecuteToggleEnabled(FlowItem? item)
        {
            if (item == null) return;

            item.Flow.IsEnabled = !item.Flow.IsEnabled;
            item.Refresh();
        }

        /// <summary>
        /// 关闭
        /// </summary>
        private void ExecuteClose()
        {
            var parameters = new DialogParameters();
            RequestClose.Invoke(parameters, ButtonResult.OK);
        }
    }

    /// <summary>
    /// 流程列表项
    /// </summary>
    public class FlowItem : BindableBase
    {
        private readonly FlowModel _flow;

        /// <summary>
        /// 流程模型
        /// </summary>
        public FlowModel Flow => _flow;

        /// <summary>
        /// 调用类型文本
        /// </summary>
        public string InvokeTypeText => _flow.InvokeType switch
        {
            FlowInvokeType.Manual => "手动",
            FlowInvokeType.Timer => "定时",
            FlowInvokeType.Variable => "变量",
            FlowInvokeType.Subroutine => "子程序",
            _ => "未知"
        };

        /// <summary>
        /// 状态文本
        /// </summary>
        public string StatusText
        {
            get
            {
                if (_flow.StepsEncrypted)
                    return "🔒 步序已加密";
                
                if (_flow.RunState == FlowRunState.Running)
                    return "▶ 运行中";
                
                if (_flow.IsEnabled)
                    return "✓ 已启用";
                else
                    return "✗ 已禁用";
            }
        }

        /// <summary>
        /// 启用状态文本
        /// </summary>
        public string EnabledText => _flow.IsEnabled ? "禁用" : "启用";

        /// <summary>
        /// 构造函数
        /// </summary>
        public FlowItem(FlowModel flow)
        {
            _flow = flow;
            _flow.PropertyChanged += (s, e) => Refresh();
        }

        /// <summary>
        /// 刷新显示
        /// </summary>
        public void Refresh()
        {
            RaisePropertyChanged(nameof(InvokeTypeText));
            RaisePropertyChanged(nameof(StatusText));
            RaisePropertyChanged(nameof(EnabledText));
        }
    }
}
