using System;
using System.Collections.Generic;

namespace Core.Interfaces.Database
{
    /// <summary>数据库类型。新增成员不破坏既有 .vms（存的是名字，不是序号）。</summary>
    public enum DbProviderKind
    {
        Sqlite = 0,

        /// <summary>MySQL / MariaDB（同一协议，同一个 Provider）。</summary>
        MySql = 1
    }

    /// <summary>打开方式。只读档由宿主在连接串层强制，不依赖 SQL 前缀判断。</summary>
    public enum DbOpenMode
    {
        ReadWrite = 0,
        ReadOnly = 1
    }

    /// <summary>命名语句的用途。插件步骤按它做"类型不匹配"的硬拒绝。</summary>
    public enum DbStatementKind
    {
        Query = 0,
        Execute = 1,
        WriteRow = 2
    }

    /// <summary>
    /// 错误分类。只用于"要不要重试/怎么提示"的判断，不参与成功与否的判定。
    /// v1 不做自动重试（架构硬闸 6：不自动重试非幂等语句），分类只上屏与记日志。
    /// </summary>
    public enum DbErrorKind
    {
        None = 0,
        Transient = 1,
        Deadlock = 2,
        Timeout = 3,
        Constraint = 4,
        Syntax = 5,
        Permission = 6,
        NotFound = 7,
        Disabled = 8,
        Unknown = 9
    }

    /// <summary>
    /// 数据库 profile 的只读快照（插件与宿主页共用）。
    /// 不含密码：SQLite 没有密码；将来 SQL Server/MySQL 的凭据走 CredentialId，不进这个结构。
    /// </summary>
    public sealed class DbProfileInfo
    {
        public Guid ProfileId { get; init; }

        /// <summary>显示名（可改，身份是 <see cref="ProfileId"/>）。</summary>
        public string Name { get; init; } = string.Empty;

        public DbProviderKind Provider { get; init; }

        /// <summary>SQLite 文件绝对路径。界面展示用；日志里按需省略。MySQL 配置下为空。</summary>
        public string FilePath { get; init; } = string.Empty;

        /// <summary>MySQL/MariaDB 服务器地址（SQLite 配置下为空）。</summary>
        public string Server { get; init; } = string.Empty;

        /// <summary>MySQL/MariaDB 端口（默认 3306）。</summary>
        public int Port { get; init; }

        /// <summary>MySQL/MariaDB 数据库名。</summary>
        public string DatabaseName { get; init; } = string.Empty;

        public DbOpenMode OpenMode { get; init; }

        /// <summary>单条语句的默认超时（秒）。语句可覆盖，取两者较小值。</summary>
        public int TimeoutSeconds { get; init; }

        /// <summary>单次查询默认最多返回行数。语句可覆盖，取两者较小值。</summary>
        public int MaxRows { get; init; }

        public bool IsEnabled { get; init; }

        /// <summary>Provider 显示名（SQLite / MySQL·MariaDB）。</summary>
        public string ProviderText => DbProviderNames.Text(Provider);

        /// <summary>
        /// 连接目标的一句话（SQLite = 文件路径；MySQL = server:port/database）。
        /// 插件窗口与宿主页都用它展示，避免各自拼字符串时把两种 Provider 弄混。
        /// </summary>
        public string TargetText => Provider == DbProviderKind.MySql
            ? $"{Server}:{Port}/{DatabaseName}"
            : FilePath;

        /// <summary>下拉显示文本：名称（Provider，只读）。</summary>
        public string Caption => $"{Name}（{ProviderText}{(OpenMode == DbOpenMode.ReadOnly ? "·只读" : string.Empty)}）";
    }

    /// <summary>
    /// 语句参数定义（只读，来自宿主保存的命名语句）。
    /// <see cref="Name"/> 不含 '@'；SQL 里写 @Name。
    /// </summary>
    public sealed class DbStatementParameter
    {
        public string Name { get; init; } = string.Empty;

        /// <summary>Text / Integer / Real / Boolean / DateTime / Blob。</summary>
        public string DbType { get; init; } = DbTypeNames.Text;

        public bool IsRequired { get; init; }

        /// <summary>默认值（文本）。必填且无默认值时，插件侧端口标 IsRequired。</summary>
        public string DefaultValue { get; init; } = string.Empty;

        public string Description { get; init; } = string.Empty;
    }

    /// <summary>命名语句的只读快照（含 SQL 预览；插件窗口只读展示，不可编辑）。</summary>
    public sealed class DbStatementInfo
    {
        public Guid StatementId { get; init; }
        public Guid ProfileId { get; init; }
        public string Name { get; init; } = string.Empty;
        public DbStatementKind Kind { get; init; }

        /// <summary>SQL 全文。插件窗口只读预览；不落 .vms。</summary>
        public string SqlPreview { get; init; } = string.Empty;

        public IReadOnlyList<DbStatementParameter> Parameters { get; init; } = Array.Empty<DbStatementParameter>();

        public int TimeoutSeconds { get; init; }
        public int MaxRows { get; init; }
        public int MaxAffectedRows { get; init; }
        public bool IsEnabled { get; init; }

        /// <summary>界面显示文本：名称（Kind 中文）。</summary>
        public string Caption => $"{Name}（{DbStatementKindNames.Text(Kind)}）";
    }

    /// <summary>一次调用的参数值。Name 不含 '@'。</summary>
    public sealed class DbParameterValue
    {
        public DbParameterValue() { }

        public DbParameterValue(string name, object? value)
        {
            Name = name;
            Value = value;
        }

        public string Name { get; init; } = string.Empty;
        public object? Value { get; init; }
    }

    /// <summary>一次调用的资源上限与取消令牌。服务端会再与 profile/语句上的上限取较小值。</summary>
    public sealed class DbCommandOptions
    {
        /// <summary>0 = 用 profile/语句上的值。</summary>
        public int TimeoutSeconds { get; init; }

        /// <summary>0 = 用 profile/语句上的值。</summary>
        public int MaxRows { get; init; }

        /// <summary>0 = 用语句上的值；仍为 0 表示不限制（v1 只对 Execute/WriteRow 生效）。</summary>
        public int MaxAffectedRows { get; init; }

        public System.Threading.CancellationToken CancellationToken { get; init; }

        public static DbCommandOptions Default { get; } = new();
    }

    /// <summary>查询结果。行内值已按列序排好；<see cref="Truncated"/> 表示被 MaxRows 截断。</summary>
    public sealed class DbQueryResult
    {
        public IReadOnlyList<string> Columns { get; init; } = Array.Empty<string>();
        public IReadOnlyList<object?[]> Rows { get; init; } = Array.Empty<object?[]>();
        public int RowCount { get; init; }
        public bool Truncated { get; init; }
        public double ElapsedMs { get; init; }
    }

    public sealed class DbExecutionResult
    {
        public int AffectedRows { get; init; }
        public double ElapsedMs { get; init; }
    }

    public sealed class DbBatchResult
    {
        public int SucceededRows { get; init; }
        public int? FailedRowIndex { get; init; }
        public double ElapsedMs { get; init; }
    }

    /// <summary>测试语句结果：默认已回滚；<see cref="RolledBack"/>=false 表示真写入了。</summary>
    public sealed class DbTestResult
    {
        public DbQueryResult? Query { get; init; }
        public DbExecutionResult? Execution { get; init; }
        public bool RolledBack { get; init; }
        public double ElapsedMs { get; init; }
    }

    /// <summary>
    /// 插件步骤存在 .vms 里的参数快照（定义 + 当前值）。
    ///
    /// 为什么 .vms 要存定义：流程编译器对运行实例只调 <c>ApplyConfigValues</c>，那时没有宿主可查语句定义，
    /// 不存就建不出参数端口（表现为运行期"参数缺失"，且不报错）。
    /// 只存名字/类型/必填/默认值，不存 SQL、不存连接串（C2 契约）。
    /// </summary>
    public sealed class DbStatementParameterBinding
    {
        public string Name { get; set; } = string.Empty;
        public string DbType { get; set; } = DbTypeNames.Text;
        public bool IsRequired { get; set; }
        public string DefaultValue { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        public DbStatementParameterBinding Clone() => new()
        {
            Name = Name,
            DbType = DbType,
            IsRequired = IsRequired,
            DefaultValue = DefaultValue,
            Description = Description
        };

        public static DbStatementParameterBinding From(DbStatementParameter def) => new()
        {
            Name = def.Name,
            DbType = def.DbType,
            IsRequired = def.IsRequired,
            DefaultValue = def.DefaultValue,
            Description = def.Description
        };
    }

    /// <summary>Provider 中文/显示名（界面下拉与摘要共用一份）。</summary>
    public static class DbProviderNames
    {
        public const string Sqlite = "SQLite";
        public const string MySql = "MySQL·MariaDB";

        public static string Text(DbProviderKind kind) => kind switch
        {
            DbProviderKind.MySql => MySql,
            _ => Sqlite
        };

        /// <summary>界面下拉的候选（值仍是枚举）。</summary>
        public static IReadOnlyList<DbProviderKind> All { get; } = new[]
        {
            DbProviderKind.Sqlite,
            DbProviderKind.MySql
        };
    }

    /// <summary>参数类型字面量与显示名。界面下拉与转换共用一份，避免两处漂移。</summary>
    public static class DbTypeNames
    {
        public const string Text = "Text";
        public const string Integer = "Integer";
        public const string Real = "Real";
        public const string Boolean = "Boolean";
        public const string DateTime = "DateTime";
        public const string Blob = "Blob";

        public static readonly IReadOnlyList<string> All = new[]
        {
            Text, Integer, Real, Boolean, DateTime, Blob
        };

        public static bool IsKnown(string? name) =>
            name is Text or Integer or Real or Boolean or DateTime or Blob;
    }

    /// <summary>语句类型中文名（只用于界面；身份仍是枚举/名字）。</summary>
    public static class DbStatementKindNames
    {
        public static string Text(DbStatementKind kind) => kind switch
        {
            DbStatementKind.Query => "查询",
            DbStatementKind.Execute => "执行",
            DbStatementKind.WriteRow => "写一行",
            _ => kind.ToString()
        };
    }

    /// <summary>错误类别中文名（日志与界面共用）。</summary>
    public static class DbErrorKindNames
    {
        public static string Text(DbErrorKind kind) => kind switch
        {
            DbErrorKind.None => "无",
            DbErrorKind.Transient => "暂时性错误",
            DbErrorKind.Deadlock => "死锁",
            DbErrorKind.Timeout => "超时",
            DbErrorKind.Constraint => "约束冲突",
            DbErrorKind.Syntax => "语法错误",
            DbErrorKind.Permission => "权限不足",
            DbErrorKind.NotFound => "找不到对象",
            DbErrorKind.Disabled => "已禁用",
            _ => "未知错误"
        };
    }
}
