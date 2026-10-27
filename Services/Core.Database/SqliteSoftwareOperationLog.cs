using System;
using System.Collections.Generic;
using System.IO;
using Core.Interfaces.Operations;
using Microsoft.Data.Sqlite;

namespace Core.Database
{
    /// <summary>
    /// 软件级操作记录的 SQLite 实现。
    ///
    /// 三条设计约束（每一条都对应一种"看起来能用、现场会出事"的写法）：
    /// 1. **fail-open**：任何异常都只记日志，绝不向上抛。审计不能成为新的故障源。
    /// 2. **WAL**：本机磁盘 + 可能被 Web 端并发读。WAL 让"写的同时能读"，
    ///    且不需要读方拿写锁。⚠ 只适用于本地磁盘；方案级库放共享盘时不能照抄。
    /// 3. **保留期复用审计设置**：同一份 `AuditRetentionDays`，避免出现两个"多久清理一次"。
    /// </summary>
    public sealed class SqliteSoftwareOperationLog : ISoftwareOperationLog, IDisposable
    {
        private const string TableName = "software_events";

        private readonly string _databasePath;
        private readonly Func<int> _retentionDays;
        private readonly Action<string>? _warn;
        private readonly object _gate = new();

        private bool _initialized;
        private bool _disabled;
        private string _lastError = string.Empty;

        public SqliteSoftwareOperationLog(
            string databasePath,
            Func<int>? retentionDays = null,
            Action<string>? warn = null)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
                throw new ArgumentException("数据库路径不能为空", nameof(databasePath));

            _databasePath = databasePath;
            _retentionDays = retentionDays ?? (() => 90);
            _warn = warn;
        }

        public string DatabasePath => _disabled ? string.Empty : _databasePath;

        /// <summary>默认落点：程序目录 `Data\operations.db`（与 AppConfig.json / Audit\ 同一约定）。</summary>
        public static string DefaultPath
            => Path.Combine(AppContext.BaseDirectory, "Data", "operations.db");

        public void Record(SoftwareOperationEntry entry)
        {
            if (entry == null || _disabled) return;

            try
            {
                lock (_gate)
                {
                    if (!EnsureInitialized()) return;

                    using var connection = OpenConnection();
                    using var command = connection.CreateCommand();
                    command.CommandText =
                        "INSERT INTO " + TableName + " (" +
                        "ts_utc, machine, app_version, actor, actor_role, category, action, outcome, " +
                        "subject, subject_id, solution_id, solution_name, detail" +
                        ") VALUES (" +
                        "$ts, $machine, $version, $actor, $role, $category, $action, $outcome, " +
                        "$subject, $subjectId, $solutionId, $solutionName, $detail);";

                    command.Parameters.AddWithValue("$ts", entry.TimestampUtc.ToString("O"));
                    command.Parameters.AddWithValue("$machine", entry.Machine ?? string.Empty);
                    command.Parameters.AddWithValue("$version", entry.AppVersion ?? string.Empty);
                    command.Parameters.AddWithValue("$actor", entry.Actor ?? string.Empty);
                    command.Parameters.AddWithValue("$role", entry.ActorRole ?? string.Empty);
                    command.Parameters.AddWithValue("$category", entry.Category ?? string.Empty);
                    command.Parameters.AddWithValue("$action", entry.Action ?? string.Empty);
                    command.Parameters.AddWithValue("$outcome", entry.Outcome ?? string.Empty);
                    command.Parameters.AddWithValue("$subject", entry.Subject ?? string.Empty);
                    command.Parameters.AddWithValue("$subjectId", entry.SubjectId ?? string.Empty);
                    command.Parameters.AddWithValue("$solutionId",
                        entry.SolutionId.HasValue ? entry.SolutionId.Value.ToString() : (object)DBNull.Value);
                    command.Parameters.AddWithValue("$solutionName", entry.SolutionName ?? string.Empty);
                    command.Parameters.AddWithValue("$detail", entry.Detail ?? string.Empty);
                    command.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                // fail-open：只记一次同类错误，避免每条都刷屏
                var message = ex.GetType().Name + ": " + ex.Message;
                if (!string.Equals(message, _lastError, StringComparison.Ordinal))
                {
                    _lastError = message;
                    _warn?.Invoke("[软件操作记录] 写入失败（已忽略，不影响业务）：" + message);
                }
            }
        }

        /// <summary>按条件查询（断言与后续界面共用）。失败返回空表，不抛。</summary>
        public IReadOnlyList<SoftwareOperationEntry> Query(
            string? category = null,
            string? actor = null,
            Guid? solutionId = null,
            int limit = 200)
        {
            var result = new List<SoftwareOperationEntry>();
            if (_disabled) return result;

            try
            {
                lock (_gate)
                {
                    if (!EnsureInitialized()) return result;

                    using var connection = OpenConnection();
                    using var command = connection.CreateCommand();
                    command.CommandText =
                        "SELECT ts_utc, machine, app_version, actor, actor_role, category, action, outcome, " +
                        "subject, subject_id, solution_id, solution_name, detail FROM " + TableName +
                        " WHERE ($category IS NULL OR category = $category)" +
                        "   AND ($actor IS NULL OR actor = $actor)" +
                        "   AND ($solutionId IS NULL OR solution_id = $solutionId)" +
                        " ORDER BY id DESC LIMIT $limit;";

                    command.Parameters.AddWithValue("$category", (object?)category ?? DBNull.Value);
                    command.Parameters.AddWithValue("$actor", (object?)actor ?? DBNull.Value);
                    command.Parameters.AddWithValue("$solutionId",
                        solutionId.HasValue ? solutionId.Value.ToString() : (object)DBNull.Value);
                    command.Parameters.AddWithValue("$limit", limit);

                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        result.Add(new SoftwareOperationEntry
                        {
                            TimestampUtc = DateTime.TryParse(reader.GetString(0), null,
                                System.Globalization.DateTimeStyles.RoundtripKind, out var ts)
                                ? ts
                                : DateTime.UtcNow,
                            Machine = reader.GetString(1),
                            AppVersion = reader.GetString(2),
                            Actor = reader.GetString(3),
                            ActorRole = reader.GetString(4),
                            Category = reader.GetString(5),
                            Action = reader.GetString(6),
                            Outcome = reader.GetString(7),
                            Subject = reader.GetString(8),
                            SubjectId = reader.GetString(9),
                            SolutionId = reader.IsDBNull(10) ? null : Guid.Parse(reader.GetString(10)),
                            SolutionName = reader.GetString(11),
                            Detail = reader.GetString(12),
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _warn?.Invoke("[软件操作记录] 查询失败：" + ex.Message);
            }

            return result;
        }

        /// <summary>
        /// 分页查询（操作记录弹窗用）：时间范围 + 关键字 + offset/limit，并回传总条数。
        /// 关键字匹配 对象 / 动作 / 明细。
        /// </summary>
        public IReadOnlyList<SoftwareOperationEntry> QueryPage(
            string? category,
            string? actor,
            Guid? solutionId,
            DateTime? fromUtc,
            DateTime? toUtc,
            string? keyword,
            int offset,
            int limit,
            out int total)
        {
            total = 0;
            var result = new List<SoftwareOperationEntry>();
            if (_disabled) return result;

            try
            {
                lock (_gate)
                {
                    if (!EnsureInitialized()) return result;

                    using var connection = OpenConnection();
                    var where =
                        " WHERE ($category IS NULL OR category = $category)" +
                        "   AND ($actor IS NULL OR actor = $actor)" +
                        "   AND ($solutionId IS NULL OR solution_id = $solutionId)" +
                        "   AND ($from IS NULL OR ts_utc >= $from)" +
                        "   AND ($to IS NULL OR ts_utc <= $to)" +
                        "   AND ($kw IS NULL OR subject LIKE $kw OR action LIKE $kw OR detail LIKE $kw)";

                    using (var count = connection.CreateCommand())
                    {
                        count.CommandText = "SELECT COUNT(*) FROM " + TableName + where + ";";
                        AddFilters(count, category, actor, solutionId, fromUtc, toUtc, keyword);
                        total = Convert.ToInt32(count.ExecuteScalar() ?? 0);
                    }

                    using var command = connection.CreateCommand();
                    command.CommandText =
                        "SELECT ts_utc, machine, app_version, actor, actor_role, category, action, outcome, " +
                        "subject, subject_id, solution_id, solution_name, detail FROM " + TableName + where +
                        " ORDER BY ts_utc DESC, id DESC LIMIT $limit OFFSET $offset;";
                    AddFilters(command, category, actor, solutionId, fromUtc, toUtc, keyword);
                    command.Parameters.AddWithValue("$limit", Math.Max(1, limit));
                    command.Parameters.AddWithValue("$offset", Math.Max(0, offset));

                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        result.Add(new SoftwareOperationEntry
                        {
                            TimestampUtc = DateTime.TryParse(reader.GetString(0), null,
                                System.Globalization.DateTimeStyles.RoundtripKind, out var ts)
                                ? ts
                                : DateTime.UtcNow,
                            Machine = reader.GetString(1),
                            AppVersion = reader.GetString(2),
                            Actor = reader.GetString(3),
                            ActorRole = reader.GetString(4),
                            Category = reader.GetString(5),
                            Action = reader.GetString(6),
                            Outcome = reader.GetString(7),
                            Subject = reader.GetString(8),
                            SubjectId = reader.GetString(9),
                            SolutionId = reader.IsDBNull(10) ? null : Guid.Parse(reader.GetString(10)),
                            SolutionName = reader.GetString(11),
                            Detail = reader.GetString(12),
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _warn?.Invoke("[软件操作记录] 分页查询失败：" + ex.Message);
            }

            return result;
        }

        /// <summary>
        /// 列出库里出现过的操作者，按出现次数降序（筛选下拉的候选）。
        /// 空操作者（未登录/系统动作）不入列。同为 fail-open：失败返回空表，不抛。
        /// </summary>
        public IReadOnlyList<string> QueryActors(int limit = 200)
        {
            var result = new List<string>();
            if (_disabled) return result;

            try
            {
                lock (_gate)
                {
                    if (!EnsureInitialized()) return result;

                    using var connection = OpenConnection();
                    using var command = connection.CreateCommand();
                    command.CommandText =
                        "SELECT actor, COUNT(*) AS n FROM " + TableName +
                        " WHERE actor <> '' GROUP BY actor ORDER BY n DESC, actor ASC LIMIT $limit;";
                    command.Parameters.AddWithValue("$limit", Math.Max(1, limit));

                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                        result.Add(reader.GetString(0));
                }
            }
            catch (Exception ex)
            {
                _warn?.Invoke("[软件操作记录] 操作者列表查询失败：" + ex.Message);
            }

            return result;
        }

        private static void AddFilters(
            SqliteCommand command, string? category, string? actor, Guid? solutionId,
            DateTime? fromUtc, DateTime? toUtc, string? keyword)
        {
            command.Parameters.AddWithValue("$category", (object?)category ?? DBNull.Value);
            command.Parameters.AddWithValue("$actor", (object?)actor ?? DBNull.Value);
            command.Parameters.AddWithValue("$solutionId",
                solutionId.HasValue ? solutionId.Value.ToString() : (object)DBNull.Value);
            command.Parameters.AddWithValue("$from", fromUtc?.ToString("O") ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$to", toUtc?.ToString("O") ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$kw",
                string.IsNullOrWhiteSpace(keyword) ? (object)DBNull.Value : "%" + keyword.Trim() + "%");
        }

        /// <summary>按保留期清理；返回删除行数。失败返回 0，不抛。</summary>
        public int PruneExpired()
        {
            if (_disabled) return 0;

            try
            {
                lock (_gate)
                {
                    if (!EnsureInitialized()) return 0;

                    var days = Math.Max(1, _retentionDays());
                    var cutoff = DateTime.UtcNow.AddDays(-days).ToString("O");

                    using var connection = OpenConnection();
                    using var command = connection.CreateCommand();
                    command.CommandText = "DELETE FROM " + TableName + " WHERE ts_utc < $cutoff;";
                    command.Parameters.AddWithValue("$cutoff", cutoff);
                    return command.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                _warn?.Invoke("[软件操作记录] 清理失败：" + ex.Message);
                return 0;
            }
        }

        private bool EnsureInitialized()
        {
            if (_initialized) return true;
            if (_disabled) return false;

            try
            {
                var folder = Path.GetDirectoryName(_databasePath);
                if (!string.IsNullOrWhiteSpace(folder)) Directory.CreateDirectory(folder!);

                using var connection = OpenConnection();

                using (var pragma = connection.CreateCommand())
                {
                    // WAL：写不挡读（Web 端可能同时在读）。只适用于本地磁盘。
                    pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
                    pragma.ExecuteNonQuery();
                }

                using (var create = connection.CreateCommand())
                {
                    create.CommandText =
                        "CREATE TABLE IF NOT EXISTS " + TableName + " (" +
                        "id INTEGER PRIMARY KEY AUTOINCREMENT," +
                        "ts_utc TEXT NOT NULL," +
                        "machine TEXT NOT NULL DEFAULT ''," +
                        "app_version TEXT NOT NULL DEFAULT ''," +
                        "actor TEXT NOT NULL DEFAULT ''," +
                        "actor_role TEXT NOT NULL DEFAULT ''," +
                        "category TEXT NOT NULL DEFAULT ''," +
                        "action TEXT NOT NULL DEFAULT ''," +
                        "outcome TEXT NOT NULL DEFAULT ''," +
                        "subject TEXT NOT NULL DEFAULT ''," +
                        "subject_id TEXT NOT NULL DEFAULT ''," +
                        "solution_id TEXT NULL," +
                        "solution_name TEXT NOT NULL DEFAULT ''," +
                        "detail TEXT NOT NULL DEFAULT ''" +
                        ");" +
                        "CREATE INDEX IF NOT EXISTS ix_software_events_category_ts ON " + TableName + "(category, ts_utc);" +
                        "CREATE INDEX IF NOT EXISTS ix_software_events_actor_ts ON " + TableName + "(actor, ts_utc);" +
                        "CREATE INDEX IF NOT EXISTS ix_software_events_solution_ts ON " + TableName + "(solution_id, ts_utc);";
                    create.ExecuteNonQuery();
                }

                _initialized = true;
                PruneExpired();
                return true;
            }
            catch (Exception ex)
            {
                // 建库失败就整体停用：之后每次调用都直接返回，不再反复撞同一个错。
                _disabled = true;
                _warn?.Invoke("[软件操作记录] 初始化失败，本次运行不再记录：" + ex.Message);
                return false;
            }
        }

        private SqliteConnection OpenConnection()
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = _databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                // 两个进程（软件 + 断言宿主）同时开库时不要立刻抛：等 5 秒
                DefaultTimeout = 5,
            };

            var connection = new SqliteConnection(builder.ToString());
            connection.Open();
            return connection;
        }

        public void Dispose()
        {
            // 连接都是 using 局部变量，没有长连接要关；这里只标记停用。
            _disabled = true;
        }
    }
}
