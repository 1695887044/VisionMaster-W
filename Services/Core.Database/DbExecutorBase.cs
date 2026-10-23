using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using Core.Interfaces;
using Core.Interfaces.Database;

namespace Core.Database
{
    /// <summary>
    /// 数据库执行器底板：连接生命周期、参数绑定、事务、行数/影响行数上限、取消、测试回滚。
    ///
    /// Provider 只负责四件事：怎么建连接、开会话要设什么、版本查询是什么、怎么清池。
    /// 其余（参数规则、事务边界、截断、上限、错误文案）只有一份——
    /// 两份实现各写一遍，SQLite 与 MySQL 的行为迟早分叉。
    ///
    /// 【一次调用 = 一个事务】写路径显式 BeginTransaction；查询是单语句（数据库自身原子）。
    /// 测试路径在同一事务里执行后 ROLLBACK。
    /// </summary>
    internal abstract class DbExecutorBase
    {
        protected DbExecutorBase(ILogService? log) => Log = log;

        protected ILogService? Log { get; }

        public abstract DbProviderKind Provider { get; }

        /// <summary>退出时释放连接池（宿主模块的退出任务调用）。</summary>
        public abstract void ClearPools();

        protected abstract DbConnection CreateConnection(DatabaseProfile profile, int timeoutSeconds);

        protected abstract void ConfigureSession(DbConnection connection, int timeoutSeconds);

        protected abstract string VersionQuery { get; }

        /// <summary>Provider 自己的"命令被取消"判据（SQLite 是错误码 9，MySQL 是 1317）。</summary>
        protected virtual bool IsCancellation(Exception ex, CancellationToken token) => false;

        // ---------------------------------------------------------------- 连接

        internal bool TryOpen(DatabaseProfile profile, int timeoutSeconds, out DbConnection connection, out string? error)
        {
            connection = null!;
            error = null;

            try
            {
                var conn = CreateConnection(profile, Math.Max(1, timeoutSeconds));
                conn.Open();
                ConfigureSession(conn, Math.Max(1, timeoutSeconds));
                connection = conn;
                return true;
            }
            catch (Exception ex)
            {
                var kind = DbErrorClassifier.Classify(ex);
                error = DbErrorClassifier.Describe(ex, kind);
                Log?.Warn($"[数据库] 打开失败 provider={Provider} profile={profile.Name} "
                          + $"target={profile.TargetText}：{error}");
                return false;
            }
        }

        // ---------------------------------------------------------------- 测试连接

        public bool TryTest(DatabaseProfile profile, out string? versionText, out double elapsedMs, out string? error)
        {
            versionText = null;
            elapsedMs = 0;
            error = null;

            var sw = Stopwatch.StartNew();
            if (!TryOpen(profile, profile.TimeoutSeconds, out var conn, out error))
                return false;

            using (conn)
            {
                try
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = VersionQuery;
                    cmd.CommandTimeout = Math.Max(1, profile.TimeoutSeconds);
                    versionText = Convert.ToString(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
                    elapsedMs = sw.Elapsed.TotalMilliseconds;
                    return true;
                }
                catch (Exception ex)
                {
                    var kind = DbErrorClassifier.Classify(ex);
                    error = DbErrorClassifier.Describe(ex, kind);
                    elapsedMs = sw.Elapsed.TotalMilliseconds;
                    return false;
                }
            }
        }

        // ---------------------------------------------------------------- 查询

        public bool TryQuery(
            DatabaseProfile profile,
            NamedStatement statement,
            IReadOnlyList<DbParameterValue> parameters,
            int timeoutSeconds,
            int maxRows,
            CancellationToken cancellationToken,
            out DbQueryResult result,
            out string? error)
        {
            result = null!;
            error = null;
            cancellationToken.ThrowIfCancellationRequested();

            if (!TryOpen(profile, timeoutSeconds, out var conn, out error))
                return false;

            using (conn)
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = statement.Sql;
                    cmd.CommandTimeout = Math.Max(1, timeoutSeconds);

                    if (!BindParameters(cmd, statement, parameters, out error))
                        return false;

                    using var registration = RegisterCancel(cmd, cancellationToken);
                    using var reader = cmd.ExecuteReader();

                    result = ReadRows(reader, maxRows, sw);
                    return true;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (IsCancellation(ex, cancellationToken))
                {
                    throw new OperationCanceledException(cancellationToken);
                }
                catch (Exception ex)
                {
                    var kind = DbErrorClassifier.Classify(ex);
                    error = DbErrorClassifier.Describe(ex, kind);
                    return false;
                }
            }
        }

        // ---------------------------------------------------------------- 执行

        public bool TryExecute(
            DatabaseProfile profile,
            NamedStatement statement,
            IReadOnlyList<DbParameterValue> parameters,
            int timeoutSeconds,
            int maxAffectedRows,
            CancellationToken cancellationToken,
            out DbExecutionResult result,
            out string? error)
        {
            result = null!;
            error = null;
            cancellationToken.ThrowIfCancellationRequested();

            if (!TryOpen(profile, timeoutSeconds, out var conn, out error))
                return false;

            using (conn)
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    using var tx = conn.BeginTransaction();

                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = statement.Sql;
                    cmd.CommandTimeout = Math.Max(1, timeoutSeconds);
                    cmd.Transaction = tx;

                    if (!BindParameters(cmd, statement, parameters, out error))
                    {
                        tx.Rollback();
                        return false;
                    }

                    using var registration = RegisterCancel(cmd, cancellationToken);
                    var affected = cmd.ExecuteNonQuery();

                    if (maxAffectedRows > 0 && affected > maxAffectedRows)
                    {
                        tx.Rollback();
                        error = $"影响行数 {affected} 超过上限 {maxAffectedRows}，已回滚（未写入任何数据）";
                        return false;
                    }

                    tx.Commit();
                    result = new DbExecutionResult
                    {
                        AffectedRows = affected,
                        ElapsedMs = sw.Elapsed.TotalMilliseconds
                    };
                    return true;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (IsCancellation(ex, cancellationToken))
                {
                    throw new OperationCanceledException(cancellationToken);
                }
                catch (Exception ex)
                {
                    var kind = DbErrorClassifier.Classify(ex);
                    error = DbErrorClassifier.Describe(ex, kind);
                    return false;
                }
            }
        }

        // ---------------------------------------------------------------- 批量

        public bool TryExecuteBatch(
            DatabaseProfile profile,
            NamedStatement statement,
            IReadOnlyList<IReadOnlyList<DbParameterValue>> rows,
            int timeoutSeconds,
            int maxAffectedRows,
            CancellationToken cancellationToken,
            out DbBatchResult result,
            out string? error)
        {
            result = null!;
            error = null;
            cancellationToken.ThrowIfCancellationRequested();

            if (!TryOpen(profile, timeoutSeconds, out var conn, out error))
                return false;

            using (conn)
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    using var tx = conn.BeginTransaction();
                    var total = 0;

                    for (var i = 0; i < rows.Count; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        using var cmd = conn.CreateCommand();
                        cmd.CommandText = statement.Sql;
                        cmd.CommandTimeout = Math.Max(1, timeoutSeconds);
                        cmd.Transaction = tx;

                        if (!BindParameters(cmd, statement, rows[i], out var bindError))
                        {
                            tx.Rollback();
                            error = $"第 {i + 1} 行参数错误：{bindError}";
                            result = new DbBatchResult { SucceededRows = 0, FailedRowIndex = i, ElapsedMs = sw.Elapsed.TotalMilliseconds };
                            return false;
                        }

                        using var registration = RegisterCancel(cmd, cancellationToken);
                        total += cmd.ExecuteNonQuery();

                        if (maxAffectedRows > 0 && total > maxAffectedRows)
                        {
                            tx.Rollback();
                            error = $"累计影响行数 {total} 超过上限 {maxAffectedRows}，已回滚整批";
                            result = new DbBatchResult { SucceededRows = 0, FailedRowIndex = i, ElapsedMs = sw.Elapsed.TotalMilliseconds };
                            return false;
                        }
                    }

                    tx.Commit();
                    result = new DbBatchResult
                    {
                        SucceededRows = rows.Count,
                        ElapsedMs = sw.Elapsed.TotalMilliseconds
                    };
                    return true;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (IsCancellation(ex, cancellationToken))
                {
                    throw new OperationCanceledException(cancellationToken);
                }
                catch (Exception ex)
                {
                    var kind = DbErrorClassifier.Classify(ex);
                    error = DbErrorClassifier.Describe(ex, kind);
                    result = new DbBatchResult { SucceededRows = 0, ElapsedMs = sw.Elapsed.TotalMilliseconds };
                    return false;
                }
            }
        }

        // ---------------------------------------------------------------- 测试（事务内执行后回滚）

        public bool TryTestStatement(
            DatabaseProfile profile,
            NamedStatement statement,
            IReadOnlyList<DbParameterValue> parameters,
            int timeoutSeconds,
            int maxRows,
            int maxAffectedRows,
            bool commitForReal,
            CancellationToken cancellationToken,
            out DbTestResult result,
            out string? error)
        {
            result = null!;
            error = null;
            cancellationToken.ThrowIfCancellationRequested();

            if (!TryOpen(profile, timeoutSeconds, out var conn, out error))
                return false;

            using (conn)
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    using var tx = conn.BeginTransaction();

                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = statement.Sql;
                    cmd.CommandTimeout = Math.Max(1, timeoutSeconds);
                    cmd.Transaction = tx;

                    if (!BindParameters(cmd, statement, parameters, out error))
                    {
                        tx.Rollback();
                        return false;
                    }

                    using var registration = RegisterCancel(cmd, cancellationToken);

                    if (statement.Kind == DbStatementKind.Query)
                    {
                        // reader 必须先关掉再 commit/rollback：事务里还有活动语句时提交会失败。
                        DbQueryResult queryResult;
                        using (var reader = cmd.ExecuteReader())
                            queryResult = ReadRows(reader, maxRows, sw);

                        if (commitForReal) tx.Commit(); else tx.Rollback();

                        result = new DbTestResult
                        {
                            Query = queryResult,
                            RolledBack = !commitForReal,
                            ElapsedMs = sw.Elapsed.TotalMilliseconds
                        };
                        return true;
                    }

                    var affected = cmd.ExecuteNonQuery();

                    if (maxAffectedRows > 0 && affected > maxAffectedRows)
                    {
                        tx.Rollback();
                        error = $"影响行数 {affected} 超过上限 {maxAffectedRows}，已回滚（未写入任何数据）";
                        return false;
                    }

                    if (commitForReal) tx.Commit(); else tx.Rollback();

                    result = new DbTestResult
                    {
                        Execution = new DbExecutionResult
                        {
                            AffectedRows = affected,
                            ElapsedMs = sw.Elapsed.TotalMilliseconds
                        },
                        RolledBack = !commitForReal,
                        ElapsedMs = sw.Elapsed.TotalMilliseconds
                    };
                    return true;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (IsCancellation(ex, cancellationToken))
                {
                    throw new OperationCanceledException(cancellationToken);
                }
                catch (Exception ex)
                {
                    var kind = DbErrorClassifier.Classify(ex);
                    error = DbErrorClassifier.Describe(ex, kind);
                    return false;
                }
            }
        }

        // ---------------------------------------------------------------- 参数

        /// <summary>
        /// 把参数值按语句定义绑到命令上。
        /// 规则：SQL 里出现的占位符必须都有定义；必填参数不能为空（默认值可兜底）；
        /// 多余参数直接报错（宁可拒，不静默忽略——静默忽略会让人以为值生效了）。
        /// </summary>
        public bool BindParameters(
            DbCommand cmd,
            NamedStatement statement,
            IReadOnlyList<DbParameterValue> values,
            out string? error)
        {
            error = null;
            cmd.Parameters.Clear();

            var provided = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (values != null)
            {
                foreach (var v in values)
                {
                    if (v == null || string.IsNullOrWhiteSpace(v.Name)) continue;
                    provided[v.Name.Trim()] = v.Value;
                }
            }

            var defs = statement.Parameters ?? new List<DbStatementParameter>();
            var defined = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var def in defs)
            {
                var name = (def.Name ?? string.Empty).Trim();
                if (name.Length == 0) continue;
                defined.Add(name);

                provided.TryGetValue(name, out var raw);
                var isEmpty = raw is null || (raw is string s && s.Length == 0);

                if (isEmpty && !string.IsNullOrEmpty(def.DefaultValue))
                    raw = def.DefaultValue;

                isEmpty = raw is null || (raw is string s2 && s2.Length == 0);
                if (isEmpty && def.IsRequired)
                {
                    error = $"缺少必填参数 @{name}" + (string.IsNullOrEmpty(def.Description) ? string.Empty : $"（{def.Description}）");
                    return false;
                }

                if (!TryConvert(raw, def.DbType, out var converted, out var convertError))
                {
                    error = $"参数 @{name} {convertError}";
                    return false;
                }

                var p = cmd.CreateParameter();
                p.ParameterName = "@" + name;
                p.Value = converted ?? DBNull.Value;
                p.DbType = MapDbType(def.DbType);
                cmd.Parameters.Add(p);
            }

            var unknown = new List<string>();
            foreach (var key in provided.Keys)
                if (!defined.Contains(key)) unknown.Add("@" + key);

            if (unknown.Count > 0)
            {
                error = "语句没有定义这些参数：" + string.Join("、", unknown) + "（请到数据库页检查参数表）";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 参数类型 → <see cref="DbType"/>。两个 Provider 共用一份：
        /// Microsoft.Data.Sqlite 与 MySqlConnector 都认标准 DbType 映射。
        /// </summary>
        private static DbType MapDbType(string? dbType) => dbType switch
        {
            DbTypeNames.Integer => DbType.Int64,
            DbTypeNames.Boolean => DbType.Boolean,
            DbTypeNames.Real => DbType.Double,
            DbTypeNames.DateTime => DbType.DateTime,
            DbTypeNames.Blob => DbType.Binary,
            _ => DbType.String
        };

        private static bool TryConvert(object? raw, string? dbType, out object? value, out string? error)
        {
            value = null;
            error = null;

            if (raw is null || raw is DBNull)
                return true;

            var text = raw as string;
            switch (dbType)
            {
                case DbTypeNames.Integer:
                    if (raw is bool) { error = "期望整数，实际是布尔值"; return false; }
                    if (raw is int or long or short or byte or sbyte or uint or ulong)
                    {
                        value = Convert.ToInt64(raw, CultureInfo.InvariantCulture);
                        return true;
                    }
                    if (text != null && long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
                    {
                        value = l;
                        return true;
                    }
                    error = $"期望整数，实际是 '{raw}'";
                    return false;

                case DbTypeNames.Real:
                    if (raw is double or float or decimal)
                    {
                        value = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                        return true;
                    }
                    if (raw is int or long)
                    {
                        value = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                        return true;
                    }
                    // NumberStyles.Float 不含 AllowThousands：拒绝 "1,234" 这种带千分位的文本
                    if (text != null && double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                    {
                        value = d;
                        return true;
                    }
                    error = $"期望数值，实际是 '{raw}'";
                    return false;

                case DbTypeNames.Boolean:
                    if (raw is bool b) { value = b; return true; }
                    if (text != null)
                    {
                        var t = text.Trim();
                        if (string.Equals(t, "true", StringComparison.OrdinalIgnoreCase) || t == "1")
                        {
                            value = true;
                            return true;
                        }
                        if (string.Equals(t, "false", StringComparison.OrdinalIgnoreCase) || t == "0")
                        {
                            value = false;
                            return true;
                        }
                    }
                    if (raw is int or long)
                    {
                        value = Convert.ToInt64(raw, CultureInfo.InvariantCulture) != 0;
                        return true;
                    }
                    error = $"期望布尔值（true/false/1/0），实际是 '{raw}'";
                    return false;

                case DbTypeNames.DateTime:
                    if (raw is DateTime dt) { value = dt; return true; }
                    if (text != null && DateTime.TryParse(text.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                    {
                        value = parsed;
                        return true;
                    }
                    error = $"期望日期时间，实际是 '{raw}'";
                    return false;

                case DbTypeNames.Blob:
                    if (raw is byte[] bytes) { value = bytes; return true; }
                    if (text != null)
                    {
                        try
                        {
                            value = Convert.FromBase64String(text.Trim());
                            return true;
                        }
                        catch (FormatException)
                        {
                            error = "期望 Base64 文本或字节数组";
                            return false;
                        }
                    }
                    error = $"期望字节数组，实际是 {raw.GetType().Name}";
                    return false;

                default: // Text
                    value = raw as string ?? Convert.ToString(raw, CultureInfo.InvariantCulture);
                    return true;
            }
        }

        // ---------------------------------------------------------------- 读行 / 取消

        private static DbQueryResult ReadRows(DbDataReader reader, int maxRows, Stopwatch sw)
        {
            var columns = new string[reader.FieldCount];
            for (var i = 0; i < columns.Length; i++) columns[i] = reader.GetName(i);

            var rows = new List<object?[]>();
            var truncated = false;
            while (reader.Read())
            {
                if (rows.Count >= maxRows)
                {
                    truncated = true;
                    break;
                }

                var row = new object?[columns.Length];
                for (var i = 0; i < columns.Length; i++)
                {
                    var value = reader.GetValue(i);
                    row[i] = value is DBNull ? null : value;
                }
                rows.Add(row);
            }

            return new DbQueryResult
            {
                Columns = columns,
                Rows = rows,
                RowCount = rows.Count,
                Truncated = truncated,
                ElapsedMs = sw.Elapsed.TotalMilliseconds
            };
        }

        private static CancellationTokenRegistration RegisterCancel(DbCommand cmd, CancellationToken token)
        {
            if (!token.CanBeCanceled) return default;

            return token.Register(static state =>
            {
                try { ((DbCommand)state!).Cancel(); }
                catch { /* 取消失败不改变主路径；命令自己会在下一拍返回 */ }
            }, cmd);
        }
    }
}
