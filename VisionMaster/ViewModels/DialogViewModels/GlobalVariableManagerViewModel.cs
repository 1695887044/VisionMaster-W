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

        /// <summary>
        /// 连接协议显示名（仅网络来源使用）：Modbus TCP / Siemens S7 / Modbus RTU。
        /// 不直接绑 <c>Protocol.ToString()</c>——那会显示成 ModbusTcp / SiemensS7。
        /// </summary>
        public string ProtocolText { get; set; } = string.Empty;

        private int _count;
        /// <summary>来源下变量数（INPC：新建/删除变量后计数徽标实时刷新）</summary>
        public int Count { get => _count; set => SetProperty(ref _count, value); }

        private bool _isOnline;
        /// <summary>连接在线状态（INPC：连接状态变化后徽标与副行实时刷新）</summary>
        public bool IsOnline
        {
            get => _isOnline;
            set
            {
                if (SetProperty(ref _isOnline, value))
                    RaisePropertyChanged(nameof(Subtitle));
            }
        }

        /// <summary>
        /// 左栏来源卡片的副行（纯显示）：
        /// 网络 = 协议 · 在线/离线；本地 = 内存变量 · 可编辑；系统 = 宿主维护 · 只读。
        /// 计算属性保证连接断线/重连时副行跟着 IsOnline 一起变，而不是停在建树那一刻。
        /// </summary>
        public string Subtitle => IsNetwork
            ? $"{ProtocolText} · {(IsOnline ? "在线" : "离线")}"
            : string.Equals(Key, GlobalVariableManagerViewModel.SystemSourceKey, StringComparison.Ordinal)
                ? "宿主维护 · 只读"
                : "内存变量 · 可编辑";

        // ==================================================================
        //  二级分类（设备 → 分组）：来源行 Level=0，分组行 Level=1。
        //  左栏仍是同一个 ListBox（按 IsNetwork 分节），分组行缩进渲染。
        // ==================================================================

        /// <summary>0 = 来源行；1 = 分组行（全部 / 自定义分组 / 未分组）。</summary>
        public int Level { get; set; }

        /// <summary>分组行所属连接名；来源行为空。</summary>
        public string ParentKey { get; set; } = string.Empty;

        /// <summary>分组行的分组 Id：null = 全部；Guid.Empty = 未分组；其余 = 方案分组。</summary>
        public Guid? GroupId { get; set; }

        public bool IsGroup => Level == 1;

        /// <summary>只有连接来源行有展开箭头（本地/系统/分组行都没有）。</summary>
        public bool ShowExpand => IsNetwork && !IsGroup;

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }
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

        /// <summary>弹窗服务：用于打开数组编辑弹窗（扫描组编辑器已不在变量管理里开）</summary>
        private readonly IDialogService _dialogs;

        public string Title => "变量管理";

        #region 来源树（设备 → 显示分组 二级）

        public ObservableCollection<VariableSourceNode> SourceNodes { get; } = new();

        /// <summary>已展开的连接（按连接名记；重建左栏后保留用户的展开状态）。</summary>
        private readonly HashSet<string> _expandedConnections = new(StringComparer.Ordinal);
        private bool _expandedInitialized;

        private VariableSourceNode? _selectedNode;
        private VariableSourceNode? _selectedSource;
        private string _selectedSourceKey = "Local";
        private Guid? _selectedGroupId;
        private bool _selectedIsGroupRow;

        /// <summary>左栏当前选中的节点（来源行或分组行）。ListBox 双向绑定到它。</summary>
        public VariableSourceNode? SelectedNode
        {
            get => _selectedNode;
            set => ApplySelection(value);
        }

        /// <summary>
        /// 当前来源行（本地 / 系统 / 某条连接）。分组行选中时返回它的父连接。
        /// 保留这个属性是为了让"按来源建变量 / 收放网络三列"的既有代码不必区分层级。
        /// </summary>
        public VariableSourceNode? SelectedSource
        {
            get => _selectedSource;
            set
            {
                if (value == null) return;
                SelectedNode = value.IsGroup ? FindSourceNode(value.ParentKey) : value;
            }
        }

        /// <summary>当前选中的显示分组：null = 全部；Guid.Empty = 未分组；其余 = 方案分组。</summary>
        public Guid? SelectedGroupId => _selectedGroupId;

        public bool IsGroupScope => _selectedGroupId != null && !IsSearching;

        public bool IsSearching => !string.IsNullOrWhiteSpace(SearchText);

        public string SearchScopeLabel => "搜索范围：全部分组";

        /// <summary>当前分组显示名（全部/未分组也给出可上屏文案）。</summary>
        public string SelectedGroupName
        {
            get
            {
                if (_selectedGroupId == null) return "全部";
                if (_selectedGroupId == Guid.Empty) return VariableGroupEditorViewModel.UngroupedName;
                return Groups.FirstOrDefault(g => g.Id == _selectedGroupId)?.Name
                       ?? VariableGroupEditorViewModel.UngroupedName;
            }
        }

        /// <summary>明细表当前可见的根行数（分组/搜索过滤后），卡片头徽标与状态条用它。</summary>
        private int _visibleRootCount;
        public int VisibleRootCount
        {
            get => _visibleRootCount;
            private set => SetProperty(ref _visibleRootCount, value);
        }

        /// <summary>状态条上的作用域文案：来源 / 分组，或"全部分组"（搜索时）。</summary>
        public string SelectedScopeLabel
        {
            get
            {
                string sourceName = _selectedSource?.DisplayName ?? "本地变量";
                if (IsSearching) return sourceName + " / 全部分组";
                return _selectedGroupId == null ? sourceName : sourceName + " / " + SelectedGroupName;
            }
        }

        // 方案在启动早期可能还没挂上（WorkspaceContext.CurrentSolution 在 SwitchSolution 之前为 null）；
        // 此时分组表按空处理，避免打开变量管理就 NRE。真实运行期一定有方案。
        private List<VariableGroupDescriptor> Groups
            => _workspace.CurrentSolution?.VariableGroups ?? new List<VariableGroupDescriptor>();

        /// <summary>
        /// 左栏选中项落账：来源行 → 全部；分组行 → 父连接 + 该组。
        /// 只影响筛选与"建哪种变量"的默认值，不触碰变量模型。
        /// </summary>
        private void ApplySelection(VariableSourceNode? node)
        {
            // 分组视图刷新/集合重建时 ListBox 可能把选中项写回 null；
            // 这里忽略 null，避免"左栏没选中、右栏显示本地"的错位。
            if (node == null) return;
            // 必须显式给属性名：SetProperty 的 [CallerMemberName] 在 ApplySelection 里会
            // 报成 "ApplySelection"，绑定（ListBox.SelectedItem）收不到 SelectedNode 变更。
            if (!SetProperty(ref _selectedNode, node, nameof(SelectedNode))) return;

            _selectedIsGroupRow = node.IsGroup;
            if (node.IsGroup)
            {
                _selectedSourceKey = node.ParentKey;
                _selectedGroupId = node.GroupId;
            }
            else
            {
                _selectedSourceKey = node.Key;
                _selectedGroupId = null;
            }

            _selectedSource = FindSourceNode(_selectedSourceKey);
            RaisePropertyChanged(nameof(SelectedSource));
            RaisePropertyChanged(nameof(SelectedGroupId));
            RaisePropertyChanged(nameof(SelectedGroupName));
            RaisePropertyChanged(nameof(IsGroupScope));
            RaisePropertyChanged(nameof(SelectedScopeLabel));

            // 左栏选中项是"要建哪种变量"的唯一决定者（原中栏的本地/网络胶囊已撤）：
            // 选本地/系统节点 → IsNetworkSource=false（明细表收起网络三列）；
            // 选连接/连接下的分组 → true，并把「所属连接」跟着换成它（草稿的默认值从这来）
            IsNetworkSource = _selectedSource?.IsNetwork == true;
            SelectedConnection = IsNetworkSource
                ? Connections.FirstOrDefault(c => c.ConnectionName == _selectedSourceKey)
                : null;

            UpdateFilteredList();
        }

        private VariableSourceNode? FindSourceNode(string? key)
            => SourceNodes.FirstOrDefault(s => !s.IsGroup
                && string.Equals(s.Key, key, StringComparison.Ordinal));

        private VariableSourceNode? FindNode(string key, Guid? groupId, bool isGroupRow)
        {
            if (groupId != null)
            {
                return SourceNodes.FirstOrDefault(s => s.IsGroup
                    && string.Equals(s.ParentKey, key, StringComparison.Ordinal)
                    && s.GroupId == groupId);
            }

            // groupId == null 有两种节点：来源行（选中=全部）与「全部」分组行。
            // 重建后必须按"上次选的是不是分组行"还原，否则高亮会从「全部」跳到父连接。
            return isGroupRow
                ? SourceNodes.FirstOrDefault(s => s.IsGroup
                    && string.Equals(s.ParentKey, key, StringComparison.Ordinal)
                    && s.GroupId == null)
                : FindSourceNode(key);
        }

        private void RestoreSelection(string sourceKey, Guid? groupId, bool isGroupRow)
        {
            var node = FindNode(sourceKey, groupId, isGroupRow)
                       ?? FindSourceNode(sourceKey)
                       ?? SourceNodes.FirstOrDefault();
            if (node != null) SelectedNode = node;
        }

        /// <summary>
        /// 重建左栏（来源行 + 已展开连接的「全部 / 分组 / 未分组」行）。
        /// 连接默认全部展开（首次建树时初始化），之后按用户折叠状态保留。
        /// </summary>
        private void RebuildSourceTree()
        {
            var currentKey = _selectedSourceKey;
            var currentGroup = _selectedGroupId;
            var currentIsGroupRow = _selectedIsGroupRow;

            var connections = _communicationManager.GetAllConnections();

            // 来源树每次从管理器现取，连接集合也必须同步刷新：
            // 否则弹窗存活期间新增/删除连接会出现"左栏看得到、SelectedConnection 找不到"。
            Connections.Clear();
            foreach (var conn in connections)
                Connections.Add(conn);

            if (!_expandedInitialized)
            {
                foreach (var conn in connections)
                    _expandedConnections.Add(conn.ConnectionName);
                _expandedInitialized = true;
            }

            SourceNodes.Clear();
            SourceNodes.Add(new VariableSourceNode
            {
                Key = "Local",
                DisplayName = "本地变量",
                Icon = "\uf15b",
                Level = 0,
                Count = _workspace.GlobalVariables.Count(v => v.VariableType == VariableType.Local),
                IsOnline = true
            });

            // 系统变量（运动 / IO 点等）：由宿主按方案与设备维护，这里只读展示。
            // 单独一个来源节点的理由：它们不属于"本地变量"——放进本地池会让用户以为可以改初始值/删除，
            // 而那些操作要么没有落点（初始值不回灌设备），要么会破坏宿主维护的集合。
            SourceNodes.Add(new VariableSourceNode
            {
                Key = SystemSourceKey,
                DisplayName = "系统变量",
                Icon = "\uf013",
                Level = 0,
                Count = CountSystemVariables(),
                IsOnline = true
            });

            foreach (var conn in connections)
            {
                var source = new VariableSourceNode
                {
                    Key = conn.ConnectionName,
                    DisplayName = conn.ConnectionName,
                    Icon = "\uf1eb",
                    IsNetwork = true,
                    Level = 0,
                    ProtocolText = ProtocolDisplayName(conn.Protocol),
                    Count = CountVariables(conn.ConnectionName, null),
                    IsOnline = _communicationManager.GetConnection(conn.ConnectionName)?.IsConnected ?? false,
                    IsExpanded = _expandedConnections.Contains(conn.ConnectionName)
                };
                SourceNodes.Add(source);

                if (!source.IsExpanded) continue;

                // 二级分类：全部 → 方案分组（按定义顺序）→ 未分组。
                // 空分组也显示（计数 0）：分组表是方案级资源，用户刚建完就要能在树上看到。
                SourceNodes.Add(CreateGroupNode(source, null, "全部", "\uf0ca"));
                foreach (var group in Groups)
                    SourceNodes.Add(CreateGroupNode(source, group.Id, group.Name, "\uf02b"));
                SourceNodes.Add(CreateGroupNode(source, Guid.Empty, VariableGroupEditorViewModel.UngroupedName, "\uf01c"));
            }

            RestoreSelection(currentKey, currentGroup, currentIsGroupRow);
        }

        private VariableSourceNode CreateGroupNode(
            VariableSourceNode parent, Guid? groupId, string name, string icon)
            => new()
            {
                Key = parent.Key + "|" + (groupId?.ToString("N") ?? "all"),
                DisplayName = name,
                Icon = icon,
                IsNetwork = true,
                Level = 1,
                ParentKey = parent.Key,
                GroupId = groupId,
                Count = CountVariables(parent.Key, groupId),
                IsOnline = parent.IsOnline,
            };

        /// <summary>
        /// 某连接下、某显示分组里的变量数。groupId = null 表示"全部"。
        /// 悬空分组 Id（分组被外部删除 / 方案来自别处）按「未分组」统计，与界面显示一致。
        /// </summary>
        private int CountVariables(string connectionName, Guid? groupId)
            => _workspace.GlobalVariables.OfType<NetworkVariableModel>()
                .Count(v => string.Equals(v.ConnectionName, connectionName, StringComparison.Ordinal)
                            && (groupId == null || NormalizeGroupId(v.GroupId) == groupId.Value));

        private Guid NormalizeGroupId(Guid id)
            => id == Guid.Empty || Groups.Any(g => g.Id == id) ? id : Guid.Empty;

        /// <summary>展开/折叠连接行（只重建左栏，不改变右侧选中作用域）。</summary>
        private void ToggleSourceExpand(VariableSourceNode? node)
        {
            if (node == null || !node.ShowExpand) return;
            if (!_expandedConnections.Add(node.Key))
                _expandedConnections.Remove(node.Key);
            RebuildSourceTree();
        }

        /// <summary>协议枚举 → 界面显示名（左栏副行用；不直接把枚举成员名上屏）</summary>
        internal static string ProtocolDisplayName(CommunicationType protocol) => protocol switch
        {
            CommunicationType.ModbusTcp => "Modbus TCP",
            CommunicationType.ModbusRtu => "Modbus RTU",
            CommunicationType.SiemensS7 => "Siemens S7",
            // P0-F 新增的三个协议：漏在兜底分支里会显示成 MitsubishiMc / OmronFins / FreeProtocol
            CommunicationType.MitsubishiMc => "三菱 MC",
            CommunicationType.OmronFins => "欧姆龙 FINS",
            CommunicationType.FreeProtocol => "自由协议",
            CommunicationType.OpcUa => "OPC UA",
            _ => protocol.ToString(),
        };

        /// <summary>轻量刷新来源/分组计数（新建/删除/改分组后调用，不重建树不改变选中）</summary>
        private void RefreshSourceCounts()
        {
            foreach (var node in SourceNodes)
            {
                if (node.IsGroup)
                {
                    node.Count = CountVariables(node.ParentKey, node.GroupId);
                    continue;
                }

                node.Count = node.Key switch
                {
                    "Local" => _workspace.GlobalVariables.Count(v => v.VariableType == VariableType.Local),
                    SystemSourceKey => CountSystemVariables(),
                    _ => CountVariables(node.Key, null)
                };
            }
        }

        /// <summary>来源树里「系统变量」节点的键（与连接名同名也不冲突：连接名不允许叫 System.*，见创建校验）。</summary>
        public const string SystemSourceKey = "System";

        /// <summary>系统维护的变量：系统变量（运动/状态）+ IO 点变量。</summary>
        private static bool IsSystemVariable(IVariable? variable)
            => variable?.VariableType is VariableType.System or VariableType.Io;

        private int CountSystemVariables() => CopySystemVariables().Count;

        /// <summary>
        /// 系统变量快照。
        ///
        /// 两个必须：① 系统/IO 变量自 2026-10-18 起住在 <c>Workspace.SystemVariables</c>，
        /// **不在** <c>GlobalVariables</c> 里（不落盘），所以明细树必须显式并进来，
        /// 否则「系统变量」来源永远是 0 行、只读横幅下面是一张空表；
        /// ② 该集合由运动发布器/IO 目录在后台线程改写，直接 foreach 会抛
        /// 「集合已更改，枚举器无效」——按 ScadaVariablePickerViewModel 的同一做法重试 3 次。
        /// </summary>
        private List<IVariable> CopySystemVariables()
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    var snapshot = new List<IVariable>();
                    foreach (var variable in _workspace.SystemVariables)
                        snapshot.Add(variable);
                    return snapshot;
                }
                catch (InvalidOperationException)
                {
                    // 发布器正在改写集合，再试一次
                }
            }

            return new List<IVariable>();
        }

        /// <summary>明细树数据源 = 用户变量 + 系统变量快照（系统侧只读展示）。</summary>
        protected override IEnumerable<IVariable> EnumerateTreeVariables()
            => _workspace.GlobalVariables.Concat(CopySystemVariables());

        private void RefreshSourceStates()
        {
            foreach (var node in SourceNodes.Where(s => s.IsNetwork && !s.IsGroup))
            {
                node.IsOnline = _communicationManager.GetConnection(node.Key)?.IsConnected ?? false;
            }

            // 正在编辑当前值时不重建树：RefreshTree 会 Clear+重建节点，
            // 把 IsEditingValue 与输入中的草稿一起丢掉（断线抖动是产线常态，
            // 一次抖动就吞掉操作员刚敲的值不可接受）。徽标由上面的 IsOnline 通知刷新。
            if (FilteredDisplayNodes.Any(n => n.IsEditingValue)) return;

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
                if (!SetProperty(ref _searchText, value)) return;
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
            foreach (var node in FilterDisplayNodes(
                         DisplayNodes, _selectedSourceKey, _selectedGroupId, SearchText?.Trim() ?? ""))
            {
                FilteredDisplayNodes.Add(node);
            }

            // 卡片头徽标 / 状态条用的是"当前作用域里可见的根行数"：
            // 分组筛选与搜索都会改变它，不能继续绑来源总数。
            RefreshVisibleRootCount();
            RaisePropertyChanged(nameof(IsSearching));
            RaisePropertyChanged(nameof(IsGroupScope));
            RaisePropertyChanged(nameof(SelectedScopeLabel));
        }

        /// <summary>
        /// 重算当前可见根行数（卡片头 / 状态条徽标）。
        /// 正常路径由 <see cref="UpdateFilteredList"/> 调用；直接改写 FilteredDisplayNodes 的
        /// 夹具（视图冒烟）也用它同步徽标，避免渲染出"0 个变量"。
        /// </summary>
        public void RefreshVisibleRootCount()
            => VisibleRootCount = FilteredDisplayNodes.Count(n => n.IsRootNode);

        /// <summary>
        /// 明细表的筛选：来源（本地 / 某条连接）+ 显示分组 + 搜索。
        ///
        /// 抽成**纯函数**是为了能直接被断言（VM 的构造函数要通信管理器、桥接器、弹窗服务，
        /// 在冒烟里凑齐这些依赖不划算；而这段逻辑恰恰是最容易出错、也最该守住的一段）。
        ///
        /// ★ 子节点必须跟随父级 —— 这条是 2026-10-11 修复的核心：
        /// 旧实现只对"根节点"判来源、判搜索，子节点（数组元素行）无条件加进列表。
        /// 后果是切到"网络变量"时，本地数组变量的 [0]/[1] 会以
        /// "没有变量名、没有来源、类型还被列宽截断"的残行留在表里，既看不懂也不能操作。
        /// DisplayNodes 是"根 + 它的子节点紧随其后"的扁平表（见基类 UpdateFlatList），
        /// 所以一个游标量就足以表达"跟随父级"。
        ///
        /// ★ 搜索必须跨分组：在「监控分组」里搜一个属于「控制分组」的点，也要能搜到；
        /// 否则用户的第一反应是"明明有这个变量却搜不到"。搜索时忽略分组过滤，
        /// 由界面的「搜索范围：全部分组」提示当前口径。
        /// </summary>
        public static List<VariableNode> FilterDisplayNodes(
            IEnumerable<VariableNode> displayNodes, string sourceKey, string query)
            => FilterDisplayNodes(displayNodes, sourceKey, null, query);

        public static List<VariableNode> FilterDisplayNodes(
            IEnumerable<VariableNode> displayNodes, string sourceKey, Guid? groupId, string query)
        {
            var result = new List<VariableNode>();
            bool rootAccepted = false;
            bool searching = !string.IsNullOrEmpty(query);

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

                    // 分组：只对网络变量生效；本地/系统没有分组维度。
                    // 搜索时忽略分组（跨分组搜索），见方法注释。
                    bool matchGroup = searching
                        || groupId == null
                        || !node.IsNetwork
                        || node.GroupId == groupId.Value;

                    // 搜索：名称 / 描述 / 地址，任一命中即可
                    bool matchSearch = !searching
                        || node.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                        || (node.Description?.IndexOf(query, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
                        || (node.Address?.IndexOf(query, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0;

                    rootAccepted = matchSource && matchGroup && matchSearch;
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
        /// <para>由左栏「变量来源」的选中项驱动（见 <see cref="ApplySelection"/>），
        /// 代码后置靠它收放明细表的「分组/扫描组/地址」三列，所以它必须留在 VM 上。</para>
        /// </summary>
        public bool IsNetworkSource
        {
            get => _isNetworkSource;
            set
            {
                // 2026-10-15 中栏「本地/网络」分段胶囊撤掉后，本属性只由 SelectedSource 驱动，
                // 是"右栏三列收放"的纯派生信号。这里不能再反向改 SelectedSource：
                // 选中「系统变量」(IsNetwork=false) 时若走旧的 else 分支，会被强制改回「本地变量」，
                // 导致系统来源永远进不去（系统横幅/只读 chip 全部不可达）。
                if (!SetProperty(ref _isNetworkSource, value)) return;
                ExportCommand.RaiseCanExecuteChanged();
                ImportCommand.RaiseCanExecuteChanged();
            }
        }

        public ObservableCollection<CommunicationConfig> Connections { get; } = new();

        private CommunicationConfig? _selectedConnection;
        /// <summary>
        /// 当前选中的连接（左栏选中连接节点 / 其下分组时同步过来）。
        /// 「添加变量」弹窗的「所属连接」默认值、扫描组下拉的候选都用它。
        /// </summary>
        public CommunicationConfig? SelectedConnection
        {
            get => _selectedConnection;
            set
            {
                if (!SetProperty(ref _selectedConnection, value)) return;
                RefreshNewScanGroupOptions();
                ExportCommand.RaiseCanExecuteChanged();
                ImportCommand.RaiseCanExecuteChanged();
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
        internal static string ToStoredScanGroup(string? displayName) =>
            string.IsNullOrWhiteSpace(displayName)
            || string.Equals(displayName, PollScheduler.DefaultGroupName, StringComparison.Ordinal)
                ? string.Empty
                : displayName;

        // 扫描组编辑器只在「通讯设置 → 连接 → 操作 → 扫描组」里开。
        // 变量管理只保留"给变量选组"（明细表的扫描组下拉），不再提供组表编辑入口：
        // 组表是连接级调度资源（周期 / 请求配额 / 段合并），改它的影响面是整条连接；
        // 且它需要 IHardwareEditGate 的"管理员 + 流程未运行"门禁与通讯配置置脏——
        // 这两样都不在变量管理的职责里（见 docs/code-changes 的扫描组入口评审记录）。

        /// <summary>存储区显示名表：界面显示中文，落库仍用原始枚举（显示与数据解耦）</summary>
        internal static readonly System.Collections.Generic.Dictionary<Enum, string> AreaDisplayNames = BuildAreaDisplayNames();

        /// <summary>
        /// 构造显示名表：Modbus / S7 用显式文案（带读写语义），MC / FINS 直接取枚举上的
        /// <c>[Display(Name)]</c>——它们本来就把中文写好了，只是之前没人读（R40/R84）。
        /// </summary>
        private static System.Collections.Generic.Dictionary<Enum, string> BuildAreaDisplayNames()
        {
            var map = new System.Collections.Generic.Dictionary<Enum, string>
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

            foreach (McArea area in Enum.GetValues(typeof(McArea)))
                map[area] = EnumDisplayName(area);
            foreach (FinsArea area in Enum.GetValues(typeof(FinsArea)))
                map[area] = EnumDisplayName(area);

            return map;
        }

        /// <summary>枚举成员的 <c>[Display(Name)]</c>；没标就回退成员名（绝不返回空白）</summary>
        private static string EnumDisplayName(Enum value)
        {
            var field = value.GetType().GetField(value.ToString());
            var display = field?
                .GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.DisplayAttribute), false)
                .OfType<System.ComponentModel.DataAnnotations.DisplayAttribute>()
                .FirstOrDefault();
            return string.IsNullOrWhiteSpace(display?.Name) ? value.ToString() : display!.Name!;
        }

        /// <summary>某连接的存储区候选（显示名，顺序 = 枚举声明序）；连接为空/协议未实现时给空表</summary>
        internal static IReadOnlyList<string> GetAreaOptions(CommunicationConfig? connection)
        {
            if (connection == null) return Array.Empty<string>();

            return connection.Protocol switch
            {
                // ModbusRtu 与 ModbusTcp 的地址语义完全相同（都是 Modbus 存储区），只是链路不同。
                // 漏掉它会让"串口变量"在变量管理里根本选不出存储区——IO 层下线后串口就彻底没法配了。
                CommunicationType.ModbusTcp or CommunicationType.ModbusRtu
                    => Enum.GetValues(typeof(ModbusArea)).Cast<ModbusArea>().Select(a => AreaDisplayNames[a]).ToList(),
                CommunicationType.SiemensS7
                    => Enum.GetValues(typeof(S7Area)).Cast<S7Area>().Select(a => AreaDisplayNames[a]).ToList(),
                // 存储区候选必须按**厂商**收窄：同一份 McArea 枚举，松下的解析器只认 R/X/Y/L/D，
                // 基恩士的 F/S/V 需要 FM/SM·SD 前缀（拼 "F100" 会被解析器直接拒）。
                // 不收窄的后果是"建的时候全绿、轮询时才报不支持的数据类型"。
                CommunicationType.MitsubishiMc
                    => AllowedMcAreas(McVendorOf(connection)).Select(a => AreaDisplayNames[a]).ToList(),
                CommunicationType.OmronFins
                    => Enum.GetValues(typeof(FinsArea)).Cast<FinsArea>().Select(a => AreaDisplayNames[a]).ToList(),
                _ => Array.Empty<string>(),
            };
        }

        /// <summary>连接配置里的 MC 厂商变体（非 MC / 取不到时按三菱）</summary>
        internal static McVendor McVendorOf(CommunicationConfig? connection)
            => (connection?.Config as MitsubishiMcConfig)?.Vendor ?? McVendor.Mitsubishi;

        /// <summary>该厂商的 MC 解析器实际支持的存储区（顺序 = 枚举声明序）</summary>
        private static IEnumerable<McArea> AllowedMcAreas(McVendor vendor) => vendor switch
        {
            // HSL 的 ParsePanasonicFrom 只认 R/X/Y/L/D（含 LD/TN/TS/CN/CS/SD 变体）
            McVendor.Panasonic => new[] { McArea.D, McArea.R, McArea.X, McArea.Y, McArea.L },
            // HSL 的 ParseKeyenceFrom 的 F 只认 FM、S 只认 SM/SD、没有 V
            McVendor.Keyence => new[]
            {
                McArea.D, McArea.W, McArea.R, McArea.ZR, McArea.M,
                McArea.X, McArea.Y, McArea.L, McArea.B,
            },
            _ => Enum.GetValues(typeof(McArea)).Cast<McArea>(),
        };

        /// <summary>协议的默认存储区显示名（Modbus = 保持寄存器 / S7 = 内部标志位，与旧表单的默认选中一致）</summary>
        internal static string GetDefaultAreaName(CommunicationConfig? connection) => connection?.Protocol switch
        {
            CommunicationType.ModbusTcp or CommunicationType.ModbusRtu => AreaDisplayNames[ModbusArea.HoldingRegisters],
            CommunicationType.SiemensS7 => AreaDisplayNames[S7Area.M],
            CommunicationType.MitsubishiMc => AreaDisplayNames[AllowedMcAreas(McVendorOf(connection)).First()],
            CommunicationType.OmronFins => AreaDisplayNames[FinsArea.D],
            _ => string.Empty,
        };

        /// <summary>
        /// 显示名 → 存储区枚举值（按该协议/厂商的候选表反查）。
        /// 草稿里存的是显示名（属性网格渲染的是字符串），落库前必须回到枚举——
        /// 直接拿显示名当值等于把界面文案写进配置。
        /// <para>同时接受**枚举成员名**：存量导入文件（xlsx）里 MC/FINS 的存储区列是 "D"/"W"，
        /// 改显示名不能让它们导不进来。显式按名字比较而不是 Enum.TryParse，
        /// 顺带把 "7" 这类数字串挡在门外（Enum.TryParse 会把它们当枚举值）。</para>
        /// </summary>
        internal static bool TryResolveArea(CommunicationConfig? connection, string? displayName, out Enum area)
        {
            area = null!;
            if (connection == null || string.IsNullOrWhiteSpace(displayName)) return false;

            var candidates = connection.Protocol switch
            {
                CommunicationType.ModbusTcp or CommunicationType.ModbusRtu
                    => Enum.GetValues(typeof(ModbusArea)).Cast<ModbusArea>().Select(a => (Enum)a),
                CommunicationType.SiemensS7
                    => Enum.GetValues(typeof(S7Area)).Cast<S7Area>().Select(a => (Enum)a),
                CommunicationType.MitsubishiMc
                    => AllowedMcAreas(McVendorOf(connection)).Select(a => (Enum)a),
                CommunicationType.OmronFins
                    => Enum.GetValues(typeof(FinsArea)).Cast<FinsArea>().Select(a => (Enum)a),
                _ => Enumerable.Empty<Enum>(),
            };

            foreach (var candidate in candidates)
            {
                if (string.Equals(AreaDisplayNames[candidate], displayName, StringComparison.Ordinal)
                    || string.Equals(candidate.ToString(), displayName, StringComparison.OrdinalIgnoreCase))
                {
                    area = candidate;
                    return true;
                }
            }

            return false;
        }

        /// <summary>所选存储区是否线圈/离散输入（仅 bool 合法，且不使用位偏移）</summary>
        internal static bool IsCoilOrDiscreteArea(Enum? area)
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
        internal static readonly List<DataTypeOption> AllTypes = new()
        {
            new DataTypeOption { DisplayName = "整数 (int)", ActualType = typeof(int) },
            new DataTypeOption { DisplayName = "小数 (double)", ActualType = typeof(double) },
            new DataTypeOption { DisplayName = "单精度 (float)", ActualType = typeof(float) },
            new DataTypeOption { DisplayName = "文本 (string)", ActualType = typeof(string) },
            new DataTypeOption { DisplayName = "布尔 (bool)", ActualType = typeof(bool) },
            new DataTypeOption { DisplayName = "整数数组 (int[])", ActualType = typeof(int[]) },
            new DataTypeOption { DisplayName = "小数数组 (double[])", ActualType = typeof(double[]) },
            new DataTypeOption { DisplayName = "单精度数组 (float[])", ActualType = typeof(float[]) },
            new DataTypeOption { DisplayName = "文本数组 (string[])", ActualType = typeof(string[]) },
            new DataTypeOption { DisplayName = "布尔数组 (bool[])", ActualType = typeof(bool[]) },
            new DataTypeOption { DisplayName = "图像 (HImage)", ActualType = typeof(HImage) },
        };

        /// <summary>
        /// 某来源可选类型（弹窗的类型候选）：
        /// 网络来源下只过滤图像（设备侧没有对应点位）；文本、文本数组、bool 数组由创建校验按
        /// 协议/存储区再收窄（bool 数组只对 Modbus 线圈/离散输入有意义，string[] 需要每元素字节数）。
        /// </summary>
        internal static IEnumerable<DataTypeOption> TypesFor(bool isNetwork)
            => isNetwork
                ? AllTypes.Where(t => t.ActualType != typeof(HImage))
                : AllTypes;

        /// <summary>显示名 → CLR 类型；认不出返回 null（弹窗确认时的第一道闸）</summary>
        internal static Type? ResolveType(string? displayName)
            => string.IsNullOrWhiteSpace(displayName)
                ? null
                : AllTypes.FirstOrDefault(t => string.Equals(t.DisplayName, displayName, StringComparison.Ordinal))?.ActualType;

        /// <summary>CLR 类型 → 界面显示名（导入/导出用；未知类型回退 CLR 名，绝不返回空白）。</summary>
        internal static string TypeDisplayName(Type? type)
        {
            if (type == null) return string.Empty;
            var normalized = Nullable.GetUnderlyingType(type) ?? type;
            return AllTypes.FirstOrDefault(t => t.ActualType == normalized)?.DisplayName ?? normalized.Name;
        }

        /// <summary>存储区枚举 → 界面显示名（导出用；未知枚举回退成员名）。</summary>
        internal static string AreaDisplayName(Enum? area)
        {
            if (area == null) return string.Empty;
            return AreaDisplayNames.TryGetValue(area, out var name) ? name : area.ToString();
        }

        public DelegateCommand AddCommand { get; }
        public DelegateCommand<VariableNode> DeleteCommand { get; }
        public DelegateCommand<VariableNode> EditArrayCommand { get; }
        public DelegateCommand<VariableNode> WriteArrayCommand { get; }
        public DelegateCommand<VariableNode> ResetCommand { get; }

        /// <summary>数组元素行 ↺：只把该元素恢复为基准数组的对应项（不碰其它元素）</summary>
        public DelegateCommand<VariableNode> ResetElementCommand { get; }

        /// <summary>复制变量名（HMI 控件绑定预留入口）</summary>
        public DelegateCommand<VariableNode> CopyNameCommand { get; }

        /// <summary>重命名变量（经注册表统一入口，连线/监视项/轮询注册自动跟随）</summary>
        public DelegateCommand<VariableNode> RenameCommand { get; }

        /// <summary>展开/折叠左栏连接行（只影响左栏，不改变右侧作用域）。</summary>
        public DelegateCommand<VariableSourceNode> ToggleSourceExpandCommand { get; }

        /// <summary>
        /// 打开显示分组编辑器（变量管理内的覆盖层）。
        /// <para>与扫描组的分工：分组是方案级、纯展示分类，只做归类与筛选；
        /// 扫描组是连接级调度资源，只在通讯设置里维护。</para>
        /// </summary>
        public DelegateCommand OpenGroupManagerCommand { get; }

        public DelegateCommand CloseGroupManagerCommand { get; }

        /// <summary>导出当前设备的网络变量表（xlsx；全部 / 当前显示分组二选一）。</summary>
        public DelegateCommand ExportCommand { get; }

        /// <summary>从 xlsx 批量导入当前设备的网络变量（差异预览 + 事务应用）。</summary>
        public DelegateCommand ImportCommand { get; }

        /// <summary>显示分组编辑器（方案级、纯展示分类）。</summary>
        public VariableGroupEditorViewModel GroupEditor { get; }

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

            // 显示分组编辑器：方案级资源，读 _workspace.CurrentSolution.VariableGroups。
            // 必须在命令之前建好（命令体执行时才读它，但字段不能为空）。
            GroupEditor = new VariableGroupEditorViewModel(workspace);
            GroupEditor.Changed += OnVariableGroupsChanged;

            AddCommand = new DelegateCommand(AddVariable);
            CloseCommand = new DelegateCommand(() => RequestClose.Invoke(new DialogResult(ButtonResult.OK)));
            DeleteCommand = new DelegateCommand<VariableNode>(DeleteVariable);
            ResetCommand = new DelegateCommand<VariableNode>(ResetVariable);
            ResetElementCommand = new DelegateCommand<VariableNode>(ResetElement);
            EditArrayCommand = new DelegateCommand<VariableNode>(ExecuteEditArray);
            WriteArrayCommand = new DelegateCommand<VariableNode>(ExecuteWriteArray);
            CopyNameCommand = new DelegateCommand<VariableNode>(
                n => Clipboard.SetText(n?.Name ?? ""),
                n => n != null && n.IsRootNode);
            RenameCommand = new DelegateCommand<VariableNode>(
                RenameVariable,
                n => n != null && n.IsRootNode && n.OriginalModel != null && !IsSystemVariable(n.OriginalModel));

            ToggleSourceExpandCommand = new DelegateCommand<VariableSourceNode>(ToggleSourceExpand);
            OpenGroupManagerCommand = new DelegateCommand(() => GroupEditor.Open());
            CloseGroupManagerCommand = new DelegateCommand(() => GroupEditor.Close());

            // 批量导入/导出只对"某条设备连接"有意义：本地/系统来源没有设备地址表可导。
            ExportCommand = new DelegateCommand(ExecuteExport,
                () => IsNetworkSource && SelectedConnection != null && _workspace.CurrentSolution != null);
            ImportCommand = new DelegateCommand(ExecuteImport,
                () => IsNetworkSource && SelectedConnection != null && _workspace.CurrentSolution != null);

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

            // 显示分组（仅网络变量）：选项 = 未分组 + 方案分组；悬空 Id 归入未分组。
            // 回调必须在赋完初值后再挂，否则构造节点时就会触发一次"变更"。
            if (isNetwork)
            {
                var groupId = NormalizeGroupId((gv as NetworkVariableModel)?.GroupId ?? Guid.Empty);
                node.GroupId = groupId;
                node.GroupOptions.Add(new VariableGroupOption
                {
                    Id = Guid.Empty,
                    Name = VariableGroupEditorViewModel.UngroupedName
                });
                foreach (var group in Groups)
                    node.GroupOptions.Add(new VariableGroupOption { Id = group.Id, Name = group.Name });

                node.SelectedGroup = node.GroupOptions.FirstOrDefault(o => o.Id == groupId)
                                     ?? node.GroupOptions[0];
                node.GroupChanged = OnGroupChanged;
            }

            // 编辑结束（提交/取消）后补一次长度重建：编辑期间那次推送被跳过了
            node.ValueEditCompleted += OnNodeValueEditCompleted;
            return node;
        }

        /// <summary>
        /// 显示分组下拉变更：只回写变量模型 + 刷新左栏计数，**不重注册轮询**。
        /// 与扫描组（<see cref="OnScanGroupChanged"/> 必须 ReRegister）刻意不同：
        /// 分组只做归类与筛选，不影响采集。
        /// </summary>
        private void OnGroupChanged(VariableNode node)
        {
            if (node?.OriginalModel is not NetworkVariableModel nv) return;

            var option = node.SelectedGroup;
            if (option == null)
            {
                // 选项被重填时 ComboBox 可能短暂把 SelectedItem 写成 null：
                // 绝不能用它覆盖模型；把选中项回填成模型当前组，避免单元格留空。
                var fallback = node.GroupOptions.FirstOrDefault(o => o.Id == node.GroupId)
                               ?? node.GroupOptions.FirstOrDefault();
                if (fallback != null && !ReferenceEquals(node.SelectedGroup, fallback))
                    node.SelectedGroup = fallback;
                return;
            }

            if (node.GroupId == option.Id && nv.GroupId == option.Id) return;

            ApplyGroupSelection(nv, node, option);
            RefreshSourceCounts();
            UpdateFilteredList();
        }

        /// <summary>
        /// 显示分组选择落账（纯函数，便于断言）：
        /// null 选项不覆盖模型；同 Id 不重复写。返回落账后的 GroupId。
        /// </summary>
        public static Guid ApplyGroupSelection(
            NetworkVariableModel? model, VariableNode? node, VariableGroupOption? option)
        {
            if (node == null) return Guid.Empty;
            if (model == null || option == null) return node.GroupId;

            node.GroupId = option.Id;
            model.GroupId = option.Id;
            return option.Id;
        }

        /// <summary>
        /// 分组表变更（新增 / 改名 / 删除）后的刷新。
        /// <para>必须重建变量节点：每个节点的 <c>GroupOptions</c> 是建节点那一刻的快照，
        /// 只刷新左栏会让明细表的下拉继续显示旧组名。重建按 VariableId 保留数组展开态。</para>
        /// <para>当前选中的分组被删掉时回落到「未分组」（分组表即时生效，没有撤销可找回）。</para>
        /// </summary>
        private void OnVariableGroupsChanged()
        {
            if (_selectedGroupId is { } gid && gid != Guid.Empty && Groups.All(g => g.Id != gid))
                _selectedGroupId = Guid.Empty;

            RebuildSourceTree();
            RefreshTree();
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

            string previous = nv.ScanGroup ?? string.Empty;
            if (string.Equals(previous, stored, StringComparison.Ordinal)) return;

            nv.ScanGroup = stored;
            if (!_bridge.ReRegister(nv))
            {
                // 注册失败（连接不存在/地址非法）：模型必须退回，否则界面显示新组、轮询仍按旧组。
                nv.ScanGroup = previous;
                node.ScanGroupText = string.IsNullOrWhiteSpace(previous)
                    ? PollScheduler.DefaultGroupName
                    : previous;
                Notifier.ShowWarning($"扫描组「{selected}」重注册失败（连接不存在或地址非法），已恢复原扫描组。");
            }
        }

        #region 批量导入 / 导出

        private void ExecuteExport()
        {
            var connection = SelectedConnection;
            if (connection == null) return;

            var parameters = new DialogParameters
            {
                { DeviceVariableExportViewModel.ConnectionNameKey, connection.ConnectionName },
                { DeviceVariableExportViewModel.CurrentGroupIdKey, _selectedGroupId ?? Guid.Empty },
                { DeviceVariableExportViewModel.HasCurrentGroupKey, _selectedGroupId != null },
                { DeviceVariableExportViewModel.CurrentGroupNameKey, SelectedGroupName },
            };
            _dialogs.ShowDialog(DeviceVariableExportViewModel.DialogName, parameters, OnExportClosed);
        }

        private void OnExportClosed(IDialogResult result)
        {
            if (result.Result != ButtonResult.OK) return;
            if (!result.Parameters.TryGetValue<string>(DeviceVariableExportViewModel.ExportedPathKey, out var path)) return;

            result.Parameters.TryGetValue<int>(DeviceVariableExportViewModel.ExportedCountKey, out var count);
            Notifier.ShowSuccess($"变量表已导出（{count} 个变量）：{path}");
        }

        private void ExecuteImport()
        {
            var connection = SelectedConnection;
            if (connection == null) return;

            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Excel 工作簿 (*.xlsx)|*.xlsx",
                Title = "选择变量表",
                CheckFileExists = true,
            };
            if (dialog.ShowDialog() != true) return;

            var parameters = new DialogParameters
            {
                { DeviceVariableImportViewModel.FilePathKey, dialog.FileName },
                { DeviceVariableImportViewModel.ConnectionNameKey, connection.ConnectionName },
            };
            _dialogs.ShowDialog(DeviceVariableImportViewModel.DialogName, parameters, OnImportClosed);
        }

        /// <summary>
        /// 导入弹窗关闭：只在确认（含结果页 × 返回 OK）时重建左栏与明细；取消保持用户原选择。
        /// <para>导入是即时落地（结果页只是回执），变量集合与分组表可能已被新增变量、
        /// 自动建组改过；结果页点 × 也会返回 OK，所以确认分支必须刷新。</para>
        /// </summary>
        private void OnImportClosed(IDialogResult result)
        {
            // 取消（未应用）不改工作区：保留用户原来的分组选择，不重建左栏。
            if (result.Result != ButtonResult.OK) return;

            var connectionName = SelectedConnection?.ConnectionName;

            RebuildSourceTree();
            if (connectionName != null)
            {
                // 导入后统一回到该设备的「全部」：新增的变量可能落在任意分组，
                // 停留在导入前的分组会让用户以为"导进来的没几个"。
                var source = FindSourceNode(connectionName);
                if (source != null) SelectedSource = source;
            }
            RefreshTree();

            if (!result.Parameters.TryGetValue<int>(DeviceVariableImportViewModel.AddedKey, out var added)) return;

            result.Parameters.TryGetValue<int>(DeviceVariableImportViewModel.UpdatedKey, out var updated);
            result.Parameters.TryGetValue<int>(DeviceVariableImportViewModel.DeletedKey, out var deleted);
            result.Parameters.TryGetValue<int>(DeviceVariableImportViewModel.SkippedKey, out var skipped);

            var parts = new List<string> { $"新增 {added}", $"更新 {updated}" };
            if (deleted > 0) parts.Add($"删除 {deleted}");
            if (skipped > 0) parts.Add($"跳过 {skipped}");
            Notifier.ShowSuccess($"变量表导入完成：{string.Join(" · ", parts)}");
        }

        #endregion

        protected override void CreateChildNodes(IVariable gv, VariableNode parentNode)
        {
            var defArray = gv.DefaultValue as Array;
            var valArray = gv.Value as Array;
            int len = Math.Max(defArray?.Length ?? 0, valArray?.Length ?? 0);

            // 网络数组：AddressConfig.Length 是配置的唯一真相源（导入可改大也可改小）。
            // 不能与旧 Value/基准取 max——5→3 时旧值长度 5 会让树永远显示 5 行，
            // 与数组编辑弹窗（按地址长度 3 行）和轮询跨度脱节。
            if (gv is NetworkVariableModel nv && nv.AddressConfig != null)
                len = nv.AddressConfig.Length;

            var elementType = gv.DataType.GetElementType();

            for (int i = 0; i < len; i++)
            {
                var child = new VariableNode
                {
                    Name = $"[{i}]",
                    DataType = elementType,
                    TypeName = elementType?.Name,
                    ChildDefaultValue =
                        defArray != null && i < defArray.Length ? defArray.GetValue(i) : null,
                    ChildValue =
                        valArray != null && i < valArray.Length ? valArray.GetValue(i) : null,
                    // 子行要能就地改元素值：得知道"我属于哪个变量、是第几项"
                    // （写回逻辑见 VariableNode.CommitChildElementEdit）
                    OriginalModel = parentNode.OriginalModel,
                    ElementIndex = i,
                    Level = 1,
                    SourceLabel = parentNode.SourceLabel,
                    IsNetwork = parentNode.IsNetwork,
                    IsConnected = parentNode.IsConnected,
                };

                // 编辑结束（提交/取消）后补一次长度重建（见 OnNodeValueEditCompleted）
                child.ValueEditCompleted += OnNodeValueEditCompleted;
                parentNode.Children.Add(child);
            }
        }

        /// <summary>
        /// 单元格编辑结束（提交成功或 Esc 取消）后的补偿：
        /// 编辑期间 <see cref="OnVariableValueChanged"/> 会跳过"长度变化 → 重建子行"（防止吞掉草稿），
        /// 若外部恰在编辑中换了数组长度，取消路径没有任何模型事件能补上这次重建——这里补。
        /// </summary>
        private void OnNodeValueEditCompleted(VariableNode node)
        {
            var model = node?.OriginalModel;
            if (model == null) return;

            var root = NodesOf(model).FirstOrDefault(n => n.IsRootNode);
            if (root == null) return;

            if (ShouldRebuildArrayRows(model, root.Children.Count))
                RefreshTree();
        }

        private void OnVariableValueChanged(object sender, EventArgs e)
        {
            // 值变化就地刷新（2026-10-11 收口，取代 C4 的"脏标记 + 800ms 全树重建"节拍）：
            // 节点对象在两次结构变化之间保持稳定，正在编辑的单元格不再有被重建吞掉的窗口期，
            // 轮询推送也不再引发整树 Clear+重建。事件可能来自流程/轮询线程 → 封送回 UI 线程再动 UI。
            if (sender is not IVariable gv) return;
            VisionMaster.Helpers.SafeDispatch.BeginInvoke(() =>
            {
                var nodes = NodesOf(gv).ToList();
                if (nodes.Count == 0) return;

                // 数组长度变了（典型：流程写入另一组条码 / 坐标）：子行是按长度生成的，
                // 只刷显示会留下旧行数——「N 项」徽标、[i] 行、超出新长度的提交全部错位。
                // 编辑中不重建（会吞掉输入中的草稿），提交后由 RefreshTree 兜底。
                if (!nodes.Any(n => n.IsEditingValue) && ShouldRebuildArrayRows(gv, nodes[0].Children.Count))
                {
                    RefreshTree();
                    return;
                }

                foreach (var node in nodes)
                    node.RefreshValueDisplay();
            });
        }

        /// <summary>
        /// 数组值推送后是否需要重建子行：期望行数（max(当前值长度, 基准长度)，与
        /// <see cref="CreateChildNodes"/> 同口径）与现有子行数不一致才重建。
        /// <para>抽成静态纯函数便于断言：VM 构造要通信管理器 / 桥接器 / 弹窗服务，
        /// 冒烟里凑齐这些依赖不划算，而这段恰恰是最容易漏的一段。</para>
        /// </summary>
        public static bool ShouldRebuildArrayRows(IVariable? variable, int currentChildCount)
        {
            if (variable?.DataType?.IsArray != true) return false;

            // 与 CreateChildNodes 同口径：当前值可能是 null（流程写 null / 复位），
            // 此时按 0 计，不能因为"不是数组"就直接放行（那会和建行口径分叉）。
            int currentLength = variable.Value is Array current ? current.Length : 0;
            int baselineLength = (variable.DefaultValue as Array)?.Length ?? 0;

            // 网络数组的元素个数是配置项（AddressConfig.Length），是唯一真相源；
            // 旧 Value/基准可能更长（缩小场景），不能取 max，否则 5→3 永远判不出差异。
            if (variable is NetworkVariableModel nv && nv.AddressConfig != null)
                return nv.AddressConfig.Length != currentChildCount;

            return Math.Max(currentLength, baselineLength) != currentChildCount;
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
                BuildAddressPreview,
                // 候选走回调：弹窗里切换「所属连接」时，属性网格整表重建、候选按新协议重算
                new NetworkVariableDraftContext
                {
                    AreaOptionsFor = name => GetAreaOptions(FindConnection(name)),
                    DefaultAreaFor = name => GetDefaultAreaName(FindConnection(name)),
                    ScanGroupsFor = name => GetScanGroupOptions(name),
                })
            {
                TypeName = typeOptions.FirstOrDefault() ?? string.Empty,
                ConnectionName = connection?.ConnectionName ?? string.Empty,
                // 扫描组默认沿用"建完记住"的那个（RefreshNewScanGroupOptions 已保证它在组表里）
                ScanGroupName = SelectedNewScanGroup,
                AreaName = connection == null ? string.Empty : GetDefaultAreaName(connection),
                Offset = "0",
                BitOffset = 0,
            };
        }

        /// <summary>按连接名取当前方案的连接（弹窗存活期间连接可能被删，取不到返回 null）</summary>
        private CommunicationConfig? FindConnection(string? connectionName)
            => string.IsNullOrWhiteSpace(connectionName)
                ? null
                : Connections.FirstOrDefault(c => string.Equals(c.ConnectionName, connectionName, StringComparison.Ordinal));

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

            if (!TryResolveArea(connection, draft.AreaName, out var area)) return "（先选存储区）";

            if (!TryBuildAddress(connection.Protocol, area, draft.Offset, draft.BitOffset, targetType,
                    out var address, out var error,
                    station: draft.Station,
                    elementCount: targetType.IsArray || targetType == typeof(string) ? draft.ElementCount : 1,
                    elementByteLength: targetType.IsArray && targetType.GetElementType() == typeof(string)
                        ? draft.ElementByteLength
                        : 0,
                    addressOffset: draft.AddressOffset,
                    useLegacyAddress: draft.UseLegacyAddress,
                    dbNumber: draft.DbNumber,
                    mcVendor: McVendorOf(connection)))
                return $"（{error}）";

            // 预览与落库同一份构造逻辑：非法地址（40001 写法与存储区不匹配、偏移后为负等）
            // 在预览里直接说原因，而不是显示一个"看起来能读"的地址。
            var (valid, validationError) = address.Validate();
            return valid ? address.Address : $"（{validationError}）";
        }

        /// <summary>解析弹窗草稿里的初始值文本：留空 = 类型默认值；非法文本返回 false 并给出中文原因</summary>
        internal static bool TryGetNewInitialValue(string? text, Type targetType, out object? initial, out string error)
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
                // 数组给基准一份独立副本（与 VariableFactory 同一口径）：
                // 同实例会让元素就地编辑同时改掉基准
                DefaultValue = initValue is Array initArray ? initArray.Clone() : initValue,
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
            if (!TryResolveArea(connection, draft.AreaName, out var area))
            {
                EasyDialog.ShowSync("请选择存储区！", "提示");
                return;
            }
            var elementType = targetType.IsArray ? targetType.GetElementType() : null;
            bool isArray = targetType.IsArray && targetType != typeof(byte[]);
            bool isStringScalar = targetType == typeof(string);
            bool isStringArray = isArray && elementType == typeof(string);
            bool isBoolArray = isArray && elementType == typeof(bool);

            if (isArray)
            {
                if (elementType == null)
                {
                    EasyDialog.ShowSync("数组元素类型无法确定，请重新选择数据类型。", "提示");
                    return;
                }
                if (elementType == typeof(bool))
                {
                    if (!IsCoilOrDiscreteArea(area))
                    {
                        EasyDialog.ShowSync("bool 数组仅支持 Modbus 线圈/离散输入区（S7 的位数组语义不唯一）。", "提示");
                        return;
                    }
                }
                else if (elementType != typeof(string) && elementType != typeof(int)
                         && elementType != typeof(double) && elementType != typeof(float))
                {
                    EasyDialog.ShowSync(
                        $"网络数组暂不支持元素类型 {elementType.Name}（可用 int / double / float / string / bool）！",
                        "提示");
                    return;
                }

                if (isStringArray && draft.ElementByteLength < 1)
                {
                    EasyDialog.ShowSync("字符串数组必须填「每元素字节数」（>= 1）。", "提示");
                    return;
                }
            }

            if (!isArray && !isStringScalar && draft.ElementCount != 1)
            {
                // 标量填 10 曾经是"读 10 个元素但只解第一个"的静默陷阱；现在明确拦住。
                EasyDialog.ShowSync("「元素个数」只对数组类型有效；标量请填 1（或改选数组类型）。", "提示");
                return;
            }
            if ((isArray || isStringScalar) && draft.ElementCount < 1)
            {
                EasyDialog.ShowSync("元素个数 / 字符串字节长度必须 >= 1。", "提示");
                return;
            }
            if (IsCoilOrDiscreteArea(area) && targetType != typeof(bool) && !isBoolArray)
            {
                EasyDialog.ShowSync("线圈/离散输入区仅支持布尔类型（或 bool 数组）！", "提示");
                return;
            }
            if ((isArray || isStringScalar) && draft.EnableConversion)
            {
                EasyDialog.ShowSync("数组与字符串变量暂不支持工程量换算，请关闭「启用工程量换算」。", "提示");
                return;
            }

            object? initial;
            if (isArray)
            {
                // 数组没有"单值初始值"：整组值由设备轮询/整组写入维护，基准不参与恢复初始值
                initial = null;
            }
            else if (!TryGetNewInitialValue(draft.InitialValue, targetType, out initial, out var initialError))
            {
                EasyDialog.ShowSync($"初始值无效：{initialError}", "提示");
                return;
            }

            try
            {
                if (!TryBuildAddress(
                        connection.Protocol, area, draft.Offset, draft.BitOffset, targetType,
                        out var address, out var addressError,
                        station: draft.Station,
                        enableConversion: draft.EnableConversion,
                        scale: draft.Scale,
                        engineeringOffset: draft.EngineeringOffset,
                        unit: draft.Unit,
                        elementCount: isArray || isStringScalar ? draft.ElementCount : 1,
                        elementByteLength: isStringArray ? draft.ElementByteLength : 0,
                        addressOffset: draft.AddressOffset,
                        useLegacyAddress: draft.UseLegacyAddress,
                        dbNumber: draft.DbNumber,
                        mcVendor: McVendorOf(connection)))
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

                // 数组跨度不能超过单次读取上限（Modbus 120 寄存器 / S7 110 字节）：
                // 第一期不做分块，超了必须在创建时明确拒绝，而不是建完发现"永远读不到"。
                if ((isArray || isStringScalar) && address.TryCreatePollAddress(out var poll) && poll != null
                    && poll.SpanUnits > poll.MaxSegmentUnits)
                {
                    EasyDialog.ShowSync(
                        $"读取跨度 {poll.SpanUnits} 个地址单位，超过单次读取上限 {poll.MaxSegmentUnits}；" +
                        "请减少元素个数 / 字符串字节长度",
                        "提示");
                    return;
                }

                var netVar = (NetworkVariableModel)VariableFactory.CreateNetwork(
                    draft.VariableName, targetType, connection.ConnectionName,
                    address, draft.Description);

                // 在当前显示分组作用域里新建 → 直接归入该组；在「全部 / 未分组」下新建 → 未分组。
                // 只影响归类，不参与采集/轮询/写入。
                netVar.GroupId = _selectedGroupId is { } selectedGroupId && selectedGroupId != Guid.Empty
                    ? selectedGroupId
                    : Guid.Empty;

                // 建的时候就定好节拍：桥接器注册时会把 ScanGroup 抄进 CommunicationVariable，
                // 事后再改要走 ReRegister，这里一次到位
                netVar.ScanGroup = ToStoredScanGroup(draft.ScanGroupName);

                // 区域合并读开关：同样在注册时被抄进轮询计划，建的时候一次到位
                netVar.MergeAdjacentRead = draft.MergeAdjacentRead;

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
        internal static bool TryBuildAddress(
            CommunicationType protocol, Enum area, string? offset, int bitOffset, Type targetType,
            out DeviceAddressBase address, out string error,
            int station = 0, bool enableConversion = false,
            double scale = 1.0, double engineeringOffset = 0, string? unit = null,
            int elementCount = 1, int elementByteLength = 0,
            int addressOffset = 0, bool useLegacyAddress = false,
            int dbNumber = 1, McVendor mcVendor = McVendor.Mitsubishi)
        {
            address = null!;
            error = string.Empty;

            // MC 的位访问由"存储区"表达（M/X/Y/...），地址串里没有 ".位号" 形态；
            // 若把草稿里的位偏移带进去，既不会进地址，还会让基类 Validate 对
            // "0-7" 做无意义的范围校验（用户填 12 会被拒，而该字段对 MC 根本无效）。
            int effectiveBitOffset = targetType == typeof(bool) && bitOffset >= 0
                && !IsCoilOrDiscreteArea(area)
                && protocol != CommunicationType.MitsubishiMc
                ? bitOffset
                : -1;

            // 数组/字符串：Length = 元素个数（数值数组）或总字节数（字符串标量）；
            // 其它标量固定 1（不允许标量填 10——那只会让"读 10 个只解第一个"重现）。
            bool isArray = targetType.IsArray && targetType != typeof(byte[]);
            bool isStringScalar = targetType == typeof(string);
            bool isStringArray = isArray && targetType.GetElementType() == typeof(string);
            int length = isArray || isStringScalar ? Math.Max(1, elementCount) : 1;
            int perElementBytes = isStringArray ? Math.Max(0, elementByteLength) : 0;
            var dataType = ToDataValueType(targetType);

            switch (protocol)
            {
                // ModbusRtu 与 ModbusTcp 的地址模型完全相同，只是链路不同（TCP / 串口）。
                // 站号逐帧生效：一条串口挂多个从站时靠它区分（0 = 跟随连接默认站号）。
                case CommunicationType.ModbusTcp:
                case CommunicationType.ModbusRtu:
                {
                    var modbus = new ModbusAddress
                    {
                        Area = (ModbusArea)area,
                        Offset = offset ?? "0",
                        DataType = dataType,
                        BitOffset = effectiveBitOffset,
                        Station = station,
                        Length = length,
                        ElementByteLength = perElementBytes,
                        AddressOffset = addressOffset,
                        UseLegacyAddress = useLegacyAddress
                    };
                    ApplyConversion(modbus, enableConversion, scale, engineeringOffset, unit);
                    address = modbus;
                    return true;
                }

                case CommunicationType.SiemensS7:
                {
                    if (useLegacyAddress)
                    {
                        error = "S7 地址不支持「40001 写法」，请直接填字节偏移";
                        return false;
                    }

                    var s7 = new S7Address
                    {
                        Area = (S7Area)area,
                        Offset = offset ?? "0",
                        DataType = dataType,
                        BitOffset = effectiveBitOffset,
                        Length = length,
                        ElementByteLength = perElementBytes,
                        AddressOffset = addressOffset,
                        DbNumber = Math.Max(1, dbNumber)
                    };
                    ApplyConversion(s7, enableConversion, scale, engineeringOffset, unit);
                    address = s7;
                    return true;
                }

                case CommunicationType.MitsubishiMc:
                {
                    var mc = new McAddress
                    {
                        Area = (McArea)area,
                        // 厂商决定 ZR 的进制与地址校验解析器（三菱 D100 / 基恩士 DM100 / 松下 DT100）
                        Vendor = mcVendor,
                        Offset = offset ?? "0",
                        DataType = dataType,
                        BitOffset = effectiveBitOffset,
                        Length = length,
                        ElementByteLength = perElementBytes,
                        AddressOffset = addressOffset
                    };
                    ApplyConversion(mc, enableConversion, scale, engineeringOffset, unit);
                    address = mc;
                    return true;
                }

                case CommunicationType.OmronFins:
                {
                    var fins = new FinsAddress
                    {
                        Area = (FinsArea)area,
                        Offset = offset ?? "0",
                        DataType = dataType,
                        BitOffset = effectiveBitOffset,
                        Length = length,
                        ElementByteLength = perElementBytes,
                        AddressOffset = addressOffset
                    };
                    ApplyConversion(fins, enableConversion, scale, engineeringOffset, unit);
                    address = fins;
                    return true;
                }

                default:
                    error = $"协议 {protocol} 的变量创建暂不支持";
                    return false;
            }
        }

        /// <summary>把草稿里的工程量换算参数落到地址对象上（未启用则保持默认关闭）。</summary>
        private static void ApplyConversion(
            DeviceAddressBase address, bool enabled, double scale, double engineeringOffset, string? unit)
        {
            if (!enabled) return;
            address.EnableConversion = true;
            address.Scale = scale;
            address.EngineeringOffset = engineeringOffset;
            address.Unit = unit;
        }

        /// <summary>CLR 类型 → 通信地址数据类型</summary>
        private static DataValueType ToDataValueType(Type t)
        {
            var u = Nullable.GetUnderlyingType(t) ?? t;
            if (u.IsArray) u = u.GetElementType() ?? u; // 数组按元素类型映射（int[] → Int32）
            if (u == typeof(bool)) return DataValueType.Boolean;
            if (u == typeof(string)) return DataValueType.String;
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
            var model = node?.OriginalModel;
            if (model == null) return;

            // 视图里 ↺ 只在可编辑态出现；这里再守一道，防止键盘/其它入口对
            // 系统变量或只读存储区执行"恢复初始值"（写了也不会回灌设备）。
            if (IsSystemVariable(model)) return;
            if ((model as IVariableDisplaySource)?.IsReadOnly == true) return;

            model.ResetToDefault();
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

        /// <summary>
        /// 数组编辑入口：打开「编辑数组」弹窗（本地数组变量）。
        ///
        /// 与旧「强制覆盖集合...」的区别（v2 设计基线）：
        /// ① 弹窗装填的是**当前值**（屏幕上看到的那组），不是 DefaultValue；
        /// ② 默认只写当前值，勾选「同时写入初始值（基准）」才写 DefaultValue；
        /// ③ 校验在弹窗内完成——填错就地标红，不丢编辑、不关闭弹窗。
        ///
        /// 结构改动（增删 / 上下移）只放在弹窗里：表格里的 40px 元素行若也挂删除图标，
        /// 误点一下就会让后面所有下标错位，代价远大于省下的两次点击。
        /// </summary>
        private void ExecuteEditArray(VariableNode node)
        {
            if (node?.OriginalModel is not LocalVariableModel gv) return;
            if (!gv.DataType.IsArray) return;

            if (gv.DataType.GetElementType() == null)
            {
                EasyDialog.ShowSync($"变量 [{gv.Name}] 的数组元素类型无法确定，请删除后重新创建。", "无法编辑");
                return;
            }

            // A4 同口径：运行中整体替换数组 = 换掉运行步骤正在读的那组数据。
            // 删除变量有这条互锁，数组覆盖同样必须有。
            if (_workspace.CurrentFlow?.RunState == FlowRunState.Running)
            {
                EasyDialog.ShowSync(
                    "流程运行中禁止编辑数组。\n请先停止流程——运行中的步骤按对象引用取值，整体替换会让它读到另一组数据。",
                    "互锁");
                return;
            }

            var parameters = new DialogParameters
            {
                { ArrayEditorViewModel.VariableKey, gv },
            };

            _dialogs.ShowDialog(ArrayEditorViewModel.DialogName, parameters, OnArrayEditorClosed);
        }

        /// <summary>
        /// 网络数组「写入数组…」：复用数组编辑弹窗，但语义是**整组写设备**。
        /// <para>三条互锁与本地数组编辑同口径：只读存储区不给入口、运行中禁止整组替换、
        /// 弹窗确认后由 <see cref="ArrayEditorViewModel"/> 内部调用 <c>TryWriteToValue</c>。</para>
        /// </summary>
        private void ExecuteWriteArray(VariableNode? node)
        {
            if (node?.OriginalModel is not NetworkVariableModel gv) return;
            if (!gv.DataType.IsArray) return;

            if (gv.IsReadOnlyArea)
            {
                EasyDialog.ShowSync("该变量位于只读存储区（离散输入 / S7 输入映像 I），不能写入。", "只读");
                return;
            }

            if (_workspace.CurrentFlow?.RunState == FlowRunState.Running)
            {
                EasyDialog.ShowSync(
                    "流程运行中禁止整组写入数组。\n请先停止流程——整组替换会覆盖运行步骤正在读的那组数据。",
                    "互锁");
                return;
            }

            var parameters = new DialogParameters
            {
                { ArrayEditorViewModel.VariableKey, gv },
                // 弹窗是模态的，但定时/变量触发的流程仍可能在弹窗打开期间启动；
                // 确认时再查一次，避免整组替换正在运行的那组数据。
                { ArrayEditorViewModel.IsFlowRunningKey, (Func<bool>)(() =>
                    _workspace.CurrentFlow?.RunState == FlowRunState.Running) },
            };

            _dialogs.ShowDialog(ArrayEditorViewModel.DialogName, parameters, OnArrayEditorClosed);
        }

        /// <summary>数组编辑弹窗关闭回调：只有确认才落地（取消 = 活对象分毫不动）</summary>
        private void OnArrayEditorClosed(IDialogResult result)
        {
            if (result.Result != ButtonResult.OK) return;

            // 网络数组：弹窗内部已经调用 TryWriteToValue 整组写设备，这里只需要刷新镜像显示。
            if (result.Parameters.TryGetValue<NetworkVariableModel>(ArrayEditorViewModel.VariableKey, out _))
            {
                RefreshTree();
                return;
            }

            if (!result.Parameters.TryGetValue<Array>(ArrayEditorViewModel.ArrayKey, out var array) || array == null) return;
            if (!result.Parameters.TryGetValue<LocalVariableModel>(ArrayEditorViewModel.VariableKey, out var model) || model == null) return;

            // 打开时查过一次，落地时必须再查：弹窗是模态的，但定时/变量触发的流程
            // 仍可能在弹窗打开期间启动，确认后照样会整体替换运行步骤正在读的数组。
            if (_workspace.CurrentFlow?.RunState == FlowRunState.Running)
            {
                EasyDialog.ShowSync("流程已启动，本次数组编辑未应用。\n请先停止流程再编辑数组。", "互锁");
                return;
            }

            bool syncBaseline = result.Parameters.TryGetValue<bool>(ArrayEditorViewModel.SyncBaselineKey, out var sync) && sync;
            ApplyArrayEdit(model, array, syncBaseline);

            RefreshTree();   // 长度可能变了，树必须重建（子行是按长度生成的）
        }

        /// <summary>
        /// 把弹窗结果落到模型（纯静态，便于断言）：
        /// 当前值**总是**写；初始值只在 <paramref name="syncBaseline"/> 为真时写。
        ///
        /// 为什么默认不写基准：旧「强制覆盖」两者一起写，且弹窗装填的是基准——
        /// 用户看到的是旧值、点确定却把屏幕上的运行值换掉了。基准是「恢复初始值」的锚点，
        /// 改它必须是一次显式选择。
        /// </summary>
        public static void ApplyArrayEdit(LocalVariableModel? model, Array? array, bool syncBaseline)
        {
            if (model == null || array == null) return;

            model.Value = array;
            if (syncBaseline)
                model.DefaultValue = array.Clone();
        }

        /// <summary>元素级 ↺：只把该元素恢复为基准对应项（基准里没有这一项时什么都不做）</summary>
        private void ResetElement(VariableNode? node)
        {
            if (node?.OriginalModel == null) return;
            if (IsSystemVariable(node.OriginalModel)) return;
            node.ResetElementValue();
        }

        /// <summary>
        /// 把编辑器回读的文本转成目标类型的数组（纯函数，便于断言）。
        /// 失败返回 null 并把原因写进 <paramref name="error"/>。
        ///
        /// 规则（与"当前值"就地编辑同源，统一走 <see cref="VariableValueConverter"/>）：
        /// - 文本元素：原样保留（空串是合法值——条码/文本数组里"空槽"有意义）；
        /// - 其余元素：留空 = 该类型的默认值（数值 0 / 布尔 false）；
        /// - 非空文本：不变文化、布尔认 1/0/是/否/on/off、**拒绝千分位分隔符**；
        /// - 出错时给出**第几项 + 原值 + 原因**，而不是一句英文的 Convert 异常。
        /// </summary>
        public static Array? TryBuildArray(Type elementType, IReadOnlyList<string> texts, out string error)
            => TryBuildArray(elementType, texts, out error, out _);

        /// <summary>同上，额外返回出错项的下标（弹窗据此把那一行标红）</summary>
        public static Array? TryBuildArray(Type elementType, IReadOnlyList<string> texts, out string error, out int errorIndex)
        {
            error = string.Empty;
            errorIndex = -1;
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

            Type target = Nullable.GetUnderlyingType(elementType) ?? elementType;
            var array = Array.CreateInstance(elementType, texts.Count);

            for (int i = 0; i < texts.Count; i++)
            {
                string text = texts[i] ?? string.Empty;

                // 文本元素原样保留（含空串）：旧实现把空串写成 null，与弹窗提示的"文本空串"不一致，
                // 也让"故意留一个空条码槽"变成不可能。
                if (target == typeof(string))
                {
                    array.SetValue(text, i);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(text))
                {
                    array.SetValue(target.IsValueType ? Activator.CreateInstance(target) : null, i);
                    continue;
                }

                if (!VisionMaster.Helpers.VariableValueConverter.TryConvert(text, target, out var converted, out var convertError))
                {
                    error = $"第 {i + 1} 项：{convertError}";
                    errorIndex = i;
                    return null;
                }

                array.SetValue(converted, i);
            }

            return array;
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
                GroupEditor.Changed -= OnVariableGroupsChanged;
            }

            base.Dispose(disposing);
        }
    }
}
