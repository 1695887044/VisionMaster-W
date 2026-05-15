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
    /// <summary>
    /// 通讯设置对话框的ViewModel
    /// 负责管理通讯连接配置的增删改查操作
    /// </summary>
    public class CommunicationSettingsViewModel : BindableBase, IDialogAware
    {
        // 通讯管理器实例，用于管理通讯连接
        private readonly ICommunicationManager _communicationManager;

        // 通讯配置集合(过滤后显示)
        private ObservableCollection<CommunicationConfig> _configs = new();

        // 当前选中的配置
        private CommunicationConfig? _selectedConfig;

        // 搜索文本
        private string _searchText = string.Empty;

        // 所有配置(未过滤)
        private ObservableCollection<CommunicationConfig> _allConfigs = new();

        /// <summary>
        /// 对话框关闭请求监听器
        /// </summary>
        public DialogCloseListener RequestClose { get; set; }

        /// <summary>
        /// 通讯配置列表(支持UI绑定)
        /// </summary>
        public ObservableCollection<CommunicationConfig> Configs
        {
            get => _configs;
            set => SetProperty(ref _configs, value);
        }

        /// <summary>
        /// 当前选中的配置
        /// </summary>
        public CommunicationConfig? SelectedConfig
        {
            get => _selectedConfig;
            set => SetProperty(ref _selectedConfig, value);
        }

        /// <summary>
        /// 搜索文本，用于过滤配置列表
        /// </summary>
        public string SearchText
        {
            get => _searchText;
            set
            {
                SetProperty(ref _searchText, value);
                FilterConfigs();
            }
        }

        /// <summary>
        /// 添加新配置命令
        /// </summary>
        public DelegateCommand AddCommand { get; }

        /// <summary>
        /// 删除配置命令
        /// </summary>
        public DelegateCommand<CommunicationConfig> DeleteCommand { get; }

        /// <summary>
        /// 测试连接命令
        /// </summary>
        public DelegateCommand<CommunicationConfig> TestConnectionCommand { get; }

        /// <summary>
        /// 关闭对话框命令
        /// </summary>
        public DelegateCommand CloseCommand { get; }

        /// <summary>
        /// 对话框标题
        /// </summary>
        public string Title => "通讯设置";

        /// <summary>
        /// 构造函数，初始化通讯管理器
        /// </summary>
        public CommunicationSettingsViewModel()
        {
            // 创建通讯管理器实例
            _communicationManager = new AdvancedCommunicationManager();

            // 初始化命令
            AddCommand = new DelegateCommand(ExecuteAdd);
            DeleteCommand = new DelegateCommand<CommunicationConfig>(ExecuteDelete);
            TestConnectionCommand = new DelegateCommand<CommunicationConfig>(ExecuteTestConnection);
            CloseCommand = new DelegateCommand(ExecuteClose);
        }

        /// <summary>
        /// 判断是否可以关闭对话框
        /// </summary>
        /// <returns>始终返回true</returns>
        public bool CanCloseDialog() => true;

        /// <summary>
        /// 对话框关闭时的处理
        /// </summary>
        public void OnDialogClosed()
        {
            // 停止通讯管理器
            _communicationManager.StopAll();
        }

        /// <summary>
        /// 对话框打开时的处理
        /// </summary>
        /// <param name="parameters">对话框参数</param>
        public void OnDialogOpened(IDialogParameters parameters)
        {
            // 从参数中获取配置列表
            if (parameters.ContainsKey("Configs"))
            {
                var configs = parameters.GetValue<ObservableCollection<CommunicationConfig>>("Configs");
                LoadConfigs(configs);
            }
        }

        /// <summary>
        /// 加载配置列表
        /// </summary>
        /// <param name="configs">配置列表</param>
        public void LoadConfigs(ObservableCollection<CommunicationConfig> configs)
        {
            _allConfigs.Clear();
            Configs.Clear();

            // 遍历并加载每个配置
            foreach (var config in configs)
            {
                _allConfigs.Add(config);

                // 将配置添加到通讯管理器
                _communicationManager.AddConnection(config);

                Configs.Add(config);
            }
        }

        /// <summary>
        /// 根据搜索文本过滤配置列表
        /// </summary>
        private void FilterConfigs()
        {
            Configs.Clear();

            foreach (var config in _allConfigs)
            {
                // 如果搜索文本为空或配置名称包含搜索文本，则显示
                if (string.IsNullOrWhiteSpace(SearchText) ||
                    config.ConnectionName.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                {
                    Configs.Add(config);
                }
            }
        }

        /// <summary>
        /// 执行添加配置操作
        /// </summary>
        private void ExecuteAdd()
        {
            // 创建新的通讯配置
            var newConfig = new CommunicationConfig();

            // 显示属性编辑对话框
            var result = EasyDialog.ShowPropertyGridSync("创建新通信", newConfig);
            if (!result)
            {
                return;
            }

            // 添加到通讯管理器
            _communicationManager.AddConnection(newConfig);

            // 添加到集合
            _allConfigs.Add(newConfig);
            Configs.Add(newConfig);

            // 选中新配置
            SelectedConfig = newConfig;
        }

        /// <summary>
        /// 执行删除配置操作
        /// </summary>
        /// <param name="config">要删除的配置</param>
        private void ExecuteDelete(CommunicationConfig? config)
        {
            if (config == null) return;

            // 从通讯管理器移除
            _communicationManager.RemoveConnection(config.ConnectionName);

            // 从集合移除
            _allConfigs.Remove(config);
            Configs.Remove(config);
        }

        /// <summary>
        /// 执行测试连接操作
        /// </summary>
        /// <param name="config">要测试的配置</param>
        private void ExecuteTestConnection(CommunicationConfig? config)
        {
            if (config == null) return;

            try
            {
                // 使用通讯管理器测试连接
                var connection = _communicationManager.GetConnection(config.ConnectionName);
                if (connection != null)
                {
                    // 如果已连接，先断开
                    if (connection.IsConnected)
                    {
                        connection.Disconnect();
                    }

                    // 尝试连接
                    if (connection.Connect())
                    {
                        Notifier.ShowSuccess($"连接 [{config.ConnectionName}] 测试成功");
                        connection.Disconnect();
                    }
                    else
                    {
                        Notifier.ShowError($"连接 [{config.ConnectionName}] 测试失败");
                    }
                }
                else
                {
                    // 连接不存在，创建临时连接测试
                    if (_communicationManager.AddConnection(config))
                    {
                        var testConnection = _communicationManager.GetConnection(config.ConnectionName);
                        if (testConnection != null && testConnection.Connect())
                        {
                            Notifier.ShowSuccess($"连接 [{config.ConnectionName}] 测试成功");
                            testConnection.Disconnect();
                        }
                        else
                        {
                            Notifier.ShowError($"连接 [{config.ConnectionName}] 测试失败");
                        }
                        _communicationManager.RemoveConnection(config.ConnectionName);
                    }
                    else
                    {
                        Notifier.ShowError($"创建连接 [{config.ConnectionName}] 失败");
                    }
                }
            }
            catch (Exception ex)
            {
                Notifier.ShowError($"测试连接异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 执行关闭对话框操作
        /// </summary>
        private void ExecuteClose()
        {
            var parameters = new DialogParameters();
            RequestClose.Invoke(parameters, ButtonResult.OK);
        }
    }
}
