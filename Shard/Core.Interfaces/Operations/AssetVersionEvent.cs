using System;
using System.Collections.Generic;

namespace Core.Interfaces.Operations
{
    /// <summary>
    /// 一条资产版本事件（方案级）。**放在契约层是为了让 Core 与 Sqlite 实现解耦**：
    /// 版本记录本身是 `VisionMaster.Models.Assets.AssetVersionRecord`（Core 层），
    /// 而 Sqlite Provider 只允许出现在 Core.Database 层，两边不能直接引用。
    /// </summary>
    public sealed class AssetVersionEvent
    {
        public Guid RecordId { get; set; }
        public Guid AssetId { get; set; }
        public string Kind { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int Version { get; set; }
        public string ContentHash { get; set; } = string.Empty;
        public string SnapshotJson { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string Actor { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public string Note { get; set; } = string.Empty;
    }

    /// <summary>
    /// 方案级资产历史存储。**跟方案走**（`&lt;方案目录&gt;\Logs\operations.db`），
    /// 与软件级 `software_events` 是两回事：那边记"这台机器发生过什么"，这边记"这套方案的资产被谁动过"。
    /// 实现必须 fail-open：写失败只记日志，不能让资产操作失败。
    /// </summary>
    public interface ISolutionAssetHistorySink
    {
        /// <summary>写一条；失败只记日志（fail-open）。</summary>
        void Append(AssetVersionEvent entry);

        /// <summary>
        /// 写一条并报告成功与否。**迁移老数据要用它**：只有确认写进库的记录才允许从 `.vms` 里删掉，
        /// 否则库写失败 + `.vms` 已清 = 历史凭空消失。
        /// </summary>
        bool TryAppend(AssetVersionEvent entry);

        /// <summary>按资产读历史（新的在前）。读不到返回空表，不抛。</summary>
        IReadOnlyList<AssetVersionEvent> Read(Guid assetId, int limit);

        /// <summary>当前方案的历史库路径；没有方案/未保存时为空。</summary>
        string DatabasePath { get; }
    }

    public sealed class NullSolutionAssetHistorySink : ISolutionAssetHistorySink
    {
        public static readonly NullSolutionAssetHistorySink Instance = new();

        private NullSolutionAssetHistorySink() { }

        public string DatabasePath => string.Empty;

        public void Append(AssetVersionEvent entry) { }

        public bool TryAppend(AssetVersionEvent entry) => false;

        public IReadOnlyList<AssetVersionEvent> Read(Guid assetId, int limit)
            => Array.Empty<AssetVersionEvent>();
    }

    /// <summary>
    /// 进程级出口（与 <see cref="SoftwareOperationLogHost"/> 同一手法）：
    /// 启动时装配一次，Core 层的 <c>AssetVersionLog</c> 直接用，不必给每个调用点加参数。
    /// </summary>
    public static class AssetVersionSinkHost
    {
        private static ISolutionAssetHistorySink _current = NullSolutionAssetHistorySink.Instance;

        public static ISolutionAssetHistorySink Current
        {
            get => _current;
            set => _current = value ?? NullSolutionAssetHistorySink.Instance;
        }

        public static void SafeAppend(AssetVersionEvent entry)
        {
            try { _current.Append(entry); }
            catch { /* fail-open：历史写失败不影响资产操作 */ }
        }

        public static IReadOnlyList<AssetVersionEvent> SafeRead(Guid assetId, int limit)
        {
            try { return _current.Read(assetId, limit); }
            catch { return Array.Empty<AssetVersionEvent>(); }
        }
    }
}
