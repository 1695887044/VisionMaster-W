using System;
using System.Collections.Generic;
using Core.Interfaces.Database;

namespace Core.Database
{
    /// <summary>
    /// 持久化模型（databases.json 的形状）。
    ///
    /// 与 <see cref="DbProfileInfo"/> / <see cref="DbStatementInfo"/> 的分工：
    /// 这里可写、带时间戳与 SQL 全文，只给宿主页与服务用；
    /// 递给插件的永远是只读快照（DbProfileInfo / DbStatementInfo）。
    /// </summary>
    public sealed class DatabaseFileModel
    {
        public int SchemaVersion { get; set; } = 1;

        public List<DatabaseProfile> Profiles { get; set; } = new();
    }

    public sealed class DatabaseProfile
    {
        public Guid ProfileId { get; set; } = Guid.NewGuid();

        /// <summary>显示名（可改；身份是 ProfileId）。</summary>
        public string Name { get; set; } = string.Empty;

        public DbProviderKind Provider { get; set; } = DbProviderKind.Sqlite;

        /// <summary>SQLite 文件绝对路径。MySQL 配置下为空。</summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>MySQL/MariaDB 服务器地址。</summary>
        public string Server { get; set; } = string.Empty;

        /// <summary>MySQL/MariaDB 端口（默认 3306）。</summary>
        public int Port { get; set; } = 3306;

        /// <summary>MySQL/MariaDB 数据库名。</summary>
        public string DatabaseName { get; set; } = string.Empty;

        /// <summary>MySQL/MariaDB 用户名（不是机密；密码在 <see cref="PasswordCipher"/>）。</summary>
        public string UserId { get; set; } = string.Empty;

        /// <summary>
        /// DPAPI 加密后的密码（Base64）。空 = 无密码。
        /// **明文密码永不落盘**：只在保存那一刻存在，落盘前就加密。
        /// </summary>
        public string PasswordCipher { get; set; } = string.Empty;

        public DbOpenMode OpenMode { get; set; } = DbOpenMode.ReadWrite;

        /// <summary>单条语句默认超时（秒）。0 会被归一成 30。</summary>
        public int TimeoutSeconds { get; set; } = 30;

        /// <summary>单次查询默认最多返回行数。0 会被归一成 1000。</summary>
        public int MaxRows { get; set; } = 1000;

        public bool IsEnabled { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime ModifiedAt { get; set; } = DateTime.Now;
        public string ModifiedBy { get; set; } = string.Empty;

        public List<NamedStatement> Statements { get; set; } = new();

        /// <summary>连接目标的一句话（与 <see cref="DbProfileInfo.TargetText"/> 同口径）。</summary>
        public string TargetText => Provider == DbProviderKind.MySql
            ? $"{Server}:{Port}/{DatabaseName}"
            : FilePath;

        public DatabaseProfile Clone() => new()
        {
            ProfileId = ProfileId,
            Name = Name,
            Provider = Provider,
            FilePath = FilePath,
            Server = Server,
            Port = Port,
            DatabaseName = DatabaseName,
            UserId = UserId,
            PasswordCipher = PasswordCipher,
            OpenMode = OpenMode,
            TimeoutSeconds = TimeoutSeconds,
            MaxRows = MaxRows,
            IsEnabled = IsEnabled,
            CreatedAt = CreatedAt,
            ModifiedAt = ModifiedAt,
            ModifiedBy = ModifiedBy,
            Statements = Statements.ConvertAll(s => s.Clone())
        };

        public DbProfileInfo ToInfo() => new()
        {
            ProfileId = ProfileId,
            Name = Name,
            Provider = Provider,
            FilePath = FilePath,
            Server = Server,
            Port = Port,
            DatabaseName = DatabaseName,
            OpenMode = OpenMode,
            TimeoutSeconds = TimeoutSeconds,
            MaxRows = MaxRows,
            IsEnabled = IsEnabled
        };
    }

    public sealed class NamedStatement
    {
        public Guid StatementId { get; set; } = Guid.NewGuid();
        public Guid ProfileId { get; set; }
        public string Name { get; set; } = string.Empty;
        public DbStatementKind Kind { get; set; } = DbStatementKind.Query;
        public string Sql { get; set; } = string.Empty;

        /// <summary>0 = 用 profile 的默认超时。</summary>
        public int TimeoutSeconds { get; set; }

        /// <summary>0 = 用 profile 的默认最大行数。</summary>
        public int MaxRows { get; set; }

        /// <summary>0 = 不限制影响行数（v1 默认）；非 0 时超限回滚。</summary>
        public int MaxAffectedRows { get; set; }

        public bool IsEnabled { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime ModifiedAt { get; set; } = DateTime.Now;

        public List<DbStatementParameter> Parameters { get; set; } = new();

        public NamedStatement Clone() => new()
        {
            StatementId = StatementId,
            ProfileId = ProfileId,
            Name = Name,
            Kind = Kind,
            Sql = Sql,
            TimeoutSeconds = TimeoutSeconds,
            MaxRows = MaxRows,
            MaxAffectedRows = MaxAffectedRows,
            IsEnabled = IsEnabled,
            CreatedAt = CreatedAt,
            ModifiedAt = ModifiedAt,
            Parameters = new List<DbStatementParameter>(Parameters)
        };

        public DbStatementInfo ToInfo() => new()
        {
            StatementId = StatementId,
            ProfileId = ProfileId,
            Name = Name,
            Kind = Kind,
            SqlPreview = Sql,
            Parameters = Parameters,
            TimeoutSeconds = TimeoutSeconds,
            MaxRows = MaxRows,
            MaxAffectedRows = MaxAffectedRows,
            IsEnabled = IsEnabled
        };
    }
}
