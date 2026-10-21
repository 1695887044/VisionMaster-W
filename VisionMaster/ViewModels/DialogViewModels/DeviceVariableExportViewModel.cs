using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    /// 单设备变量表导出（xlsx）。
    ///
    /// 口径
    /// ----
    /// ① **只导出网络变量**：本地 / 系统变量不属于任何设备，混进来会让导入端按名字撞车；
    /// ② **范围二选一**：全部设备变量 / 当前显示分组（变量管理左栏当前选中项）；
    /// ③ **地址是结构化列，不是复合地址串**：地址预览列只给人看，导入端明确忽略它；
    /// ④ 说明页列出该连接的协议 / 存储区 / 数据类型 / 扫描组候选，导出的文件换台机器也能照着填。
    /// </summary>
    public class DeviceVariableExportViewModel : BindableBase, IDialogAware
    {
        public const string DialogName = "DeviceVariableExport";

        public const string ConnectionNameKey = "ConnectionName";

        /// <summary>当前显示分组 Id；<see cref="Guid.Empty"/> 表示「未分组」，配合 <see cref="HasCurrentGroupKey"/> 区分「全部」。</summary>
        public const string CurrentGroupIdKey = "CurrentGroupId";

        public const string HasCurrentGroupKey = "HasCurrentGroup";
        public const string CurrentGroupNameKey = "CurrentGroupName";

        public const string ExportedPathKey = "ExportedPath";
        public const string ExportedCountKey = "ExportedCount";

        private readonly IWorkspaceManager _workspace;
        private readonly AdvancedCommunicationManager _manager;

        private CommunicationConfig? _connection;
        private string _connectionName = string.Empty;
        private Guid _currentGroupId;
        private bool _hasCurrentGroup;
        private string _currentGroupName = "全部";
        private bool _isAllScope = true;
        private bool _includeGuideSheet = true;
        private bool _includeAddressPreview = true;
        private string _exportPath = string.Empty;
        private string _summary = string.Empty;
        private int _allCount;
        private int _currentGroupCount;

        public DeviceVariableExportViewModel(
            IWorkspaceManager workspace,
            AdvancedCommunicationManager manager)
        {
            _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));

            ExportCommand = new DelegateCommand(ExecuteExport, () => CanExport);
            BrowseCommand = new DelegateCommand(ExecuteBrowse);
            CancelCommand = new DelegateCommand(() => RequestClose.Invoke(new DialogResult(ButtonResult.Cancel)));
            CloseCommand = new DelegateCommand(() => RequestClose.Invoke(new DialogResult(ButtonResult.Cancel)));
        }

        #region IDialogAware

        public DialogCloseListener RequestClose { get; set; }

        public string Title => "导出变量表";

        public bool CanCloseDialog() => true;

        public void OnDialogClosed() { }

        public void OnDialogOpened(IDialogParameters parameters)
        {
            parameters.TryGetValue<string>(ConnectionNameKey, out var connectionName);
            parameters.TryGetValue<Guid>(CurrentGroupIdKey, out var groupId);
            parameters.TryGetValue<bool>(HasCurrentGroupKey, out var hasCurrentGroup);
            parameters.TryGetValue<string>(CurrentGroupNameKey, out var groupName);

            _connectionName = connectionName?.Trim() ?? string.Empty;
            _currentGroupId = groupId;
            _hasCurrentGroup = hasCurrentGroup;
            _currentGroupName = !hasCurrentGroup
                ? "未选择"
                : string.IsNullOrWhiteSpace(groupName) ? "全部" : groupName!;

            _connection = _manager.GetAllConnections()
                .FirstOrDefault(c => string.Equals(c.ConnectionName, _connectionName, StringComparison.Ordinal));
            if (_connection == null)
            {
                Notifier.ShowError($"连接 [{_connectionName}] 不存在，无法导出变量表");
                return;
            }

            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrWhiteSpace(documents)) documents = Environment.CurrentDirectory;
            ExportPath = Path.Combine(
                documents,
                DeviceVariableExportService.BuildDefaultFileName(_connectionName, DateTime.Now));

            RefreshCounts();
            RaisePropertyChanged(nameof(Subtitle));
            RaisePropertyChanged(nameof(HasCurrentGroup));
            RaisePropertyChanged(nameof(CurrentGroupName));
        }

        #endregion

        #region 绑定属性

        public string Subtitle => _connection == null
            ? "仅导出网络变量"
            : $"{_connectionName} · {GlobalVariableManagerViewModel.ProtocolDisplayName(_connection.Protocol)} · 仅网络变量";

        /// <summary>全部设备变量数</summary>
        public int AllCount
        {
            get => _allCount;
            private set => SetProperty(ref _allCount, value);
        }

        /// <summary>当前显示分组里的变量数（未选中分组时为 0）</summary>
        public int CurrentGroupCount
        {
            get => _currentGroupCount;
            private set => SetProperty(ref _currentGroupCount, value);
        }

        public bool HasCurrentGroup
        {
            get => _hasCurrentGroup;
            private set => SetProperty(ref _hasCurrentGroup, value);
        }

        public string CurrentGroupName
        {
            get => _currentGroupName;
            private set => SetProperty(ref _currentGroupName, value);
        }

        /// <summary>true = 全部变量；false = 当前分组</summary>
        public bool IsAllScope
        {
            get => _isAllScope;
            set
            {
                if (!SetProperty(ref _isAllScope, value)) return;
                RaisePropertyChanged(nameof(IsCurrentGroupScope));
                RaisePropertyChanged(nameof(SelectedCount));
                RaisePropertyChanged(nameof(CanExport));
                RefreshSummary();
                ExportCommand.RaiseCanExecuteChanged();
            }
        }

        /// <summary>与 <see cref="IsAllScope"/> 互斥（单选卡的第二项）。</summary>
        public bool IsCurrentGroupScope
        {
            get => !_isAllScope;
            set
            {
                if (value) IsAllScope = false;
            }
        }

        public bool IncludeGuideSheet
        {
            get => _includeGuideSheet;
            set => SetProperty(ref _includeGuideSheet, value);
        }

        public bool IncludeAddressPreview
        {
            get => _includeAddressPreview;
            set => SetProperty(ref _includeAddressPreview, value);
        }

        public string ExportPath
        {
            get => _exportPath;
            set
            {
                if (SetProperty(ref _exportPath, value))
                    ExportCommand.RaiseCanExecuteChanged();
            }
        }

        public string Summary
        {
            get => _summary;
            private set => SetProperty(ref _summary, value);
        }

        public int SelectedCount => IsAllScope ? AllCount : CurrentGroupCount;

        public bool CanExport => SelectedCount > 0 && !string.IsNullOrWhiteSpace(ExportPath);

        #endregion

        #region 命令

        public DelegateCommand ExportCommand { get; }
        public DelegateCommand BrowseCommand { get; }
        public DelegateCommand CancelCommand { get; }
        public DelegateCommand CloseCommand { get; }

        private void ExecuteBrowse()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Excel 工作簿 (*.xlsx)|*.xlsx",
                Title = "导出变量表",
                FileName = Path.GetFileName(ExportPath),
                InitialDirectory = SafeDirectory(ExportPath),
                DefaultExt = ".xlsx",
                AddExtension = true,
                OverwritePrompt = true,
            };
            if (dialog.ShowDialog() != true) return;
            ExportPath = dialog.FileName;
        }

        private void ExecuteExport()
        {
            if (_connection == null || !CanExport) return;

            // 路径由「浏览…」选定（默认落到我的文档）；这里直接写，不再弹一次保存框。
            if (string.IsNullOrWhiteSpace(ExportPath))
            {
                ExecuteBrowse();
                if (string.IsNullOrWhiteSpace(ExportPath)) return;
            }

            if (File.Exists(ExportPath)
                && !EasyDialog.ShowSync("覆盖文件", $"「{ExportPath}」已存在，是否覆盖？"))
            {
                return;
            }

            try
            {
                var variables = SelectedVariables().ToList();
                if (variables.Count == 0)
                {
                    Notifier.ShowWarning("当前范围没有可导出的变量");
                    return;
                }

                DeviceVariableExportService.Export(
                    ExportPath,
                    _connection,
                    variables,
                    GroupList(),
                    new DeviceVariableIoOptions
                    {
                        IncludeGuideSheet = IncludeGuideSheet,
                        IncludeAddressPreview = IncludeAddressPreview
                    });

                RequestClose.Invoke(new DialogResult(ButtonResult.OK)
                {
                    Parameters = new DialogParameters
                    {
                        { ExportedPathKey, ExportPath },
                        { ExportedCountKey, variables.Count }
                    }
                });
            }
            catch (Exception ex)
            {
                EasyDialog.ShowSync("导出失败", $"写入「{ExportPath}」失败：{ex.Message}");
            }
        }

        #endregion

        #region 数据

        private List<VariableGroupDescriptor> GroupList()
            => _workspace.CurrentSolution?.VariableGroups?.ToList() ?? new List<VariableGroupDescriptor>();

        private List<NetworkVariableModel> DeviceVariables()
            => _workspace.GlobalVariables.OfType<NetworkVariableModel>()
                .Where(v => string.Equals(v.ConnectionName, _connectionName, StringComparison.Ordinal))
                .ToList();

        private IEnumerable<NetworkVariableModel> SelectedVariables()
        {
            var groups = GroupList();
            var variables = DeviceVariables();
            if (IsAllScope) return variables;
            return variables.Where(v => NormalizeGroupId(v.GroupId, groups) == _currentGroupId);
        }

        private static Guid NormalizeGroupId(Guid id, IReadOnlyList<VariableGroupDescriptor> groups)
            => id == Guid.Empty || groups.Any(g => g.Id == id) ? id : Guid.Empty;

        private void RefreshCounts()
        {
            var groups = GroupList();
            var variables = DeviceVariables();
            AllCount = variables.Count;
            CurrentGroupCount = _hasCurrentGroup
                ? variables.Count(v => NormalizeGroupId(v.GroupId, groups) == _currentGroupId)
                : 0;

            RaisePropertyChanged(nameof(SelectedCount));
            RaisePropertyChanged(nameof(CanExport));
            ExportCommand.RaiseCanExecuteChanged();
            RefreshSummary();
        }

        private void RefreshSummary()
        {
            if (_connection == null) return;

            var groups = GroupList();
            if (IsAllScope)
            {
                var parts = DeviceVariables()
                    .GroupBy(v => GroupName(v, groups))
                    .Select(g => $"{g.Key} {g.Count()}");
                Summary = $"将导出 {AllCount} 个变量：{string.Join(" / ", parts)}";
            }
            else
            {
                Summary = $"将导出 {CurrentGroupCount} 个变量（{_currentGroupName}）";
            }
        }

        private static string GroupName(NetworkVariableModel variable, IReadOnlyList<VariableGroupDescriptor> groups)
            => groups.FirstOrDefault(g => g.Id == variable.GroupId)?.Name ?? VariableGroupEditorViewModel.UngroupedName;

        private static string SafeDirectory(string path)
        {
            try
            {
                var directory = Path.GetDirectoryName(path);
                return !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory)
                    ? directory!
                    : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            }
            catch
            {
                return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            }
        }

        #endregion
    }
}
