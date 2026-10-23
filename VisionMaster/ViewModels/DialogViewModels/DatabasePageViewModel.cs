using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Core.Database;
using Core.Interfaces;
using Core.Interfaces.Database;
using Microsoft.Win32;
using Prism.Commands;
using Prism.Dialogs;
using Prism.Mvvm;
using UI.CustomControl;
using VisionMaster.Models;
using VisionMaster.Services;
using VisionMaster.Services.Hardware;

namespace VisionMaster.ViewModels.DialogViewModels
{
    /// <summary>
    /// 硬件管理 → 数据库页（嵌入页）。
    ///
    /// 职责边界（与设计基线一致）：
    ///   · 本页只做**配置 + 封装**：profile、命名语句、参数表、测试；
    ///   · 流程执行归 Plugin.Database + Core.Database，本页不持有运行期连接；
    ///   · 权限只消费 <see cref="IHardwareEditGateAware"/> 推来的结果，不在页面里自判角色；
    ///   · 删除前查当前方案引用（深遍历容器），有引用直接拒绝并列出步骤。
    ///
    /// 身份一律用 Guid（ProfileId / StatementId），显示名可改；改名不断链。
    /// </summary>
    public sealed class DatabasePageViewModel : BindableBase, IEmbeddedDialogPage, IHardwareEditGateAware
    {
        private readonly DatabaseService _service;
        private readonly IWorkspaceManager? _workspace;
        private readonly IUserNotifier _notifier;

        private bool _loading;
        private bool _canEdit = true;
        private string? _gateReason;
        private bool _isDirty;
        private string _statusText = string.Empty;
        private string _profileTestMessage = string.Empty;
        private string _profileTestLevelKey = "none";
        private string _statementTestMessage = string.Empty;
        private string _statementTestLevelKey = "none";
        private string _previewText = string.Empty;

        private ProfileRow? _selectedProfile;
        private StatementRow? _selectedStatement;

        public DatabasePageViewModel(DatabaseService service, IWorkspaceManager? workspace = null, IUserNotifier? notifier = null)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            // 可空：断言/渲染探针没有工作区；删除引用检查会返回"没有引用"（真机永远有工作区）
            _workspace = workspace;
            _notifier = notifier ?? NullUserNotifier.Instance;

            AddProfileCommand = new DelegateCommand(AddProfile, () => CanEdit);
            SaveProfileCommand = new DelegateCommand(SaveProfile, () => CanEdit && SelectedProfile != null);
            DeleteProfileCommand = new DelegateCommand(DeleteProfile, () => CanEdit && SelectedProfile != null);
            BrowseDatabaseFileCommand = new DelegateCommand(BrowseDatabaseFile, () => CanEdit && SelectedProfile?.IsSqlite == true);
            CreateDatabaseFileCommand = new DelegateCommand(CreateDatabaseFile, () => CanEdit && SelectedProfile?.IsSqlite == true);
            TestConnectionCommand = new DelegateCommand(TestConnection, () => SelectedProfile != null);
            ClearPasswordCommand = new DelegateCommand(ClearPassword, () => CanEdit && SelectedProfile?.IsMySql == true);

            AddStatementCommand = new DelegateCommand(AddStatement, () => CanEdit && SelectedProfile != null);
            SaveStatementCommand = new DelegateCommand(SaveStatement, () => CanEdit && SelectedStatement != null);
            DeleteStatementCommand = new DelegateCommand(DeleteStatement, () => CanEdit && SelectedStatement != null);
            TestStatementCommand = new DelegateCommand(TestStatement, () => SelectedStatement != null);
            AddParameterCommand = new DelegateCommand(AddParameter, () => CanEdit && SelectedStatement != null);
            RemoveParameterCommand = new DelegateCommand(RemoveLastParameter, () => CanEdit && SelectedStatement != null);
            RefreshCommand = new DelegateCommand(Reload);
        }

        // ---------------------------------------------------------------- 绑定面

        public ObservableCollection<ProfileRow> Profiles { get; } = new();

        public ObservableCollection<StatementRow> Statements { get; } = new();

        public ObservableCollection<string> DbTypes { get; } = new(DbTypeNames.All);

        public IReadOnlyList<KindOption> KindOptions { get; } = new[]
        {
            new KindOption(DbStatementKind.Query, "查询"),
            new KindOption(DbStatementKind.Execute, "执行"),
            new KindOption(DbStatementKind.WriteRow, "写一行")
        };

        /// <summary>数据库类型下拉（值仍是枚举；显示名走 DbProviderNames）。</summary>
        public IReadOnlyList<ProviderOption> ProviderOptions { get; } = DbProviderNames.All
            .Select(p => new ProviderOption(p, DbProviderNames.Text(p)))
            .ToList();

        public DelegateCommand AddProfileCommand { get; }
        public DelegateCommand SaveProfileCommand { get; }
        public DelegateCommand DeleteProfileCommand { get; }
        public DelegateCommand BrowseDatabaseFileCommand { get; }
        public DelegateCommand CreateDatabaseFileCommand { get; }
        public DelegateCommand TestConnectionCommand { get; }
        public DelegateCommand ClearPasswordCommand { get; }
        public DelegateCommand AddStatementCommand { get; }
        public DelegateCommand SaveStatementCommand { get; }
        public DelegateCommand DeleteStatementCommand { get; }
        public DelegateCommand TestStatementCommand { get; }
        public DelegateCommand AddParameterCommand { get; }
        public DelegateCommand RemoveParameterCommand { get; }
        public DelegateCommand RefreshCommand { get; }

        public ProfileRow? SelectedProfile
        {
            get => _selectedProfile;
            set
            {
                if (!SetProperty(ref _selectedProfile, value)) return;
                if (!_loading) LoadStatements(value);
                RaisePropertyChanged(nameof(HasProfile));
                RaisePropertyChanged(nameof(CanTestProfile));
                RaiseAllCommands();
            }
        }

        public StatementRow? SelectedStatement
        {
            get => _selectedStatement;
            set
            {
                if (!SetProperty(ref _selectedStatement, value)) return;
                if (!_loading) AttachStatementDirtyTracking(value);
                RaisePropertyChanged(nameof(HasStatement));
                RaisePropertyChanged(nameof(CanTestStatement));
                RaiseAllCommands();
            }
        }

        public bool HasProfile => _selectedProfile != null;

        public bool HasStatement => _selectedStatement != null;

        public bool CanEdit
        {
            get => _canEdit;
            private set
            {
                if (!SetProperty(ref _canEdit, value)) return;
                RaisePropertyChanged(nameof(IsReadOnly));
                RaiseAllCommands();
            }
        }

        public bool IsReadOnly => !CanEdit;

        public string? GateReason
        {
            get => _gateReason;
            private set
            {
                if (!SetProperty(ref _gateReason, value)) return;
                RaisePropertyChanged(nameof(HasGateReason));
            }
        }

        public bool HasGateReason => !string.IsNullOrWhiteSpace(_gateReason);

        public bool IsDirty
        {
            get => _isDirty;
            private set
            {
                if (!SetProperty(ref _isDirty, value)) return;
                RaisePropertyChanged(nameof(DirtyText));
            }
        }

        public string DirtyText => IsDirty ? "未保存" : "已保存";

        public string StatusText
        {
            get => _statusText;
            private set => SetProperty(ref _statusText, value);
        }

        public string ConfigPath => _service.ConfigPath;

        public string ProfileTestMessage
        {
            get => _profileTestMessage;
            private set
            {
                if (!SetProperty(ref _profileTestMessage, value)) return;
                RaisePropertyChanged(nameof(HasProfileTestMessage));
            }
        }

        public string ProfileTestLevelKey
        {
            get => _profileTestLevelKey;
            private set => SetProperty(ref _profileTestLevelKey, value);
        }

        public bool HasProfileTestMessage => !string.IsNullOrWhiteSpace(_profileTestMessage);

        public string StatementTestMessage
        {
            get => _statementTestMessage;
            private set
            {
                if (!SetProperty(ref _statementTestMessage, value)) return;
                RaisePropertyChanged(nameof(HasStatementTestMessage));
            }
        }

        public string StatementTestLevelKey
        {
            get => _statementTestLevelKey;
            private set => SetProperty(ref _statementTestLevelKey, value);
        }

        public bool HasStatementTestMessage => !string.IsNullOrWhiteSpace(_statementTestMessage);

        public string PreviewText
        {
            get => _previewText;
            private set
            {
                if (!SetProperty(ref _previewText, value)) return;
                RaisePropertyChanged(nameof(HasPreview));
            }
        }

        public bool HasPreview => !string.IsNullOrWhiteSpace(_previewText);

        public bool CanTestProfile => _selectedProfile != null;

        public bool CanTestStatement => _selectedStatement != null;

        // ---------------------------------------------------------------- 嵌入页生命周期

        public void OnEmbeddedOpened(IDialogParameters parameters) => Reload();

        public void OnEmbeddedActivated()
        {
            // 切回来时如果没改过就刷新（别的入口可能刚加过 profile）；有改动绝不覆盖用户输入
            if (!IsDirty) Reload();
        }

        public void OnEmbeddedDeactivated() { }

        public void OnEmbeddedClosed() { }

        public bool CanCloseEmbedded(out string? reason)
        {
            reason = null;
            if (!IsDirty) return true;

            if (EasyDialog.ShowSync("数据库配置", "有未保存的修改，关闭后将丢失。确定关闭？"))
                return true;

            reason = "数据库页有未保存的修改";
            return false;
        }

        public void ApplyEditGate(bool canEditConfig, bool canOperateRuntime, string? configReason, string? runtimeReason)
        {
            CanEdit = canEditConfig;
            GateReason = canEditConfig ? null : configReason;
        }

        // ---------------------------------------------------------------- 加载

        public void Reload()
        {
            var wasLoading = _loading;
            _loading = true;
            try
            {
                var snapshot = _service.Snapshot();
                var selectedId = _selectedProfile?.ProfileId;

                Profiles.Clear();
                foreach (var model in snapshot.Profiles.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
                    Profiles.Add(ProfileRow.From(model));

                var selected = Profiles.FirstOrDefault(p => p.ProfileId == selectedId) ?? Profiles.FirstOrDefault();
                SetProperty(ref _selectedProfile, selected, nameof(SelectedProfile));
                RaisePropertyChanged(nameof(HasProfile));
                LoadStatements(selected);
                IsDirty = false;

                StatusText = string.IsNullOrWhiteSpace(_service.LastLoadError)
                    ? $"{Profiles.Count} 个数据库 · {_service.ConfigPath}"
                    : _service.LastLoadError!;
            }
            finally
            {
                _loading = wasLoading;
            }

            RaiseAllCommands();
        }

        private void LoadStatements(ProfileRow? profile)
        {
            var selectedId = _selectedStatement?.StatementId;
            Statements.Clear();

            if (profile != null)
            {
                foreach (var model in profile.Model.Statements.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
                    Statements.Add(StatementRow.From(model));
            }

            var selected = Statements.FirstOrDefault(s => s.StatementId == selectedId) ?? Statements.FirstOrDefault();
            SetProperty(ref _selectedStatement, selected, nameof(SelectedStatement));
            RaisePropertyChanged(nameof(HasStatement));
            AttachStatementDirtyTracking(selected);
            RaiseAllCommands();
        }

        // ---------------------------------------------------------------- 数据库 profile

        private void AddProfile()
        {
            if (!ConfirmDiscardDirty()) return;

            var dir = Path.GetDirectoryName(_service.ConfigPath) ?? AppContext.BaseDirectory;
            var row = new ProfileRow
            {
                ProfileId = Guid.NewGuid(),
                Name = UniqueProfileName(),
                Provider = DbProviderKind.Sqlite,
                FilePath = Path.Combine(dir, "production.db"),
                Port = "3306",
                OpenMode = DbOpenMode.ReadWrite,
                TimeoutSeconds = "30",
                MaxRows = "1000",
                IsEnabled = true,
                IsNew = true
            };

            Profiles.Add(row);
            SelectedProfile = row;
            IsDirty = true;
            StatusText = "新建数据库配置：填写文件路径后点『保存数据库』";
        }

        private string UniqueProfileName()
        {
            var index = 1;
            while (Profiles.Any(p => string.Equals(p.Name, "数据库" + index, StringComparison.OrdinalIgnoreCase)))
                index++;
            return "数据库" + index;
        }

        private void SaveProfile()
        {
            var row = _selectedProfile;
            if (row == null) return;

            if (!TryParsePositive(row.TimeoutSeconds, "超时", 1, 3600, out var timeout, out var error)
                || !TryParsePositive(row.MaxRows, "最大返回行数", 1, 1_000_000, out var maxRows, out error))
            {
                _notifier.ShowWarn(error);
                return;
            }

            var model = row.ToModel();
            model.TimeoutSeconds = timeout;
            model.MaxRows = maxRows;

            // 只有用户这次动过密码才把明文交给服务加密；没动过就沿用库里已有密文
            var plainPassword = row.PasswordTouched ? (row.Password ?? string.Empty) : null;

            if (!_service.TrySaveProfile(model, plainPassword, out error))
            {
                _notifier.ShowError(error ?? "保存失败");
                return;
            }

            row.TimeoutSeconds = timeout.ToString();
            row.MaxRows = maxRows.ToString();
            row.IsNew = false;
            row.MarkPasswordSaved();
            IsDirty = false;
            StatusText = $"已保存数据库：{row.Name}";
            _notifier.ShowInfo($"数据库已保存：{row.Name}");
        }

        private void DeleteProfile()
        {
            var row = _selectedProfile;
            if (row == null) return;

            var references = CollectProfileReferences(row.ProfileId);
            if (references.Count > 0)
            {
                _notifier.ShowError("该数据库仍被流程引用，不能删除：" + string.Join("、", references.Take(5)));
                return;
            }

            if (!EasyDialog.ShowSync("删除数据库", $"删除「{row.Name}」及其下 {row.Model.Statements.Count} 条命名语句？\n（数据库文件本身不会被删除）"))
                return;

            if (!_service.TryDeleteProfile(row.ProfileId, out var error))
            {
                _notifier.ShowError(error ?? "删除失败");
                return;
            }

            Profiles.Remove(row);
            SelectedProfile = Profiles.FirstOrDefault();
            IsDirty = false;
            StatusText = $"已删除数据库：{row.Name}";
        }

        private IReadOnlyList<string> CollectProfileReferences(Guid profileId)
        {
            var hits = new List<string>();
            hits.AddRange(DatabaseReferenceScanner.FindReferences(_workspace, profileId, Guid.Empty));

            foreach (var statement in _selectedProfile?.Model.Statements ?? new List<NamedStatement>())
                hits.AddRange(DatabaseReferenceScanner.FindReferences(_workspace, Guid.Empty, statement.StatementId));

            return hits.Distinct(StringComparer.Ordinal).ToList();
        }

        private void BrowseDatabaseFile()
        {
            var row = _selectedProfile;
            if (row == null) return;

            var dialog = new OpenFileDialog
            {
                Title = "选择 SQLite 数据库文件",
                Filter = "SQLite 数据库|*.db;*.sqlite;*.sqlite3|所有文件|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true) row.FilePath = dialog.FileName;
        }

        private void CreateDatabaseFile()
        {
            var row = _selectedProfile;
            if (row == null) return;

            var dialog = new SaveFileDialog
            {
                Title = "新建 SQLite 数据库文件",
                Filter = "SQLite 数据库|*.db;*.sqlite;*.sqlite3",
                DefaultExt = ".db",
                AddExtension = true,
                OverwritePrompt = false
            };

            if (dialog.ShowDialog() == true)
            {
                row.FilePath = dialog.FileName;
                StatusText = "文件将在保存数据库时创建";
            }
        }

        private void TestConnection()
        {
            var row = _selectedProfile;
            if (row == null) return;

            var ok = _service.TryTest(row.ProfileId, out var version, out var elapsedMs, out var error);
            if (ok)
            {
                ProfileTestLevelKey = "ok";
                ProfileTestMessage = $"连接成功 · {row.ProviderText} {version} · {elapsedMs:F0}ms";
            }
            else
            {
                ProfileTestLevelKey = "fail";
                ProfileTestMessage = error ?? "连接失败（未返回原因）";
            }
        }

        /// <summary>PasswordBox 的唯一入口（密码不参与数据绑定，避免明文进绑定引擎）。</summary>
        public void SetPassword(string password)
        {
            if (_selectedProfile == null) return;
            _selectedProfile.Password = password;
            IsDirty = true;
        }

        /// <summary>清空已保存密码（保存后生效）。</summary>
        public void ClearPassword()
        {
            if (_selectedProfile == null) return;
            _selectedProfile.MarkPasswordCleared();
            IsDirty = true;
        }

        // ---------------------------------------------------------------- 命名语句

        private void AddStatement()
        {
            var profile = _selectedProfile;
            if (profile == null) return;
            if (!ConfirmDiscardDirty()) return;

            var row = new StatementRow
            {
                StatementId = Guid.NewGuid(),
                ProfileId = profile.ProfileId,
                Name = UniqueStatementName(profile),
                Kind = DbStatementKind.Query,
                Sql = "SELECT 1",
                TimeoutSeconds = "0",
                MaxRows = "0",
                MaxAffectedRows = "0",
                IsEnabled = true,
                IsNew = true
            };

            profile.Model.Statements.Add(row.ToModel());
            Statements.Add(row);
            SelectedStatement = row;
            IsDirty = true;
            StatusText = "新建命名语句：保存前请把 SQL 与参数表对齐";
        }

        private static string UniqueStatementName(ProfileRow profile)
        {
            var index = 1;
            while (profile.Model.Statements.Any(s => string.Equals(s.Name, "Statement" + index, StringComparison.OrdinalIgnoreCase)))
                index++;
            return "Statement" + index;
        }

        private void SaveStatement()
        {
            var row = _selectedStatement;
            if (row == null) return;

            var model = row.ToModel();
            if (!_service.TrySaveStatement(model, out var error))
            {
                StatementTestLevelKey = "fail";
                StatementTestMessage = error ?? "保存失败";
                _notifier.ShowError(error ?? "保存失败");
                return;
            }

            row.IsNew = false;
            IsDirty = false;
            StatementTestLevelKey = "ok";
            StatementTestMessage = $"已保存 · {row.Name}";
            StatusText = $"已保存语句：{row.Name}";
        }

        private void DeleteStatement()
        {
            var row = _selectedStatement;
            if (row == null) return;

            var references = DatabaseReferenceScanner.FindReferences(_workspace, Guid.Empty, row.StatementId);
            if (references.Count > 0)
            {
                _notifier.ShowError("该语句仍被流程引用，不能删除：" + string.Join("、", references.Take(5)));
                return;
            }

            if (!EasyDialog.ShowSync("删除命名语句", $"删除「{row.Name}」？"))
                return;

            if (!_service.TryDeleteStatement(row.StatementId, out var error))
            {
                _notifier.ShowError(error ?? "删除失败");
                return;
            }

            _selectedProfile?.Model.Statements.RemoveAll(s => s.StatementId == row.StatementId);
            Statements.Remove(row);
            SelectedStatement = Statements.FirstOrDefault();
            IsDirty = false;
            StatusText = $"已删除语句：{row.Name}";
        }

        private void TestStatement()
        {
            var row = _selectedStatement;
            if (row == null) return;

            var values = new List<DbParameterValue>();
            foreach (var parameter in row.Parameters)
            {
                if (parameter.IsRequired && string.IsNullOrWhiteSpace(parameter.DefaultValue))
                {
                    StatementTestLevelKey = "fail";
                    StatementTestMessage = $"参数 @{parameter.Name} 是必填且没有默认值：测试前请先填默认值";
                    return;
                }
                values.Add(new DbParameterValue(parameter.Name, parameter.DefaultValue));
            }

            var ok = _service.TryTestStatement(
                row.StatementId, values, new DbCommandOptions(), commitForReal: false,
                out var result, out var error);

            if (!ok)
            {
                StatementTestLevelKey = "fail";
                StatementTestMessage = error ?? "测试失败（未返回原因）";
                PreviewText = string.Empty;
                return;
            }

            var rows = result.Query?.RowCount ?? result.Execution?.AffectedRows ?? 0;
            var rowsText = row.Kind == DbStatementKind.Query ? $"返回 {rows} 行" : $"影响 {rows} 行";
            StatementTestLevelKey = "ok";
            StatementTestMessage = $"测试通过 · {rowsText} · {result.ElapsedMs:F0}ms"
                                   + (result.RolledBack ? " · 已回滚（未写入数据）" : " · 已提交");

            PreviewText = result.Query != null ? FormatPreview(result.Query) : string.Empty;
        }

        private static string FormatPreview(DbQueryResult query)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(string.Join("\t", query.Columns));
            foreach (var row in query.Rows.Take(100))
                sb.AppendLine(string.Join("\t", row.Select(v => v?.ToString() ?? "NULL")));
            if (query.Truncated) sb.AppendLine("（已按最大行数截断）");
            return sb.ToString();
        }

        private void AddParameter()
        {
            var row = _selectedStatement;
            if (row == null) return;

            var index = 1;
            while (row.Parameters.Any(p => string.Equals(p.Name, "param" + index, StringComparison.OrdinalIgnoreCase)))
                index++;

            row.Parameters.Add(new ParameterRow { Name = "param" + index, DbType = DbTypeNames.Text });
            IsDirty = true;
        }

        private void RemoveLastParameter()
        {
            var row = _selectedStatement;
            if (row == null || row.Parameters.Count == 0) return;

            row.Parameters.RemoveAt(row.Parameters.Count - 1);
            IsDirty = true;
        }

        // ---------------------------------------------------------------- 脏标记

        private void AttachStatementDirtyTracking(StatementRow? row)
        {
            if (row == null) return;

            row.PropertyChanged -= OnRowChanged;
            row.PropertyChanged += OnRowChanged;

            foreach (var parameter in row.Parameters)
            {
                parameter.PropertyChanged -= OnRowChanged;
                parameter.PropertyChanged += OnRowChanged;
            }

            row.Parameters.CollectionChanged -= OnParametersChanged;
            row.Parameters.CollectionChanged += OnParametersChanged;
        }

        private void OnParametersChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (ParameterRow item in e.NewItems)
                {
                    item.PropertyChanged -= OnRowChanged;
                    item.PropertyChanged += OnRowChanged;
                }
            }
            if (!_loading) IsDirty = true;
        }

        private void OnRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (!_loading) IsDirty = true;
        }

        private bool ConfirmDiscardDirty()
        {
            if (!IsDirty) return true;
            return EasyDialog.ShowSync("数据库配置", "当前修改尚未保存，继续将丢弃。确定继续？");
        }

        private void RaiseAllCommands()
        {
            AddProfileCommand.RaiseCanExecuteChanged();
            SaveProfileCommand.RaiseCanExecuteChanged();
            DeleteProfileCommand.RaiseCanExecuteChanged();
            BrowseDatabaseFileCommand.RaiseCanExecuteChanged();
            CreateDatabaseFileCommand.RaiseCanExecuteChanged();
            TestConnectionCommand.RaiseCanExecuteChanged();
            ClearPasswordCommand.RaiseCanExecuteChanged();
            AddStatementCommand.RaiseCanExecuteChanged();
            SaveStatementCommand.RaiseCanExecuteChanged();
            DeleteStatementCommand.RaiseCanExecuteChanged();
            TestStatementCommand.RaiseCanExecuteChanged();
            AddParameterCommand.RaiseCanExecuteChanged();
            RemoveParameterCommand.RaiseCanExecuteChanged();
        }

        private static bool TryParsePositive(string text, string label, int min, int max, out int value, out string? error)
        {
            if (!int.TryParse(text, out value) || value < min || value > max)
            {
                error = $"{label}必须是 {min}~{max} 之间的整数";
                value = min;
                return false;
            }

            error = null;
            return true;
        }

        // ---------------------------------------------------------------- 行模型

        public sealed class ProfileRow : BindableBase
        {
            private string _name = string.Empty;
            private DbProviderKind _provider = DbProviderKind.Sqlite;
            private string _filePath = string.Empty;
            private string _server = string.Empty;
            private string _port = "3306";
            private string _databaseName = string.Empty;
            private string _userId = string.Empty;
            private string? _password;
            private bool _hasStoredPassword;
            private DbOpenMode _openMode = DbOpenMode.ReadWrite;
            private string _timeoutSeconds = "30";
            private string _maxRows = "1000";
            private bool _isEnabled = true;

            public Guid ProfileId { get; set; }
            public bool IsNew { get; set; }
            public DatabaseProfile Model { get; set; } = new();

            public string Name
            {
                get => _name;
                set
                {
                    if (!SetProperty(ref _name, value)) return;
                    RaisePropertyChanged(nameof(Caption));
                }
            }

            public DbProviderKind Provider
            {
                get => _provider;
                set
                {
                    if (!SetProperty(ref _provider, value)) return;
                    RaisePropertyChanged(nameof(IsSqlite));
                    RaisePropertyChanged(nameof(IsMySql));
                    RaisePropertyChanged(nameof(ProviderText));
                    RaisePropertyChanged(nameof(Summary));
                }
            }

            public bool IsSqlite => _provider == DbProviderKind.Sqlite;

            public bool IsMySql => _provider == DbProviderKind.MySql;

            public string ProviderText => DbProviderNames.Text(_provider);

            public string FilePath
            {
                get => _filePath;
                set
                {
                    if (!SetProperty(ref _filePath, value)) return;
                    RaisePropertyChanged(nameof(Summary));
                }
            }

            public string Server
            {
                get => _server;
                set
                {
                    if (!SetProperty(ref _server, value)) return;
                    RaisePropertyChanged(nameof(Summary));
                }
            }

            public string Port
            {
                get => _port;
                set
                {
                    if (!SetProperty(ref _port, value)) return;
                    RaisePropertyChanged(nameof(Summary));
                }
            }

            public string DatabaseName
            {
                get => _databaseName;
                set
                {
                    if (!SetProperty(ref _databaseName, value)) return;
                    RaisePropertyChanged(nameof(Summary));
                }
            }

            public string UserId
            {
                get => _userId;
                set => SetProperty(ref _userId, value);
            }

            /// <summary>
            /// 用户这次输入的明文密码。null = 没动过（保存时沿用已存密文）；空串 = 清空。
            /// 视图用 PasswordBox，值只经 <see cref="DatabasePageViewModel.SetPassword"/> 进来。
            /// </summary>
            public string? Password
            {
                get => _password;
                set
                {
                    if (!SetProperty(ref _password, value)) return;
                    PasswordTouched = true;
                    RaisePropertyChanged(nameof(PasswordHint));
                }
            }

            public bool PasswordTouched { get; private set; }

            /// <summary>库里已有密文（不代表这次要改）。</summary>
            public bool HasStoredPassword
            {
                get => _hasStoredPassword;
                private set
                {
                    if (!SetProperty(ref _hasStoredPassword, value)) return;
                    RaisePropertyChanged(nameof(PasswordHint));
                }
            }

            public string? PasswordHint => PasswordTouched
                ? (string.IsNullOrEmpty(Password) ? "保存后将清空密码" : "保存后将更新密码")
                : (HasStoredPassword ? "已保存密码（留空表示不修改）" : "未设置密码");

            /// <summary>保存成功后由 VM 调用：密码状态归位（明文不留在内存里）。</summary>
            public void MarkPasswordSaved()
            {
                HasStoredPassword = PasswordTouched ? !string.IsNullOrEmpty(Password) : HasStoredPassword;
                PasswordTouched = false;
                _password = null;
                RaisePropertyChanged(nameof(Password));
                RaisePropertyChanged(nameof(PasswordHint));
            }

            public void MarkPasswordCleared()
            {
                _password = string.Empty;
                PasswordTouched = true;
                RaisePropertyChanged(nameof(Password));
                RaisePropertyChanged(nameof(PasswordHint));
            }

            public DbOpenMode OpenMode
            {
                get => _openMode;
                set
                {
                    if (!SetProperty(ref _openMode, value)) return;
                    RaisePropertyChanged(nameof(IsReadOnly));
                    RaisePropertyChanged(nameof(OpenModeText));
                }
            }

            public bool IsReadOnly
            {
                get => _openMode == DbOpenMode.ReadOnly;
                set => OpenMode = value ? DbOpenMode.ReadOnly : DbOpenMode.ReadWrite;
            }

            public string OpenModeText => _openMode == DbOpenMode.ReadOnly ? "只读" : "读写";

            public string TimeoutSeconds
            {
                get => _timeoutSeconds;
                set => SetProperty(ref _timeoutSeconds, value);
            }

            public string MaxRows
            {
                get => _maxRows;
                set => SetProperty(ref _maxRows, value);
            }

            public bool IsEnabled
            {
                get => _isEnabled;
                set => SetProperty(ref _isEnabled, value);
            }

            public string Caption => string.IsNullOrWhiteSpace(Name) ? "(未命名)" : Name;

            public string Summary => $"{ProviderText} · {OpenModeText} · {TargetText} · {Model.Statements.Count} 条语句";

            public string TargetText => _provider == DbProviderKind.MySql
                ? $"{Server}:{Port}/{DatabaseName}"
                : Path.GetFileName(FilePath);

            public DatabaseProfile ToModel() => new()
            {
                ProfileId = ProfileId,
                Name = Name,
                Provider = Provider,
                FilePath = FilePath,
                Server = Server,
                Port = int.TryParse(Port, out var port) ? port : 3306,
                DatabaseName = DatabaseName,
                UserId = UserId,
                OpenMode = OpenMode,
                TimeoutSeconds = int.TryParse(TimeoutSeconds, out var t) ? t : 30,
                MaxRows = int.TryParse(MaxRows, out var m) ? m : 1000,
                IsEnabled = IsEnabled,
                Statements = Model.Statements
            };

            public static ProfileRow From(DatabaseProfile model) => new()
            {
                ProfileId = model.ProfileId,
                Model = model,
                Name = model.Name,
                Provider = model.Provider,
                FilePath = model.FilePath,
                Server = model.Server,
                Port = model.Port > 0 ? model.Port.ToString() : "3306",
                DatabaseName = model.DatabaseName,
                UserId = model.UserId,
                HasStoredPassword = !string.IsNullOrEmpty(model.PasswordCipher),
                OpenMode = model.OpenMode,
                TimeoutSeconds = model.TimeoutSeconds.ToString(),
                MaxRows = model.MaxRows.ToString(),
                IsEnabled = model.IsEnabled
            };
        }

        public sealed class StatementRow : BindableBase
        {
            private string _name = string.Empty;
            private DbStatementKind _kind = DbStatementKind.Query;
            private string _sql = string.Empty;
            private string _timeoutSeconds = "0";
            private string _maxRows = "0";
            private string _maxAffectedRows = "0";
            private bool _isEnabled = true;

            public Guid StatementId { get; set; }
            public Guid ProfileId { get; set; }
            public bool IsNew { get; set; }
            public NamedStatement Model { get; set; } = new();
            public ObservableCollection<ParameterRow> Parameters { get; } = new();

            public string Name
            {
                get => _name;
                set
                {
                    if (!SetProperty(ref _name, value)) return;
                    RaisePropertyChanged(nameof(Caption));
                }
            }

            public DbStatementKind Kind
            {
                get => _kind;
                set
                {
                    if (!SetProperty(ref _kind, value)) return;
                    RaisePropertyChanged(nameof(Caption));
                    RaisePropertyChanged(nameof(KindText));
                }
            }

            public string KindText => DbStatementKindNames.Text(_kind);

            public string Sql
            {
                get => _sql;
                set => SetProperty(ref _sql, value);
            }

            public string TimeoutSeconds
            {
                get => _timeoutSeconds;
                set => SetProperty(ref _timeoutSeconds, value);
            }

            public string MaxRows
            {
                get => _maxRows;
                set => SetProperty(ref _maxRows, value);
            }

            public string MaxAffectedRows
            {
                get => _maxAffectedRows;
                set => SetProperty(ref _maxAffectedRows, value);
            }

            public bool IsEnabled
            {
                get => _isEnabled;
                set => SetProperty(ref _isEnabled, value);
            }

            public string Caption => $"{Name}（{KindText}）";

            public NamedStatement ToModel() => new()
            {
                StatementId = StatementId,
                ProfileId = ProfileId,
                Name = Name,
                Kind = Kind,
                Sql = Sql,
                TimeoutSeconds = int.TryParse(TimeoutSeconds, out var t) ? t : 0,
                MaxRows = int.TryParse(MaxRows, out var m) ? m : 0,
                MaxAffectedRows = int.TryParse(MaxAffectedRows, out var a) ? a : 0,
                IsEnabled = IsEnabled,
                Parameters = Parameters.Select(p => p.ToModel()).ToList()
            };

            public static StatementRow From(NamedStatement model)
            {
                var row = new StatementRow
                {
                    StatementId = model.StatementId,
                    ProfileId = model.ProfileId,
                    Model = model,
                    Name = model.Name,
                    Kind = model.Kind,
                    Sql = model.Sql,
                    TimeoutSeconds = model.TimeoutSeconds.ToString(),
                    MaxRows = model.MaxRows.ToString(),
                    MaxAffectedRows = model.MaxAffectedRows.ToString(),
                    IsEnabled = model.IsEnabled
                };

                foreach (var parameter in model.Parameters)
                    row.Parameters.Add(ParameterRow.From(parameter));

                return row;
            }
        }

        public sealed class ParameterRow : BindableBase
        {
            private string _name = string.Empty;
            private string _dbType = DbTypeNames.Text;
            private bool _isRequired;
            private string _defaultValue = string.Empty;
            private string _description = string.Empty;

            public string Name
            {
                get => _name;
                set => SetProperty(ref _name, value);
            }

            public string DbType
            {
                get => _dbType;
                set => SetProperty(ref _dbType, value);
            }

            public bool IsRequired
            {
                get => _isRequired;
                set => SetProperty(ref _isRequired, value);
            }

            public string DefaultValue
            {
                get => _defaultValue;
                set => SetProperty(ref _defaultValue, value);
            }

            public string Description
            {
                get => _description;
                set => SetProperty(ref _description, value);
            }

            public DbStatementParameter ToModel() => new()
            {
                Name = Name,
                DbType = DbType,
                IsRequired = IsRequired,
                DefaultValue = DefaultValue,
                Description = Description
            };

            public static ParameterRow From(DbStatementParameter model) => new()
            {
                Name = model.Name,
                DbType = model.DbType,
                IsRequired = model.IsRequired,
                DefaultValue = model.DefaultValue,
                Description = model.Description
            };
        }
    }

    /// <summary>语句类型下拉项（值仍是枚举，显示中文）。</summary>
    public sealed class KindOption
    {
        public KindOption(DbStatementKind value, string text)
        {
            Value = value;
            Text = text;
        }

        public DbStatementKind Value { get; }
        public string Text { get; }
    }

    /// <summary>数据库类型下拉项（值仍是枚举）。</summary>
    public sealed class ProviderOption
    {
        public ProviderOption(DbProviderKind value, string text)
        {
            Value = value;
            Text = text;
        }

        public DbProviderKind Value { get; }
        public string Text { get; }
    }

    /// <summary>无通知实现：渲染探针/断言宿主构造页面时用，消息丢弃但不抛。</summary>
    internal sealed class NullUserNotifier : IUserNotifier
    {
        public static readonly NullUserNotifier Instance = new();

        public void ShowInfo(string message) { }
        public void ShowWarn(string message) { }
        public void ShowError(string message) { }
    }
}
