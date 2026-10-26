using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace VisionMaster.Models.Assets
{
    /// <summary>
    /// 资产版本历史的一条记录。随 .vms 落盘（只存元数据与可回滚快照，不存文件本体）。
    /// </summary>
    public sealed class AssetVersionRecord
    {
        public Guid RecordId { get; set; } = Guid.NewGuid();
        public Guid AssetId { get; set; }
        public string Kind { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int Version { get; set; }
        public string ContentHash { get; set; } = string.Empty;

        /// <summary>可回滚快照（标定是 Transform 的 JSON；文件资产是路径/哈希）。不承诺能还原二进制。</summary>
        public string SnapshotJson { get; set; } = string.Empty;

        public string Action { get; set; } = string.Empty;
        public string Actor { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public string Note { get; set; } = string.Empty;
    }

    /// <summary>某一作用域（工位/整线）当前选中的默认资产。</summary>
    public sealed class AssetDefaultPointer
    {
        public string Kind { get; set; } = string.Empty;
        public string Scope { get; set; } = string.Empty;
        public Guid AssetId { get; set; }
    }

    /// <summary>版本历史的统一写入口：保证时间/操作者/快照口径只有一份。</summary>
    public static class AssetVersionLog
    {
        public const int MaxRecordsPerAsset = 50;

        public static AssetVersionRecord Append(
            SolutionModel solution,
            Guid assetId,
            string kind,
            string name,
            int version,
            string contentHash,
            string snapshotJson,
            string action,
            string actor,
            string note = "")
        {
            if (solution == null) throw new ArgumentNullException(nameof(solution));
            solution.AssetVersionLog ??= new List<AssetVersionRecord>();

            var record = new AssetVersionRecord
            {
                AssetId = assetId,
                Kind = kind ?? string.Empty,
                Name = name ?? string.Empty,
                Version = version,
                ContentHash = contentHash ?? string.Empty,
                SnapshotJson = snapshotJson ?? string.Empty,
                Action = action ?? string.Empty,
                Actor = string.IsNullOrWhiteSpace(actor) ? "未登录" : actor.Trim(),
                CreatedAtUtc = DateTime.UtcNow,
                Note = note ?? string.Empty,
            };
            solution.AssetVersionLog.Add(record);

            Trim(solution, assetId);
            return record;
        }

        public static IReadOnlyList<AssetVersionRecord> Of(SolutionModel solution, Guid assetId)
        {
            if (solution?.AssetVersionLog == null) return Array.Empty<AssetVersionRecord>();
            return solution.AssetVersionLog
                .Where(r => r.AssetId == assetId)
                .OrderByDescending(r => r.CreatedAtUtc)
                .ToArray();
        }

        private static void Trim(SolutionModel solution, Guid assetId)
        {
            var mine = solution.AssetVersionLog.Where(r => r.AssetId == assetId)
                .OrderByDescending(r => r.CreatedAtUtc)
                .Skip(MaxRecordsPerAsset)
                .ToList();
            if (mine.Count == 0) return;
            foreach (var record in mine)
                solution.AssetVersionLog.Remove(record);
        }

        public static string Snapshot(object value)
            => value == null ? string.Empty : JsonConvert.SerializeObject(value, Formatting.None);
    }
}
