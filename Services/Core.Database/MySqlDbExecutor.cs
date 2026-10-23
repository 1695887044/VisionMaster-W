using System;
using System.Data.Common;
using System.Threading;
using Core.Interfaces;
using Core.Interfaces.Database;
using MySqlConnector;

namespace Core.Database
{
    /// <summary>
    /// MySQL / MariaDB Provider（同一协议，同一个实现）。
    ///
    /// 【只读档的边界】MySQL 没有"连接级只读"开关：`SET SESSION TRANSACTION READ ONLY`
    /// 只影响显式事务，autocommit 语句照样能写。所以只读档在这里靠两道应用层闸：
    /// ① 保存期/执行期语句类型白名单（只允许 Query）；
    /// ② 建议现场给该账号只授 SELECT 权限——数据库侧的 GRANT 才是硬保证，文档里写清。
    ///
    /// 【事务】写路径依赖 InnoDB。MyISAM 表不支持事务，回滚不会生效——这一点也写进帮助页。
    /// </summary>
    internal sealed class MySqlDbExecutor : DbExecutorBase
    {
        public MySqlDbExecutor(ILogService? log) : base(log) { }

        public override DbProviderKind Provider => DbProviderKind.MySql;

        protected override string VersionQuery => "SELECT VERSION()";

        protected override DbConnection CreateConnection(DatabaseProfile profile, int timeoutSeconds)
        {
            if (string.IsNullOrWhiteSpace(profile.Server))
                throw new InvalidOperationException("MySQL 服务器地址为空");
            if (string.IsNullOrWhiteSpace(profile.DatabaseName))
                throw new InvalidOperationException("MySQL 数据库名为空");
            if (string.IsNullOrWhiteSpace(profile.UserId))
                throw new InvalidOperationException("MySQL 用户名为空");

            var password = DbCredentialProtector.Unprotect(profile.PasswordCipher);
            return new MySqlConnection(BuildConnectionString(profile, password, timeoutSeconds));
        }

        /// <summary>连接串构造（内部可见：断言宿主直接核对字段映射，不连库）。</summary>
        internal static string BuildConnectionString(DatabaseProfile profile, string password, int timeoutSeconds)
        {
            var builder = new MySqlConnectionStringBuilder
            {
                Server = (profile.Server ?? string.Empty).Trim(),
                Port = (uint)(profile.Port > 0 ? profile.Port : 3306),
                Database = (profile.DatabaseName ?? string.Empty).Trim(),
                UserID = (profile.UserId ?? string.Empty).Trim(),
                Password = password ?? string.Empty,
                ConnectionTimeout = (uint)Math.Max(1, timeoutSeconds),
                DefaultCommandTimeout = (uint)Math.Max(1, timeoutSeconds),
                Pooling = true,
                // 默认 Preferred：服务端支持 TLS 就加密，不支持也不强行失败（现场常见自签/未开 TLS）
                SslMode = MySqlSslMode.Preferred,
                // 关掉用户变量：@name 一律按参数绑定，避免"忘了定义参数却当变量用"的静默行为
                AllowUserVariables = false
            };

            return builder.ToString();
        }

        protected override void ConfigureSession(DbConnection connection, int timeoutSeconds)
        {
            // MySQL 没有 busy_timeout；锁等待由 innodb_lock_wait_timeout 控制（服务端默认 50s）。
            // 显式压到与语句超时一致，避免"命令超时 30s、锁等待 50s"这种自相矛盾的等待。
            try
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SET SESSION innodb_lock_wait_timeout = " + Math.Max(1, Math.Min(timeoutSeconds, 300));
                cmd.ExecuteNonQuery();
            }
            catch
            {
                // 非 InnoDB / 账号无权限时忽略：会话设置失败不影响主路径
            }
        }

        protected override bool IsCancellation(Exception ex, CancellationToken token) =>
            ex is MySqlException { Number: 1317 } && token.IsCancellationRequested;

        public override void ClearPools()
        {
            try { MySqlConnection.ClearAllPools(); }
            catch { /* 退出链上的清理失败不该让进程退不出去 */ }
        }
    }
}
