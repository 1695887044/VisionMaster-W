using Core.Interfaces;
using Prism.Commands;
using Prism.Dialogs;
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace VisionMaster.ViewModels.DialogViewModels
{
    /// <summary>
    /// 插件自定义配置视图的公共底板 ViewModel
    /// 顶部：插件图标+名称
    /// 中间：注入插件自定义视图（实现 IPluginConfigView）
    /// 底部：状态/耗时/执行/确认/取消
    /// </summary>
    public class PluginConfigShellViewModel : BindableBase, IDialogAware
    {
        private IStepConfigData _stepData;
        private IPluginConfigView _pluginView;

        #region IDialogAware

        public DialogCloseListener RequestClose { get; }

        public bool CanCloseDialog() => true;

        public void OnDialogClosed() { }

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
            if (_pluginView == null)
            {
                StatusText = "状态: 插件视图未实现 IPluginConfigView";
                return;
            }

            IsExecuting = true;
            StatusText = "状态: 执行中...";

            try
            {
                var sw = Stopwatch.StartNew();
                var result = _pluginView.OnExecute();
                sw.Stop();

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
