using Core.Interfaces;
using Prism.Commands;
using Prism.Dialogs;
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VisionMaster.Services;

namespace VisionMaster.ViewModels.DialogViewModels
{
    /// <summary>
    /// 插件自定义配置视图的公共底板 ViewModel
    /// 顶部：插件图标+名称
    /// 中间：注入插件自定义视图（实现 IPluginConfigView）
    /// 底部：状态/耗时/执行/确认/取消
    /// 试运行由 PluginTestRunner 基建统一执行（与正式运行共用插件的 RunAlgorithm）
    /// </summary>
    public class PluginConfigShellViewModel : BindableBase, IDialogAware
    {
        private IStepConfigData _stepData;
        private IPluginConfigView _pluginView;
        private IVisionPlugin _plugin;
        private readonly IWorkspaceManager _workspace;
        private readonly ILogService _logger;

        public PluginConfigShellViewModel(IWorkspaceManager workspace, ILogService logger)
        {
            _workspace = workspace;
            _logger = logger;
            ExecuteCommand = new DelegateCommand(ExecutePlugin, () => CanExecute);
            ConfirmCommand = new DelegateCommand(Confirm);
            CancelCommand = new DelegateCommand(Cancel);
        }

        #region IDialogAware

        public DialogCloseListener RequestClose { get; }

        public bool CanCloseDialog() => true;

        public void OnDialogClosed()
        {
            // 配置窗口关闭（确认/取消/叉掉均走此回调）：释放配置实例持有的非托管资源（如 HImage 预览图）
            // 配置实例由 ProcessViewModel.ResolvePluginInstance 每次 new（Activator.CreateInstance），
            // 与流程运行实例完全隔离，Dispose 不影响流程执行
            try
            {
                _plugin?.Dispose();
            }
            catch (Exception ex)
            {
                _logger?.Info($"释放插件配置实例异常: {ex.Message}");
            }
            finally
            {
                _plugin = null;
                _pluginView = null;
                PluginViewContent = null;
            }
        }

        public void OnDialogOpened(IDialogParameters parameters)
        {
            if (parameters.TryGetValue<IStepConfigData>("StepData", out var stepData))
                _stepData = stepData;

            if (parameters.TryGetValue<FrameworkElement>("PluginView", out FrameworkElement viewObj))
            {
                // 视图由插件DLL返回，同时实现 IPluginConfigView 接口

                _pluginView = viewObj.DataContext as IPluginConfigView;
                PluginViewContent = viewObj;
            }

            if (parameters.TryGetValue<IVisionPlugin>("Plugin", out var plugin))
                _plugin = plugin;

            if (_stepData != null)
            {
                PluginIcon = _stepData.Icon ?? "\uf110";
                PluginName = _stepData.StepName ?? "插件配置";
                Title = _stepData.StepName ?? "插件配置";
                PluginDescription = _stepData.Description ?? string.Empty;
                _pluginView?.Initialize(_stepData);
            }
            RaisePropertyChanged(nameof(PluginIcon));
            RaisePropertyChanged(nameof(PluginName));
            RaisePropertyChanged(nameof(PluginDescription));
            RaisePropertyChanged(nameof(PluginViewContent));
        }

        #endregion

        #region 绑定属性

        public string Title { get; set; }
        public string PluginIcon { get; private set; }
        public string PluginName { get; private set; }
        public string PluginDescription { get; private set; }
        public object PluginViewContent { get; private set; }

        private string _statusText = "状态: 待执行";
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        private string _elapsedText = "耗时: 0 ms";
        public string ElapsedText
        {
            get => _elapsedText;
            set => SetProperty(ref _elapsedText, value);
        }

        private bool _isExecuting;
        public bool IsExecuting
        {
            get => _isExecuting;
            set
            {
                if (SetProperty(ref _isExecuting, value))
                {
                    RaisePropertyChanged(nameof(CanExecute));
                }
            }
        }

        public bool CanExecute => !IsExecuting;

        #endregion

        #region 命令

        public ICommand ExecuteCommand { get; private set; }
        public ICommand ConfirmCommand { get; private set; }
        public ICommand CancelCommand { get; private set; }

        #endregion
        public PluginConfigShellViewModel()
        {
            ExecuteCommand = new DelegateCommand(ExecutePlugin, () => CanExecute);
            ConfirmCommand = new DelegateCommand(Confirm);
            CancelCommand = new DelegateCommand(Cancel);
        }
        private void ExecutePlugin()
        {
            if (_plugin == null)
            {
                StatusText = "状态: 插件实例未注入";
                return;
            }

            IsExecuting = true;
            StatusText = "状态: 执行中...";

            try
            {
                // 试运行基建：解析链接 → 灌端口 → 执行插件唯一的 RunAlgorithm
                var result = PluginTestRunner.Run(_plugin, _stepData, _workspace, _logger);

                ElapsedText = $"耗时: {result.ElapsedMs} ms";
                StatusText = result.Success
                    ? $"状态: ✅ 成功 - {result.Message}"
                    : $"状态: ❌ 失败 - {result.ErrorMessage}";
            }
            catch (Exception ex)
            {
                ElapsedText = $"耗时: 0 ms";
                StatusText = $"状态: ❌ 异常 - {ex.Message}";
            }
            finally
            {
                IsExecuting = false;
            }
        }

        private void Confirm()
        {
            _pluginView?.OnConfirm(_stepData);
            RequestClose.Invoke(ButtonResult.OK);
        }

        private void Cancel()
        {
            _pluginView?.OnCancel();
            RequestClose.Invoke(ButtonResult.Cancel);
        }
    }
}
