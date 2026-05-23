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


        /// <summary>
        /// 对话框关闭请求监听器
        /// </summary>
        public DialogCloseListener RequestClose { get; set; }

        /// <summary>
        /// 通讯配置列表(支持UI绑定)
        /// </summary>
        public ObservableCollection<CommunicationConfig> Configs
        {
            get => field;
            set => SetProperty(ref field, value);
        } = new();

        /// <summary>
        /// 当前选中的配置
        /// </summary>
        public CommunicationConfig? SelectedConfig
        {
            get => field;
            set => SetProperty(ref field, value);
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

        public CommunicationSettingsViewModel(ICommunicationManager communicationManager)
        {
            // 使用传入的通讯管理器实例
            _communicationManager = communicationManager;
            // 初始化命令
            AddCommand = new DelegateCommand(ExecuteAdd);
            DeleteCommand = new DelegateCommand<CommunicationConfig>(ExecuteDelete);
            TestConnectionCommand = new DelegateCommand<CommunicationConfig>(ExecuteTestConnection);
            CloseCommand = new DelegateCommand(ExecuteClose);
        }


        public bool CanCloseDialog() => true;

        public void OnDialogClosed()
        {
        }

        /// <summary>
        /// 对话框打开时的处理
        /// </summary>
        /// <param name="parameters">对话框参数</param>
        public void OnDialogOpened(IDialogParameters parameters)
        {
            LoadConfigs(_communicationManager.GetAllConnections());
        }

        /// <summary>
        /// 加载配置列表
        /// </summary>
        /// <param name="configs">配置列表</param>
        public void LoadConfigs(IEnumerable<CommunicationConfig> configs)
        {
            Configs.Clear();

            // 遍历并加载每个配置
            foreach (var config in configs)
            {
                Configs.Add(config);
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
                Notifier.ShowInfo("创建新通信已取消");
                return;
            }
            _communicationManager.AddConnection(newConfig);
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
                    if (connection.IsConnected)
                    {
                        connection.Disconnect();
                    }
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
