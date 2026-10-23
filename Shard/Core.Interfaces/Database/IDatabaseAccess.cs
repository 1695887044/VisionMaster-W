using System;
using System.Collections.Generic;

namespace Core.Interfaces.Database
{
    /// <summary>
    /// 数据库运行期能力面（插件唯一能看到的数据库契约）。
    ///
    /// 【为什么挂在 IExecutionContext 上】插件只引用 Core.Interfaces，物理上够不到宿主的
    /// DatabaseService / Provider；与 Cameras / Motions / Comms 同一范式，由宿主实现并递送。
    ///
    /// 【边界（架构硬闸，不要在实现里放宽）】
    ///   · 不暴露 DbConnection / DbTransaction / DbDataReader；连接与事务由宿主释放；
    ///   · 一次调用 = 一个事务；跨步骤长事务、游标、多结果集一律没有；
    ///   · 只允许"命名语句 + 类型化参数"，没有内联 SQL 入口，没有字符串拼接入口；
    ///   · 每次调用必须有超时、CancellationToken、行数/影响行数上限；
    ///   · 失败返回 false + 可直接上屏的中文原因，禁止静默成功。
    ///
    /// 【失败语义由调用方决定】写步骤默认 FailFast=true（抛异常停线），读步骤默认只置失败。
    /// 服务层不做"要不要停线"的判断，只如实返回结果。
    /// </summary>
    public interface IDatabaseAccess
    {
        // ---- 元数据（纯本地查表，不连库） ----

        /// <summary>全部 profile（按显示名排序）。没有配置时返回空集合，不返回 null。</summary>
        IReadOnlyList<DbProfileInfo> ListProfiles();

        /// <summary>按稳定身份取 profile；不存在时返回 false + 原因。</summary>
        bool TryGetProfile(Guid profileId, out DbProfileInfo info, out string? error);

        /// <summary>某个 profile 下的语句（按显示名排序）。profile 不存在时返回空集合。</summary>
        IReadOnlyList<DbStatementInfo> ListStatements(Guid profileId);

        /// <summary>按稳定身份取语句（StatementId 全局唯一）；不存在时返回 false + 原因。</summary>
        bool TryGetStatement(Guid statementId, out DbStatementInfo info, out string? error);

        // ---- 连接 ----

        /// <summary>
        /// 测试连接：打开库并执行 <c>SELECT sqlite_version()</c>。
        /// 成功时 <paramref name="versionText"/> 是数据库版本，<paramref name="elapsedMs"/> 是往返耗时。
        /// </summary>
        bool TryTest(Guid profileId, out string? versionText, out double elapsedMs, out string? error);

        // ---- 执行（一次调用 = 一个事务） ----

        /// <summary>
        /// 执行查询语句。0 行是成功（<c>RowCount=0</c>），不是失败。
        /// 超出 MaxRows 时 <see cref="DbQueryResult.Truncated"/>=true，只返回前 MaxRows 行。
        /// </summary>
        bool TryQuery(
            Guid statementId,
            IReadOnlyList<DbParameterValue> parameters,
            DbCommandOptions options,
            out DbQueryResult result,
            out string? error);

        /// <summary>
        /// 执行写语句（UPDATE / DELETE / INSERT）。返回实际影响行数。
        /// 超过 MaxAffectedRows 时在事务内回滚并返回失败（宁可不写，也不误删）。
        /// </summary>
        bool TryExecute(
            Guid statementId,
            IReadOnlyList<DbParameterValue> parameters,
            DbCommandOptions options,
            out DbExecutionResult result,
            out string? error);

        /// <summary>
        /// 同一语句、多组参数、一个事务：全有或全无。
        /// 任一行失败即整批回滚，<see cref="DbBatchResult.FailedRowIndex"/> 指出失败行（从 0 开始）。
        /// </summary>
        bool TryExecuteBatch(
            Guid statementId,
            IReadOnlyList<IReadOnlyList<DbParameterValue>> rows,
            DbCommandOptions options,
            out DbBatchResult result,
            out string? error);

        // ---- 测试 ----

        /// <summary>
        /// 测试语句（宿主页与插件窗口共用这一条，禁止各自再写一套事务逻辑）。
        /// <paramref name="commitForReal"/>=false 时在事务内执行后 ROLLBACK（默认）；
        /// true 才真写，调用方必须已做二次确认。
        /// </summary>
        bool TryTestStatement(
            Guid statementId,
            IReadOnlyList<DbParameterValue> parameters,
            DbCommandOptions options,
            bool commitForReal,
            out DbTestResult result,
            out string? error);
    }

    /// <summary>
    /// <see cref="IDatabaseAccess"/> 的空实现：没有接入数据库服务时使用。
    /// 与 NullCameraProvider / NullMotionProvider / NullCommVariableAccess 同一口径——
    /// 调用方只判返回值，不必为"能力可能不存在"再写一层判空。
    /// </summary>
    public sealed class NullDatabaseAccess : IDatabaseAccess
    {
        public static readonly NullDatabaseAccess Instance = new();

        public const string NotAvailable = "当前执行环境没有接入数据库服务，无法执行数据库步骤";

        public IReadOnlyList<DbProfileInfo> ListProfiles() => Array.Empty<DbProfileInfo>();

        public bool TryGetProfile(Guid profileId, out DbProfileInfo info, out string? error)
        {
            info = null!;
            error = NotAvailable;
            return false;
        }

        public IReadOnlyList<DbStatementInfo> ListStatements(Guid profileId) => Array.Empty<DbStatementInfo>();

        public bool TryGetStatement(Guid statementId, out DbStatementInfo info, out string? error)
        {
            info = null!;
            error = NotAvailable;
            return false;
        }

        public bool TryTest(Guid profileId, out string? versionText, out double elapsedMs, out string? error)
        {
            versionText = null;
            elapsedMs = 0;
            error = NotAvailable;
            return false;
        }

        public bool TryQuery(
            Guid statementId,
            IReadOnlyList<DbParameterValue> parameters,
            DbCommandOptions options,
            out DbQueryResult result,
            out string? error)
        {
            result = null!;
            error = NotAvailable;
            return false;
        }

        public bool TryExecute(
            Guid statementId,
            IReadOnlyList<DbParameterValue> parameters,
            DbCommandOptions options,
            out DbExecutionResult result,
            out string? error)
        {
            result = null!;
            error = NotAvailable;
            return false;
        }

        public bool TryExecuteBatch(
            Guid statementId,
            IReadOnlyList<IReadOnlyList<DbParameterValue>> rows,
            DbCommandOptions options,
            out DbBatchResult result,
            out string? error)
        {
            result = null!;
            error = NotAvailable;
            return false;
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
            error = NotAvailable;
            return false;
        }
    }
}
