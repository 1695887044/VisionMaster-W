using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Core.Interfaces;
using Core.Interfaces.Database;
using Newtonsoft.Json;

namespace Plugin.Database
{
    /// <summary>
    /// 数据库三步骤的共用底板：profile/语句选择、参数端口重建、输出行、失败语义、配置窗口测试。
    ///
    /// 三条不能改的约定：
    /// 1) 参数端口必须在 <see cref="ApplyConfigValues"/> 里重建——流程编译器对运行实例**只调它**，
    ///    不调 Initialize（Engine\FlowCompiler.cs:883）。放 Initialize = 运行期永远没有参数端口。
    /// 2) `.vms` 里存一份参数模式快照（<see cref="Parameters"/>）：编译期没有宿主可查语句定义，
    ///    不存就建不出端口。快照只有名字/类型/必填/默认值，不含 SQL/连接串。
    /// 3) 失败语义：写步骤默认 FailFast=true（抛 → 本节点 Failed 且中断当轮），读默认只置失败。
    ///    "抛"要真正停线，必须同时覆盖 <see cref="RethrowOnException"/>——基类默认会把异常吞成 Success=false。
    /// </summary>
    public abstract class DatabasePluginBase : VisionPluginBase, IPluginCustomViewProvider, IPluginConfigContextProvider
    {
        private readonly List<string> _dynamicInputNames = new();
        private PluginConfigContext _configContext;
        private IStepConfigData _lastStepData;
        private bool _suppressSelection;
        private DbProfileInfo _selectedProfile;
        private DbStatementInfo _selectedStatement;
        private int _portsVersion;

        protected DatabasePluginBase(bool failFastDefault)
        {
            FailFast = failFastDefault;
        }

        // ---------------------------------------------------------------- 配置（落 .vms）

        /// <summary>数据库稳定身份（显示名可改，引用不断链）。</summary>
        [StepConfig]
        public Guid ProfileId { get; set; }

        /// <summary>命名语句稳定身份（全局唯一）。</summary>
        [StepConfig]
        public Guid StatementId { get; set; }

        /// <summary>参数模式快照 + 参数顺序（运行期据此建端口；值本身在端口上）。</summary>
        [StepConfig]
        public ObservableCollection<DbStatementParameterBinding> Parameters { get; set; } = new();

        /// <summary>失败是否中断当轮（写默认 true，读默认 false）。</summary>
        [StepConfig]
        public bool FailFast { get; set; }

        /// <summary>单条语句超时（秒，0 = 用数据库/语句上的值）。</summary>
        [StepConfig]
        public int TimeoutSeconds { get; set; }

        /// <summary>本步骤要求的语句类型（子类给）。</summary>
        protected abstract DbStatementKind RequiredKind { get; }

        /// <summary>本步骤的固定输出（子类给）。</summary>
        protected abstract DbOutputRow[] CreateOutputRows();

        /// <summary>配置态数据库能力（由宿主经 <see cref="PluginConfigContext"/> 递送）。</summary>
        protected IDatabaseAccess Database => _configContext?.Database ?? NullDatabaseAccess.Instance;

        // ---------------------------------------------------------------- 配置视图状态

        public ObservableCollection<DbProfileInfo> ProfileOptions { get; } = new();

        public ObservableCollection<DbStatementInfo> StatementOptions { get; } = new();

        public ObservableCollection<DbParameterRow> ParameterRows { get; } = new();

        public ObservableCollection<DbOutputRow> OutputRows { get; } = new();

        /// <summary>端口集合版本：参数行重建后靠它让绑定重新取 Port（与 DataRecord 同一手法）。</summary>
        public int PortsVersion => _portsVersion;

        public DbProfileInfo SelectedProfile
        {
            get => _selectedProfile;
            set
            {
                if (_suppressSelection)
                {
                    _selectedProfile = value;
                    OnPropertyChanged(nameof(SelectedProfile));
                    return;
                }

                if (value?.ProfileId == _selectedProfile?.ProfileId) return;

                _selectedProfile = value;
                ProfileId = value?.ProfileId ?? Guid.Empty;
                StatementId = Guid.Empty;

                _suppressSelection = true;
                try
                {
                    StatementOptions.Clear();
                    if (value != null)
                        foreach (var s in Database.ListStatements(value.ProfileId)) StatementOptions.Add(s);
                    _selectedStatement = null;
                }
                finally
                {
                    _suppressSelection = false;
                }

                // 换数据库 = 换语句 = 参数全清（旧参数值对新语句没有意义）
                Parameters.Clear();
                RebuildParameterPorts();
                RaiseSelectionState();
            }
        }

        public DbStatementInfo SelectedStatement
        {
            get => _selectedStatement;
            set
            {
                if (_suppressSelection)
                {
                    _selectedStatement = value;
                    OnPropertyChanged(nameof(SelectedStatement));
                    return;
                }

                if (value?.StatementId == _selectedStatement?.StatementId) return;

                _selectedStatement = value;
                StatementId = value?.StatementId ?? Guid.Empty;
                ApplyStatementSchema(value);
                RaiseSelectionState();
            }
        }

        public string SqlPreview => _selectedStatement?.SqlPreview ?? string.Empty;

        public string StatementKindText => DbStatementKindNames.Text(RequiredKind);

        public bool HasProfiles => ProfileOptions.Count > 0;

        public bool HasStatements => StatementOptions.Count > 0;

        public bool IsReadOnlyProfile => _selectedProfile?.OpenMode == DbOpenMode.ReadOnly;

        public bool IsReadOnlyBlocked => IsReadOnlyProfile && RequiredKind != DbStatementKind.Query;

        public bool HasKindMismatch =>
            _selectedStatement != null && _selectedStatement.Kind != RequiredKind;

        /// <summary>配置态问题（可直接上屏）；null = 可以执行/确认。</summary>
        public string ConfigProblem
        {
            get
            {
                if (ProfileOptions.Count == 0) return "还没有数据库配置：请到「硬件管理 → 数据库」添加。";
                if (_selectedProfile == null) return "请选择数据库。";
                if (StatementOptions.Count == 0) return "该数据库还没有命名语句：请到「硬件管理 → 数据库」新建。";
                if (_selectedStatement == null) return "请选择命名语句。";
                if (IsReadOnlyBlocked) return "只读数据库不允许写操作。";
                if (HasKindMismatch)
                    return $"该步骤需要「{DbStatementKindNames.Text(RequiredKind)}」语句，"
                           + $"当前语句是「{DbStatementKindNames.Text(_selectedStatement.Kind)}」。";
                return null;
            }
        }

        public bool HasConfigProblem => ConfigProblem != null;

        // ---------------------------------------------------------------- 测试语句

        private string _testMessage = string.Empty;
        private string _testLevelKey = "none";
        private bool _isTesting;

        public string TestMessage
        {
            get => _testMessage;
            private set
            {
                if (_testMessage == value) return;
                _testMessage = value;
                OnPropertyChanged(nameof(TestMessage));
                OnPropertyChanged(nameof(HasTestMessage));
            }
        }

        /// <summary>none / ok / fail，供视图按令牌换底色。</summary>
        public string TestLevelKey
        {
            get => _testLevelKey;
            private set
            {
                if (_testLevelKey == value) return;
                _testLevelKey = value;
                OnPropertyChanged(nameof(TestLevelKey));
            }
        }

        public bool HasTestMessage => !string.IsNullOrWhiteSpace(_testMessage);

        public bool IsTesting
        {
            get => _isTesting;
            private set
            {
                if (_isTesting == value) return;
                _isTesting = value;
                OnPropertyChanged(nameof(IsTesting));
                OnPropertyChanged(nameof(CanTest));
            }
        }

        public bool CanTest => !_isTesting;

        /// <summary>测试当前语句（事务内执行后回滚）。失败只上屏，不抛。</summary>
        public async Task TestAsync()
        {
            if (_selectedStatement == null)
            {
                SetTest("fail", "请先选择命名语句。");
                return;
            }

            if (IsReadOnlyBlocked)
            {
                SetTest("fail", "只读数据库不允许写操作。");
                return;
            }

            if (HasKindMismatch)
            {
                SetTest("fail", $"该步骤需要「{DbStatementKindNames.Text(RequiredKind)}」语句。");
                return;
            }

            IsTesting = true;
            TestLevelKey = "none";
            TestMessage = "测试中…";

            try
            {
                var id = _selectedStatement.StatementId;
                var isQuery = _selectedStatement.Kind == DbStatementKind.Query;
                var values = BuildParameterValues();
                var options = new DbCommandOptions { TimeoutSeconds = TimeoutSeconds };
                var db = Database;

                var ok = false;
                DbTestResult result = null;
                string error = null;
                await Task.Run(() =>
                {
                    ok = db.TryTestStatement(id, values, options, false, out var r, out error);
                    result = r;
                });

                if (!ok)
                {
                    SetTest("fail", error ?? "测试失败（未返回原因）");
                    return;
                }

                var rows = result.Query?.RowCount ?? result.Execution?.AffectedRows ?? 0;
                var rowsText = isQuery ? $"返回 {rows} 行" : $"影响 {rows} 行";
                var suffix = result.RolledBack ? " · 已回滚（未写入数据）" : " · 已提交";
                SetTest("ok", $"测试通过 · {rowsText} · {result.ElapsedMs:F0}ms{suffix}");
            }
            catch (Exception ex)
            {
                SetTest("fail", ex.Message);
            }
            finally
            {
                IsTesting = false;
            }
        }

        private void SetTest(string levelKey, string message)
        {
            TestLevelKey = levelKey;
            TestMessage = message;
        }

        // ---------------------------------------------------------------- 宿主上下文

        public void SetConfigContext(PluginConfigContext context)
        {
            _configContext = context;
            ReloadOptions();
        }

        /// <summary>从宿主重新拉 profile/语句候选；不覆盖 .vms 里的参数快照。</summary>
        public void ReloadOptions()
        {
            _suppressSelection = true;
            try
            {
                ProfileOptions.Clear();
                foreach (var p in Database.ListProfiles()) ProfileOptions.Add(p);

                _selectedProfile = ProfileOptions.FirstOrDefault(p => p.ProfileId == ProfileId);

                StatementOptions.Clear();
                if (_selectedProfile != null)
                    foreach (var s in Database.ListStatements(_selectedProfile.ProfileId)) StatementOptions.Add(s);

                _selectedStatement = StatementOptions.FirstOrDefault(s => s.StatementId == StatementId);
            }
            finally
            {
                _suppressSelection = false;
            }

            OnPropertyChanged(nameof(SelectedProfile));
            OnPropertyChanged(nameof(SelectedStatement));
            RaiseSelectionState();
        }

        // ---------------------------------------------------------------- 端口/生命周期

        public override void ApplyConfigValues(IStepConfigData stepData)
        {
            // 记下来：流程编译器只调本方法（不调 Initialize），端口重建要靠它取 InputValues 回灌常量
            _lastStepData = stepData;
            base.ApplyConfigValues(stepData);
            RebuildParameterPorts();
            RebuildOutputRows();
        }

        public object GetConfigView(IStepConfigData stepData)
        {
            Initialize(stepData);
            return new DatabasePluginView { DataContext = this };
        }

        /// <summary>按参数快照重建动态输入端口（一个参数一个 <c>InputPort&lt;object&gt;</c>）。</summary>
        public void RebuildParameterPorts()
        {
            var stepData = _lastStepData ?? StepData;

            foreach (var name in _dynamicInputNames)
                RemoveDynamicInput(name);
            _dynamicInputNames.Clear();
            ParameterRows.Clear();

            foreach (var binding in Parameters ?? Enumerable.Empty<DbStatementParameterBinding>())
            {
                var name = (binding?.Name ?? string.Empty).Trim();
                if (name.Length == 0) continue;
                if (_dynamicInputNames.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var description = string.IsNullOrWhiteSpace(binding.Description) ? name : binding.Description;
                // 非必填：必填语义由服务端按"定义 + 默认值"判，端口层标必填会把"用默认值"误杀
                var port = new InputPort<object>(name, null, description) { IsRequired = false };
                AddDynamicInput(port);
                _dynamicInputNames.Add(name);

                // 端口重建后把已确认的常量值顶回去：base.ApplyConfigValues 先跑，那时端口还不存在
                if (stepData?.InputValues != null
                    && stepData.InputValues.TryGetValue(name, out var saved)
                    && !stepData.IsLinked(name))
                {
                    port.Value = saved;
                }

                ParameterRows.Add(new DbParameterRow
                {
                    Name = name,
                    DbType = binding.DbType,
                    IsRequired = binding.IsRequired,
                    Description = binding.Description,
                    Port = port
                });
            }

            _portsVersion++;
            OnPropertyChanged(nameof(PortsVersion));
        }

        private void RebuildOutputRows()
        {
            OutputRows.Clear();
            foreach (var row in CreateOutputRows()) OutputRows.Add(row);
        }

        /// <summary>语句换了：参数表按新定义重建，并清掉 .vms 里已不存在的参数键。</summary>
        private void ApplyStatementSchema(DbStatementInfo info)
        {
            var oldNames = (Parameters ?? Enumerable.Empty<DbStatementParameterBinding>())
                .Where(p => p != null)
                .Select(p => p.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToList();

            Parameters.Clear();
            if (info != null)
            {
                foreach (var def in info.Parameters)
                    Parameters.Add(DbStatementParameterBinding.From(def));
            }

            var stepData = _lastStepData ?? StepData;
            if (stepData != null)
            {
                foreach (var name in oldNames)
                {
                    var stillUsed = Parameters.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (!stillUsed) stepData.RemoveInputValue(name);
                }
            }

            RebuildParameterPorts();
        }

        public override void OnConfirm(IStepConfigData stepData)
        {
            if (!ValidateForConfirm(stepData, out var error))
                throw new InvalidOperationException(error);

            base.OnConfirm(stepData);
        }

        /// <summary>确认前校验：选型完整、类型匹配、只读档不许写、必填参数有值或已链接。</summary>
        public bool ValidateForConfirm(IStepConfigData stepData, out string error)
        {
            var problem = ConfigProblem;
            if (problem != null)
            {
                error = problem;
                return false;
            }

            foreach (var binding in Parameters ?? Enumerable.Empty<DbStatementParameterBinding>())
            {
                if (binding == null || !binding.IsRequired) continue;
                if (!string.IsNullOrEmpty(binding.DefaultValue)) continue;

                if (stepData != null && stepData.IsLinked(binding.Name)) continue;

                Inputs.TryGetValue(binding.Name, out var port);
                var value = port?.Value;
                var empty = value == null || (value is string s && s.Length == 0);
                if (empty)
                {
                    error = $"缺少必填参数 @{binding.Name}：请填写常量或链接上游。";
                    return false;
                }
            }

            error = null;
            return true;
        }

        // ---------------------------------------------------------------- 执行辅助

        protected List<DbParameterValue> BuildParameterValues()
        {
            var values = new List<DbParameterValue>();
            foreach (var binding in Parameters ?? Enumerable.Empty<DbStatementParameterBinding>())
            {
                if (binding == null || string.IsNullOrWhiteSpace(binding.Name)) continue;
                Inputs.TryGetValue(binding.Name, out var port);
                values.Add(new DbParameterValue(binding.Name, port?.Value));
            }
            return values;
        }

        protected DbCommandOptions BuildOptions(IExecutionContext context) => new()
        {
            TimeoutSeconds = TimeoutSeconds,
            CancellationToken = context?.CancellationToken ?? default
        };

        /// <summary>
        /// 结果行 → JSON 数组（列名做键）。
        /// 数值保持数值、null 保持 null、字节数组转 Base64、时间转 ISO 8601——
        /// 下游（变量赋值 / 脚本 / HTTP 上报）拿到的都是可解析的形态，不是"所有值都变字符串"。
        /// </summary>
        protected static string BuildRowsJson(DbQueryResult result)
        {
            var rows = new List<Dictionary<string, object>>();
            foreach (var row in result.Rows)
            {
                var dict = new Dictionary<string, object>(StringComparer.Ordinal);
                for (var i = 0; i < result.Columns.Count && i < row.Length; i++)
                    dict[result.Columns[i]] = NormalizeJsonValue(row[i]);
                rows.Add(dict);
            }
            return JsonConvert.SerializeObject(rows);
        }

        private static object NormalizeJsonValue(object value) => value switch
        {
            null => null,
            byte[] bytes => Convert.ToBase64String(bytes),
            DateTime dt => dt.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
            _ => value
        };

        /// <summary>
        /// 失败处理：FailFast 抛（配合 <see cref="RethrowOnException"/> 中断当轮），否则只置失败。
        /// 注意先不写 ErrorMessage —— 抛出去后基类会用"类型名: 原因"覆盖，这里保持一条来源。
        /// </summary>
        protected void HandleFailure(string error)
        {
            var message = string.IsNullOrWhiteSpace(error) ? "数据库操作失败（未返回原因）" : error;
            if (FailFast) throw new DatabaseStepException(message);
            Fail(message);
        }

        /// <summary>
        /// 基类默认吞异常（Success=false、流程继续）。FailFast 时改成上抛，
        /// 否则"写库失败停线"只是句口号——步骤红了，线照样跑。
        /// </summary>
        protected override bool RethrowOnException => FailFast;

        protected void RaiseSelectionState()
        {
            OnPropertyChanged(nameof(SqlPreview));
            OnPropertyChanged(nameof(StatementKindText));
            OnPropertyChanged(nameof(HasProfiles));
            OnPropertyChanged(nameof(HasStatements));
            OnPropertyChanged(nameof(IsReadOnlyProfile));
            OnPropertyChanged(nameof(IsReadOnlyBlocked));
            OnPropertyChanged(nameof(HasKindMismatch));
            OnPropertyChanged(nameof(ConfigProblem));
            OnPropertyChanged(nameof(HasConfigProblem));
        }
    }

    /// <summary>参数行（配置视图用）：定义 + 当前端口。</summary>
    public sealed class DbParameterRow
    {
        public string Name { get; init; } = string.Empty;
        public string DbType { get; init; } = string.Empty;
        public bool IsRequired { get; init; }
        public string Description { get; init; } = string.Empty;
        public IInputPort Port { get; init; }

        public string DisplayName => "@" + Name;
        public string RequiredText => IsRequired ? "必填" : "可选";
        public string TypeText => DbType;
    }

    /// <summary>输出行（配置视图用，只读展示）。</summary>
    public sealed class DbOutputRow
    {
        public DbOutputRow(string name, string typeText, string description)
        {
            Name = name;
            TypeText = typeText;
            Description = description;
        }

        public string Name { get; }
        public string TypeText { get; }
        public string Description { get; }
    }

    /// <summary>FailFast 抛出的异常：类型名有意义，日志里一眼能看出是数据库步骤主动停线。</summary>
    public sealed class DatabaseStepException : Exception
    {
        public DatabaseStepException(string message) : base(message) { }
    }
}
