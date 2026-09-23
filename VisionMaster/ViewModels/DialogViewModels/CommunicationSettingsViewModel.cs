using Prism.Commands;
using Prism.Mvvm;
using Prism.Dialogs;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using VisionMaster.Communications;
using VisionMaster.Models;
using VisionMaster.Services;
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

        /// <summary>弹窗服务：用于打开扫描组编辑器（子弹窗）</summary>
        private readonly IDialogService _dialogs;

        /// <summary>工作区：删除连接前要数清"这条连接下挂着多少变量"（删连接不删变量，但必须报出影响面）</summary>
        private readonly IWorkspaceManager _workspace;


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
                    RefreshView();
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
                    RefreshView();
            }
        }

        /// <summary>状态筛选下拉选项</summary>
        public string[] StatusFilters { get; } = { "全部状态", "在线", "离线" };

        /// <summary>
        /// 当前选中的配置（表格的单选行，供"编辑/删除/扫描组"这类只作用于一条的操作使用）
        /// </summary>
        public CommunicationConfig? SelectedConfig
        {
            get => field;
            set => SetProperty(ref field, value);
        }

        /// <summary>
        /// 表格的多选行（由 <see cref="UI.Behaviors.DataGridSelectionBehavior"/> 双向同步）。
        /// 与 <see cref="SelectedConfig"/> 并存：单选属性表达"焦点行"，本集合表达"要批量处理哪些行"。
        /// </summary>
        public ObservableCollection<CommunicationConfig> SelectedConfigs { get; } = new();

        /// <summary>已选行数（底栏"已选 N 项"文案）</summary>
        public int SelectedCount => SelectedConfigs.Count;

        /// <summary>是否有选中行：批量按钮的 IsEnabled 直接绑它（int 不能直接当 bool 用）</summary>
        public bool HasSelection => SelectedConfigs.Count > 0;

        /// <summary>表格里一条连接都没有——真·空状态，引导"添加设备"</summary>
        public bool IsEmpty => Configs.Count == 0;

        /// <summary>
        /// 有连接但被搜索/状态筛选挡光了——另一种空状态，引导"换个条件"。
        /// 两种空状态必须分开：明明有 20 条连接却提示"还没有添加设备"，
        /// 用户会以为数据丢了而重新录入一遍。
        /// </summary>
        public bool IsFilteredEmpty => Configs.Count > 0 && !_configsView.Cast<object>().Any();

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
        /// 扫描组命令：打开扫描组编辑器（一条连接一个组表，变量只存组名引用）
        /// </summary>
        public DelegateCommand<CommunicationConfig> EditScanGroupsCommand { get; }

        /// <summary>批量连接：连接表格里当前选中的若干条</summary>
        public DelegateCommand ConnectSelectedCommand { get; }

        /// <summary>批量断开：断开表格里当前选中的若干条</summary>
        public DelegateCommand DisconnectSelectedCommand { get; }

        /// <summary>一键连接：连接当前筛选结果里的全部连接</summary>
        public DelegateCommand ConnectAllCommand { get; }

        /// <summary>一键断开：断开当前筛选结果里的全部连接</summary>
        public DelegateCommand DisconnectAllCommand { get; }

        /// <summary>全选当前筛选结果（不必按住 Ctrl 也能多选）</summary>
        public DelegateCommand SelectAllCommand { get; }

        /// <summary>清空选择</summary>
        public DelegateCommand ClearSelectionCommand { get; }

        /// <summary>
        /// 关闭对话框命令
        /// </summary>
        public DelegateCommand CloseCommand { get; }

        /// <summary>
        /// 对话框标题
        /// </summary>
        public string Title => "通讯设置";

        public CommunicationSettingsViewModel(AdvancedCommunicationManager communicationManager, IDialogService dialogs, IWorkspaceManager workspace)
        {
            // 使用传入的通讯管理器实例（具体类型：Connect/Disconnect 不在接口上）
            _communicationManager = communicationManager;
            _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
            _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            // 初始化过滤视图（搜索/状态筛选的数据源）
            _configsView = CollectionViewSource.GetDefaultView(Configs);
            _configsView.Filter = FilterConfig;
            // 初始化命令
            AddCommand = new DelegateCommand(ExecuteAdd);
            DeleteCommand = new DelegateCommand<CommunicationConfig>(ExecuteDelete);
            // 正在测试的那条连接置灰：测试是"连一次再关掉"，同一条连接并发测两次会互踩。
            // canExecute 写在命令上（项目约定），置灰状态由 _testingConnection 驱动
            TestConnectionCommand = new DelegateCommand<CommunicationConfig>(ExecuteTestConnection,
                c => c != null && !string.Equals(c.ConnectionName, _testingConnection, StringComparison.Ordinal));
            ToggleConnectionCommand = new DelegateCommand<CommunicationConfig>(ExecuteToggleConnection);
            EditCommand = new DelegateCommand<CommunicationConfig>(ExecuteEdit);
            EditScanGroupsCommand = new DelegateCommand<CommunicationConfig>(ExecuteEditScanGroups);
            ConnectSelectedCommand = new DelegateCommand(ExecuteConnectSelected);
            DisconnectSelectedCommand = new DelegateCommand(ExecuteDisconnectSelected);
            ConnectAllCommand = new DelegateCommand(ExecuteConnectAll);
            DisconnectAllCommand = new DelegateCommand(ExecuteDisconnectAll);
            SelectAllCommand = new DelegateCommand(ExecuteSelectAll);
            ClearSelectionCommand = new DelegateCommand(SelectedConfigs.Clear);
            CloseCommand = new DelegateCommand(ExecuteClose);

            // 选中数/空状态都是"集合内容"的派生属性，靠集合变更事件统一重算——
            // 手工在每个增删点补 RaisePropertyChanged 迟早会漏一处
            SelectedConfigs.CollectionChanged += (_, _) =>
            {
                RaisePropertyChanged(nameof(SelectedCount));
                RaisePropertyChanged(nameof(HasSelection));
            };
            Configs.CollectionChanged += (_, _) => NotifyListStateChanged();
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

        /// <summary>
        /// 重算筛选结果并刷新空状态。
        /// 所有影响筛选的地方都走这里，避免"改了筛选条件但空状态没跟着更新"。
        /// </summary>
        private void RefreshView()
        {
            _configsView.Refresh();
            NotifyListStateChanged();
        }

        /// <summary>通知两个空状态属性（它们的值取决于 Configs 与筛选结果的条数，不是可写字段）</summary>
        private void NotifyListStateChanged()
        {
            RaisePropertyChanged(nameof(IsEmpty));
            RaisePropertyChanged(nameof(IsFilteredEmpty));
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

        /// <summary>本对话框发起的"连接"等待回报的连接名（收到终态事件即移除，保证只提示一次）</summary>
        private readonly HashSet<string> _pendingConnect = new(StringComparer.Ordinal);

        /// <summary>本对话框发起的"断开"等待回报的连接名</summary>
        private readonly HashSet<string> _pendingDisconnect = new(StringComparer.Ordinal);

        /// <summary>
        /// 批量操作尚未回报的连接名（连接/断开共用）。
        /// 为什么要它：批量发起 10 条会陆续回报 10 次，若逐条弹提示就是 20 个气泡盖满界面；
        /// 故批量期间逐条结果只累加计数，等这一批全部回报完再汇总成一条。
        /// </summary>
        private readonly HashSet<string> _batchPending = new(StringComparer.Ordinal);

        /// <summary>本批批量操作的动词（"连接"/"断开"），仅用于汇总文案</summary>
        private string _batchVerb = "";

        /// <summary>本批批量操作里未成功的条数</summary>
        private int _batchFailed;

        /// <summary>本批批量操作的发起总条数（发起时定下，不随回报变化）</summary>
        private int _batchTotal;

        /// <summary>正在测试连通性的连接名（非 null 时该行的"连通测试"按钮置灰，防同一条连接并发测试）</summary>
        private string? _testingConnection;

        /// <summary>
        /// 连接状态变化回报。
        /// 事件由连接专属线程触发，而 Notifier 与集合视图都只能在 UI 线程碰：必须先封送。
        /// </summary>
        private void OnConnectionStateChanged(object? sender, ConnectionStateChangedEventArgs e)
            => VisionMaster.Helpers.SafeDispatch.BeginInvoke(() => HandleStateChanged(e));

        private void HandleStateChanged(ConnectionStateChangedEventArgs e)
        {
            // 先把"这次回报要不要提示、提示什么、什么语气"算出来，再决定是逐条弹还是并入批量汇总。
            // 旧写法直接在分支里 Notifier，批量时无法抑制逐条气泡
            string? toast = null;
            ToastKind kind = ToastKind.Info;
            bool failed = false;

            if (_pendingConnect.Contains(e.ConnectionName))
            {
                if (e.NewState == ConnectionState.Connected)
                {
                    _pendingConnect.Remove(e.ConnectionName);
                    toast = $"连接 [{e.ConnectionName}] 已建立（轮询已启动）";
                    kind = ToastKind.Success;
                }
                else if (e.NewState == ConnectionState.Reconnecting)
                {
                    // 首次建连失败：如实说"后台仍在重试"，而不是报"建立失败"（那是终态语气，
                    // 实际 Worker 还会按退避继续连，直到重试预算用尽才进 Error）
                    _pendingConnect.Remove(e.ConnectionName);
                    toast = $"连接 [{e.ConnectionName}] 未建立，后台按退避重试中";
                    kind = ToastKind.Error;
                    failed = true;
                }
                else
                {
                    return; // Connecting 等中间态不提示，避免刷屏
                }
            }
            else if (e.NewState == ConnectionState.Disconnected && _pendingDisconnect.Remove(e.ConnectionName))
            {
                toast = $"连接 [{e.ConnectionName}] 已断开";
            }
            else
            {
                return;
            }

            RefreshView(); // 状态变了，"在线/离线"筛选结果要跟着变

            // 批量操作期间不逐条弹：累加计数，等这一批全部回报完汇总成一条
            if (_batchPending.Remove(e.ConnectionName))
            {
                if (failed) _batchFailed++;
                if (_batchPending.Count == 0)
                {
                    if (_batchFailed == 0)
                        Notifier.ShowSuccess($"批量{_batchVerb}完成：{_batchTotal} 条全部成功");
                    else
                        Notifier.ShowError($"批量{_batchVerb}完成：{_batchTotal} 条中 {_batchFailed} 条未成功（状态列已标出）");
                    _batchVerb = "";
                    _batchTotal = 0;
                    _batchFailed = 0;
                }
                return;
            }

            if (toast == null) return;
            switch (kind)
            {
                case ToastKind.Success: Notifier.ShowSuccess(toast); break;
                case ToastKind.Error: Notifier.ShowError(toast); break;
                default: Notifier.ShowInfo(toast); break;
            }
        }

        /// <summary>提示语气：决定用成功/错误/普通三种气泡中的哪一种</summary>
        private enum ToastKind
        {
            Info,
            Success,
            Error,
        }

        /// <summary>
        /// 加载配置列表
        /// </summary>
        /// <param name="configs">配置列表</param>
        public void LoadConfigs(IEnumerable<CommunicationConfig> configs)
        {
            Configs.Clear();
            // 多选集合里留的是上一批对象引用：不清会让"批量连接"作用在已不在表格里的旧对象上
            SelectedConfigs.Clear();

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
        /// 执行删除配置操作。
        ///
        /// A2/A3：删连接**不可撤销**（连接参数 + 扫描组表一起没），而一条连接下可能挂着几百个点位，
        /// 旧实现零确认零提示——点错一下整条产线的采集就断了，用户还得自己猜为什么变量全是空值。
        /// 现在先报出影响面再删。
        ///
        /// 口径：**只删连接，不删变量**。变量被算子/连线/监视项按名字引用，删变量会级联到流程，
        /// 通讯模块不该拥有流程语义；变量保留为"离线定义"（<see cref="VariablePersistenceService"/>
        /// 本就按离线定义落盘），但会立即停止采集，这一点必须在确认框里说清。
        /// </summary>
        /// <param name="config">要删除的配置</param>
        private void ExecuteDelete(CommunicationConfig? config)
        {
            if (config == null) return;

            var varCount = CountVariablesOf(config.ConnectionName);

            var message = $"确定要删除连接 [{config.ConnectionName}] 吗？\n" +
                          "此操作不可撤销：该连接的参数与扫描组配置会一并丢失。";
            if (varCount > 0)
            {
                message += $"\n\n该连接下有 {varCount} 个变量：它们会保留在方案里（变为离线定义，不会被一起删除），" +
                           "但从删除那一刻起停止采集。";
            }
            if (_workspace.CurrentFlow?.RunState == FlowRunState.Running)
                message += "\n\n警告：流程正在运行，删除连接会让读取这些变量的算子立即报错！";

            // ShowSync 的签名是 (title, message)——注意别把两个参数写反，反了标题会变成一句长文案
            if (!EasyDialog.ShowSync("删除连接确认", message)) return;

            // 从通讯管理器移除
            _communicationManager.RemoveConnection(config.ConnectionName);
            Configs.Remove(config);
            SelectedConfigs.Remove(config); // 多选集合里还留着这条已删对象，不清会让批量命令作用到它
            if (ReferenceEquals(SelectedConfig, config))
                SelectedConfig = null;

            Notifier.ShowInfo(varCount > 0
                ? $"连接 [{config.ConnectionName}] 已删除；其下 {varCount} 个变量已变为离线定义并停止采集"
                : $"连接 [{config.ConnectionName}] 已删除");
        }

        /// <summary>数一下某条连接下挂着多少个网络变量（删连接确认框要报出影响面）</summary>
        private int CountVariablesOf(string connectionName)
            => _workspace.GlobalVariables.OfType<NetworkVariableModel>()
                .Count(v => string.Equals(v.ConnectionName, connectionName, StringComparison.Ordinal));

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
            RefreshView(); // 参与搜索的字段（IP/端口/协议）可能变了，刷新筛选结果
        }

        /// <summary>
        /// 打开扫描组编辑器。只传连接名，活对象由编辑器自己按名字去 Manager 里取——
        /// 传引用反而危险：编辑期间这条连接可能已被删除，编辑器会拿着一个"已不在册"的对象改。
        /// </summary>
        private void ExecuteEditScanGroups(CommunicationConfig? config)
        {
            if (config == null) return;

            var parameters = new DialogParameters
            {
                { ScanGroupEditorViewModel.ConnectionNameKey, config.ConnectionName },
            };

            _dialogs.ShowDialog(ScanGroupEditorViewModel.DialogName, parameters, OnScanGroupsClosed);
        }

        /// <summary>扫描组编辑器关闭后的处理</summary>
        private void OnScanGroupsClosed(IDialogResult result)
        {
            // 取消 = 活对象从未被改动（编辑器全程在副本上编辑），无需回滚。
            // 确定 = 编辑器内部已执行 _live.CopyFrom(_working)，而 _live 与连接列表里的 config 是同一个实例
            //        （AdvancedCommunicationManager.GetAllConnections 返回的是活对象而非副本），
            //        且 CopyFrom 末尾会 NotifyScanGroupsChanged，所以「轮询」列文案已自行刷新。
            if (result.Result != ButtonResult.OK) return;

            // 组表变更同时伴随 UpdateConnection（连接被重建、状态回落到 Disconnected 等待重连），
            // 整行重新同步一次最稳，也顺带让「状态」筛选结果跟上
            RefreshView();
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
                    _pendingDisconnect.Add(config.ConnectionName);
                    _communicationManager.RequestDisconnect(config.ConnectionName);
                    Notifier.ShowInfo($"正在断开 [{config.ConnectionName}] …");
                }
                else
                {
                    _pendingConnect.Add(config.ConnectionName);
                    _communicationManager.RequestConnect(config.ConnectionName);
                    Notifier.ShowInfo($"正在连接 [{config.ConnectionName}] …");
                }
            }
            catch (Exception ex)
            {
                // 登记阶段就失败（例如该连接不存在、命令队列已满）必须清掉等待标记，
                // 否则后面一条无关的状态事件会被误当成"本次操作的结果"来提示
                _pendingConnect.Remove(config.ConnectionName);
                _pendingDisconnect.Remove(config.ConnectionName);
                Notifier.ShowError($"连接操作异常：{ex.Message}");
            }
        }

        #region 批量连接 / 断开

        /// <summary>批量连接：把表格里选中的、当前未连接的连接逐条发起建连</summary>
        private void ExecuteConnectSelected()
            => ExecuteBatch(_selectedOrNull(), wantConnected: true);

        /// <summary>批量断开：把表格里选中的、当前已连接的连接逐条发起断开</summary>
        private void ExecuteDisconnectSelected()
            => ExecuteBatch(_selectedOrNull(), wantConnected: false);

        /// <summary>
        /// 一键连接：作用于**当前筛选结果**（而非全部连接）。
        /// 用筛选结果的理由：用户先按状态筛出"离线"再点一键连接，意图就是"把这些都连上"；
        /// 若作用于全部连接，会连带把没显示在表格里的连接也改了状态，用户看不到却已经发生。
        /// </summary>
        private void ExecuteConnectAll()
            => ExecuteBatch(_configsView.OfType<CommunicationConfig>(), wantConnected: true);

        /// <summary>一键断开：作用于当前筛选结果（理由同上）</summary>
        private void ExecuteDisconnectAll()
            => ExecuteBatch(_configsView.OfType<CommunicationConfig>(), wantConnected: false);

        /// <summary>取当前多选行；一条都没选时返回 null（由 <see cref="ExecuteBatch"/> 统一提示）</summary>
        private IEnumerable<CommunicationConfig>? _selectedOrNull()
            => SelectedConfigs.Count == 0 ? null : SelectedConfigs.ToList();

        /// <summary>
        /// 全选**当前筛选结果**（不是全部连接）。
        /// 为什么要有它：DataGrid 的多选靠 Ctrl / Shift，不点破用户根本不知道能多选；
        /// 有了"全选"就能覆盖"把这些都连上"的高频场景，不必逼用户学快捷键。
        /// </summary>
        private void ExecuteSelectAll()
        {
            var visible = _configsView.OfType<CommunicationConfig>().ToList();
            if (visible.Count == 0) return;

            // 先清再加：先清能保证"全选"是幂等的（重复点不会累积重复项）
            SelectedConfigs.Clear();
            foreach (var config in visible)
                SelectedConfigs.Add(config);
        }

        /// <summary>
        /// 批量连接/断开的公共执行体。
        /// 全程只调非阻塞的 <see cref="AdvancedCommunicationManager.RequestConnect"/> /
        /// <see cref="AdvancedCommunicationManager.RequestDisconnect"/>：真正的建连/断开由各连接专属线程
        /// 并行执行，结果经 <see cref="AdvancedCommunicationManager.ConnectionStateChanged"/> 陆续回报。
        /// 绝不能在这里用同步版 Connect/Disconnect——那会串行硬等 N × TimeoutMs，界面直接假死。
        /// </summary>
        /// <param name="targets">要处理的连接；null = 用户没选任何行</param>
        /// <param name="wantConnected">true = 连接，false = 断开</param>
        private void ExecuteBatch(IEnumerable<CommunicationConfig>? targets, bool wantConnected)
        {
            if (targets == null)
            {
                Notifier.ShowWarning("请先在表格中勾选要操作的连接（按住 Ctrl / Shift 可多选）");
                return;
            }

            // 已经在目标状态的跳过：已连接的再"连接"、已断开的再"断开"都是空操作，
            // 计进总数只会让"N 条中 M 条未成功"的汇总失真
            var pending = targets
                .Where(c => wantConnected
                    ? c.State != ConnectionState.Connected
                    : c.State == ConnectionState.Connected)
                .ToList();

            if (pending.Count == 0)
            {
                Notifier.ShowInfo(wantConnected ? "所选连接均已是连接状态" : "所选连接均未连接");
                return;
            }

            string verb = wantConnected ? "连接" : "断开";

            // 上一批还没回报完就再点一次：把旧批次并进新批次（同一个字典，计数按新批重算），
            // 否则旧的 _batchPending 残留会让新批永远等不到"清空"而不再汇总
            _batchPending.Clear();
            _batchVerb = verb;
            _batchTotal = pending.Count;
            _batchFailed = 0;

            int issued = 0;
            foreach (var config in pending)
            {
                try
                {
                    if (wantConnected)
                    {
                        _pendingConnect.Add(config.ConnectionName);
                        _communicationManager.RequestConnect(config.ConnectionName);
                    }
                    else
                    {
                        _pendingDisconnect.Add(config.ConnectionName);
                        _communicationManager.RequestDisconnect(config.ConnectionName);
                    }
                    _batchPending.Add(config.ConnectionName);
                    issued++;
                }
                catch (Exception ex)
                {
                    // 单条登记失败不该中断整批：清掉它的等待标记（避免残留），计入失败数继续下一条
                    _pendingConnect.Remove(config.ConnectionName);
                    _pendingDisconnect.Remove(config.ConnectionName);
                    _batchFailed++;
                    Notifier.ShowError($"[{config.ConnectionName}] {verb}操作异常：{ex.Message}");
                }
            }

            // 一条都没登记成功：不会有任何状态回报，_batchPending 永远清不掉，必须在这里收尾
            if (issued == 0)
            {
                _batchPending.Clear();
                _batchVerb = "";
                _batchTotal = 0;
                _batchFailed = 0;
                return;
            }

            _batchTotal = issued;
            Notifier.ShowInfo($"已发起 {issued} 条连接的{verb}请求，状态将陆续刷新…");
        }

        #endregion

        /// <summary>
        /// 测试连接（**只探测，不改变连接状态**，且**非阻塞**）。
        /// 旧实现先 Disconnect 再 Connect 最后又 Disconnect，会把正在使用的连接打断，
        /// 还绕过连接专属线程直接用裸连接，与状态机打架；<see cref="AdvancedCommunicationManager.TestConnectionAsync"/>
        /// 对已连接的连接直接返回成功，不触碰现有链路。
        ///
        /// A1：同步版 <c>TestConnection</c> 内部是 <c>GetAwaiter().GetResult()</c>，在 UI 线程上硬等一个
        /// TimeoutMs（默认 3 秒）——偏偏"连不上"时必然等满，而那正是用户最需要按这个按钮的场景，
        /// 界面会白屏、被系统判定"无响应"。这里改 await 异步版，等待期间界面照常可操作。
        /// </summary>
        /// <param name="config">要测试的配置</param>
        private async void ExecuteTestConnection(CommunicationConfig? config)
        {
            if (config == null) return;

            // 同一条连接重复点击没有意义（"连一次再关掉"并发两次会互踩）。
            // 按钮置灰由命令的 canExecute 负责，这里只兜住快捷键/代码路径
            if (string.Equals(config.ConnectionName, _testingConnection, StringComparison.Ordinal)) return;

            _testingConnection = config.ConnectionName;
            TestConnectionCommand.RaiseCanExecuteChanged();
            try
            {
                // await 前未 ConfigureAwait(false)：探测在 Worker 线程上跑，但结果必须回到 UI 线程再提示
                var ok = await _communicationManager.TestConnectionAsync(config.ConnectionName);
                if (ok)
                    Notifier.ShowSuccess($"连接 [{config.ConnectionName}] 测试成功");
                else
                    Notifier.ShowError($"连接 [{config.ConnectionName}] 测试失败，请检查 IP/端口后重试");
            }
            catch (Exception ex)
            {
                Notifier.ShowError($"测试连接异常: {ex.Message}");
            }
            finally
            {
                // 无论成败都要恢复按钮：漏了这一步，这条连接的"连通测试"会永久置灰
                _testingConnection = null;
                TestConnectionCommand.RaiseCanExecuteChanged();
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
