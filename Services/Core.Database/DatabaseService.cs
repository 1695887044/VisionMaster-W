using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Core.Interfaces;
using Core.Interfaces.Database;

namespace Core.Database
{
    /// <summary>
    /// 数据库服务（宿主单例）：profile/命名语句存储 + 执行。
    ///
    /// 这是插件唯一拿得到的数据库能力（经 <see cref="IDatabaseAccess"/>），
    /// 也是硬件管理数据库页的编辑后端（页面用具体类型做 CRUD）。
    ///
    /// Provider 只在这一层出现：SQLite（文件）与 MySQL/MariaDB（服务器）各一个执行器，
    /// 插件与契约完全看不到 Provider 类型。
    ///
    /// 【审计口径】每次执行记一行日志：profile 名、语句名、Kind、行数、耗时、错误类别、操作者。
    /// **不记参数值、不记 SQL 全文**（参数里可能有料号/序列号，SQL 可能含业务信息）。
    /// </summary>
    public sealed class DatabaseService : IDatabaseAccess
    {
        private readonly object _gate = new();
        private readonly DatabaseStore _store;
        private readonly IReadOnlyDictionary<DbProviderKind, DbExecutorBase> _executors;
        private readonly ILogService? _log;

        public DatabaseService(ILogService? log = null, string? configPath = null)
        {
            _log = log;
            _store = new DatabaseStore(configPath);
            _executors = new Dictionary<DbProviderKind, DbExecutorBase>
            {
                [DbProviderKind.Sqlite] = new SqliteDbExecutor(log),
                [DbProviderKind.MySql] = new MySqlDbExecutor(log)
            };

            _store.Load();
            if (!string.IsNullOrWhiteSpace(_store.LastLoadError))
                _log?.Warn("[数据库] " + _store.LastLoadError);
        }

        /// <summary>databases.json 的实际路径（页面页脚展示）。</summary>
        public string ConfigPath => _store.FilePath;

        public string? LastLoadError => _store.LastLoadError;

        // ================================================================ 元数据（插件面）

        public IReadOnlyList<DbProfileInfo> ListProfiles()
        {
            lock (_gate)
            {
                return _store.Load().Profiles
                    .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(p => p.ToInfo())
                    .ToList();
            }
        }

        public bool TryGetProfile(Guid profileId, out DbProfileInfo info, out string? error)
        {
            lock (_gate)
            {
                var profile = _store.Load().Profiles.FirstOrDefault(p => p.ProfileId == profileId);
                if (profile == null)
                {
                    info = null!;
                    error = "找不到该数据库配置（可能已被删除），请到硬件管理 → 数据库页确认";
                    return false;
                }

                info = profile.ToInfo();
                error = null;
                return true;
            }
        }

        public IReadOnlyList<DbStatementInfo> ListStatements(Guid profileId)
        {
            lock (_gate)
            {
                var profile = _store.Load().Profiles.FirstOrDefault(p => p.ProfileId == profileId);
                if (profile == null) return Array.Empty<DbStatementInfo>();

                return profile.Statements
                    .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(s => s.ToInfo())
                    .ToList();
            }
        }

        public bool TryGetStatement(Guid statementId, out DbStatementInfo info, out string? error)
        {
            lock (_gate)
            {
                var found = FindStatementLocked(statementId);
                if (found.statement == null)
                {
                    info = null!;
                    error = "找不到该命名语句（可能已被删除），请到硬件管理 → 数据库页确认";
                    return false;
                }

                info = found.statement.ToInfo();
                error = null;
                return true;
            }
        }

        // ================================================================ 连接

        public bool TryTest(Guid profileId, out string? versionText, out double elapsedMs, out string? error)
        {
            versionText = null;
            elapsedMs = 0;

            DatabaseProfile? profile;
            lock (_gate)
            {
                profile = _store.Load().Profiles.FirstOrDefault(p => p.ProfileId == profileId)?.Clone();
            }

            if (profile == null)
            {
                error = "找不到该数据库配置";
                return false;
            }

            if (!TryGetExecutor(profile, out var executor, out error))
                return false;

            var ok = executor.TryTest(profile, out versionText, out elapsedMs, out error);
            Audit(profile, null, "Test", 0, elapsedMs, error);
            return ok;
        }

        // ================================================================ 执行

        public bool TryQuery(
            Guid statementId,
            IReadOnlyList<DbParameterValue> parameters,
            DbCommandOptions options,
            out DbQueryResult result,
            out string? error)
        {
            result = null!;
            options ??= DbCommandOptions.Default;

            if (!TryResolve(statementId, out var profile, out var statement, out error))
                return false;

            if (!TryGetExecutor(profile, out var executor, out error))
                return false;

            if (statement.Kind != DbStatementKind.Query)
            {
                error = $"该语句是『{DbStatementKindNames.Text(statement.Kind)}』类型，不能用于查询步骤";
                return false;
            }

            if (!StatementValidator.Validate(statement, profile, out error))
                return false;

            var timeout = ResolveTimeout(profile, statement, options);
            var maxRows = ResolveMaxRows(profile, statement, options);

            var ok = executor.TryQuery(
                profile, statement, parameters, timeout, maxRows, options.CancellationToken,
                out result, out error);

            Audit(profile, statement, "Query", ok ? result.RowCount : 0,
                ok ? result.ElapsedMs : 0, error);
            return ok;
        }

        public bool TryExecute(
            Guid statementId,
            IReadOnlyList<DbParameterValue> parameters,
            DbCommandOptions options,
            out DbExecutionResult result,
            out string? error)
        {
            result = null!;
            options ??= DbCommandOptions.Default;

            if (!TryResolve(statementId, out var profile, out var statement, out error))
                return false;

            if (!TryGetExecutor(profile, out var executor, out error))
                return false;

            if (statement.Kind == DbStatementKind.Query)
            {
                error = "该语句是『查询』类型，不能用于写步骤";
                return false;
            }

            if (!StatementValidator.Validate(statement, profile, out error))
                return false;

            var timeout = ResolveTimeout(profile, statement, options);
            var maxAffected = ResolveMaxAffected(statement, options);

            var ok = executor.TryExecute(
                profile, statement, parameters, timeout, maxAffected, options.CancellationToken,
                out result, out error);

            Audit(profile, statement, "Execute", ok ? result.AffectedRows : 0,
                ok ? result.ElapsedMs : 0, error);
            return ok;
        }

        public bool TryExecuteBatch(
            Guid statementId,
            IReadOnlyList<IReadOnlyList<DbParameterValue>> rows,
            DbCommandOptions options,
            out DbBatchResult result,
            out string? error)
        {
            result = null!;
            options ??= DbCommandOptions.Default;

            if (!TryResolve(statementId, out var profile, out var statement, out error))
                return false;

            if (!TryGetExecutor(profile, out var executor, out error))
                return false;

            if (statement.Kind == DbStatementKind.Query)
            {
                error = "该语句是『查询』类型，不能用于写步骤";
                return false;
            }

            if (!StatementValidator.Validate(statement, profile, out error))
                return false;

            var timeout = ResolveTimeout(profile, statement, options);
            var maxAffected = ResolveMaxAffected(statement, options);

            var ok = executor.TryExecuteBatch(
                profile, statement, rows ?? Array.Empty<IReadOnlyList<DbParameterValue>>(),
                timeout, maxAffected, options.CancellationToken, out result, out error);

            Audit(profile, statement, "Batch", ok ? result.SucceededRows : 0,
                ok ? result.ElapsedMs : 0, error);
            return ok;
        }

        public bool TryTestStatement(
            Guid statementId,
            IReadOnlyList<DbParameterValue> parameters,
            DbCommandOptions options,
            bool commitForReal,
            out DbTestResult result,
            out string? error)
        {
            result = null!;
            options ??= DbCommandOptions.Default;

            if (!TryResolve(statementId, out var profile, out var statement, out error))
                return false;

            if (!TryGetExecutor(profile, out var executor, out error))
                return false;

            if (!StatementValidator.Validate(statement, profile, out error))
                return false;

            var timeout = ResolveTimeout(profile, statement, options);
            var maxRows = ResolveMaxRows(profile, statement, options);
            var maxAffected = ResolveMaxAffected(statement, options);

            var ok = executor.TryTestStatement(
                profile, statement, parameters, timeout, maxRows, maxAffected, commitForReal,
                options.CancellationToken, out result, out error);

            var rows = ok ? (result.Query?.RowCount ?? result.Execution?.AffectedRows ?? 0) : 0;
            Audit(profile, statement, commitForReal ? "Test(commit)" : "Test(rollback)", rows,
                ok ? result.ElapsedMs : 0, error);
            return ok;
        }

        // ================================================================ 编辑面（宿主页）

        /// <summary>取一份深拷贝（页面编辑草稿用；改它不会影响服务里的活对象）。</summary>
        public DatabaseFileModel Snapshot()
        {
            lock (_gate) return CloneModel(_store.Load());
        }

        public void Reload()
        {
            lock (_gate) _store.Invalidate();
        }

        /// <summary>
        /// 退出时释放 Provider 的连接池（宿主模块的退出任务调用）。
        /// 放在服务里而不是宿主：Provider 只允许出现在 Core.Database，宿主不直接碰 Microsoft.Data.Sqlite。
        /// </summary>
        public static void ClearPools()
        {
            try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); }
            catch { /* 退出链上的清理失败不该让进程退不出去 */ }

            try { MySqlConnector.MySqlConnection.ClearAllPools(); }
            catch { /* 同上 */ }
        }

        public bool TrySaveProfile(DatabaseProfile profile, out string? error)
            => TrySaveProfile(profile, null, out error);

        /// <summary>
        /// 保存 profile。
        /// <paramref name="plainPassword"/> 非 null 表示"用户这次填了新密码"（含空串 = 清空），
        /// 为 null 表示"没动密码"，沿用 <see cref="DatabaseProfile.PasswordCipher"/> 里已有的密文。
        /// 明文密码只在这一刻存在，落盘前就变成 DPAPI 密文。
        /// </summary>
        public bool TrySaveProfile(DatabaseProfile profile, string? plainPassword, out string? error)
        {
            error = null;
            if (profile == null) { error = "数据库配置为空"; return false; }

            var name = (profile.Name ?? string.Empty).Trim();
            if (name.Length == 0) { error = "数据库名称不能为空"; return false; }

            if (profile.Provider is not (DbProviderKind.Sqlite or DbProviderKind.MySql))
            {
                error = $"不支持的数据库类型：{profile.Provider}";
                return false;
            }

            if (profile.TimeoutSeconds <= 0 || profile.TimeoutSeconds > 3600)
            {
                error = "超时必须在 1~3600 秒之间";
                return false;
            }

            if (profile.MaxRows <= 0 || profile.MaxRows > 1_000_000)
            {
                error = "最大返回行数必须在 1~1000000 之间";
                return false;
            }

            var path = string.Empty;
            if (profile.Provider == DbProviderKind.Sqlite)
            {
                path = (profile.FilePath ?? string.Empty).Trim();
                if (path.Length == 0) { error = "数据库文件路径不能为空"; return false; }

                var ext = Path.GetExtension(path);
                if (!ext.Equals(".db", StringComparison.OrdinalIgnoreCase)
                    && !ext.Equals(".sqlite", StringComparison.OrdinalIgnoreCase)
                    && !ext.Equals(".sqlite3", StringComparison.OrdinalIgnoreCase))
                {
                    error = "只允许 .db / .sqlite / .sqlite3 文件";
                    return false;
                }

                try { path = Path.GetFullPath(path); }
                catch (Exception ex) { error = $"路径不合法：{ex.Message}"; return false; }

                var dir = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                {
                    error = $"目录不存在：{dir}";
                    return false;
                }

                if (profile.OpenMode == DbOpenMode.ReadOnly && !File.Exists(path))
                {
                    error = "只读配置要求数据库文件已存在";
                    return false;
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(profile.Server)) { error = "MySQL 服务器地址不能为空"; return false; }
                if (profile.Port is < 1 or > 65535) { error = "MySQL 端口必须在 1~65535 之间"; return false; }
                if (string.IsNullOrWhiteSpace(profile.DatabaseName)) { error = "MySQL 数据库名不能为空"; return false; }
                if (string.IsNullOrWhiteSpace(profile.UserId)) { error = "MySQL 用户名不能为空"; return false; }
            }

            lock (_gate)
            {
                var model = _store.Load();
                var duplicate = model.Profiles.FirstOrDefault(
                    p => p.ProfileId != profile.ProfileId && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
                if (duplicate != null)
                {
                    error = $"已存在同名数据库：{name}";
                    return false;
                }

                // SQLite 读写档：保存时就打开一次（不存在则创建），把"路径写错"挡在保存期而不是首次执行。
                // MySQL 刻意不在这里连库：配置阶段服务器可能没开，保存不应被网络状态绑架；连不通由「测试连接」暴露。
                if (profile.Provider == DbProviderKind.Sqlite && profile.OpenMode == DbOpenMode.ReadWrite)
                {
                    var probe = new DatabaseProfile
                    {
                        Provider = DbProviderKind.Sqlite,
                        FilePath = path,
                        OpenMode = DbOpenMode.ReadWrite,
                        TimeoutSeconds = profile.TimeoutSeconds
                    };
                    if (!TryGetExecutor(probe, out var probeExecutor, out error))
                        return false;
                    if (!probeExecutor.TryOpen(probe, Math.Min(profile.TimeoutSeconds, 5), out var conn, out error))
                        return false;
                    conn.Dispose();
                }

                // 密码口径：plainPassword=null 表示"这次没改密码"→ 沿用库里的密文；
                // 非 null（含空串）表示"用户改了/清空了"→ 重新加密或置空。
                var existing = model.Profiles.FirstOrDefault(p => p.ProfileId == profile.ProfileId);
                var cipher = plainPassword != null
                    ? DbCredentialProtector.Protect(plainPassword)
                    : (existing?.PasswordCipher ?? string.Empty);

                if (existing == null)
                {
                    var created = profile.Clone();
                    created.ProfileId = profile.ProfileId == Guid.Empty ? Guid.NewGuid() : profile.ProfileId;
                    created.Name = name;
                    created.FilePath = path;
                    created.Server = (profile.Server ?? string.Empty).Trim();
                    created.Port = profile.Port > 0 ? profile.Port : 3306;
                    created.DatabaseName = (profile.DatabaseName ?? string.Empty).Trim();
                    created.UserId = (profile.UserId ?? string.Empty).Trim();
                    created.PasswordCipher = cipher;
                    created.CreatedAt = DateTime.Now;
                    created.ModifiedAt = DateTime.Now;
                    created.ModifiedBy = Environment.UserName;
                    model.Profiles.Add(created);
                }
                else
                {
                    existing.Name = name;
                    existing.Provider = profile.Provider;
                    existing.FilePath = path;
                    existing.Server = (profile.Server ?? string.Empty).Trim();
                    existing.Port = profile.Port > 0 ? profile.Port : 3306;
                    existing.DatabaseName = (profile.DatabaseName ?? string.Empty).Trim();
                    existing.UserId = (profile.UserId ?? string.Empty).Trim();
                    existing.PasswordCipher = cipher;
                    existing.OpenMode = profile.OpenMode;
                    existing.TimeoutSeconds = profile.TimeoutSeconds;
                    existing.MaxRows = profile.MaxRows;
                    existing.IsEnabled = profile.IsEnabled;
                    existing.ModifiedAt = DateTime.Now;
                    existing.ModifiedBy = Environment.UserName;
                }

                try
                {
                    _store.Save(model);
                    _log?.Info($"[数据库] 保存数据库配置：{name}（{DbProviderNames.Text(profile.Provider)}）");
                    return true;
                }
                catch (Exception ex)
                {
                    error = $"保存失败：{ex.Message}";
                    return false;
                }
            }
        }

        public bool TryDeleteProfile(Guid profileId, out string? error)
        {
            error = null;
            lock (_gate)
            {
                var model = _store.Load();
                var existing = model.Profiles.FirstOrDefault(p => p.ProfileId == profileId);
                if (existing == null) { error = "找不到该数据库配置"; return false; }

                model.Profiles.Remove(existing);
                try
                {
                    _store.Save(model);
                    _log?.Info($"[数据库] 删除数据库配置：{existing.Name}");
                    return true;
                }
                catch (Exception ex)
                {
                    error = $"保存失败：{ex.Message}";
                    return false;
                }
            }
        }

        /// <summary>只校验不保存（页面编辑时即时提示用）。</summary>
        public bool TryValidateStatement(NamedStatement statement, out string? error)
        {
            lock (_gate)
            {
                var profile = _store.Load().Profiles.FirstOrDefault(p => p.ProfileId == statement.ProfileId);
                if (profile == null)
                {
                    error = "找不到所属数据库";
                    return false;
                }
                return StatementValidator.Validate(statement, profile, out error);
            }
        }

        public bool TrySaveStatement(NamedStatement statement, out string? error)
        {
            error = null;
            if (statement == null) { error = "语句为空"; return false; }

            var name = (statement.Name ?? string.Empty).Trim();
            if (name.Length == 0) { error = "语句名不能为空"; return false; }

            lock (_gate)
            {
                var model = _store.Load();
                var profile = model.Profiles.FirstOrDefault(p => p.ProfileId == statement.ProfileId);
                if (profile == null) { error = "找不到所属数据库"; return false; }

                var duplicate = profile.Statements.FirstOrDefault(
                    s => s.StatementId != statement.StatementId && string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
                if (duplicate != null)
                {
                    error = $"该数据库下已存在同名语句：{name}";
                    return false;
                }

                var draft = statement.Clone();
                draft.Name = name;
                draft.ProfileId = profile.ProfileId;

                if (!StatementValidator.Validate(draft, profile, out error))
                    return false;

                var existing = profile.Statements.FirstOrDefault(s => s.StatementId == statement.StatementId);
                if (existing == null)
                {
                    if (draft.StatementId == Guid.Empty) draft.StatementId = Guid.NewGuid();
                    draft.CreatedAt = DateTime.Now;
                    draft.ModifiedAt = DateTime.Now;
                    profile.Statements.Add(draft);
                }
                else
                {
                    existing.Name = draft.Name;
                    existing.Kind = draft.Kind;
                    existing.Sql = draft.Sql;
                    existing.TimeoutSeconds = draft.TimeoutSeconds;
                    existing.MaxRows = draft.MaxRows;
                    existing.MaxAffectedRows = draft.MaxAffectedRows;
                    existing.IsEnabled = draft.IsEnabled;
                    existing.Parameters = draft.Parameters;
                    existing.ModifiedAt = DateTime.Now;
                }

                try
                {
                    _store.Save(model);
                    _log?.Info($"[数据库] 保存命名语句：{profile.Name}/{name}（{DbStatementKindNames.Text(draft.Kind)}）");
                    return true;
                }
                catch (Exception ex)
                {
                    error = $"保存失败：{ex.Message}";
                    return false;
                }
            }
        }

        public bool TryDeleteStatement(Guid statementId, out string? error)
        {
            error = null;
            lock (_gate)
            {
                var model = _store.Load();
                foreach (var profile in model.Profiles)
                {
                    var existing = profile.Statements.FirstOrDefault(s => s.StatementId == statementId);
                    if (existing == null) continue;

                    profile.Statements.Remove(existing);
                    try
                    {
                        _store.Save(model);
                        _log?.Info($"[数据库] 删除命名语句：{profile.Name}/{existing.Name}");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        error = $"保存失败：{ex.Message}";
                        return false;
                    }
                }
            }

            error = "找不到该命名语句";
            return false;
        }

        // ================================================================ 内部

        private bool TryGetExecutor(DatabaseProfile profile, out DbExecutorBase executor, out string? error)
        {
            if (_executors.TryGetValue(profile.Provider, out var found))
            {
                executor = found;
                error = null;
                return true;
            }

            executor = null!;
            error = $"不支持的数据库类型：{profile.Provider}（当前支持 SQLite / MySQL·MariaDB）";
            return false;
        }

        private bool TryResolve(
            Guid statementId,
            out DatabaseProfile profile,
            out NamedStatement statement,
            out string? error)
        {
            profile = null!;
            statement = null!;

            lock (_gate)
            {
                var found = FindStatementLocked(statementId);
                if (found.statement == null)
                {
                    error = "找不到该命名语句（可能已被删除），请到硬件管理 → 数据库页确认";
                    return false;
                }

                if (!found.statement.IsEnabled)
                {
                    error = $"语句『{found.statement.Name}』已被禁用";
                    return false;
                }

                if (found.profile == null)
                {
                    error = "语句所属的数据库配置不存在";
                    return false;
                }

                if (!found.profile.IsEnabled)
                {
                    error = $"数据库『{found.profile.Name}』已被禁用";
                    return false;
                }

                profile = found.profile.Clone();
                statement = found.statement.Clone();
                error = null;
                return true;
            }
        }

        private (DatabaseProfile? profile, NamedStatement? statement) FindStatementLocked(Guid statementId)
        {
            foreach (var profile in _store.Load().Profiles)
            {
                var statement = profile.Statements.FirstOrDefault(s => s.StatementId == statementId);
                if (statement != null) return (profile, statement);
            }
            return (null, null);
        }

        private static int ResolveTimeout(DatabaseProfile profile, NamedStatement statement, DbCommandOptions options)
        {
            var values = new[] { profile.TimeoutSeconds, statement.TimeoutSeconds, options.TimeoutSeconds }
                .Where(v => v > 0)
                .ToArray();
            return values.Length == 0 ? 30 : Math.Min(values.Min(), 3600);
        }

        private static int ResolveMaxRows(DatabaseProfile profile, NamedStatement statement, DbCommandOptions options)
        {
            var values = new[] { profile.MaxRows, statement.MaxRows, options.MaxRows }
                .Where(v => v > 0)
                .ToArray();
            return values.Length == 0 ? 1000 : Math.Min(values.Min(), 1_000_000);
        }

        private static int ResolveMaxAffected(NamedStatement statement, DbCommandOptions options)
        {
            var values = new[] { statement.MaxAffectedRows, options.MaxAffectedRows }
                .Where(v => v > 0)
                .ToArray();
            return values.Length == 0 ? 0 : values.Min();
        }

        private void Audit(
            DatabaseProfile profile,
            NamedStatement? statement,
            string operation,
            int rows,
            double elapsedMs,
            string? error)
        {
            if (_log == null) return;

            var text = $"[数据库] op={operation} profile=\"{profile.Name}\""
                       + (statement == null ? string.Empty : $" statement=\"{statement.Name}\" kind={statement.Kind}")
                       + $" rows={rows} elapsed={elapsedMs:F1}ms user={Environment.UserName}";

            // 错误文案本身以错误类别开头（DbErrorClassifier.Describe），不再单独记枚举
            if (string.IsNullOrEmpty(error)) _log.Info(text);
            else _log.Warn(text + $" error={error}");
        }

        private static DatabaseFileModel CloneModel(DatabaseFileModel model) => new()
        {
            SchemaVersion = model.SchemaVersion,
            Profiles = model.Profiles.Select(p => p.Clone()).ToList()
        };
    }
}
