using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using Prism.Commands;
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
                    // 联动新建变量区的本地/网络胶囊：选本地→本地模式，选网络连接→网络模式
                    if (value != null)
                        IsNetworkSource = value.IsNetwork;
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
                node.Count = node.Key == "Local"
                    ? _workspace.GlobalVariables.Count(v => v.VariableType == VariableType.Local)
                    : _workspace.GlobalVariables.Count(v => v is NetworkVariableModel n && n.ConnectionName == node.Key);
            }
        }

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
            var sourceKey = SelectedSource?.Key ?? "Local";
            var query = SearchText?.Trim() ?? "";

            foreach (var node in DisplayNodes)
            {
                // 来源筛选：根节点判定来源（本地节点只显本地，连接节点只显该连接），子节点跟随父级
                if (node.IsRootNode)
                {
                    bool matchSource = sourceKey == "Local"
                        ? !node.IsNetwork
                        : (node.IsNetwork && node.SourceLabel == sourceKey);
                    if (!matchSource) continue;
                }

                // 搜索筛选
                if (query.Length > 0 && node.IsRootNode
                    && node.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0
                    && (node.Description?.IndexOf(query, StringComparison.OrdinalIgnoreCase) ?? -1) < 0
                    && (node.Address?.IndexOf(query, StringComparison.OrdinalIgnoreCase) ?? -1) < 0)
                {
                    continue;
                }

                FilteredDisplayNodes.Add(node);
            }
        }

        #endregion

        #region 新建变量（本地/网络双模式）

        private bool _isNetworkSource;
        /// <summary>新建面板当前来源：false=本地 true=网络（胶囊切换，两个 Radio 双向绑定互斥同步）。
        /// 与左侧来源树双向联动：切本地→选中"本地变量"分组，切网络→选中当前/首个网络连接分组</summary>
        public bool IsNetworkSource
        {
            get => _isNetworkSource;
            set
            {
                if (SetProperty(ref _isNetworkSource, value))
                {
                    RaisePropertyChanged(nameof(IsLocalSource));
                    RaisePropertyChanged(nameof(FilteredTypes));
                    SelectedAreaType = null;
                    NewBitOffset = 0; // S7 布尔必须带位偏移（M0.0），默认位 0

                    // 网络模式仅支持标量：当前类型（string/数组）不合法时回落到首个合法项
                    if (value && (SelectedType == null || !FilteredTypes.Contains(SelectedType)))
                        SelectedType = FilteredTypes.First();

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

        /// <summary>IsNetworkSource 的反值（供"本地"胶囊直接双向绑定）</summary>
        public bool IsLocalSource
        {
            get => !_isNetworkSource;
            set => IsNetworkSource = !value;
        }

        public ObservableCollection<CommunicationConfig> Connections { get; } = new();

        private CommunicationConfig? _selectedConnection;
        public CommunicationConfig? SelectedConnection
        {
            get => _selectedConnection;
            set
            {
                if (SetProperty(ref _selectedConnection, value))
                {
                    RefreshAreaOptions();
                    NewBitOffset = 0; // S7 布尔必须带位偏移（M0.0），默认位 0
                }
            }
        }

        public ObservableCollection<Enum> AreaOptions { get; } = new();

        private Enum? _selectedAreaType;
        public Enum? SelectedAreaType
        {
            get => _selectedAreaType;
            set
            {
                if (SetProperty(ref _selectedAreaType, value))
                {
                    RaisePropertyChanged(nameof(IsCoilOrDiscrete));
                    RaisePropertyChanged(nameof(IsBitMode)); // 存储区切换影响位访问判定（线圈区无位偏移）
                }
            }
        }

        private string _newOffset = "0";
        public string NewOffset
        {
            get => _newOffset;
            set => SetProperty(ref _newOffset, value);
        }

        private int _newBitOffset = 0;
        public int NewBitOffset
        {
            get => _newBitOffset;
            set
            {
                if (SetProperty(ref _newBitOffset, value))
                    RaisePropertyChanged(nameof(IsBitMode));
            }
        }

        /// <summary>是否位访问（Boolean+位偏移有效且非线圈区），决定位偏移输入框可见性与地址构造</summary>
        public bool IsBitMode => SelectedType?.ActualType == typeof(bool) && NewBitOffset >= 0 && !IsCoilOrDiscrete;

        /// <summary>所选存储区是否线圈/离散输入（仅 bool 合法）</summary>
        public bool IsCoilOrDiscrete => SelectedAreaType is ModbusArea.Coils or ModbusArea.DiscreteInputs;

        private int _newPollIntervalMs = 1000;
        public int NewPollIntervalMs
        {
            get => _newPollIntervalMs;
            set => SetProperty(ref _newPollIntervalMs, value);
        }

        private void RefreshAreaOptions()
        {
            AreaOptions.Clear();
            switch (SelectedConnection?.Protocol)
            {
                case CommunicationType.ModbusTcp:
                    foreach (ModbusArea a in Enum.GetValues(typeof(ModbusArea))) AreaOptions.Add(a);
                    SelectedAreaType = ModbusArea.HoldingRegisters;
                    break;
                case CommunicationType.SiemensS7:
                    foreach (S7Area a in Enum.GetValues(typeof(S7Area))) AreaOptions.Add(a);
                    SelectedAreaType = S7Area.M;
                    break;
            }
        }

        #endregion

        /// <summary>数据类型是否布尔（网络模式下决定位偏移输入框显示）</summary>
        public bool IsBoolType => SelectedType?.ActualType == typeof(bool);

        public string NewVarName
        {
            get => field;
            set => SetProperty(ref field, value);
        }

        public string NewVarDescription
        {
            get => field;
            set => SetProperty(ref field, value);
        }

        public DataTypeOption SelectedType
        {
            get => field;
            set
            {
                if (SetProperty(ref field, value))
                {
                    RaisePropertyChanged(nameof(IsBitMode));
                    RaisePropertyChanged(nameof(IsBoolType));
                }
            }
        }

        public ObservableCollection<DataTypeOption> AvailableTypes { get; } =
            new()
            {
                new DataTypeOption { DisplayName = "整数 (int)", ActualType = typeof(int) },
                new DataTypeOption { DisplayName = "小数 (double)", ActualType = typeof(double) },
                new DataTypeOption { DisplayName = "文本 (string)", ActualType = typeof(string) },
                new DataTypeOption { DisplayName = "布尔 (bool)", ActualType = typeof(bool) },
                new DataTypeOption { DisplayName = "整数数组 (int[])", ActualType = typeof(int[]) },
                new DataTypeOption { DisplayName = "小数数组 (double[])", ActualType = typeof(double[]) },
                new DataTypeOption { DisplayName = "文本数组 (string[])", ActualType = typeof(string[]) },
                new DataTypeOption { DisplayName = "布尔数组 (bool[])", ActualType = typeof(bool[]) },
            };

        /// <summary>
        /// 当前模式可选类型（新建面板下拉绑定此属性）：
        /// 网络模式下过滤文本与数组——通信层仅支持标量点读，
        /// ToDataValueType 对 string/数组会兜底成 Int32，导致地址语义错误（隐患修复）
        /// </summary>
        public IEnumerable<DataTypeOption> FilteredTypes =>
            _isNetworkSource
                ? AvailableTypes.Where(t =>
                    t.ActualType != typeof(string) &&
                    t.ActualType != typeof(string[]) &&
                    !t.ActualType.IsArray)
                : AvailableTypes;

        public DelegateCommand AddCommand { get; }
        public DelegateCommand<VariableNode> DeleteCommand { get; }
        public DelegateCommand<VariableNode> EditArrayCommand { get; }
        public DelegateCommand<VariableNode> ResetCommand { get; }

        /// <summary>网络变量写值（弹出单值输入，直通设备）</summary>
        public DelegateCommand<VariableNode> WriteValueCommand { get; }

        /// <summary>复制变量名（HMI 控件绑定预留入口）</summary>
        public DelegateCommand<VariableNode> CopyNameCommand { get; }

        public DelegateCommand<VariableSourceNode> SelectSourceCommand { get; }

        public GlobalVariableManagerViewModel(
            IWorkspaceManager workspace,
            AdvancedCommunicationManager communicationManager,
            NetworkVariableBridge bridge)
            : base(workspace)
        {
            _communicationManager = communicationManager;
            _bridge = bridge;

            SelectedType = AvailableTypes.First();

            AddCommand = new DelegateCommand(AddVariable);
            DeleteCommand = new DelegateCommand<VariableNode>(DeleteVariable);
            ResetCommand = new DelegateCommand<VariableNode>(ResetVariable);
            EditArrayCommand = new DelegateCommand<VariableNode>(ExecuteEditArray);
            WriteValueCommand = new DelegateCommand<VariableNode>(ExecuteWriteValue);
            CopyNameCommand = new DelegateCommand<VariableNode>(
                n => Clipboard.SetText(n?.Name ?? ""),
                n => n != null && n.IsRootNode);

            SelectSourceCommand = new DelegateCommand<VariableSourceNode>(node => SelectedSource = node);

            foreach (var conn in _communicationManager.GetAllConnections())
                Connections.Add(conn);

            // 连接状态变化 → 徽标联动（命名方法：Dispose 才能正确退订）
            _communicationManager.ConnectionStateChanged += OnConnectionStateChangedHandler;

            // 变量增删 → 来源树计数刷新
            _workspace.GlobalVariables.CollectionChanged += OnVariablesChangedForCountsHandler;

            // C4：值变化的全树重建合并刷新——旧实现"每值一变立即整树 Clear+重建"，
            // 高频变化时右表闪烁、正在编辑的单元格被重建吞掉；改为脏标记 + 800ms 节拍合并
            _treeRefreshTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(800),
            };
            _treeRefreshTimer.Tick += (s, e) =>
            {
                if (!_treeDirty) return;
                _treeDirty = false;
                RefreshTree();
            };
            _treeRefreshTimer.Start();

            RebuildSourceTree();
            RefreshTree();
        }

        private bool _treeDirty;
        private readonly System.Windows.Threading.DispatcherTimer _treeRefreshTimer;

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
            }
            else
            {
                node.IsConnected = true;
            }
            return node;
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
            // C4：只标脏不打全树——由 800ms 节拍的 DispatcherTimer 合并刷新。
            // 该事件可能由流程线程触发，故只写 bool 标记（UI 更新统一回到定时器所在的 UI 线程）
            if (sender is LocalVariableModel)
                _treeDirty = true;
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

        #region 新建变量

        private void AddVariable()
        {
            if (string.IsNullOrWhiteSpace(NewVarName))
            {
                EasyDialog.ShowSync("变量名不能为空！", "提示");
                return;
            }

            // C3：查重改大小写不敏感——监视栏/搜索匹配均为 OrdinalIgnoreCase，
            // 旧口径(Ordinal)允许 Var 与 var 共存，两套匹配规则互相矛盾
            if (_workspace.GlobalVariables.Any(s => s.Name.Equals(NewVarName, StringComparison.OrdinalIgnoreCase)))
            {
                EasyDialog.ShowSync("底层引擎已存在同名变量，请更换名称！", "提示");
                return;
            }

            Type targetType = SelectedType.ActualType;

            if (IsNetworkSource)
            {
                AddNetworkVariable(targetType);
            }
            else
            {
                AddLocalVariable(targetType);
            }
        }

        private void AddLocalVariable(Type targetType)
        {
            object initValue = targetType.IsArray
                ? Array.CreateInstance(targetType.GetElementType(), 0)
                : targetType == typeof(string) ? string.Empty : Activator.CreateInstance(targetType);

            var newVar = new LocalVariableModel
            {
                Name = NewVarName,
                DataType = targetType,
                Description = NewVarDescription,
                DefaultValue = initValue,
                Value = initValue,
            };

            _workspace.GlobalVariables.Add(newVar); // 集合事件自动挂监听 + 树刷新

            // 切换到本地来源分组，确保新变量立即可见（此前停留在非本地来源时新变量被过滤器吞掉）
            SelectedSource = SourceNodes.FirstOrDefault(s => s.Key == "Local");

            NewVarName = string.Empty;
            NewVarDescription = string.Empty;
            Notifier.ShowSuccess($"本地变量 [{newVar.Name}] 创建成功");
        }

        private void AddNetworkVariable(Type targetType)
        {
            if (SelectedConnection == null)
            {
                EasyDialog.ShowSync("请选择所属连接！", "提示");
                return;
            }
            if (SelectedAreaType == null)
            {
                EasyDialog.ShowSync("请选择存储区！", "提示");
                return;
            }
            if (IsCoilOrDiscrete && targetType != typeof(bool))
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

            DeviceAddressBase address;
            try
            {
                switch (SelectedConnection.Protocol)
                {
                    case CommunicationType.ModbusTcp:
                        address = new ModbusAddress
                        {
                            Area = (ModbusArea)SelectedAreaType,
                            Offset = NewOffset,
                            DataType = ToDataValueType(targetType),
                            BitOffset = IsBitMode ? NewBitOffset : -1
                        };
                        break;
                    case CommunicationType.SiemensS7:
                        address = new S7Address
                        {
                            Area = (S7Area)SelectedAreaType,
                            Offset = NewOffset,
                            DataType = ToDataValueType(targetType),
                            BitOffset = IsBitMode ? NewBitOffset : -1
                        };
                        break;
                    default:
                        EasyDialog.ShowSync($"协议 {SelectedConnection.Protocol} 的变量创建暂不支持", "提示");
                        return;
                }

                var (valid, error) = address.Validate();
                if (!valid)
                {
                    EasyDialog.ShowSync(error, "地址配置无效");
                    return;
                }

                var netVar = (NetworkVariableModel)VariableFactory.CreateNetwork(
                    NewVarName, targetType, SelectedConnection.ConnectionName,
                    address, NewVarDescription, pollIntervalMs: NewPollIntervalMs);

                _workspace.GlobalVariables.Add(netVar); // 桥接器监听集合 → 自动 Bind + RegisterVariable + 镜像接线

                // 切换到对应连接来源分组，确保新变量立即可见
                SelectedSource = SourceNodes.FirstOrDefault(s => s.Key == SelectedConnection.ConnectionName);

                NewVarName = string.Empty;
                NewVarDescription = string.Empty;
                Notifier.ShowSuccess($"网络变量 [{netVar.Name}] 创建成功（{SelectedConnection.ConnectionName} @ {address.Address}）");
            }
            catch (Exception ex)
            {
                EasyDialog.ShowSync($"创建网络变量失败：{ex.Message}", "错误");
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

        /// <summary>数组编辑器（本地数组变量）</summary>
        private void ExecuteEditArray(VariableNode node)
        {
            if (node?.OriginalModel is not LocalVariableModel gv) return;
            if (!gv.DataType.IsArray) return;
            ExecuteEditArrayCore(gv);
        }

        /// <summary>数组元素编辑对话框（编辑默认值并同步当前值）</summary>
        private async void ExecuteEditArrayCore(LocalVariableModel gv)
        {
            Type elementType = gv.DataType.GetElementType();
            var editList = new ObservableCollection<ArrayItemWrapper>();

            if (gv.DefaultValue is Array arr)
            {
                foreach (var item in arr)
                {
                    var wrapper = new ArrayItemWrapper { StringValue = item?.ToString() ?? "" };
                    wrapper.RemoveCommand = new DelegateCommand(() => editList.Remove(wrapper));
                    editList.Add(wrapper);
                }
            }

            string elementTypeName = elementType?.Name ?? "元素";
            var editor = new VariableArrayEditor { Elements = editList };
            var ok = EasyDialog.ShowPropertyGridSync($"编辑数组 [{gv.Name}]", editor);
            if (!ok) return;

            try
            {
                var newArray = Array.CreateInstance(elementType, editList.Count);
                for (int i = 0; i < editList.Count; i++)
                {
                    var converted = Convert.ChangeType(
                        editList[i].StringValue,
                        Nullable.GetUnderlyingType(elementType) ?? elementType);
                    newArray.SetValue(converted, i);
                }
                gv.DefaultValue = newArray;
                gv.Value = newArray.Clone();
                RefreshTree();
            }
            catch (Exception ex)
            {
                EasyDialog.ShowSync($"数组转换失败：{ex.Message}", "错误");
            }
            await System.Threading.Tasks.Task.CompletedTask;
        }

        /// <summary>数组编辑对话框承载对象（PropertyGrid 用）</summary>
        public class VariableArrayEditor
        {
            [System.ComponentModel.DisplayName("元素数")]
            public int Count => Elements?.Count ?? 0;
            public ObservableCollection<ArrayItemWrapper> Elements { get; set; } = new();
        }

        /// <summary>网络变量写值：弹出单值输入 → 显式下发并按真实结果反馈（禁止无条件报成功）</summary>
        private void ExecuteWriteValue(VariableNode node)
        {
            if (node?.OriginalModel is not NetworkVariableModel netVar) return;

            var editor = new VariableValueEditor
            {
                VariableName = netVar.Name,
                Address = node.Address ?? "",
                StringValue = netVar.Value?.ToString() ?? ""
            };
            var ok = EasyDialog.ShowPropertyGridSync("写值到设备", editor);
            if (!ok) return;

            try
            {
                object converted = Convert.ChangeType(
                    editor.StringValue,
                    Nullable.GetUnderlyingType(netVar.DataType) ?? netVar.DataType);

                // TryWriteToValue 无条件下发（同值也写，"再写一次"是命令不是状态设置），
                // 失败原因（离线/未配置地址/驱动异常）如实呈现——旧实现吞掉一切后报"已写入"，是产线安全隐患
                if (netVar.TryWriteToValue(converted, out var error))
                    Notifier.ShowSuccess($"[{netVar.Name}] 已写入 {editor.StringValue}");
                else
                    EasyDialog.ShowSync($"写入失败：{error}", "错误");
            }
            catch (Exception ex)
            {
                EasyDialog.ShowSync($"写入失败：{ex.Message}", "错误");
            }
        }

        /// <summary>写值对话框承载对象（PropertyGrid 自动生成表单）</summary>
        public class VariableValueEditor
        {
            [System.ComponentModel.DisplayName("变量")]
            public string VariableName { get; set; } = "";
            [System.ComponentModel.DisplayName("设备地址")]
            public string Address { get; set; } = "";
            [System.ComponentModel.DisplayName("新值")]
            public string StringValue { get; set; } = "";
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

        #region 内部类与UI构建
        public class ArrayItemWrapper : BindableBase
        {
            private string _stringValue;
            public string StringValue
            {
                get => _stringValue;
                set => SetProperty(ref _stringValue, value);
            }
            public DelegateCommand RemoveCommand { get; set; }
        }
        #endregion

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // C4：合并刷新定时器随弹窗停机
                _treeRefreshTimer.Stop();

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
