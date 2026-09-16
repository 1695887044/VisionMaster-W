using Prism.Commands;
using Prism.Mvvm;
using Prism.Dialogs;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
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
        // 通讯管理器实例，用于管理通讯连接（具体类型：Connect/Disconnect 不在接口上）
        private readonly AdvancedCommunicationManager _communicationManager;


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

        /// <summary>配置集合过滤视图（搜索/状态筛选）</summary>
        private readonly ICollectionView _configsView;

        private string _searchText = "";
        /// <summary>搜索关键字（匹配连接名称 / IP:端口 / 协议）</summary>
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                    _configsView.Refresh();
            }
        }

        private string _statusFilter = "全部状态";
        /// <summary>状态筛选：全部状态 / 在线 / 离线</summary>
        public string StatusFilter
        {
            get => _statusFilter;
            set
            {
                if (SetProperty(ref _statusFilter, value))
                    _configsView.Refresh();
            }
        }

        /// <summary>状态筛选下拉选项</summary>
        public string[] StatusFilters { get; } = { "全部状态", "在线", "离线" };

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
        /// 连接/断开切换命令：建立常驻连接（轮询变量依赖此状态）
        /// </summary>
        public DelegateCommand<CommunicationConfig> ToggleConnectionCommand { get; }

        /// <summary>
        /// 编辑参数命令（PropertyGrid 弹窗修改 IP/端口等配置）
        /// </summary>
        public DelegateCommand<CommunicationConfig> EditCommand { get; }

        /// <summary>
        /// 关闭对话框命令
        /// </summary>
        public DelegateCommand CloseCommand { get; }

        /// <summary>
        /// 对话框标题
        /// </summary>
        public string Title => "通讯设置";

        public CommunicationSettingsViewModel(AdvancedCommunicationManager communicationManager)
        {
            // 使用传入的通讯管理器实例（具体类型：Connect/Disconnect 不在接口上）
            _communicationManager = communicationManager;
            // 初始化过滤视图（搜索/状态筛选的数据源）
            _configsView = CollectionViewSource.GetDefaultView(Configs);
            _configsView.Filter = FilterConfig;
            // 初始化命令
            AddCommand = new DelegateCommand(ExecuteAdd);
            DeleteCommand = new DelegateCommand<CommunicationConfig>(ExecuteDelete);
            TestConnectionCommand = new DelegateCommand<CommunicationConfig>(ExecuteTestConnection);
            ToggleConnectionCommand = new DelegateCommand<CommunicationConfig>(ExecuteToggleConnection);
            EditCommand = new DelegateCommand<CommunicationConfig>(ExecuteEdit);
            CloseCommand = new DelegateCommand(ExecuteClose);
        }

        /// <summary>搜索 + 状态过滤谓词</summary>
        private bool FilterConfig(object obj)
        {
            if (obj is not CommunicationConfig config) return false;

            // 状态筛选
            if (StatusFilter == "在线" && config.State != ConnectionState.Connected) return false;
            if (StatusFilter == "离线" && config.State == ConnectionState.Connected) return false;

            // 关键字（名称 / IP:端口 / 协议）
            var query = SearchText?.Trim();
            if (string.IsNullOrEmpty(query)) return true;
            return config.ConnectionName.Contains(query, StringComparison.OrdinalIgnoreCase)
                || (config.Config.ToString()?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
                || config.Protocol.ToString().Contains(query, StringComparison.OrdinalIgnoreCase);
        }


        public bool CanCloseDialog() => true;

        public void OnDialogClosed()
        {
            // 订阅必须成对：Manager 是单例，不退订会导致每次打开对话框泄漏一份订阅
            _communicationManager.ConnectionStateChanged -= OnConnectionStateChanged;
        }

        /// <summary>
        /// 对话框打开时的处理
        /// </summary>
        /// <param name="parameters">对话框参数</param>
        public void OnDialogOpened(IDialogParameters parameters)
        {
            // 非阻塞的连接/断开由状态事件回报结果，所以打开时订阅、关闭时退订
            _communicationManager.ConnectionStateChanged += OnConnectionStateChanged;
            LoadConfigs(_communicationManager.GetAllConnections());
        }

        /// <summary>本对话框发起的"连接"等待回报的连接名（收到状态事件即清空，保证只提示一次）</summary>
        private string? _pendingConnect;

        /// <summary>本对话框发起的"断开"等待回报的连接名</summary>
        private string? _pendingDisconnect;

        /// <summary>
        /// 连接状态变化回报。
        /// 事件由连接专属线程触发，而 Notifier 与集合视图都只能在 UI 线程碰：必须先封送。
        /// </summary>
        private void OnConnectionStateChanged(object? sender, ConnectionStateChangedEventArgs e)
            => VisionMaster.Helpers.SafeDispatch.BeginInvoke(() => HandleStateChanged(e));

        private void HandleStateChanged(ConnectionStateChangedEventArgs e)
        {
            if (e.ConnectionName == _pendingConnect)
            {
                if (e.NewState == ConnectionState.Connected)
                {
                    _pendingConnect = null;
                    Notifier.ShowSuccess($"连接 [{e.ConnectionName}] 已建立（轮询已启动）");
                }
                else if (e.NewState == ConnectionState.Reconnecting)
                {
                    // 首次建连失败：如实说"后台仍在重试"，而不是报"建立失败"（那是终态语气，
                    // 实际 Worker 还会按退避继续连，直到重试预算用尽才进 Error）
                    _pendingConnect = null;
                    Notifier.ShowError($"连接 [{e.ConnectionName}] 未建立，后台按退避重试中");
                }
                else
                {
                    return; // Connecting 等中间态不提示，避免刷屏
                }
            }
            else if (e.ConnectionName == _pendingDisconnect && e.NewState == ConnectionState.Disconnected)
            {
                _pendingDisconnect = null;
                Notifier.ShowInfo($"连接 [{e.ConnectionName}] 已断开");
            }
            else
            {
                return;
            }

            _configsView.Refresh(); // 状态变了，"在线/离线"筛选结果要跟着变
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
        /// 编辑参数：在**副本**上用 PropertyGrid 修改，确定后才回写到活对象。
        /// 直接编辑活对象会留下"点了取消也已经被改脏"的隐患——PropertyGrid 是即时写入属性值的，
        /// 而 ShellViewModel 关闭时会无条件 SaveConfigAsync()，脏数据就会落盘。
        /// </summary>
        private void ExecuteEdit(CommunicationConfig? config)
        {
            if (config == null) return;

            // Clone() 会深拷链路配置且不触碰活对象，但它刻意把名字加了 "_Copy" 后缀；
            // 这里改回原名只是为了弹窗显示正常——用户一旦在弹窗里改名字，副本名就会与原名不同，
            // 正好当作"是否改名"的判据
            var copy = config.Clone();
            copy.ConnectionName = config.ConnectionName;

            var ok = EasyDialog.ShowPropertyGridSync($"编辑 [{config.ConnectionName}]", copy);
            if (!ok) return; // 取消：活对象从未被改动，无需回滚

            if (!string.Equals(copy.ConnectionName, config.ConnectionName, StringComparison.Ordinal))
            {
                // 禁止改名：Manager.UpdateConnection 是按 ConnectionName 定位旧连接的，
                // 名字一变就变成"新增一条 + 旧连接无人更新"，旧连接被孤立、已注册变量也会静默失联
                Notifier.ShowWarning($"不允许修改连接名称，[{config.ConnectionName}] 的本次修改已放弃。如需改名请删除后重新添加。");
                return;
            }

            config.CopyFrom(copy);
            _communicationManager.UpdateConnection(config); // 已连接时新参数在下次重连后生效
            _configsView.Refresh(); // 参与搜索的字段（IP/端口/协议）可能变了，刷新筛选结果
        }

        /// <summary>
        /// 连接/断开切换（**非阻塞**）：只登记意图后立即返回，真正的建连/断开由连接专属线程排队执行，
        /// 结果经 <see cref="AdvancedCommunicationManager.ConnectionStateChanged"/> 回报（见 HandleStateChanged）。
        /// 旧实现走同步 Connect/Disconnect，会在 UI 线程上硬等一个 TimeoutMs（默认 3 秒）而卡住界面。
        /// </summary>
        private void ExecuteToggleConnection(CommunicationConfig? config)
        {
            if (config == null) return;

            try
            {
                if (config.State == ConnectionState.Connected)
                {
                    // 先登记等待标记再发起：状态事件可能来得极快，标记晚设就会漏掉回报
                    _pendingDisconnect = config.ConnectionName;
                    _communicationManager.RequestDisconnect(config.ConnectionName);
                    Notifier.ShowInfo($"正在断开 [{config.ConnectionName}] …");
                }
                else
                {
                    _pendingConnect = config.ConnectionName;
                    _communicationManager.RequestConnect(config.ConnectionName);
                    Notifier.ShowInfo($"正在连接 [{config.ConnectionName}] …");
                }
            }
            catch (Exception ex)
            {
                // 登记阶段就失败（例如该连接不存在、命令队列已满）必须清掉等待标记，
                // 否则后面一条无关的状态事件会被误当成"本次操作的结果"来提示
                _pendingConnect = null;
                _pendingDisconnect = null;
                Notifier.ShowError($"连接操作异常：{ex.Message}");
            }
        }

        /// <summary>
        /// 测试连接（**只探测，不改变连接状态**）。
        /// 旧实现先 Disconnect 再 Connect 最后又 Disconnect，会把正在使用的连接打断，
        /// 还绕过连接专属线程直接用裸连接，与状态机打架；<see cref="AdvancedCommunicationManager.TestConnection"/>
        /// 对已连接的连接直接返回成功，不触碰现有链路。
        /// </summary>
        /// <param name="config">要测试的配置</param>
        private void ExecuteTestConnection(CommunicationConfig? config)
        {
            if (config == null) return;

            try
            {
                var ok = _communicationManager.TestConnection(config.ConnectionName);
                if (ok)
                    Notifier.ShowSuccess($"连接 [{config.ConnectionName}] 测试成功");
                else
                    Notifier.ShowError($"连接 [{config.ConnectionName}] 测试失败，请检查 IP/端口后重试");
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
