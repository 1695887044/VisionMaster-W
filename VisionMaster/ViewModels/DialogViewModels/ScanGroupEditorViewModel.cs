using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Threading;
using Prism.Commands;
using Prism.Dialogs;
using Prism.Mvvm;
using UI.CustomControl;
using VisionMaster.Communications;
using VisionMaster.Models;
using VisionMaster.Services;

namespace VisionMaster.ViewModels.DialogViewModels
{
    /// <summary>
    /// 扫描组编辑器（对标 KEPServerEX 的 Scan Class 管理页 / Ignition 的 Tag Group）。
    ///
    /// 它解决的是"一条连接只有一个节拍"这个硬约束：联锁点要 100ms、统计点 10s 就够，
    /// 调快则 PLC 被打爆、调慢则联锁失控。分组之后，请求配额优先给短周期组，
    /// 长周期组降频腾出配额（总请求数能降一个数量级）。
    ///
    /// 三条口径
    /// ---------
    /// ① **默认组是虚拟的**：它永远存在、不可删不可改名，周期就是连接的
    ///    <see cref="CommunicationConfig.ReadCycleMs"/>（唯一真相源，不落盘）。
    ///    所以这里对它只开放"周期"一列，改它 = 改 ReadCycleMs。
    /// ② **编辑的是副本，确定才回写**：与"编辑参数"弹窗同一手法。直接改活对象的话，
    ///    点取消也已经改脏了，而 Shell 关闭时会无条件落盘——脏数据就进了方案文件。
    /// ③ **优先级由周期决定，与列表顺序无关**：拖动只改显示顺序。列表顺序若也参与调度，
    ///    用户会以为"拖到上面就跑得快"，而真正决定快慢的是周期——两个真相源必然对不上。
    ///
    /// 改名 / 删组的级联要改**两处**（见 <see cref="CascadeRename"/>）：
    /// 通信管理器里的已注册变量（轮询归属）+ 工作区里的变量模型（落盘与界面显示）。
    /// 只改一处，要么本次不生效，要么下次 RebindAll 时被旧值灌回来。
    /// </summary>
    public class ScanGroupEditorViewModel : BindableBase, IDialogAware
    {
        /// <summary>弹窗注册名（与 App.xaml.cs 的 RegisterDialog 一致）</summary>
        public const string DialogName = "ScanGroupEditor";

        /// <summary>入参键：要编辑哪条连接的扫描组表（与 CommunicationSettingsViewModel 约定一致）</summary>
        public const string ConnectionNameKey = "ConnectionName";

        private readonly AdvancedCommunicationManager _manager;
        private readonly IWorkspaceManager _workspace;

        /// <summary>诊断刷新节拍（1s）。只在弹窗存活期间跑，关闭即停</summary>
        private readonly DispatcherTimer _statsTimer;

        /// <summary>连接当前活配置（只读它的 State 判在线；组表编辑一律走 <see cref="_working"/>）</summary>
        private CommunicationConfig? _live;

        /// <summary>编辑副本：用户所有改动只落在它身上，"确定"才回写活配置</summary>
        private CommunicationConfig? _working;

        /// <summary>打开时快照的自定义组名，用于在"确定"时算出哪些组被删了（删组要级联回落）</summary>
        private readonly List<string> _originalGroupNames = new();

        private string _connectionName = string.Empty;
        private ScanGroupRow? _selectedGroup;
        private string? _copyTarget;
        private string _statsHint = string.Empty;
        private bool _hasLiveStats;

        public ScanGroupEditorViewModel(AdvancedCommunicationManager manager, IWorkspaceManager workspace)
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
            _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));

            // canExecute 一律放在命令上（而不是界面上绑 IsEnabled）：按钮的置灰状态由"数据是否允许"
            // 决定，写在命令里就只有一处判断，也不会出现"界面置灰了、快捷键/代码仍能执行"的错位。
            // _working == null 表示连接不在册（打开弹窗时已被别处删掉）。此时两个"往组表里加组"的
            // 命令都必须置灰：方法体里虽有同样的判空，但按钮仍可点、点了静默无反应，用户只会以为卡了。
            AddGroupCommand = new DelegateCommand(ExecuteAddGroup, () => _working != null && !IsAtGroupLimit);
            ApplyPresetCommand = new DelegateCommand<string>(ExecuteApplyPreset, _ => _working != null && !IsAtGroupLimit);
            DeleteGroupCommand = new DelegateCommand(ExecuteDeleteGroup, () => CanDeleteSelected);
            CopyToConnectionCommand = new DelegateCommand(ExecuteCopyToConnection, () => !string.IsNullOrEmpty(CopyTarget));
            ConfirmCommand = new DelegateCommand(ExecuteConfirm);
            CancelCommand = new DelegateCommand(() => RequestClose.Invoke(ButtonResult.Cancel));

            _statsTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            _statsTimer.Tick += OnStatsTick;
        }

        #region IDialogAware

        public DialogCloseListener RequestClose { get; set; }

        public string Title => string.IsNullOrEmpty(_connectionName) ? "扫描组" : $"扫描组 - {_connectionName}";

        public bool CanCloseDialog() => true;

        public void OnDialogClosed()
        {
            _statsTimer.Stop(); // 弹窗每次都是新实例，但定时器不停会一直持有 VM（且每秒空跑一次采集）
        }

        public void OnDialogOpened(IDialogParameters parameters)
        {
            parameters.TryGetValue<string>(ConnectionNameKey, out var name);
            _connectionName = name?.Trim() ?? string.Empty;
            RaisePropertyChanged(nameof(Title));

            _live = _manager.GetAllConnections()
                .FirstOrDefault(c => string.Equals(c.ConnectionName, _connectionName, StringComparison.Ordinal));

            if (_live == null)
            {
                // 连接被别处删掉了：如实说明并置空，界面退化为只读空表，不静默装成"这条连接没有组"
                Notifier.ShowError($"连接 [{_connectionName}] 不存在，无法编辑扫描组");
                _working = null;
                StatsHint = "连接不存在";
                return;
            }

            _working = _live.Clone();
            _working.ConnectionName = _live.ConnectionName; // Clone 刻意加了 "_Copy" 后缀，弹窗显示要原名

            _originalGroupNames.Clear();
            _originalGroupNames.AddRange(_working.ScanGroups.Select(g => g.Name));

            LoadCopyTargets();
            RebuildRows();
            RefreshStats();
            _statsTimer.Start();
        }

        #endregion

        #region 绑定属性

        /// <summary>编辑区行（首行恒为虚拟默认组）</summary>
        public ObservableCollection<ScanGroupRow> Groups { get; } = new();

        /// <summary>诊断区行（1s 刷新；离线时为空）</summary>
        public ObservableCollection<ScanGroupStatRow> Stats { get; } = new();

        /// <summary>可复制到的其他连接（不含自己）</summary>
        public ObservableCollection<string> CopyTargets { get; } = new();

        /// <summary>当前选中行（"删除"按钮作用对象）</summary>
        public ScanGroupRow? SelectedGroup
        {
            get => _selectedGroup;
            set
            {
                if (!SetProperty(ref _selectedGroup, value)) return;
                RaisePropertyChanged(nameof(CanDeleteSelected));
                DeleteGroupCommand.RaiseCanExecuteChanged();
            }
        }

        /// <summary>默认组不可删——删掉它，未分组的变量就没有落点了</summary>
        public bool CanDeleteSelected => SelectedGroup is { IsDefault: false };

        /// <summary>复制目标连接</summary>
        public string? CopyTarget
        {
            get => _copyTarget;
            set
            {
                if (!SetProperty(ref _copyTarget, value)) return;
                CopyToConnectionCommand.RaiseCanExecuteChanged();
            }
        }

        /// <summary>自定义组数（不含默认组）</summary>
        public int CustomGroupCount => Groups.Count(r => !r.IsDefault);

        /// <summary>是否已达组数上限（达上限则"新增组""一键预设"置灰）</summary>
        public bool IsAtGroupLimit => CustomGroupCount >= CommunicationConfig.MaxScanGroups;

        /// <summary>组数提示："共 3 组 / 上限 8"；达上限时补一句代价说明</summary>
        public string GroupCountHint => IsAtGroupLimit
            ? $"共 {CustomGroupCount} 组 / 上限 {CommunicationConfig.MaxScanGroups}（组越多，组内地址合并率越低）"
            : $"共 {CustomGroupCount} 组 / 上限 {CommunicationConfig.MaxScanGroups}";

        /// <summary>诊断区提示文案（离线 / 无变量时说明原因，避免空表看起来像故障）</summary>
        public string StatsHint
        {
            get => _statsHint;
            private set => SetProperty(ref _statsHint, value);
        }

        /// <summary>诊断区是否有实时数据（false 时显示 <see cref="StatsHint"/>）</summary>
        public bool HasLiveStats
        {
            get => _hasLiveStats;
            private set => SetProperty(ref _hasLiveStats, value);
        }

        #endregion

        #region 命令

        public DelegateCommand AddGroupCommand { get; }

        public DelegateCommand<string> ApplyPresetCommand { get; }

        public DelegateCommand DeleteGroupCommand { get; }

        public DelegateCommand CopyToConnectionCommand { get; }

        public DelegateCommand ConfirmCommand { get; }

        public DelegateCommand CancelCommand { get; }

        /// <summary>新增一个自定义组（默认名"组N"、默认周期 1000ms），并选中它以便立刻改名</summary>
        private void ExecuteAddGroup()
        {
            if (_working == null || IsAtGroupLimit) return;

            var row = new ScanGroupRow(this, NextGroupName(), 1000, isDefault: false);
            Groups.Add(row);
            SelectedGroup = row;

            AfterStructureChanged();
        }

        /// <summary>一键追加一个预设档位的组（同名组已存在则跳过——预设是"补齐档位"，不是"再来一个"）</summary>
        private void ExecuteApplyPreset(string? intervalText)
        {
            if (_working == null || IsAtGroupLimit) return;
            if (!int.TryParse(intervalText, out var intervalMs)) return;

            var name = $"{intervalMs}ms";
            if (Groups.Any(r => string.Equals(r.Name?.Trim(), name, StringComparison.Ordinal)))
            {
                Notifier.ShowInfo($"已存在「{name}」组，本次未重复添加");
                return;
            }

            var row = new ScanGroupRow(this, name, intervalMs, isDefault: false);
            Groups.Add(row);
            SelectedGroup = row;

            AfterStructureChanged();
        }

        /// <summary>
        /// 删除选中组：该组变量回落到默认组（**不删变量**）。必须先说清影响面再动手——
        /// "删了一个组，几千个点悄悄换了节拍"是操作员最难自查的一类变更。
        /// </summary>
        private void ExecuteDeleteGroup()
        {
            var row = SelectedGroup;
            if (_working == null || row is null || row.IsDefault) return;

            int affected = CountVariablesInGroup(row.OriginalName);
            var message = affected > 0
                ? $"删除扫描组「{row.Name}」？\n\n该组当前有 {affected} 个变量，它们会回落到默认组（周期 {DefaultIntervalMs} ms），变量本身不会被删除。"
                : $"删除扫描组「{row.Name}」？\n\n该组当前没有变量。";

            if (!EasyDialog.ShowSync("删除扫描组", message)) return;

            Groups.Remove(row);
            SelectedGroup = null;

            AfterStructureChanged();
        }

        /// <summary>
        /// 把本连接的**自定义组表**复制到另一条连接（不含默认组周期）。
        /// 只拷组表、不动对方的 ReadCycleMs：默认组周期是那条连接自己的节奏，
        /// 跟着一起改会连带改掉它所有未分组变量的周期——那不是"复制组表"，那是"改配置"。
        /// </summary>
        private void ExecuteCopyToConnection()
        {
            var target = CopyTarget;
            if (_working == null || string.IsNullOrEmpty(target)) return;

            var targetConfig = _manager.GetAllConnections()
                .FirstOrDefault(c => string.Equals(c.ConnectionName, target, StringComparison.Ordinal));
            if (targetConfig == null)
            {
                Notifier.ShowError($"连接 [{target}] 不存在，复制已取消");
                return;
            }

            int count = _working.ScanGroups.Count;
            if (count == 0)
            {
                Notifier.ShowInfo("当前连接没有自定义组可复制（默认组不参与复制）");
                return;
            }

            if (!EasyDialog.ShowSync("复制扫描组",
                    $"将把 {count} 个自定义组覆盖到 [{target}]。\n\n" +
                    $"· 对方原有的自定义组会被替换\n" +
                    $"· 对方组名对不上的变量会回落到默认组（变量不会丢）\n" +
                    $"· 对方的默认组周期保持不变\n\n是否继续？"))
            {
                return;
            }

            targetConfig.ScanGroups = _working.ScanGroups.Select(g => g.Clone()).ToList();

            // UpdateConnection 是 Remove + Add，Add 之后状态恒为 Disconnected——
            // 原本在线的连接必须显式请求重连，否则"复制个组表"就把对方轮询停了
            RebuildConnection(targetConfig);

            Notifier.ShowSuccess($"已将 {count} 个扫描组复制到 [{target}]");
        }

        /// <summary>
        /// 确定：级联改名/回落 → 组表回写活配置 → 重建连接 → 关闭。
        /// 顺序不可颠倒：级联必须在 <see cref="AdvancedCommunicationManager.UpdateConnection"/> 之前，
        /// 因为 UpdateConnection 内部复用的是同一批已注册变量对象（见 Manager.ReassignScanGroup 注释）。
        /// </summary>
        private void ExecuteConfirm()
        {
            if (_working == null || _live == null)
            {
                // 连接在弹窗打开期间被别处删掉了：没有活对象可写，这个弹窗已经失去意义。
                // 如实提示并主动关闭，而不是静默 return——否则用户点「确定」毫无反应，
                // 只会以为程序卡死，还得自己找「取消」才出得去。
                RequestClose.Invoke(new DialogParameters(), ButtonResult.Cancel);
                Notifier.ShowWarning($"连接 [{_connectionName}] 已不存在，本次未做任何修改");
                return;
            }

            SyncWorkingFromRows();

            if (!Validate(out var error))
            {
                Notifier.ShowWarning(error);
                return;
            }

            var renames = new List<(string Old, string New)>();
            foreach (var row in Groups.Where(r => !r.IsDefault))
            {
                var newName = row.Name.Trim();
                if (!string.Equals(row.OriginalName, newName, StringComparison.Ordinal))
                    renames.Add((row.OriginalName, newName));
            }

            // 删组判定：打开时存在、现在不在列表里、且不是"改了名"的那几个
            var renamedFrom = new HashSet<string>(renames.Select(r => r.Old), StringComparer.Ordinal);
            var finalNames = new HashSet<string>(Groups.Where(r => !r.IsDefault).Select(r => r.Name.Trim()), StringComparer.Ordinal);
            var deleted = _originalGroupNames
                .Where(old => !renamedFrom.Contains(old) && !finalNames.Contains(old))
                .ToList();

            foreach (var (oldName, newName) in renames)
                CascadeRename(oldName, newName);
            foreach (var oldName in deleted)
                CascadeRename(oldName, null);

            _live.CopyFrom(_working);
            RebuildConnection(_live);

            _statsTimer.Stop();
            RequestClose.Invoke(new DialogParameters(), ButtonResult.OK);

            int moved = renames.Count + deleted.Count;
            Notifier.ShowSuccess(moved == 0
                ? $"扫描组已更新（{CustomGroupCount} 个自定义组）"
                : $"扫描组已更新（{CustomGroupCount} 个自定义组，{moved} 个组发生改名/删除，相关变量已级联）");
        }

        #endregion

        #region 编辑区同步

        /// <summary>行内改名后：同步副本组表 + 刷新预览（变量数与段数会随组名变化而重算）</summary>
        internal void OnGroupNameEdited(ScanGroupRow row)
        {
            SyncWorkingFromRows();
            RefreshPreview();
        }

        /// <summary>行内周期改动后：同步副本组表（周期变了，优先级顺序也变，预览要重算）</summary>
        internal void OnIntervalEdited(ScanGroupRow row)
        {
            SyncWorkingFromRows();
            RefreshPreview();
        }

        /// <summary>
        /// 把编辑区的行回灌进副本组表。
        /// 编辑区是唯一的手工输入面，副本组表是**派生**的——预览与"确定"都只认副本，
        /// 所以每次编辑后都要同步一次，避免"界面上改了、真正生效的还是旧值"。
        /// </summary>
        private void SyncWorkingFromRows()
        {
            if (_working == null) return;

            var defaultRow = Groups.FirstOrDefault(r => r.IsDefault);
            if (defaultRow != null)
                _working.ReadCycleMs = defaultRow.IntervalMs; // 默认组周期的唯一真相源就是 ReadCycleMs

            _working.ScanGroups = Groups
                .Where(r => !r.IsDefault)
                .Select(r => new ScanGroupConfig(r.Name?.Trim() ?? string.Empty, r.IntervalMs))
                .ToList();
        }

        /// <summary>结构变化（增/删组/排序）后：刷新计数、命令可用性、预览</summary>
        private void AfterStructureChanged()
        {
            RaisePropertyChanged(nameof(CustomGroupCount));
            RaisePropertyChanged(nameof(IsAtGroupLimit));
            RaisePropertyChanged(nameof(GroupCountHint));

            // 组数变化会改变"是否已达上限"，命令的置灰状态必须跟着重算（canExecute 不会自动重评估）
            AddGroupCommand.RaiseCanExecuteChanged();
            ApplyPresetCommand.RaiseCanExecuteChanged();
            DeleteGroupCommand.RaiseCanExecuteChanged();

            RefreshPreview();
        }

        /// <summary>
        /// 拖动排序：把 <paramref name="source"/> 移到 <paramref name="target"/> 的位置。
        /// <para>**只改列表顺序**（顺带决定组表在方案文件里的落盘顺序），不碰任何周期——
        /// 调度优先级恒由周期升序决定，与列表顺序无关。若这里顺手也改了周期，
        /// 用户就会以为"拖到上面 = 跑得快"，而真正的快慢由周期说了算，两个真相源必然对不上。</para>
        /// </summary>
        internal void MoveGroup(ScanGroupRow source, ScanGroupRow target)
        {
            if (source is null || target is null || ReferenceEquals(source, target)) return;
            if (source.IsDefault || target.IsDefault) return; // 默认组恒在首行，不参与拖动

            int from = Groups.IndexOf(source);
            int to = Groups.IndexOf(target);
            if (from < 0 || to < 0) return;

            Groups.Move(from, to);
            SyncWorkingFromRows();
        }

        /// <summary>重建编辑区行（首行固定为虚拟默认组）</summary>
        private void RebuildRows()
        {
            Groups.Clear();
            if (_working == null) return;

            Groups.Add(new ScanGroupRow(this, PollScheduler.DefaultGroupName, _working.ReadCycleMs, isDefault: true));
            foreach (var group in _working.ScanGroups)
                Groups.Add(new ScanGroupRow(this, group.Name, group.IntervalMs, isDefault: false));

            SelectedGroup = null;
            AfterStructureChanged();
        }

        /// <summary>生成不重名的默认组名（组1、组2…）</summary>
        private string NextGroupName()
        {
            for (int i = 1; i < 1000; i++)
            {
                var candidate = $"组{i}";
                if (!Groups.Any(r => string.Equals(r.Name?.Trim(), candidate, StringComparison.Ordinal)))
                    return candidate;
            }
            return $"组{Guid.NewGuid():N}".Substring(0, 8);
        }

        private int DefaultIntervalMs => Groups.FirstOrDefault(r => r.IsDefault)?.IntervalMs ?? 1000;

        #endregion

        #region 校验与级联

        /// <summary>
        /// 校验组表合法性。规则与编译期过滤（<c>ScanGroupTable.Resolve</c>）**同源**：
        /// 周期区间取 <see cref="PollScheduler.MinIntervalMs"/>/<see cref="PollScheduler.MaxIntervalMs"/>，
        /// 保留名取 <see cref="PollScheduler.DefaultGroupName"/>。
        /// 这里拦下来的东西，编译期会**静默丢弃**——所以必须在点确定时就说清楚，不能让用户以为改成功了。
        /// </summary>
        private bool Validate(out string error)
        {
            error = string.Empty;
            var seen = new HashSet<string>(StringComparer.Ordinal) { PollScheduler.DefaultGroupName };

            foreach (var row in Groups.Where(r => !r.IsDefault))
            {
                var name = row.Name?.Trim() ?? string.Empty;

                if (name.Length == 0)
                {
                    error = "组名不能为空（不需要的组请用「删除」移除）";
                    return false;
                }
                if (string.Equals(name, PollScheduler.DefaultGroupName, StringComparison.Ordinal))
                {
                    error = $"「{PollScheduler.DefaultGroupName}」是保留名，不能用作自定义组名";
                    return false;
                }
                if (!seen.Add(name))
                {
                    error = $"组名「{name}」重复，请改成唯一的名称";
                    return false;
                }
                if (row.IntervalMs < PollScheduler.MinIntervalMs || row.IntervalMs > PollScheduler.MaxIntervalMs)
                {
                    error = $"「{name}」的周期 {row.IntervalMs} ms 超出合法区间 " +
                            $"[{PollScheduler.MinIntervalMs}, {PollScheduler.MaxIntervalMs}]";
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 组改名/删组的级联。两处都要改，缺一不可：
        /// <para>① 通信管理器里的已注册变量（<see cref="CommunicationVariable.ScanGroup"/>）——
        ///     它是"变量归哪个组"在**轮询侧**的唯一依据；</para>
        /// <para>② 工作区里的变量模型（<c>NetworkVariableModel.ScanGroup</c>）——
        ///     它负责**落盘**（方案文件里存的就是它）与界面显示；</para>
        /// 只改①：下次 RebindAll（方案重载）会把旧组名从模型灌回来，级联白做。
        /// 只改②：本次运行不生效，要等下次注册才认。
        /// </summary>
        /// <param name="oldName">原组名</param>
        /// <param name="newName">新组名；null/空 = 回落到默认组（删组场景）</param>
        private void CascadeRename(string oldName, string? newName)
        {
            if (string.IsNullOrWhiteSpace(oldName)) return;

            _manager.ReassignScanGroup(_connectionName, oldName, newName);

            var target = string.IsNullOrWhiteSpace(newName) ? string.Empty : newName.Trim();
            foreach (var variable in _workspace.GlobalVariables.OfType<NetworkVariableModel>())
            {
                if (!string.Equals(variable.ConnectionName, _connectionName, StringComparison.Ordinal)) continue;
                if (!string.Equals(variable.ScanGroup?.Trim(), oldName, StringComparison.Ordinal)) continue;

                variable.ScanGroup = target;
            }
        }

        /// <summary>数一下某组名当前挂着多少个变量（删组确认框要报出影响面）</summary>
        private int CountVariablesInGroup(string groupName)
            => _workspace.GlobalVariables.OfType<NetworkVariableModel>()
                .Count(v => string.Equals(v.ConnectionName, _connectionName, StringComparison.Ordinal)
                            && string.Equals(v.ScanGroup?.Trim(), groupName, StringComparison.Ordinal));

        #endregion

        #region 回写与诊断

        /// <summary>
        /// 重建连接对象（组表变更必须走 UpdateConnection 才生效）。
        /// UpdateConnection 内部是 Remove + Add，而 Add 之后状态恒为 Disconnected——
        /// 原本在线的连接必须显式请求重连，否则"改个扫描组周期"就把这条连接的轮询静默停掉了。
        /// </summary>
        private void RebuildConnection(CommunicationConfig config)
        {
            bool wasConnected = config.State == ConnectionState.Connected;

            _manager.UpdateConnection(config);

            if (wasConnected)
                _manager.RequestConnect(config.ConnectionName); // 非阻塞：结果经 ConnectionStateChanged 回报
        }

        /// <summary>
        /// 刷新编辑区的"变量数 / 段数"（静态画像，不依赖在线）。
        /// 走 <see cref="AdvancedCommunicationManager.GetScanGroupPreview"/> 并传入**编辑副本**，
        /// 这样"刚加的组"立刻能看到它落了多少变量，而不必先点确定。
        /// </summary>
        private void RefreshPreview()
        {
            if (_working == null) return;

            var preview = _manager.GetScanGroupPreview(_connectionName, _working);
            var map = new Dictionary<string, ScanGroupStats>(StringComparer.Ordinal);
            foreach (var stat in preview)
                map[stat.GroupName] = stat;

            foreach (var row in Groups)
            {
                if (map.TryGetValue(row.Name?.Trim() ?? string.Empty, out var stat))
                {
                    row.VariableCount = stat.VariableCount;
                    row.SegmentCount = stat.SegmentCount;
                }
                else
                {
                    // 没进预览 = 该组没有变量（或组表非法被过滤），如实显示 0 而不是留着上一次的数字
                    row.VariableCount = 0;
                    row.SegmentCount = 0;
                }
            }
        }

        /// <summary>
        /// 诊断节拍（1s）。之所以要单独包一层 try/catch，而不是直接把 <see cref="RefreshStats"/> 挂给 Tick：
        /// <c>DispatcherTimer.Tick</c> 的异常没有兜底，会一路冒到 Dispatcher 上**把整个软件打崩**——
        /// 而这里只是"每秒瞄一眼诊断数据"，不值得为一次读取失败赔上整个界面。
        /// <para>处置：停表 + 把原因写进 <see cref="StatsHint"/>（诊断区空态文案，用户看得见），
        /// 且**不弹气泡**——若不停表、每秒失败一次还弹一次气泡，就是标准的日志风暴。
        /// 停表后诊断区停留在失败原因上，关掉重开即可重试。</para>
        /// </summary>
        private void OnStatsTick(object? sender, EventArgs e)
        {
            try
            {
                RefreshStats();
            }
            catch (Exception ex)
            {
                _statsTimer.Stop();
                HasLiveStats = false;
                StatsHint = $"诊断数据读取失败，已停止自动刷新：{ex.Message}";
            }
        }

        /// <summary>
        /// 刷新诊断区（1s 一次）。数据是**值语义快照**（<see cref="ScanGroupStats"/> 是 record struct），
        /// UI 拿到后与 Worker 线程再无共享，不需要锁。
        /// <para>重建调度器时 Manager 是整体替换实例，所以 UI 最多读到"上一个实例的快照"一个刷新周期，
        /// 不崩不错。</para>
        /// </summary>
        private void RefreshStats()
        {
            if (_live == null)
            {
                HasLiveStats = false;
                StatsHint = "连接不存在";
                return;
            }

            if (_live.State != ConnectionState.Connected)
            {
                HasLiveStats = false;
                StatsHint = "连接未建立，实测周期与达成率要等连接后才测得到（变量数/段数见上方编辑区）";
                Stats.Clear();
                return;
            }

            var snapshot = _manager.GetScanGroupStats(_connectionName);
            if (snapshot.Count == 0)
            {
                HasLiveStats = false;
                StatsHint = "该连接还没有已注册的变量，暂无可诊断的扫描组";
                Stats.Clear();
                return;
            }

            // 原位更新而不是 Clear+Add：1s 一次的 Clear 会让选中态/滚动位置每秒被重置
            if (Stats.Count != snapshot.Count)
            {
                Stats.Clear();
                foreach (var stat in snapshot)
                    Stats.Add(new ScanGroupStatRow(stat));
            }
            else
            {
                for (int i = 0; i < snapshot.Count; i++)
                    Stats[i].Update(snapshot[i]);
            }

            HasLiveStats = true;
            StatsHint = string.Empty;
        }

        private void LoadCopyTargets()
        {
            CopyTargets.Clear();
            foreach (var config in _manager.GetAllConnections())
            {
                if (config == null) continue;
                if (string.Equals(config.ConnectionName, _connectionName, StringComparison.Ordinal)) continue;
                CopyTargets.Add(config.ConnectionName);
            }

            CopyTarget = CopyTargets.FirstOrDefault();
        }

        #endregion
    }

    /// <summary>
    /// 编辑区的一行。默认组与自定义组共用它，靠 <see cref="IsDefault"/> 区分可编辑性：
    /// 默认组只开放周期列（改它 = 改连接的 <c>ReadCycleMs</c>），组名列只读。
    /// </summary>
    public sealed class ScanGroupRow : BindableBase
    {
        private readonly ScanGroupEditorViewModel _owner;

        private string _name;
        private int _intervalMs;
        private int _variableCount;
        private int _segmentCount;

        internal ScanGroupRow(ScanGroupEditorViewModel owner, string name, int intervalMs, bool isDefault)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _name = name ?? string.Empty;
            _intervalMs = intervalMs;
            IsDefault = isDefault;
            OriginalName = name ?? string.Empty;
        }

        /// <summary>是否虚拟默认组（不可删、不可改名）</summary>
        public bool IsDefault { get; }

        /// <summary>打开弹窗时的组名。用于在"确定"时判定"这行是改了名还是原样"</summary>
        internal string OriginalName { get; }

        /// <summary>组名（默认组只读）</summary>
        public string Name
        {
            get => _name;
            set
            {
                if (!SetProperty(ref _name, value)) return;
                if (IsDefault) return;
                _owner.OnGroupNameEdited(this);
            }
        }

        /// <summary>扫描周期（ms）。默认组的这一列直接对应连接的 ReadCycleMs</summary>
        public int IntervalMs
        {
            get => _intervalMs;
            set
            {
                if (!SetProperty(ref _intervalMs, value)) return;
                _owner.OnIntervalEdited(this);
            }
        }

        /// <summary>该组当前挂着的变量数（静态画像，来自编辑副本编译出的调度器）</summary>
        public int VariableCount
        {
            get => _variableCount;
            internal set => SetProperty(ref _variableCount, value);
        }

        /// <summary>该组每轮的段读次数（= 设备往返次数）。同变量数下段数越少，合并率越高</summary>
        public int SegmentCount
        {
            get => _segmentCount;
            internal set => SetProperty(ref _segmentCount, value);
        }

        /// <summary>默认组的组名固定显示"默认组"（虚拟组不落盘，没有别的名字）</summary>
        public string DisplayName => IsDefault ? PollScheduler.DefaultGroupName : _name;
    }

    /// <summary>
    /// 诊断区的一行。做成可变对象（<see cref="Update"/> 原位刷新）而不是每秒重建：
    /// 重建会让 DataGrid 的选中行与滚动位置每秒被重置一次，1s 的刷新频率下根本没法看。
    /// </summary>
    public sealed class ScanGroupStatRow : BindableBase
    {
        /// <summary>达成率进度条的像素总长（与 XAML 里进度条容器的宽度保持一致）</summary>
        private const double BarTrackWidth = 90;

        private ScanGroupStats _stat;

        public ScanGroupStatRow(ScanGroupStats stat) => _stat = stat;

        internal void Update(ScanGroupStats stat)
        {
            _stat = stat;
            RaisePropertyChanged(nameof(TargetText));
            RaisePropertyChanged(nameof(ActualText));
            RaisePropertyChanged(nameof(AchieveText));
            RaisePropertyChanged(nameof(BarWidth));
            RaisePropertyChanged(nameof(StatusText));
            RaisePropertyChanged(nameof(StatusColor));
            RaisePropertyChanged(nameof(IsMeasured));
        }

        public string GroupName => _stat.GroupName;

        public string TargetText => $"{_stat.TargetIntervalMs} ms";

        /// <summary>实测周期；还没测到（重连后首拍之前）显示"—"，不要显示 0 让人误以为"快得测不出来"</summary>
        public string ActualText => IsMeasured ? $"{_stat.AvgActualMs} ms" : "—";

        public bool IsMeasured => _stat.AvgActualMs > 0;

        /// <summary>达成率 = 目标周期 / 实测周期，封顶 100%</summary>
        public double AchieveRate => _stat.AchieveRate;

        public string AchieveText => IsMeasured ? $"{_stat.AchieveRate * 100:0}%" : "—";

        public double BarWidth => BarTrackWidth * (IsMeasured ? _stat.AchieveRate : 0);

        /// <summary>
        /// 分档：≥90% 正常 / 70~90% 偏慢 / &lt;70% 未达标。
        /// 未达标说明本组被"同拍内排在它前面的短周期组"拖慢了——同一 socket 串行，这是物理上限，
        /// 不是 bug。处置只有两条：给该组降频，或把两组拆到不同连接上。
        /// </summary>
        public string StatusText => !IsMeasured
            ? "待测"
            : _stat.AchieveRate >= 0.9 ? "正常"
            : _stat.AchieveRate >= 0.7 ? "偏慢"
            : "未达标";

        public string StatusColor => !IsMeasured
            ? "#909399"
            : _stat.AchieveRate >= 0.9 ? "#12B76A"
            : _stat.AchieveRate >= 0.7 ? "#F79009"
            : "#E5484D";
    }
}
