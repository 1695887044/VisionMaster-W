using System;

namespace Core.Interfaces.Operations
{
    /// <summary>
    /// 软件级操作记录（跨方案、跨重启、跟机器走）。
    ///
    /// 与"方案级审计"的分工
    /// ---------
    /// 这里只记**跟机器/跟人走**的事实：身份与权限、方案生命周期、系统参数、软件启停、安全动作。
    /// 生产记录、报警历史、资产内容这些是方案业务数据，必须跟方案走，不进这张表
    /// （否则库会无限增长，且方案拷到另一台机器就断链）。
    ///
    /// 实现必须 **fail-open**：写失败只能记日志，绝不能让登录/保存/启动失败。
    /// </summary>
    public interface ISoftwareOperationLog
    {
        /// <summary>记一条。实现不得抛异常。</summary>
        void Record(SoftwareOperationEntry entry);

        /// <summary>当前库的绝对路径（界面/排障显示用）；没有可用库时为空。</summary>
        string DatabasePath { get; }
    }

    /// <summary>一条软件级操作记录。</summary>
    public sealed class SoftwareOperationEntry
    {
        /// <summary>库内自增主键。写入时忽略；查询回读时填充（明细区显示「记录 Id」用）。</summary>
        public long Id { get; set; }

        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

        /// <summary>分类：Identity / Permission / Solution / Settings / Lifecycle / Safety。</summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>动作：登录 / 登出 / 打开方案 / 保存系统参数 …</summary>
        public string Action { get; set; } = string.Empty;

        /// <summary>结果：成功 / 拒绝 / 失败。</summary>
        public string Outcome { get; set; } = "成功";

        public string Actor { get; set; } = string.Empty;
        public string ActorRole { get; set; } = string.Empty;

        /// <summary>对象（用户名 / 方案名 / 参数名…）。</summary>
        public string Subject { get; set; } = string.Empty;
        public string SubjectId { get; set; } = string.Empty;

        /// <summary>发生在哪个方案里（可为空：登录、启动这类还没有方案）。</summary>
        public Guid? SolutionId { get; set; }
        public string SolutionName { get; set; } = string.Empty;

        public string Detail { get; set; } = string.Empty;

        public string Machine { get; set; } = Environment.MachineName;
        public string AppVersion { get; set; } = string.Empty;
    }

    /// <summary>没有接库时的空实现：所有调用都是 no-op。</summary>
    public sealed class NullSoftwareOperationLog : ISoftwareOperationLog
    {
        public static readonly NullSoftwareOperationLog Instance = new();

        private NullSoftwareOperationLog() { }

        public string DatabasePath => string.Empty;

        public void Record(SoftwareOperationEntry entry) { }
    }

    /// <summary>
    /// 进程级出口。**为什么用静态而不是构造注入**：
    /// Core 层的 <c>AppSettingsService</c>、断言宿主、以及大量 `new ScadaAccessPolicy()`
    /// 的调用点都够不到宿主容器；给它们全部加一个构造参数，改动面远大于收益，
    /// 而且漏传一处就是"这条永远不记"（静默缺失，比没有更坏）。
    /// 启动时由宿主装配一次，之后全进程共用；断言宿主可以换成临时库。
    /// </summary>
    public static class SoftwareOperationLogHost
    {
        private static ISoftwareOperationLog _current = NullSoftwareOperationLog.Instance;

        public static ISoftwareOperationLog Current
        {
            get => _current;
            set => _current = value ?? NullSoftwareOperationLog.Instance;
        }

        /// <summary>安全记录：任何异常都吞掉（记录本身不许成为故障源）。</summary>
        public static void SafeRecord(Action<SoftwareOperationEntry> fill)
        {
            try
            {
                var entry = new SoftwareOperationEntry();
                fill?.Invoke(entry);
                _current.Record(entry);
            }
            catch
            {
                // 有意吞掉：记录失败不该影响业务
            }
        }
    }
}
