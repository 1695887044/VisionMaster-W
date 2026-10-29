using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    /// 单设备变量表导入（xlsx）：选择文件 → 差异预览 → 事务应用 → 结果。
    ///
    /// 安全口径
    /// ----
    /// ① **默认全有或全无**：存在错误行时「应用」置灰；勾选「跳过错误行」才允许只应用合法行；
    /// ② **默认不删**：文件中没有的变量默认保留；删除必须显式勾选，且会列出将删的变量名；
    /// ③ **流程运行中禁止导入**：运行中的步骤按对象引用取值，导入会改地址 / 类型或删除变量；
    /// ④ **更新走事务 + 重注册**：地址 / 扫描组 / 区域合并读 / 类型变化后调用
    ///    <see cref="NetworkVariableBridge.ReRegister"/>，否则轮询仍按旧快照读；任一步失败全量回滚。
    /// </summary>
    public class DeviceVariableImportViewModel : BindableBase, IDialogAware
    {
        public const string DialogName = "DeviceVariableImport";

        public const string FilePathKey = "FilePath";
        public const string ConnectionNameKey = "ConnectionName";

        public const string AddedKey = "Added";
        public const string UpdatedKey = "Updated";
        public const string DeletedKey = "Deleted";
        public const string SkippedKey = "Skipped";

        private readonly IWorkspaceManager _workspace;
        private readonly AdvancedCommunicationManager _manager;
        private readonly NetworkVariableBridge _bridge;

        private CommunicationConfig? _connection;
        private string _connectionName = string.Empty;
        private string _filePath = string.Empty;
        private string _fileName = string.Empty;
        private List<DeviceVariableRawRow> _rows = new();
        private DeviceVariableImportPlan? _plan;
        private DeviceVariableImportResult? _appliedResult;

        /// <summary>是否真的调用过 Apply（含失败）：失败后关闭也要让变量管理刷新，
        /// 因为回滚未完全成功时模型可能已有部分变化。</summary>
        private bool _attemptedApply;
        private int _rowCount;
        private bool _skipErrors;
        private bool _deleteMissing;
        private bool _autoCreateDisplayGroups = true;
        private bool _isResultStep;
        private string _resultSummary = string.Empty;
        private string _globalError = string.Empty;

        public DeviceVariableImportViewModel(
            IWorkspaceManager workspace,
            AdvancedCommunicationManager manager,
            NetworkVariableBridge bridge)
        {
            _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
            _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));

            ApplyCommand = new DelegateCommand(ExecuteApply, () => CanApply);
            CancelCommand = new DelegateCommand(() => CloseWithResult(
                _attemptedApply ? ButtonResult.OK : ButtonResult.Cancel));
            DoneCommand = new DelegateCommand(() => CloseWithResult(ButtonResult.OK));
            CloseCommand = new DelegateCommand(() => CloseWithResult(
                _appliedResult != null || _attemptedApply ? ButtonResult.OK : ButtonResult.Cancel));
            ReselectFileCommand = new DelegateCommand(ExecuteReselectFile, () => _connection != null);
            DownloadTemplateCommand = new DelegateCommand(ExecuteDownloadTemplate, () => _connection != null);
        }

        #region IDialogAware

        public DialogCloseListener RequestClose { get; set; }

        public string Title => "导入变量表";

        public bool CanCloseDialog() => true;

        public void OnDialogClosed() { }

        public void OnDialogOpened(IDialogParameters parameters)
        {
            parameters.TryGetValue<string>(FilePathKey, out var filePath);
            parameters.TryGetValue<string>(ConnectionNameKey, out var connectionName);

            _connectionName = connectionName?.Trim() ?? string.Empty;
            FilePath = filePath ?? string.Empty;
            FileName = Path.GetFileName(FilePath);
            RaisePropertyChanged(nameof(TargetText));

            _connection = _manager.GetAllConnections()
                .FirstOrDefault(c => string.Equals(c.ConnectionName, _connectionName, StringComparison.Ordinal));
            if (_connection == null)
            {
                SetGlobalError($"连接 [{_connectionName}] 不存在，无法导入变量表。");
                return;
            }

            DownloadTemplateCommand.RaiseCanExecuteChanged();
            ReselectFileCommand.RaiseCanExecuteChanged();
            LoadFile();
        }

        #endregion

        #region 绑定属性

        public string TargetText => _connection == null
            ? $"目标设备：{_connectionName}"
            : $"目标设备：{_connectionName}（{GlobalVariableManagerViewModel.ProtocolDisplayName(_connection.Protocol)}）· 仅网络变量";

        public string FilePath
        {
            get => _filePath;
            private set => SetProperty(ref _filePath, value);
        }

        public string FileName
        {
            get => _fileName;
            private set => SetProperty(ref _fileName, value);
        }

        public int RowCount
        {
            get => _rowCount;
            private set
            {
                if (!SetProperty(ref _rowCount, value)) return;
                RaisePropertyChanged(nameof(FileSummary));
                RaisePropertyChanged(nameof(SummaryText));
            }
        }

        public string FileSummary => $"{RowCount} 行";

        /// <summary>预览行（按文件顺序；错误行也在里面，说明列给出原因）。</summary>
        public ObservableCollection<DeviceVariableImportItem> Items { get; } = new();

        public int NewCount => _plan?.NewCount ?? 0;
        public int UpdateCount => _plan?.UpdateCount ?? 0;
        public int UnchangedCount => _plan?.UnchangedCount ?? 0;
        public int ErrorCount => _plan?.ErrorCount ?? 0;
        public int MissingCount => _plan?.MissingCount ?? 0;
        public bool HasErrors => _plan?.HasErrors == true;

        public string SummaryText => _plan == null
            ? (HasGlobalError ? "文件未就绪" : string.Empty)
            : $"文件 {RowCount} 行 · 新增 {NewCount} · 更新 {UpdateCount} · 无变化 {UnchangedCount} · 错误 {ErrorCount}";

        /// <summary>存在错误行时允许勾选「跳过错误行」；没有错误时该选项无意义。</summary>
        public bool CanSkipErrors => HasErrors;

        public bool SkipErrors
        {
            get => _skipErrors;
            set
            {
                if (!SetProperty(ref _skipErrors, value)) return;
                ApplySkipState();
                UpdateOptionsAndApply();
            }
        }

        public bool DeleteMissing
        {
            get => _deleteMissing;
            set
            {
                if (!SetProperty(ref _deleteMissing, value)) return;
                UpdateOptionsAndApply();
            }
        }

        public bool AutoCreateDisplayGroups
        {
            get => _autoCreateDisplayGroups;
            set
            {
                if (!SetProperty(ref _autoCreateDisplayGroups, value)) return;
                RebuildPlan();
            }
        }

        public string DeleteWarning
        {
            get
            {
                if (!DeleteMissing || _plan == null || _plan.MissingCount == 0) return string.Empty;
                var names = _plan.MissingVariables.Take(3).Select(v => v.Name);
                var suffix = _plan.MissingCount > 3 ? $" 等 {_plan.MissingCount} 个" : string.Empty;
                return $"将删除：{string.Join("、", names)}{suffix}。引用这些变量的流程 / 画面可能断链。";
            }
        }

        public bool HasDeleteWarning => DeleteWarning.Length > 0;

        public string UnknownScanGroupNote
        {
            get
            {
                if (_plan == null || _plan.UnknownScanGroups.Count == 0) return string.Empty;
                return $"扫描组「{string.Join("」「", _plan.UnknownScanGroups)}」不存在，将回落「{PollScheduler.DefaultGroupName}」；扫描组不会自动创建。";
            }
        }

        public bool HasUnknownScanGroupNote => UnknownScanGroupNote.Length > 0;

        public string DisplayGroupNote
        {
            get
            {
                if (_plan == null) return string.Empty;
                if (AutoCreateDisplayGroups && _plan.NewDisplayGroups.Count > 0)
                    return $"将新建显示分组：{string.Join(" / ", _plan.NewDisplayGroups)}";
                if (!AutoCreateDisplayGroups && _plan.UnknownDisplayGroups.Count > 0)
                    return $"未知显示分组将回落「{VariableGroupEditorViewModel.UngroupedName}」：{string.Join(" / ", _plan.UnknownDisplayGroups)}";
                return string.Empty;
            }
        }

        public bool HasDisplayGroupNote => DisplayGroupNote.Length > 0;

        public bool CanApply => !_isResultStep
                                && _plan is { GlobalError: null } plan
                                && (!plan.HasErrors || SkipErrors)
                                && plan.ApplicableCount + (DeleteMissing ? plan.MissingCount : 0) > 0;

        public string ApplyLabel
        {
            get
            {
                if (_plan == null) return "应用";
                var total = _plan.ApplicableCount + (DeleteMissing ? _plan.MissingCount : 0);
                return total > 0 ? $"应用 {total} 项" : "应用";
            }
        }

        public string ApplyHint
        {
            get
            {
                if (_plan == null) return GlobalError;
                if (_plan.HasErrors && !SkipErrors)
                    return $"有 {_plan.ErrorCount} 行错误：请修正文件，或勾选「跳过错误行」。";

                var total = _plan.ApplicableCount + (DeleteMissing ? _plan.MissingCount : 0);
                if (total == 0) return "没有需要应用的变更。";

                var parts = new List<string>();
                if (_plan.ApplicableCount > 0) parts.Add($"将应用 {_plan.ApplicableCount} 项");
                if (SkipErrors && _plan.HasErrors) parts.Add($"跳过 {_plan.ErrorCount} 行错误");
                if (DeleteMissing && _plan.MissingCount > 0)
                    parts.Add($"删除 {_plan.MissingCount} 个文件中没有的变量");
                return string.Join("；", parts) + "。";
            }
        }

        public string GlobalError
        {
            get => _globalError;
            private set
            {
                if (!SetProperty(ref _globalError, value)) return;
                RaisePropertyChanged(nameof(HasGlobalError));
                RaisePropertyChanged(nameof(SummaryText));
            }
        }

        public bool HasGlobalError => !string.IsNullOrWhiteSpace(_globalError);

        public bool IsResultStep
        {
            get => _isResultStep;
            private set
            {
                if (!SetProperty(ref _isResultStep, value)) return;
                RaisePropertyChanged(nameof(IsPreviewStep));
                RaisePropertyChanged(nameof(CanApply));
                ApplyCommand.RaiseCanExecuteChanged();
            }
        }

        public bool IsPreviewStep => !_isResultStep;

        public string ResultSummary
        {
            get => _resultSummary;
            private set => SetProperty(ref _resultSummary, value);
        }

        #endregion

        #region 命令

        public DelegateCommand ApplyCommand { get; }
        public DelegateCommand CancelCommand { get; }
        public DelegateCommand DoneCommand { get; }
        public DelegateCommand CloseCommand { get; }
        public DelegateCommand ReselectFileCommand { get; }
        public DelegateCommand DownloadTemplateCommand { get; }

        private void ExecuteReselectFile()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                // 与主入口同口径：xlsx（本仓模板）与汇川导出的 CSV 都收
                Filter = "变量表 (*.xlsx;*.csv)|*.xlsx;*.csv|Excel 工作簿 (*.xlsx)|*.xlsx|汇川变量表 (*.csv)|*.csv",
                Title = "选择变量表",
                FileName = FileName,
            };
            if (dialog.ShowDialog() != true) return;

            FilePath = dialog.FileName;
            FileName = Path.GetFileName(FilePath);
            LoadFile();
        }

        private void ExecuteDownloadTemplate()
        {
            if (_connection == null) return;

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Excel 工作簿 (*.xlsx)|*.xlsx",
                Title = "导出变量表模板",
                FileName = $"{_connectionName}_变量表模板.xlsx",
                DefaultExt = ".xlsx",
                AddExtension = true,
                OverwritePrompt = true,
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                DeviceVariableExportService.ExportTemplate(dialog.FileName, _connection, GroupList());
                Notifier.ShowSuccess($"模板已导出：{dialog.FileName}");
            }
            catch (Exception ex)
            {
                EasyDialog.ShowSync("模板导出失败", ex.Message);
            }
        }

        private void ExecuteApply()
        {
            if (_plan == null || _isResultStep || !CanApply) return;

            // 运行锁：流程运行中变量对象正被步骤按引用取值，改地址/类型/删除都会让运行中的流程取到错的东西。
            if (_workspace.CurrentFlow?.RunState == FlowRunState.Running)
            {
                EasyDialog.ShowSync("互锁",
                    "流程运行中禁止导入变量表。\n请先停止流程——运行中的步骤按对象引用取值，导入会改地址 / 类型或删除变量。");
                return;
            }

            // 失败也要让宿主刷新：回滚未完全成功时模型可能已有部分变化。
            _attemptedApply = true;

            try
            {
                _appliedResult = DeviceVariableImportService.Apply(
                    _plan,
                    _workspace,
                    _bridge.ReRegister,
                    DeleteMissing,
                    SkipErrors);

                IsResultStep = true;
                ResultSummary = BuildResultSummary(_appliedResult);
            }
            catch (Exception ex)
            {
                // 回滚结果已由服务写进 Message（成功 / 未完全成功及原因），这里不再固定声称"已回滚"。
                EasyDialog.ShowSync("导入失败", ex.Message);
            }
        }

        private void CloseWithResult(ButtonResult result)
        {
            RequestClose.Invoke(new DialogResult(result)
            {
                Parameters = BuildResultParameters()
            });
        }

        private DialogParameters BuildResultParameters()
        {
            var parameters = new DialogParameters();
            if (_appliedResult == null) return parameters;

            parameters.Add(AddedKey, _appliedResult.Added);
            parameters.Add(UpdatedKey, _appliedResult.Updated);
            parameters.Add(DeletedKey, _appliedResult.Deleted);
            parameters.Add(SkippedKey, _appliedResult.SkippedErrors);
            return parameters;
        }

        private static string BuildResultSummary(DeviceVariableImportResult result)
        {
            var parts = new List<string>
            {
                $"新增 {result.Added}",
                $"更新 {result.Updated}"
            };
            if (result.Deleted > 0) parts.Add($"删除 {result.Deleted}");
            if (result.SkippedErrors > 0) parts.Add($"跳过 {result.SkippedErrors}");
            return string.Join(" · ", parts);
        }

        #endregion

        #region 计划

        private void LoadFile()
        {
            // 换文件 = 重新开始：删除缺失 / 跳过错误都是"对上一份文件"的决定，必须复位，
            // 否则新文件会带着上一次的「删除缺失」误删变量。
            if (_deleteMissing) DeleteMissing = false;
            if (_skipErrors) SkipErrors = false;

            try
            {
                // 读周期只在 CSV 路径上有意义：汇川的「采集周期」与它一致时不当扫描组处理
                _rows = DeviceVariableImportService.ReadRows(FilePath, _connection?.ReadCycleMs);
                RowCount = _rows.Count;
                RebuildPlan();
            }
            catch (Exception ex)
            {
                _rows = new List<DeviceVariableRawRow>();
                RowCount = 0;
                SetGlobalError($"读取文件失败：{ex.Message}");
            }
        }

        private void RebuildPlan()
        {
            if (_connection == null) return;

            try
            {
                var groups = GroupList();
                var existing = DeviceVariables();
                // 系统变量也纳入撞名检查：注册表是 Global + System 两源合并的。
                // 系统集合由运动发布器在后台改写，必须用重试快照（与变量管理主 VM 同口径）。
                var allVariables = _workspace.GlobalVariables.Concat(CopySystemVariables()).ToList();
                var scanGroups = _manager.GetScanGroupNames(_connectionName).ToList();

                _plan = DeviceVariableImportService.BuildPlan(
                    _connection,
                    _rows,
                    existing,
                    allVariables,
                    groups,
                    scanGroups,
                    AutoCreateDisplayGroups);

                if (_plan.GlobalError != null)
                {
                    SetGlobalError(_plan.GlobalError);
                    return;
                }

                GlobalError = string.Empty;
                Items.Clear();
                foreach (var item in _plan.Items)
                {
                    item.IsSkipped = SkipErrors && item.Status == DeviceVariableImportStatus.Error;
                    Items.Add(item);
                }

                RaiseCounts();
                UpdateOptionsAndApply();
            }
            catch (Exception ex)
            {
                // AutoCreateDisplayGroups 切换会直接走到这里（不在 LoadFile 的 try 里）；
                // 旧数据里损坏的 DefaultValue 等异常不能冒到 UI 线程把进程带下去。
                SetGlobalError($"生成导入预览失败：{ex.Message}");
            }
        }

        private void SetGlobalError(string message)
        {
            _plan = null;
            Items.Clear();
            GlobalError = message;
            RaiseCounts();
            UpdateOptionsAndApply();
        }

        private void ApplySkipState()
        {
            foreach (var item in Items)
                item.IsSkipped = SkipErrors && item.Status == DeviceVariableImportStatus.Error;
        }

        private void RaiseCounts()
        {
            RaisePropertyChanged(nameof(NewCount));
            RaisePropertyChanged(nameof(UpdateCount));
            RaisePropertyChanged(nameof(UnchangedCount));
            RaisePropertyChanged(nameof(ErrorCount));
            RaisePropertyChanged(nameof(MissingCount));
            RaisePropertyChanged(nameof(HasErrors));
            RaisePropertyChanged(nameof(CanSkipErrors));
            RaisePropertyChanged(nameof(SummaryText));
        }

        private void UpdateOptionsAndApply()
        {
            RaisePropertyChanged(nameof(DeleteWarning));
            RaisePropertyChanged(nameof(HasDeleteWarning));
            RaisePropertyChanged(nameof(UnknownScanGroupNote));
            RaisePropertyChanged(nameof(HasUnknownScanGroupNote));
            RaisePropertyChanged(nameof(DisplayGroupNote));
            RaisePropertyChanged(nameof(HasDisplayGroupNote));
            RaisePropertyChanged(nameof(CanApply));
            RaisePropertyChanged(nameof(ApplyLabel));
            RaisePropertyChanged(nameof(ApplyHint));
            ApplyCommand.RaiseCanExecuteChanged();
        }

        private List<VariableGroupDescriptor> GroupList()
            => _workspace.CurrentSolution?.VariableGroups?.ToList() ?? new List<VariableGroupDescriptor>();

        private List<NetworkVariableModel> DeviceVariables()
            => _workspace.GlobalVariables.OfType<NetworkVariableModel>()
                .Where(v => string.Equals(v.ConnectionName, _connectionName, StringComparison.Ordinal))
                .ToList();

        /// <summary>系统变量快照（运动发布器在后台线程改写该集合，直接枚举会偶发"集合已更改"）。</summary>
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

        #endregion
    }
}
