using Prism.Commands;
using Prism.Mvvm;
using Prism.Dialogs;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using VisionMaster.Helpers;
using VisionMaster.Models;
using VisionMaster.Services;
using Core.Events;
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
        public FlowManagerViewModel(WorkspaceContext workspace)
        {
            _workspace = workspace;
            EncryptFlowCommand = new DelegateCommand<FlowItem>(ExecuteEncryptFlow);
            DecryptFlowCommand = new DelegateCommand<FlowItem>(ExecuteDecryptFlow);
            ToggleEnabledCommand = new DelegateCommand<FlowItem>(ExecuteToggleEnabled);
            CloseCommand = new DelegateCommand(ExecuteClose);

            // 常驻面板模式：跟随工作区当前方案自动加载流程列表
            _workspace.PropertyChanged += OnWorkspacePropertyChanged;
            LoadFlowsFromWorkspace();
        }

        private readonly WorkspaceContext _workspace;

        private void OnWorkspacePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(WorkspaceContext.CurrentSolution))
            {
                LoadFlowsFromWorkspace();
            }
        }

        /// <summary>
        /// 从当前方案加载流程列表（方案未加载时清空列表）
        /// </summary>
        private void LoadFlowsFromWorkspace()
        {
            var flows = _workspace.CurrentSolution?.Flows;
            if (flows != null)
            {
                LoadFlows(flows);
            }
            else
            {
                _allFlows.Clear();
                Flows.Clear();
            }
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
                Notifier.ShowWarning("该流程已锁定（禁止运行）");
                return;
            }

            try
            {
                string key = EncryptionHelper.GenerateKey();
                item.Flow.EncryptedKey = key;
                item.Flow.StepsEncrypted = true;
                item.Refresh();

                // 如实说明：这里的"锁定"只表达"禁止运行"，步序内容仍以明文随方案落盘
                // （真正的机密性保护另立一期：落盘加密 + 密钥管理）
                Notifier.ShowSuccess($"流程 [{item.Flow.FlowName}] 已锁定：禁止运行（注意：文件内容仍是明文）");
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
                Notifier.ShowWarning("该流程未锁定");
                return;
            }

            try
            {
                item.Flow.EncryptedKey = null;
                item.Flow.StepsEncrypted = false;
                item.Refresh();
                
                Notifier.ShowSuccess($"流程 [{item.Flow.FlowName}] 已解锁：恢复可运行");
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
        /// 调用方式文本（领域层单一口径：无自动触发显示「手动」，否则按勾选拼接）
        /// </summary>
        public string InvokeTypeText => _flow.InvokeType.DisplayText();

        // ------------------------------------------------------------------
        //  调用方式：多选勾选（直接改流程模型，落盘为位集）
        //  四位全部接通：HTTP（/flow/ 门禁）、定时（FlowTimerScheduler）、变量（上升沿触发）、
        //  子程序（「调用流程」插件 + FlowInvoker）。
        //  PendingReasonText 仍保留：将来加位而运行侧没跟上时，它把"哪一位还没接通"显示出来，
        //  避免"勾了没反应 = 软件坏了"（当前四位全接通 → 返回空串，界面自动收起那一行）。
        // ------------------------------------------------------------------

        public bool IsTimerInvoke
        {
            get => HasFlag(FlowInvokeType.Timer);
            set => SetFlag(FlowInvokeType.Timer, value);
        }

        public bool IsVariableInvoke
        {
            get => HasFlag(FlowInvokeType.Variable);
            set => SetFlag(FlowInvokeType.Variable, value);
        }

        public bool IsSubroutineInvoke
        {
            get => HasFlag(FlowInvokeType.Subroutine);
            set => SetFlag(FlowInvokeType.Subroutine, value);
        }

        /// <summary>HTTP 外部调用：唯一带门禁的位——没勾这里，/flow/{流程名} 一律 403</summary>
        public bool IsHttpInvoke
        {
            get => HasFlag(FlowInvokeType.Http);
            set => SetFlag(FlowInvokeType.Http, value);
        }

        /// <summary>
        /// 已勾选但运行侧还没接通的调用位的一句话原因（全部接通或没勾时为"[]"）。
        /// 界面把它挂在勾选框的行尾，用户看到"待接通"就不会误以为勾了没生效是坏了。
        /// </summary>
        public string PendingReasonText
        {
            get
            {
                var reasons = new System.Collections.Generic.List<string>(3);
                foreach (var flag in new[] { FlowInvokeType.Timer, FlowInvokeType.Variable, FlowInvokeType.Subroutine })
                {
                    if (HasFlag(flag) && flag.PendingReason() is { } reason)
                        reasons.Add(reason);
                }
                return reasons.Count == 0 ? string.Empty : string.Join("；", reasons);
            }
        }

        private bool HasFlag(FlowInvokeType flag) => (_flow.InvokeType & flag) != 0;

        private void SetFlag(FlowInvokeType flag, bool on)
        {
            _flow.InvokeType = on
                ? (_flow.InvokeType | flag)
                : (_flow.InvokeType & ~flag);
            Refresh();
            // 四个勾选框互相独立，但显示文本 / 待接通提示由整体位集派生：一次改动一并重发
            RaisePropertyChanged(nameof(IsTimerInvoke));
            RaisePropertyChanged(nameof(IsVariableInvoke));
            RaisePropertyChanged(nameof(IsSubroutineInvoke));
            RaisePropertyChanged(nameof(IsHttpInvoke));
            RaisePropertyChanged(nameof(PendingReasonText));
        }

        /// <summary>
        /// 状态文本
        /// </summary>
        public string StatusText
        {
            get
            {
                if (_flow.StepsEncrypted)
                    return "🔒 已锁定（禁止运行）";
                
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

        /// <summary>刷新显示（流程模型任何一处变更都会走到这里：勾选框 / 状态 / 汇总文本一起重发）</summary>
        public void Refresh()
        {
            RaisePropertyChanged(nameof(InvokeTypeText));
            RaisePropertyChanged(nameof(StatusText));
            RaisePropertyChanged(nameof(EnabledText));
            RaisePropertyChanged(nameof(IsTimerInvoke));
            RaisePropertyChanged(nameof(IsVariableInvoke));
            RaisePropertyChanged(nameof(IsSubroutineInvoke));
            RaisePropertyChanged(nameof(IsHttpInvoke));
            RaisePropertyChanged(nameof(PendingReasonText));
        }
    }
}
