using Prism.Commands;
using Prism.Mvvm;
using Prism.Dialogs;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using VisionMaster.Communications;
using VisionMaster.Models;
using UI.CustomControl;

namespace VisionMaster.ViewModels.DialogViewModels
{
    public class CommunicationSettingsViewModel : BindableBase, IDialogAware
    {
        private ObservableCollection<CommunicationConfig> _configs = new();
        private CommunicationConfig? _selectedConfig;
        private string _searchText = string.Empty;

        public DialogCloseListener RequestClose { get; set; }

        public ObservableCollection<CommunicationConfig> Configs
        {
            get => _configs;
            set => SetProperty(ref _configs, value);
        }

        public CommunicationConfig? SelectedConfig
        {
            get => _selectedConfig;
            set => SetProperty(ref _selectedConfig, value);
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                SetProperty(ref _searchText, value);
                FilterConfigs();
            }
        }

        private ObservableCollection<CommunicationConfig> _allConfigs = new();

        public DelegateCommand AddCommand { get; }
        public DelegateCommand<CommunicationConfig> DeleteCommand { get; }
        public DelegateCommand<CommunicationConfig> TestConnectionCommand { get; }
        public DelegateCommand CloseCommand { get; }

        public string Title => "通讯设置";

        public CommunicationSettingsViewModel()
        {
            AddCommand = new DelegateCommand(ExecuteAdd);
            DeleteCommand = new DelegateCommand<CommunicationConfig>(ExecuteDelete);
            TestConnectionCommand = new DelegateCommand<CommunicationConfig>(ExecuteTestConnection);
            CloseCommand = new DelegateCommand(ExecuteClose);
        }

        public bool CanCloseDialog() => true;

        public void OnDialogClosed()
        {
        }

        public void OnDialogOpened(IDialogParameters parameters)
        {
            if (parameters.ContainsKey("Configs"))
            {
                var configs = parameters.GetValue<ObservableCollection<CommunicationConfig>>("Configs");
                LoadConfigs(configs);
            }
        }

        public void LoadConfigs(ObservableCollection<CommunicationConfig> configs)
        {
            _allConfigs.Clear();
            Configs.Clear();

            foreach (var config in configs)
            {
                _allConfigs.Add(config);
                Configs.Add(config);
            }
        }

        private void FilterConfigs()
        {
            Configs.Clear();

            foreach (var config in _allConfigs)
            {
                if (string.IsNullOrWhiteSpace(SearchText) ||
                    config.ConnectionName.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                {
                    Configs.Add(config);
                }
            }
        }

        private void ExecuteAdd()
        {
            var newConfig = new CommunicationConfig();
            var result = EasyDialog.ShowPropertyGridSync("创建新通信", newConfig);
            if (!result)
            {
                return;
            }
            _allConfigs.Add(newConfig);
            Configs.Add(newConfig);

           
            SelectedConfig = newConfig;
        }

        private void ExecuteDelete(CommunicationConfig? config)
        {
            if (config == null) return;

            _allConfigs.Remove(config);
            Configs.Remove(config);
        }

        private void ExecuteTestConnection(CommunicationConfig? config)
        {
            if (config == null) return;

            try
            {
                var manager = new AdvancedCommunicationManager();
                if (manager.AddConnection(config))
                {
                    var connection = manager.GetConnection(config.ConnectionName);
                    if (connection != null && connection.Connect())
                    {
                        UI.CustomControl.Notifier.ShowSuccess($"连接 [{config.ConnectionName}] 测试成功");
                        connection.Disconnect();
                    }
                    else
                    {
                        UI.CustomControl.Notifier.ShowError($"连接 [{config.ConnectionName}] 测试失败");
                    }
                    manager.RemoveConnection(config.ConnectionName);
                }
                else
                {
                    UI.CustomControl.Notifier.ShowError($"创建连接 [{config.ConnectionName}] 失败");
                }
            }
            catch (Exception ex)
            {
                UI.CustomControl.Notifier.ShowError($"测试连接异常: {ex.Message}");
            }
        }

        private void ExecuteClose()
        {
            var parameters = new DialogParameters();
            RequestClose.Invoke(parameters, ButtonResult.OK);
        }
    }
}
