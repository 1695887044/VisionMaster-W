using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using HalconDotNet;
using Prism.Commands;
using Prism.Dialogs;
using UI.CustomControl;
using UI.Helper;
using VisionMaster.Communications;
using VisionMaster.Models;
using VisionMaster.Services;

namespace VisionMaster.ViewModels.DialogViewModels
{
    public class DataTypeOption
    {
        public string DisplayName { get; set; }
        public Type ActualType { get; set; }
    }

    /// <summary>
    /// 来源树节点：本地池 + 每条通信连接一个子节点
    /// </summary>
    public class VariableSourceNode : BindableBase
    {
        public string Key { get; set; } = "All";            // All | Local | 连接名
        public string DisplayName { get; set; } = "全部";
        public string Icon { get; set; } = "\uf0ac";
        public bool IsNetwork { get; set; }

        private int _count;
        /// <summary>来源下变量数（INPC：新建/删除变量后计数徽标实时刷新）</summary>
        public int Count { get => _count; set => SetProperty(ref _count, value); }

        private bool _isOnline;
        /// <summary>连接在线状态（INPC：连接状态变化后徽标实时刷新）</summary>
        public bool IsOnline { get => _isOnline; set => SetProperty(ref _isOnline, value); }
    }

    /// <summary>
    /// 变量管理（变量中心）：
    /// 本地变量 + 网络变量的创建/编辑/实时监控/写值/删除；
    /// 网络变量经 NetworkVariableBridge 接入通信管理器轮询体系（镜像值自动上屏）
    /// </summary>
    public class GlobalVariableManagerViewModel : GlobalVariableViewModelBase, IDialogAware
    {
        private readonly AdvancedCommunicationManager _communicationManager;
        private readonly NetworkVariableBridge _bridge;

        /// <summary>弹窗服务：用于打开扫描组编辑器（与「通讯设置 → 操作列 → 扫描组」是同一个弹窗）</summary>
        private readonly IDialogService _dialogs;

        public string Title => "变量管理";

        #region 来源树

        public ObservableCollection<VariableSourceNode> SourceNodes { get; } = new();

        private VariableSourceNode? _selectedSource;
        public VariableSourceNode? SelectedSource
        {
            get => _selectedSource;
            set
            {
                if (SetProperty(ref _selectedSource, value))
                {
                    UpdateFilteredList();

                    // 左栏选中项是"要建哪种变量"的唯一决定者（原中栏的本地/网络胶囊已撤）：
                    // 选本地节点 → IsNetworkSource=false（明细表收起网络三列）；
                    // 选连接节点 → true，并把「所属连接」跟着换成它（草稿的默认值从这来）
                    if (value != null)
                    {
                        IsNetworkSource = value.IsNetwork;

                        if (value.IsNetwork)
                            SelectedConnection = Connections.FirstOrDefault(c => c.ConnectionName == value.Key);
                        else
                            SelectedConnection = null;
                    }
                }
            }
        }

        /// <summary>重建来源树并刷新计数（方案变量或连接变化时调用）。
        /// 本地变量与网络变量分组显示：本地节点只显示本地变量，连接节点只显示该连接的网络变量</summary>
        private void RebuildSourceTree()
        {
            var currentKey = SelectedSource?.Key ?? "Local";

            SourceNodes.Clear();
            SourceNodes.Add(new VariableSourceNode { Key = "Local", DisplayName = "本地变量", Icon = "\uf15b", Count = _workspace.GlobalVariables.Count(v => v.VariableType == VariableType.Local), IsOnline = true });

            // 系统变量（运动 / IO 点等）：由宿主按方案与设备维护，这里只读展示。
            // 单独一个来源节点的理由：它们不属于"本地变量"——放进本地池会让用户以为可以改初始值/删除，
            // 而那些操作要么没有落点（初始值不回灌设备），要么会破坏宿主维护的集合。
            SourceNodes.Add(new VariableSourceNode
            {
                Key = SystemSourceKey,
                DisplayName = "系统变量",
                Icon = "\uf013",
                Count = CountSystemVariables(),
                IsOnline = true
            });

            foreach (var conn in _communicationManager.GetAllConnections())
            {
                SourceNodes.Add(new VariableSourceNode
                {
                    Key = conn.ConnectionName,
                    DisplayName = conn.ConnectionName,
                    Icon = "\uf1eb",
                    IsNetwork = true,
                    Count = _workspace.GlobalVariables.Count(v => v is NetworkVariableModel n && n.ConnectionName == conn.ConnectionName),
                    IsOnline = _communicationManager.GetConnection(conn.ConnectionName)?.IsConnected ?? false
                });
            }

            SelectedSource = SourceNodes.FirstOrDefault(s => s.Key == currentKey) ?? SourceNodes[0];
        }

        /// <summary>轻量刷新来源计数（新建/删除变量后调用，不重建树不改变选中）</summary>
        private void RefreshSourceCounts()
        {
            foreach (var node in SourceNodes)
            {
                node.Count = node.Key switch
                {
                    "Local" => _workspace.GlobalVariables.Count(v => v.VariableType == VariableType.Local),
                    SystemSourceKey => CountSystemVariables(),
                    _ => _workspace.GlobalVariables.Count(v => v is NetworkVariableModel n && n.ConnectionName == node.Key)
                };
            }
        }

        /// <summary>来源树里「系统变量」节点的键（与连接名同名也不冲突：连接名不允许叫 System.*，见创建校验）。</summary>
        public const string SystemSourceKey = "System";

        /// <summary>系统维护的变量：系统变量（运动/状态）+ IO 点变量。</summary>
        private static bool IsSystemVariable(IVariable? variable)
            => variable?.VariableType is VariableType.System or VariableType.Io;

        private int CountSystemVariables()
            => _workspace.GlobalVariables.Count(IsSystemVariable);

        private void RefreshSourceStates()
        {
            foreach (var node in SourceNodes.Where(s => s.IsNetwork))
            {
                node.IsOnline = _communicationManager.GetConnection(node.Key)?.IsConnected ?? false;
            }
            RefreshTree(); // 节点 IsConnected 徽标同步
        }

        #endregion

        #region 明细筛选

        private string _searchText = "";
        public string SearchText
        {
            get => _searchText;
            set
            {
                SetProperty(ref _searchText, value);
                UpdateFilteredList();
            }
        }

        public ObservableCollection<VariableNode> FilteredDisplayNodes { get; } = new();

        protected override void UpdateFlatList()
        {
            base.UpdateFlatList();
            UpdateFilteredList();
        }

        private void UpdateFilteredList()
        {
            FilteredDisplayNodes.Clear();
            foreach (var node in FilterDisplayNodes(DisplayNodes, SelectedSource?.Key ?? "Local", SearchText?.Trim() ?? ""))
                FilteredDisplayNodes.Add(node);
        }

        /// <summary>
        /// 明细表的筛选：来源（本地 / 某条连接）+ 搜索。
        ///
        /// 抽成**纯函数**是为了能直接被断言（VM 的构造函数要通信管理器、桥接器、弹窗服务，
        /// 在冒烟里凑齐这些依赖不划算；而这段逻辑恰恰是最容易出错、也最该守住的一段）。
        ///
        /// ★ 子节点必须跟随父级 —— 这条是本次修复的核心：
        /// 旧实现只对"根节点"判来源、判搜索，子节点（数组元素行）无条件加进列表。
        /// 后果是切到"网络变量"时，本地数组变量的 [0]/[1] 会以
        /// "没有变量名、没有来源、类型还被列宽截断"的残行留在表里，既看不懂也不能操作。
        /// DisplayNodes 是"根 + 它的子节点紧随其后"的扁平表（见基类 UpdateFlatList），
        /// 所以一个游标量就足以表达"跟随父级"。
        /// </summary>
        public static List<VariableNode> FilterDisplayNodes(
            IEnumerable<VariableNode> displayNodes, string sourceKey, string query)
        {
            var result = new List<VariableNode>();
            bool rootAccepted = false;

            foreach (var node in displayNodes)
            {
                if (node.IsRootNode)
                {
                    // 来源：本地分组只显本地变量；系统分组只显系统/IO 变量；连接分组只显该连接的变量。
                    // "本地"必须显式排除系统变量——它们也在 GlobalVariables 集合里，
                    // 不排除就会以"可以编辑/删除"的样子混进本地池。
                    bool matchSource = sourceKey switch
                    {
                        "Local" => !node.IsNetwork && !node.IsSystemOwned,
                        SystemSourceKey => node.IsSystemOwned,
                        _ => node.IsNetwork && node.SourceLabel == sourceKey
                    };

                    // 搜索：名称 / 描述 / 地址，任一命中即可
                    bool matchSearch = string.IsNullOrEmpty(query)
                        || node.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                        || (node.Description?.IndexOf(query, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
                        || (node.Address?.IndexOf(query, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0;

                    rootAccepted = matchSource && matchSearch;
                    if (!rootAccepted) continue;
                }
                else if (!rootAccepted)
                {
                    continue;   // 数组元素行跟随父级
                }

                result.Add(node);
            }

            return result;
        }

        #endregion

        #region 新建变量（本地/网络双模式）

        private bool _isNetworkSource;
        /// <summary>
        /// 当前来源是不是网络：false=本地 true=网络。
        /// <para>由左栏「变量来源」的选中项驱动（见 <see cref="SelectedSource"/> 的 setter），
        /// 代码后置靠它收放明细表的「来源/扫描组/地址」三列，所以它必须留在 VM 上。</para>
        /// </summary>
        public bool IsNetworkSource
        {
            get => _isNetworkSource;
            set
            {
                if (SetProperty(ref _isNetworkSource, value))
                {
                    // 联动来源树：切本地→选中本地分组；切网络→保持当前网络连接或选首个
                    if (value)
                    {
                        if (SelectedSource == null || !SelectedSource.IsNetwork)
                            SelectedSource = SourceNodes.FirstOrDefault(s => s.IsNetwork);
                    }
                    else
                    {
                        SelectedSource = SourceNodes.FirstOrDefault(s => s.Key == "Local");
                    }
                }
            }
        }

        public ObservableCollection<CommunicationConfig> Connections { get; } = new();

        private CommunicationConfig? _selectedConnection;
        /// <summary>
        /// 当前选中的连接（左栏选中连接节点时同步过来）。
        /// 「添加变量」弹窗的「所属连接」默认值、「管理扫描组」的作用对象都用它。
        /// </summary>
        public CommunicationConfig? SelectedConnection
        {
            get => _selectedConnection;
            set
            {
                if (SetProperty(ref _selectedConnection, value))
                {
                    RefreshNewScanGroupOptions();

                    // 「管理扫描组」的作用对象就是这个连接，命令可用性必须跟着换
                    ManageScanGroupsCommand.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// 「添加变量」弹窗里选定的扫描组（显示值，"默认组"= 空串落库）。
        /// <para>建完**不清空**：连续录同一节拍的点位时不用每次重选。组名是连接级资源，
        /// 换连接后若旧组名不存在，<see cref="RefreshNewScanGroupOptions"/> 会回落默认组。</para>
        /// </summary>
        public string SelectedNewScanGroup
        {
            get => field;
            set => SetProperty(ref field, value);
        } = PollScheduler.DefaultGroupName;

        /// <summary>某连接的组表（默认组恒在首位）；连接名为空时只有默认组</summary>
        private IReadOnlyList<string> GetScanGroupOptions(string? connectionName)
            => _communicationManager.GetScanGroupNames(connectionName ?? string.Empty);

        /// <summary>
        /// 归一「添加变量」弹窗要用的扫描组默认值：记住的组在新连接的组表里还有就继续用，
        /// 没有就回落默认组——否则弹窗会预填一个选不中的组名。
        /// </summary>
        private void RefreshNewScanGroupOptions()
        {
            var options = GetScanGroupOptions(SelectedConnection?.ConnectionName);
            string remembered = SelectedNewScanGroup;
            SelectedNewScanGroup = !string.IsNullOrWhiteSpace(remembered) && options.Contains(remembered)
                ? remembered
                : PollScheduler.DefaultGroupName;
        }

        /// <summary>显示值 → 落库值："默认组"是虚拟组，模型里以空串表示（PollScheduler 约定：空名 → 默认组）</summary>
        private static string ToStoredScanGroup(string? displayName) =>
            string.IsNullOrWhiteSpace(displayName)
            || string.Equals(displayName, PollScheduler.DefaultGroupName, StringComparison.Ordinal)
                ? string.Empty
                : displayName;

        /// <summary>
        /// 打开扫描组编辑器。只传连接名，活对象由编辑器自己按名字去 Manager 里取
        /// （传引用反而危险：编辑期间这条连接可能已被删除，编辑器会拿着一个"已不在册"的对象改）。
        /// <para>为什么变量管理也要有这个入口：这里才是"录点位"的地方，用户是在「扫描组」下拉里
        /// 发现只有默认组、才想起要建组的——入口就放在下拉旁边，不必退出去开通讯设置。</para>
        /// </summary>
        private void ExecuteManageScanGroups()
        {
            var connection = SelectedConnection;
            if (connection == null) return;

            var parameters = new DialogParameters
            {
                { ScanGroupEditorViewModel.ConnectionNameKey, connection.ConnectionName },
            };

            _dialogs.ShowDialog(ScanGroupEditorViewModel.DialogName, parameters, OnScanGroupsClosed);
        }

        /// <summary>
        /// 扫描组编辑器关闭后重算两处「扫描组」下拉。
        /// <para>① 添加变量弹窗的默认值：新加的组要能立刻选到（<see cref="RefreshNewScanGroupOptions"/>）；</para>
        /// <para>② 右栏变量列表的下拉：组被改名/删除后，已有变量的可选项要跟上。变量模型里的组名
        /// 已由编辑器级联改好（<c>CascadeRename</c>），这里只负责让界面重新读一遍——
        /// 所以走 <see cref="RefreshTree"/> 重建节点（它按 VariableId 保留展开态），而不是自己造一套局部刷新。</para>
        /// <para>取消时活对象从未被改动，无需任何刷新。</para>
        /// </summary>
        private void OnScanGroupsClosed(IDialogResult result)
        {
            if (result.Result != ButtonResult.OK) return;

            RefreshNewScanGroupOptions();
            RefreshTree();
        }

        /// <summary>存储区显示名表：界面显示中文，落库仍用原始枚举（显示与数据解耦）</summary>
        private static readonly System.Collections.Generic.Dictionary<Enum, string> AreaDisplayNames = new()
        {
            [ModbusArea.Coils] = "线圈 (0x, 读写布尔)",
            [ModbusArea.DiscreteInputs] = "离散输入 (1x, 只读布尔)",
            [ModbusArea.InputRegisters] = "输入寄存器 (3x, 只读字)",
            [ModbusArea.HoldingRegisters] = "保持寄存器 (4x, 读写字)",
            [S7Area.DB] = "数据块 (DB)",
            [S7Area.I] = "输入映像区 (I)",
            [S7Area.Q] = "输出映像区 (Q)",
            [S7Area.M] = "内部标志位 (M)",
            [S7Area.V] = "V 区 (S7-200 Smart)",
        };

        /// <summary>某连接的存储区候选（显示名，顺序 = 枚举声明序）；连接为空/协议未实现时给空表</summary>
        private static IReadOnlyList<string> GetAreaOptions(CommunicationConfig? connection) => connection?.Protocol switch
        {
            CommunicationType.ModbusTcp => Enum.GetValues(typeof(ModbusArea)).Cast<ModbusArea>().Select(a => AreaDisplayNames[a]).ToList(),
            CommunicationType.SiemensS7 => Enum.GetValues(typeof(S7Area)).Cast<S7Area>().Select(a => AreaDisplayNames[a]).ToList(),
            _ => Array.Empty<string>(),
        };

        /// <summary>协议的默认存储区显示名（Modbus = 保持寄存器 / S7 = 内部标志位，与旧表单的默认选中一致）</summary>
        private static string GetDefaultAreaName(CommunicationType protocol) => protocol switch
        {
            CommunicationType.ModbusTcp => AreaDisplayNames[ModbusArea.HoldingRegisters],
            CommunicationType.SiemensS7 => AreaDisplayNames[S7Area.M],
            _ => string.Empty,
        };

        /// <summary>
        /// 显示名 → 存储区枚举值（按该协议的候选表反查）。
        /// 草稿里存的是显示名（属性网格渲染的是字符串），落库前必须回到枚举——
        /// 直接拿显示名当值等于把界面文案写进配置。
        /// </summary>
        private static bool TryResolveArea(CommunicationType protocol, string? displayName, out Enum area)
        {
            area = null!;
            if (string.IsNullOrWhiteSpace(displayName)) return false;

            if (protocol == CommunicationType.ModbusTcp)
            {
                foreach (ModbusArea a in Enum.GetValues(typeof(ModbusArea)))
                {
                    if (string.Equals(AreaDisplayNames[a], displayName, StringComparison.Ordinal))
                    {
                        area = a;
                        return true;
                    }
                }
            }
            else if (protocol == CommunicationType.SiemensS7)
            {
                foreach (S7Area a in Enum.GetValues(typeof(S7Area)))
                {
                    if (string.Equals(AreaDisplayNames[a], displayName, StringComparison.Ordinal))
                    {
                        area = a;
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>所选存储区是否线圈/离散输入（仅 bool 合法，且不使用位偏移）</summary>
        private static bool IsCoilOrDiscreteArea(Enum? area)
            => area is ModbusArea.Coils or ModbusArea.DiscreteInputs;

        #endregion

        /// <summary>
        /// 候选类型表（显示名 ↔ CLR 类型）。「添加变量」弹窗的类型候选从它生成，
        /// 确认时再按显示名反查回来——显示名只活在弹窗内，落库的始终是
        /// <see cref="DataTypeOption.ActualType"/>（同 R33/R34 的"名字 ≠ 值"口径）。
        ///
        /// 图像变量：给"图像显示"图元（Hmi.ImageView）当输入源用。
        /// 位图没法用文本初始值表达，所以它在变量表里的"初始值"单元格是只读的
        /// （见 VariableNode.IsEditableNode 的同名判断）。
        /// </summary>
        private static readonly List<DataTypeOption> AllTypes = new()
        {
            new DataTypeOption { DisplayName = "整数 (int)", ActualType = typeof(int) },
            new DataTypeOption { DisplayName = "小数 (double)", ActualType = typeof(double) },
            new DataTypeOption { DisplayName = "文本 (string)", ActualType = typeof(string) },
            new DataTypeOption { DisplayName = "布尔 (bool)", ActualType = typeof(bool) },
            new DataTypeOption { DisplayName = "整数数组 (int[])", ActualType = typeof(int[]) },
            new DataTypeOption { DisplayName = "小数数组 (double[])", ActualType = typeof(double[]) },
            new DataTypeOption { DisplayName = "文本数组 (string[])", ActualType = typeof(string[]) },
            new DataTypeOption { DisplayName = "布尔数组 (bool[])", ActualType = typeof(bool[]) },
            new DataTypeOption { DisplayName = "图像 (HImage)", ActualType = typeof(HImage) },
        };

        /// <summary>
        /// 某来源可选类型（弹窗的类型候选）：
        /// 网络来源下过滤文本、数组与图像——通信层仅支持标量点读，
        /// ToDataValueType 对 string/数组会兜底成 Int32，导致地址语义错误（隐患修复）；
        /// 图像更是设备侧根本没有对应点位，留着只会让人配出一个永远为空的变量。
        /// </summary>
        private static IEnumerable<DataTypeOption> TypesFor(bool isNetwork)
            => isNetwork
                ? AllTypes.Where(t =>
                    t.ActualType != typeof(string) &&
                    t.ActualType != typeof(string[]) &&
                    t.ActualType != typeof(HImage) &&
                    !t.ActualType.IsArray)
                : AllTypes;

        /// <summary>显示名 → CLR 类型；认不出返回 null（弹窗确认时的第一道闸）</summary>
        private static Type? ResolveType(string? displayName)
            => string.IsNullOrWhiteSpace(displayName)
                ? null
                : AllTypes.FirstOrDefault(t => string.Equals(t.DisplayName, displayName, StringComparison.Ordinal))?.ActualType;

        public DelegateCommand AddCommand { get; }
        public DelegateCommand<VariableNode> DeleteCommand { get; }
        public DelegateCommand<VariableNode> EditArrayCommand { get; }
        public DelegateCommand<VariableNode> ResetCommand { get; }

        /// <summary>复制变量名（HMI 控件绑定预留入口）</summary>
        public DelegateCommand<VariableNode> CopyNameCommand { get; }

        /// <summary>重命名变量（经注册表统一入口，连线/监视项/轮询注册自动跟随）</summary>
        public DelegateCommand<VariableNode> RenameCommand { get; }

        public DelegateCommand<VariableSourceNode> SelectSourceCommand { get; }

        /// <summary>
        /// 管理扫描组：打开连接级组表编辑器（新增/改名/删组/周期/预设）。
        /// <para>作用对象是新建面板里当前选中的「所属连接」，故未选连接时置灰。</para>
        /// </summary>
        public DelegateCommand ManageScanGroupsCommand { get; }

        /// <summary>
        /// 关闭弹窗（自绘标题栏右上角的 × 用它）。
        /// 本弹窗改的是活对象：变量的增删改一直即时生效，没有"取消回滚"语义，所以关闭即完成。
        ///
        /// 注意命令体用 lambda 延迟读 <see cref="RequestClose"/> —— 它由 Prism 在**构造之后**注入，
        /// 在构造函数里直接捕获会拿到 null（VariableBindingViewModel 同款写法）。
        /// </summary>
        public DelegateCommand CloseCommand { get; }

        public GlobalVariableManagerViewModel(
            IWorkspaceManager workspace,
            AdvancedCommunicationManager communicationManager,
            NetworkVariableBridge bridge,
            IDialogService dialogs)
            : base(workspace)
        {
            _communicationManager = communicationManager;
            _bridge = bridge;
            _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));

            AddCommand = new DelegateCommand(AddVariable);
            CloseCommand = new DelegateCommand(() => RequestClose.Invoke(new DialogResult(ButtonResult.OK)));
            DeleteCommand = new DelegateCommand<VariableNode>(DeleteVariable);
            ResetCommand = new DelegateCommand<VariableNode>(ResetVariable);
            EditArrayCommand = new DelegateCommand<VariableNode>(ExecuteEditArray);
            CopyNameCommand = new DelegateCommand<VariableNode>(
                n => Clipboard.SetText(n?.Name ?? ""),
                n => n != null && n.IsRootNode);
            RenameCommand = new DelegateCommand<VariableNode>(
                RenameVariable,
                n => n != null && n.IsRootNode && n.OriginalModel != null && !IsSystemVariable(n.OriginalModel));

            SelectSourceCommand = new DelegateCommand<VariableSourceNode>(node => SelectedSource = node);

            ManageScanGroupsCommand = new DelegateCommand(ExecuteManageScanGroups, () => SelectedConnection != null);

            foreach (var conn in _communicationManager.GetAllConnections())
                Connections.Add(conn);

            // 连接状态变化 → 徽标联动（命名方法：Dispose 才能正确退订）
            _communicationManager.ConnectionStateChanged += OnConnectionStateChangedHandler;

            // 变量增删 → 来源树计数刷新
            _workspace.GlobalVariables.CollectionChanged += OnVariablesChangedForCountsHandler;

            RebuildSourceTree();
            RefreshTree();
        }

        protected override VariableNode CreateRootNode(IVariable gv)
        {
            bool isNetwork = gv.VariableType == VariableType.Communication;
            var node = new VariableNode
            {
                OriginalModel = gv,
                Name = gv.Name,
                DataType = gv.DataType,
                TypeName = gv.DataType?.Name,
                Description = gv.Description,
                ChildDefaultValue = gv.DefaultValue,
                ChildValue = gv.Value,
                Level = 0,
                IsNetwork = isNetwork,
                SourceLabel = isNetwork ? gv.ConnectionName : "本地",
                Address = gv.AddressConfig?.Address,
            };

            if (isNetwork && !string.IsNullOrEmpty(gv.ConnectionName))
            {
                var conn = _communicationManager.GetConnection(gv.ConnectionName);
                node.IsConnected = conn?.IsConnected ?? false;

                // 「扫描组」列：可选组名来自该变量的连接（默认组恒在首位）
                foreach (var groupName in _communicationManager.GetScanGroupNames(gv.ConnectionName))
                    node.ScanGroupOptions.Add(groupName);

                // 模型里空组名 = 默认组（PollScheduler 约定），界面上要显示成"默认组"而不是空白
                node.ScanGroupText = gv is NetworkVariableModel netVar
                                     && !string.IsNullOrWhiteSpace(netVar.ScanGroup)
                    ? netVar.ScanGroup
                    : PollScheduler.DefaultGroupName;

                // 回调必须在赋完初值后再挂，否则构造节点时就会触发一次"变更"
                node.ScanGroupChanged = OnScanGroupChanged;
            }
            else
            {
                node.IsConnected = true;
            }
            return node;
        }

        /// <summary>
        /// 扫描组下拉变更：回写变量模型 + 让轮询侧按新组重排。
        /// <para>为什么必须重注册：变量归哪个组是注册那一刻抄进 <c>CommunicationVariable</c> 的快照，
        /// 光改 <c>NetworkVariableModel.ScanGroup</c> 不会换节拍——桥接器只在增删/改名时才重注册。</para>
        /// </summary>
        private void OnScanGroupChanged(VariableNode node)
        {
            if (node?.OriginalModel is not NetworkVariableModel nv) return;

            string selected = node.ScanGroupText;
            if (string.IsNullOrWhiteSpace(selected))
            {
                // 组名失效（组被删、或变量换了连接）时 ComboBox 会把 SelectedItem 写成 null：
                // 此时只把文案复位成默认组，绝不能用 null 覆盖模型
                node.ScanGroupText = PollScheduler.DefaultGroupName;
                return;
            }

            // "默认组"是虚拟组，模型里以空串表示（PollScheduler：空名 → 默认组，周期取连接 ReadCycleMs）
            string stored = ToStoredScanGroup(selected);

            if (string.Equals(nv.ScanGroup ?? string.Empty, stored, StringComparison.Ordinal)) return;

            nv.ScanGroup = stored;
            _bridge.ReRegister(nv);
        }

        protected override void CreateChildNodes(IVariable gv, VariableNode parentNode)
        {
            var defArray = gv.DefaultValue as Array;
            var valArray = gv.Value as Array;
            int len = Math.Max(defArray?.Length ?? 0, valArray?.Length ?? 0);
            var elementType = gv.DataType.GetElementType();

            for (int i = 0; i < len; i++)
            {
                parentNode.Children.Add(
                    new VariableNode
                    {
                        Name = $"[{i}]",
                        DataType = elementType,
                        TypeName = elementType?.Name,
                        ChildDefaultValue =
                            defArray != null && i < defArray.Length ? defArray.GetValue(i) : null,
                        ChildValue =
                            valArray != null && i < valArray.Length ? valArray.GetValue(i) : null,
                        // 子行要能就地改元素值：得知道"我属于哪个变量、是第几项"
                        // （写回逻辑见 VariableNode.ChildDefaultValueText）
                        OriginalModel = parentNode.OriginalModel,
                        ElementIndex = i,
                        Level = 1,
                        SourceLabel = parentNode.SourceLabel,
                        IsNetwork = parentNode.IsNetwork,
                        IsConnected = parentNode.IsConnected,
                    }
                );
            }
        }

        private void OnVariableValueChanged(object sender, EventArgs e)
        {
            // 值变化就地刷新（2026-10-11 收口，取代 C4 的"脏标记 + 800ms 全树重建"节拍）：
            // 节点对象在两次结构变化之间保持稳定，正在编辑的单元格不再有被重建吞掉的窗口期，
            // 轮询推送也不再引发整树 Clear+重建。事件可能来自流程/轮询线程 → 封送回 UI 线程再动 UI。
            if (sender is not IVariable gv) return;
            VisionMaster.Helpers.SafeDispatch.BeginInvoke(() =>
            {
                foreach (var node in NodesOf(gv))
                    node.RefreshValueDisplay();
            });
        }

        /// <summary>枚举某变量在当前树里的全部展示节点（根 + 数组元素行；子行与根共享同一模型）</summary>
        private IEnumerable<VariableNode> NodesOf(IVariable gv)
        {
            foreach (var root in _realTree)
            {
                if (!ReferenceEquals(root.OriginalModel, gv)) continue;
                yield return root;
                foreach (var child in root.Children)
                    yield return child;
            }
        }

        /// <summary>新建变量补挂值变化监听（此前仅构造时给存量变量挂接，新建变量运行中值不同步）</summary>
        protected override void OnVariableAdded(IVariable variable)
        {
            variable.ValueChanged += OnVariableValueChanged;
        }

        /// <summary>删除变量退订值变化监听，防泄漏</summary>
        protected override void OnVariableRemoved(IVariable variable)
        {
            variable.ValueChanged -= OnVariableValueChanged;
        }

        // B1：连接状态事件由通信管理器的心跳/重连定时器（线程池）直接触发、不封送，
        // 直通 RefreshSourceStates → RefreshTree 会在非 UI 线程 Clear ObservableCollection 崩溃；
        // 断线抖动是产线常态，此处必须异步封送回 UI 线程
        private void OnConnectionStateChangedHandler(object? sender, ConnectionStateChangedEventArgs e)
            => VisionMaster.Helpers.SafeDispatch.BeginInvoke(RefreshSourceStates);

        private void OnVariablesChangedForCountsHandler(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => RefreshSourceCounts();

        #region 新建变量（左栏来源决定种类 → 弹窗录入）

        /// <summary>
        /// 「＋ 添加变量」入口（同步命令体 → <see cref="AddVariableCore"/> 异步内核）。
        /// <para>为什么拆两段：<c>EasyDialog.ShowPropertyGridAsync</c> 必须 await（它内部用
        /// DispatcherPriority.Background 排弹窗，同步阻塞会与路由事件互锁），而
        /// <see cref="DelegateCommand"/> 只收 Action——与 ExecuteEditArray → ExecuteEditArrayCore
        /// 同一范式。</para>
        /// </summary>
        private void AddVariable() => AddVariableCore();

        /// <summary>
        /// 按左栏当前选中的来源构造草稿 → 弹属性网格 → 确认后走既有的创建逻辑。
        ///
        /// 【为什么校验在弹窗之后】名称/类型/地址这些字段现在都在草稿里，弹窗确认前
        /// 它们还不存在。校验口径与旧表单逐条对齐（空名 / 大小写不敏感查重 / 类型 /
        /// 线圈区布尔 / 网络标量限制 / 初始值 / 地址 Validate），只是发生时机往后挪了一步。
        /// </summary>
        private async void AddVariableCore()
        {
            // 无 WPF 应用上下文（headless 断言宿主 / 设计器 / 已关机）：EasyDialog 内部的
            // InternalExecuteAsync 虽有"Application.Current == null → 返回 false"的保护分支，
            // 但它的**前置步骤**（Dispatcher.InvokeAsync 建 FlatPropertyGrid）没有——直接调会 NRE。
            // 这里前置判空走同一口径：无应用上下文 = 弹不出去 = 按"用户取消"处理（草稿丢弃）。
            if (Application.Current == null) return;

            // 「系统变量」是只读来源：在这里点新增，用户的本意一定是"建一个自己的变量"，
            // 所以先把来源切回本地，再走原有的本地草稿流程（而不是静默什么都不做）。
            if (SelectedSource?.Key == SystemSourceKey)
                SelectedSource = SourceNodes.FirstOrDefault(s => s.Key == "Local");

            var draft = CreateDraft();
            if (draft == null) return;   // 左栏没有任何来源可选

            if (!await EasyDialog.ShowPropertyGridAsync("添加变量", draft)) return;   // 取消 = 草稿丢弃

            // 空名 / System. 保留前缀：与改名共用 VariableNameRules 一份规则。
            // 两处各写一份的结果就是交接里记的"创建侧漏拦"——用户能建出
            // System.X 把系统变量遮住，而遮住之后只有"数值不刷新"这一个无声症状。
            if (!VisionMaster.Binding.VariableNameRules.TryValidateNewName(draft.VariableName, out var nameError))
            {
                EasyDialog.ShowSync(title: "提示", message: nameError + "！");
                return;
            }

            // C3：查重改大小写不敏感——监视栏/搜索匹配均为 OrdinalIgnoreCase，
            // 旧口径(Ordinal)允许 Var 与 var 共存，两套匹配规则互相矛盾
            if (_workspace.GlobalVariables.Any(s => s.Name.Equals(draft.VariableName, StringComparison.OrdinalIgnoreCase)))
            {
                EasyDialog.ShowSync(title: "提示", message: "底层引擎已存在同名变量，请更换名称！");
                return;
            }

            var targetType = ResolveType(draft.TypeName);
            if (targetType == null)
            {
                EasyDialog.ShowSync("请选择数据类型！", "提示");
                return;
            }

            if (draft is NetworkVariableDraft networkDraft)
                AddNetworkVariable(networkDraft, targetType);
            else
                AddLocalVariable(draft, targetType);
        }

        /// <summary>
        /// 构造本次「添加变量」的草稿：种类由左栏选中项决定，字段用**当前已有状态**预填
        /// （连接 = 左栏选中的那条；扫描组 = 上次建完记住的那个；存储区 = 该协议的默认项）。
        /// </summary>
        private VariableDraft? CreateDraft()
        {
            bool isNetwork = SelectedSource?.IsNetwork == true;
            var typeOptions = TypesFor(isNetwork).Select(t => t.DisplayName).ToList();

            if (!isNetwork)
                return new LocalVariableDraft(typeOptions) { TypeName = typeOptions.FirstOrDefault() ?? string.Empty };

            var connection = SelectedConnection
                ?? Connections.FirstOrDefault(c => c.ConnectionName == SelectedSource?.Key);

            return new NetworkVariableDraft(
                typeOptions,
                Connections.Select(c => c.ConnectionName),
                GetScanGroupOptions(connection?.ConnectionName),
                GetAreaOptions(connection),
                BuildAddressPreview)
            {
                TypeName = typeOptions.FirstOrDefault() ?? string.Empty,
                ConnectionName = connection?.ConnectionName ?? string.Empty,
                // 扫描组默认沿用"建完记住"的那个（RefreshNewScanGroupOptions 已保证它在组表里）
                ScanGroupName = SelectedNewScanGroup,
                AreaName = connection == null ? string.Empty : GetDefaultAreaName(connection.Protocol),
                Offset = "0",
                BitOffset = 0,
            };
        }

        /// <summary>
        /// 草稿「地址预览」的取值委托：与创建路径共用 <see cref="TryBuildAddress"/> 一份逻辑，
        /// 所以预览里看到的串就是确认后落库的那个地址（预览与落库劈叉等于预览骗人）。
        /// 填不全时返回一句"还差什么"，而不是空白或半个地址。
        /// </summary>
        private string BuildAddressPreview(NetworkVariableDraft draft)
        {
            var connection = Connections.FirstOrDefault(c => c.ConnectionName == draft.ConnectionName);
            if (connection == null) return "（先选所属连接）";

            var targetType = ResolveType(draft.TypeName);
            if (targetType == null) return "（先选数据类型）";

            if (!TryResolveArea(connection.Protocol, draft.AreaName, out var area)) return "（先选存储区）";

            return TryBuildAddress(connection.Protocol, area, draft.Offset, draft.BitOffset, targetType, out var address, out var error)
                ? address.Address
                : $"（{error}）";
        }

        /// <summary>解析弹窗草稿里的初始值文本：留空 = 类型默认值；非法文本返回 false 并给出中文原因</summary>
        private static bool TryGetNewInitialValue(string? text, Type targetType, out object? initial, out string error)
        {
            var trimmed = text?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                initial = targetType == typeof(string) ? string.Empty : Activator.CreateInstance(targetType);
                error = "";
                return true;
            }

            if (VisionMaster.Helpers.VariableValueConverter.TryConvert(trimmed, targetType, out var converted, out error))
            {
                initial = converted;
                return true;
            }
            initial = null;
            return false;
        }

        private void AddLocalVariable(VariableDraft draft, Type targetType)
        {
            object initValue;
            if (targetType.IsArray)
            {
                initValue = Array.CreateInstance(targetType.GetElementType(), 0);
            }
            else if (!TryGetNewInitialValue(draft.InitialValue, targetType, out var parsed, out var initialError))
            {
                EasyDialog.ShowSync($"初始值无效：{initialError}", "提示");
                return;
            }
            else
            {
                initValue = parsed!;
            }

            var newVar = new LocalVariableModel
            {
                Name = draft.VariableName,
                DataType = targetType,
                Description = draft.Description,
                DefaultValue = initValue,
                Value = initValue,
            };

            _workspace.GlobalVariables.Add(newVar); // 集合事件自动挂监听 + 树刷新

            // 切换到本地来源分组，确保新变量立即可见（此前停留在非网络来源时新变量被过滤器吞掉）
            SelectedSource = SourceNodes.FirstOrDefault(s => s.Key == "Local");

            Notifier.ShowSuccess($"本地变量 [{newVar.Name}] 创建成功");
        }

        private void AddNetworkVariable(NetworkVariableDraft draft, Type targetType)
        {
            var connection = Connections.FirstOrDefault(c => c.ConnectionName == draft.ConnectionName);
            if (connection == null)
            {
                EasyDialog.ShowSync("请选择所属连接！", "提示");
                return;
            }
            if (!TryResolveArea(connection.Protocol, draft.AreaName, out var area))
            {
                EasyDialog.ShowSync("请选择存储区！", "提示");
                return;
            }
            if (IsCoilOrDiscreteArea(area) && targetType != typeof(bool))
            {
                EasyDialog.ShowSync("线圈/离散输入区仅支持布尔类型！", "提示");
                return;
            }
            // 创建入口兜底校验：通信层仅支持标量点读，文本/数组会导致地址语义错误
            if (targetType == typeof(string) || targetType.IsArray)
            {
                EasyDialog.ShowSync("网络变量仅支持数值/布尔标量类型，不支持文本与数组！", "提示");
                return;
            }
            if (!TryGetNewInitialValue(draft.InitialValue, targetType, out var initial, out var initialError))
            {
                EasyDialog.ShowSync($"初始值无效：{initialError}", "提示");
                return;
            }

            try
            {
                if (!TryBuildAddress(connection.Protocol, area, draft.Offset, draft.BitOffset, targetType, out var address, out var addressError))
                {
                    EasyDialog.ShowSync(addressError, "提示");
                    return;
                }

                var (valid, error) = address.Validate();
                if (!valid)
                {
                    EasyDialog.ShowSync(error, "地址配置无效");
                    return;
                }

                var netVar = (NetworkVariableModel)VariableFactory.CreateNetwork(
                    draft.VariableName, targetType, connection.ConnectionName,
                    address, draft.Description);

                // 建的时候就定好节拍：桥接器注册时会把 ScanGroup 抄进 CommunicationVariable，
                // 事后再改要走 ReRegister，这里一次到位
                netVar.ScanGroup = ToStoredScanGroup(draft.ScanGroupName);

                // 基准与起始值同源（弹窗的「初始值」）：此刻尚未 Bind 通信管理器，
                // Value setter 的隐式写设备会静默跳过，只落镜像值
                netVar.DefaultValue = initial;
                netVar.Value = initial;

                _workspace.GlobalVariables.Add(netVar); // 桥接器监听集合 → 自动 Bind + RegisterVariable + 镜像接线

                // 切换到对应连接来源分组，确保新变量立即可见
                SelectedSource = SourceNodes.FirstOrDefault(s => s.Key == connection.ConnectionName);

                // 建完记住本次的组（旧表单"不清空"同语义）：连续录同节拍的点位时，
                // 下一个弹窗直接预填同一个组。放在切来源之后——切来源会重算该连接的组表。
                SelectedNewScanGroup = draft.ScanGroupName;

                Notifier.ShowSuccess($"网络变量 [{netVar.Name}] 创建成功（{connection.ConnectionName} @ {address.Address}）");
            }
            catch (Exception ex)
            {
                EasyDialog.ShowSync($"创建网络变量失败：{ex.Message}", "错误");
            }
        }

        /// <summary>
        /// 按协议构造地址（创建与草稿的「地址预览」共用一份，预览与落库不许劈叉）。
        /// 位偏移只在"布尔 + 非线圈/离散区"时才真正参与（S7 的 M0.0 形态），
        /// 其余情况按 -1（无位访问）落库——判据与旧表单的 IsBitMode 同源。
        /// </summary>
        private static bool TryBuildAddress(
            CommunicationType protocol, Enum area, string? offset, int bitOffset, Type targetType,
            out DeviceAddressBase address, out string error)
        {
            address = null!;
            error = string.Empty;

            int effectiveBitOffset = targetType == typeof(bool) && bitOffset >= 0 && !IsCoilOrDiscreteArea(area)
                ? bitOffset
                : -1;

            switch (protocol)
            {
                case CommunicationType.ModbusTcp:
                    address = new ModbusAddress
                    {
                        Area = (ModbusArea)area,
                        Offset = offset ?? "0",
                        DataType = ToDataValueType(targetType),
                        BitOffset = effectiveBitOffset
                    };
                    return true;

                case CommunicationType.SiemensS7:
                    address = new S7Address
                    {
                        Area = (S7Area)area,
                        Offset = offset ?? "0",
                        DataType = ToDataValueType(targetType),
                        BitOffset = effectiveBitOffset
                    };
                    return true;

                default:
                    error = $"协议 {protocol} 的变量创建暂不支持";
                    return false;
            }
        }

        /// <summary>CLR 类型 → 通信地址数据类型</summary>
        private static DataValueType ToDataValueType(Type t)
        {
            var u = Nullable.GetUnderlyingType(t) ?? t;
            if (u == typeof(bool)) return DataValueType.Boolean;
            if (u == typeof(byte)) return DataValueType.Byte;
            if (u == typeof(short)) return DataValueType.Int16;
            if (u == typeof(ushort)) return DataValueType.UInt16;
            if (u == typeof(int)) return DataValueType.Int32;
            if (u == typeof(uint)) return DataValueType.UInt32;
            if (u == typeof(float)) return DataValueType.Float;
            if (u == typeof(double)) return DataValueType.Double;
            if (u == typeof(long)) return DataValueType.Int64;
            return DataValueType.Int32;
        }

        #endregion

        #region 编辑/复位/写值

        /// <summary>统一删除：本地直接删；网络变量经桥接器注销轮询后删</summary>
        private void DeleteVariable(VariableNode node)
        {
            var gv = node?.OriginalModel;
            if (gv == null) return;

            // 系统变量由宿主维护（IO 目录随设备/方案重建、运动变量随轴配置重建）：
            // 删掉它不会"真的消失"，下次 Rebuild 又回来；更糟的是删的那一刻引用它的画面/流程会断链。
            if (IsSystemVariable(gv))
            {
                EasyDialog.ShowSync($"系统变量 [{gv.Name}] 由系统维护（运动 / IO 等），不能删除。", "只读");
                return;
            }

            // A4：运行中禁删变量——已编译会话持有的是变量【实例引用】，
            // 运行中删除/重建同名变量，流程会继续吃"尸体"（冻结旧值）且零提示
            if (_workspace.CurrentFlow?.RunState == FlowRunState.Running)
            {
                EasyDialog.ShowSync("流程运行中禁止删除变量。\n请先停止流程——运行中的流程按对象引用取值，删除会让它读到冻结的旧值。", "互锁");
                return;
            }

            if (EasyDialog.ShowSync(
                    $"确定要删除变量 [{gv.Name}] 吗？\n警告：可能会导致引用它的算子报错！",
                    "删除确认"))
            {
                _workspace.GlobalVariables.Remove(gv); // 桥接器集合监听自动注销网络变量轮询
            }
        }

        private void ResetVariable(VariableNode node)
        {
            (node?.OriginalModel)?.ResetToDefault();
        }

        /// <summary>
        /// 变量改名：唯一入口是 <c>IVariableRegistry.TryRename</c>。
        ///
        /// 为什么不能直接写 <c>node.OriginalModel.Name = 新名</c>：
        /// 变量名在 Id 落地之前就是"地址"——连线（LinkReference.TargetPortName）、
        /// 监视项（WatchItemModel.GlobalVariableName）、网络变量轮询注册（连接名+变量名）
        /// 全都以名字为键。直接改模型名只会让这些引用静默失联（而且不报错），
        /// 而 TryRename 把"写模型 + 修索引 + 广播"一次做完，广播后由工作区的级联
        /// 把连线显示串/老连线寻址键、监视项名字一并改写过来。
        /// </summary>
        private void RenameVariable(VariableNode? node)
        {
            var model = node?.OriginalModel;
            if (model == null) return;

            if (IsSystemVariable(model))
            {
                EasyDialog.ShowSync($"系统变量 [{model.Name}] 的名字由系统生成（含设备/点位标识），不能改名。", "只读");
                return;
            }

            var (confirmed, newName) = EasyDialog.ShowTextInputSync("重命名变量", model.Name);
            if (!confirmed) return;

            // 失败原因（空名/重名/该类型不支持改名）由注册表直接给出可展示的中文文案
            if (!_workspace.VariableRegistry.TryRename(model, newName, out var error))
            {
                EasyDialog.ShowSync(error, "重命名失败");
                return;
            }

            Notifier.ShowSuccess($"变量已重命名为 [{model.Name}]");
            RefreshTree(); // 索引与引用已由级联更新，这里只刷新本弹窗的树/表
        }

        /// <summary>数组编辑器（本地数组变量）</summary>
        private void ExecuteEditArray(VariableNode node)
        {
            if (node?.OriginalModel is not LocalVariableModel gv) return;
            if (!gv.DataType.IsArray) return;
            ExecuteEditArrayCore(gv);
        }

        /// <summary>
        /// 数组元素编辑对话框（编辑默认值并同步当前值）。
        ///
        /// 改前走的是 EasyDialog.ShowPropertyGridSync —— 而 UI 库的 PropertyGrid 是**反射式生成器**体系，
        /// 它的五个生成器只有 Bool / Enum / 基础类型 / 嵌套对象 / [PropertyItem] 兜底，**没有集合这一类**。
        /// 于是挂在承载对象上的 ObservableCollection 根本不会被渲染：弹窗里只有一行只读的"元素数"，
        /// 新建数组（长度为 0）之后既看不到元素、也没地方加，等于死路。
        ///
        /// 现改用 UI 库的 DialogArrayEditor（显式列表 + 增删改 + 上下移），不再赌反射能猜对集合。
        /// </summary>
        private async void ExecuteEditArrayCore(LocalVariableModel gv)
        {
            Type? elementType = gv.DataType.GetElementType();
            if (elementType == null)
            {
                // 走到这里说明类型声明本身就是坏的（如 object[] 之外的异常形态）。
                // 必须明确告知，不能像旧版那样弹一个空面板让用户自己猜为什么改不了。
                EasyDialog.ShowSync($"变量 [{gv.Name}] 的数组元素类型无法确定，请删除后重新创建。", "无法编辑");
                return;
            }

            var editor = new DialogArrayEditor
            {
                ElementTypeName = DescribeElementType(elementType),
                Hint = "每行一个元素；留空的元素按该类型的默认值写入（数值 0 / 文本空串 / 布尔 false）。",
            };
            editor.Load((gv.DefaultValue as Array)?.Cast<object?>().Select(o => o?.ToString()));

            if (!EasyDialog.ShowSync($"编辑数组 [{gv.Name}]", editor)) return;

            var newArray = TryBuildArray(elementType, editor.Values, out string error);
            if (newArray == null)
            {
                // 任何一个元素填错就整体不落地（模型保持干净），并把原因说清楚
                EasyDialog.ShowSync($"数组转换失败：{error}\n本次修改未保存。", "错误");
                return;
            }

            gv.DefaultValue = newArray;
            gv.Value = newArray.Clone();
            RefreshTree();   // 长度可能变了，树必须重建（子行是按长度生成的）

            await System.Threading.Tasks.Task.CompletedTask;
        }

        /// <summary>
        /// 把编辑器回读的文本转成目标类型的数组（纯函数，便于断言）。
        /// 失败返回 null 并把原因写进 <paramref name="error"/>。
        ///
        /// 规则：留空的元素按该类型的默认值写入（数值 0 / 文本空串 / 布尔 false）——
        /// 空串直接 Convert.ChangeType 会抛，而"留空 = 用默认值"才是操作员的本意。
        /// </summary>
        public static Array? TryBuildArray(Type elementType, IReadOnlyList<string> texts, out string error)
        {
            error = string.Empty;
            if (elementType == null) { error = "元素类型为空"; return null; }
            if (texts == null) { error = "元素集合为空"; return null; }

            // 防御：这里要的是**元素类型**（int），不是数组类型（int[]）。
            // 传错时 Array.CreateInstance(typeof(int[]), n) 会造出 int[][]，
            // 然后 SetValue 抛 InvalidCastException —— 报错信息（"Invalid cast from 'System.String'
            // to 'System.Int32[]'"）完全指不到"参数传错了"这件事上，所以这里明说。
            if (elementType.IsArray)
            {
                error = $"元素类型不能是数组（收到 {elementType.Name}，应传元素类型本身）";
                return null;
            }

            try
            {
                Type target = Nullable.GetUnderlyingType(elementType) ?? elementType;
                var array = Array.CreateInstance(elementType, texts.Count);

                for (int i = 0; i < texts.Count; i++)
                {
                    string text = texts[i] ?? string.Empty;
                    object? converted = string.IsNullOrWhiteSpace(text)
                        ? (target.IsValueType ? Activator.CreateInstance(target) : null)
                        : Convert.ChangeType(text, target);

                    array.SetValue(converted, i);
                }

                return array;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        /// <summary>元素类型的界面文案（"Int32" / "String"；可空类型标出来）</summary>
        private static string DescribeElementType(Type elementType)
        {
            var underlying = Nullable.GetUnderlyingType(elementType);
            return underlying != null ? $"{underlying.Name}?" : elementType.Name;
        }

        #endregion

        #region IDialogAware实现
        public DialogCloseListener RequestClose { get; }

        public bool CanCloseDialog() => true;

        public void OnDialogClosed() => Dispose();

        public void OnDialogOpened(IDialogParameters parameters)
        {
            // 对话框可能在方案加载后打开：重建来源树与镜像（桥接器负责接线）
            RebuildSourceTree();
            RefreshTree();
        }
        #endregion



        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // 取消所有变量值变化事件订阅——用基类跟踪集而非当前集合：
                // 弹窗期间被 Clear/删除的旧变量不在 GlobalVariables 里，旧写法会漏退订导致弹窗连同树泄漏
                foreach (var gv in TrackedVariables)
                {
                    gv.ValueChanged -= OnVariableValueChanged;
                }

                // 取消来源树联动订阅（必须用命名方法退订，lambda -= 无效会泄漏）
                _communicationManager.ConnectionStateChanged -= OnConnectionStateChangedHandler;
                _workspace.GlobalVariables.CollectionChanged -= OnVariablesChangedForCountsHandler;
            }

            base.Dispose(disposing);
        }
    }
}
