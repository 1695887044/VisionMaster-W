using System;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Threading;
using Core.Interfaces;
using Core.Interfaces.Database;
using Microsoft.Data.Sqlite;

namespace Core.Database
{
    /// <summary>
    /// SQLite Provider：文件库、连接串带打开方式、连接级 busy_timeout。
    ///
    /// 只读档在连接串层强制（Mode=ReadOnly）——这是 SQLite 能给到的硬保证；
    /// 语句类型白名单（StatementValidator）是第二道闸。
    /// </summary>
    internal sealed class SqliteDbExecutor : DbExecutorBase
    {
        public SqliteDbExecutor(ILogService? log) : base(log) { }

        public override DbProviderKind Provider => DbProviderKind.Sqlite;

        protected override string VersionQuery => "SELECT sqlite_version()";

        protected override DbConnection CreateConnection(DatabaseProfile profile, int timeoutSeconds)
        {
            if (string.IsNullOrWhiteSpace(profile.FilePath))
                throw new InvalidOperationException("数据库文件路径为空");

            var readOnly = profile.OpenMode == DbOpenMode.ReadOnly;
            if (readOnly && !File.Exists(profile.FilePath))
                throw new InvalidOperationException($"数据库文件不存在：{profile.FilePath}");

            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = profile.FilePath,
                Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
                Pooling = true,
                DefaultTimeout = Math.Max(1, timeoutSeconds)
            };

            return new SqliteConnection(builder.ToString());
        }

        protected override void ConfigureSession(DbConnection connection, int timeoutSeconds)
        {
            // 连接级忙等：别的进程正在写时等一会儿，而不是立刻 BUSY。
            // 只设 busy_timeout，不改 journal_mode —— 那是数据库文件的持久属性，不能替用户改。
            using var pragma = connection.CreateCommand();
            pragma.CommandText = "PRAGMA busy_timeout = " + (Math.Max(1, timeoutSeconds) * 1000).ToString(CultureInfo.InvariantCulture);
            pragma.ExecuteNonQuery();
        }

        protected override bool IsCancellation(Exception ex, CancellationToken token) =>
            ex is SqliteException { SqliteErrorCode: 9 } && token.IsCancellationRequested;

        public override void ClearPools()
        {
            try { SqliteConnection.ClearAllPools(); }
            catch { /* 退出链上的清理失败不该让进程退不出去 */ }
        }
    }
}
