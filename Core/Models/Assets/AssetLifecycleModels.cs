using System;
using System.Collections.Generic;
using System.Linq;
using Core.Interfaces.Operations;
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

    /// <summary>
    /// 资产生命周期状态（归档/恢复）。**五类资产共用一张表**，不往
    /// CalibrationAsset / RecipeDefinition 上加字段：那两处是契约类型，
    /// 加字段会让"状态"出现两份真相（各自 Status 与本表），迟早漂移。
    /// 目录读取时本表优先，没有记录 = 有效。
    /// </summary>
    public sealed class AssetLifecycleState
    {
        public Guid AssetId { get; set; }
        public string Kind { get; set; } = string.Empty;

        /// <summary>有效 / 已归档</summary>
        public string State { get; set; } = "有效";

        public DateTime ChangedAtUtc { get; set; } = DateTime.UtcNow;
        public string ChangedBy { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;
    }

    /// <summary>字段级差异的一条。</summary>
    public sealed class AssetFieldDiff
    {
        public AssetFieldDiff(string field, string before, string after)
        {
            Field = field ?? string.Empty;
            Before = before ?? string.Empty;
            After = after ?? string.Empty;
        }

        public string Field { get; }
        public string Before { get; }
        public string After { get; }
        public bool Changed => !string.Equals(Before, After, StringComparison.Ordinal);
    }

    /// <summary>
    /// 版本历史的统一写入口：保证时间/操作者/快照口径只有一份。
    ///
    /// 写在哪（2026-10-27 起）
    /// ---------
    /// **只写方案级库** `<方案目录>\Logs\operations.db`，不再往 `.vms` 里追加。
    /// 用户已确认"整个方案目录一起归档"是唯一支持的用法，所以 `.vms` 里那份
    /// 降级为**只读老数据**（老方案仍能读，见 <see cref="Of"/> 的合并读）。
    ///
    /// 为什么搬走：`.vms` 里每个资产最多 50 条且每次保存整份重写——历史越多方案文件越大、保存越慢。
    /// </summary>
    public static class AssetVersionLog
    {
        /// <summary>老 `.vms` 里的历史上限（只对"老数据"有意义，新记录不再进 `.vms`）。</summary>
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

            // 只写库。`.vms` 里那份不再追加 —— 老方案的历史仍由 Of() 合并读出。
            var sink = AssetVersionSinkHost.Current;
            var sinkReady = !ReferenceEquals(sink, NullSolutionAssetHistorySink.Instance)
                            && !string.IsNullOrWhiteSpace(sink.DatabasePath);

            if (sinkReady)
            {
                sink.Append(new AssetVersionEvent
                {
                    RecordId = record.RecordId,
                    AssetId = record.AssetId,
                    Kind = record.Kind,
                    Name = record.Name,
                    Version = record.Version,
                    ContentHash = record.ContentHash,
                    SnapshotJson = record.SnapshotJson,
                    Action = record.Action,
                    Actor = record.Actor,
                    CreatedAtUtc = record.CreatedAtUtc,
                    Note = record.Note,
                });

                return record;
            }

            // 兜底：没有可用历史库（断言宿主、没接库的宿主、方案未保存）时仍写 `.vms`。
            // 宁可回退到旧行为，也不能让历史静默消失——"记录丢了"比"方案文件大一点"严重得多。
            solution.AssetVersionLog ??= new List<AssetVersionRecord>();
            solution.AssetVersionLog.Add(record);
            Trim(solution, assetId);

            return record;
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

        /// <summary>
        /// 读历史：**库优先，`.vms` 兜底**，按 RecordId 去重后按时间倒序。
        /// 老方案（历史只在 `.vms` 里）照样看得到；新记录只在库里，超出 50 条也不丢。
        /// </summary>
        public static IReadOnlyList<AssetVersionRecord> Of(SolutionModel solution, Guid assetId)
        {
            var merged = new Dictionary<Guid, AssetVersionRecord>();

            foreach (var stored in AssetVersionSinkHost.SafeRead(assetId, 500))
            {
                if (stored.RecordId == Guid.Empty || merged.ContainsKey(stored.RecordId)) continue;
                merged[stored.RecordId] = new AssetVersionRecord
                {
                    RecordId = stored.RecordId,
                    AssetId = stored.AssetId,
                    Kind = stored.Kind,
                    Name = stored.Name,
                    Version = stored.Version,
                    ContentHash = stored.ContentHash,
                    SnapshotJson = stored.SnapshotJson,
                    Action = stored.Action,
                    Actor = stored.Actor,
                    CreatedAtUtc = stored.CreatedAtUtc,
                    Note = stored.Note,
                };
            }

            if (solution?.AssetVersionLog != null)
            {
                foreach (var record in solution.AssetVersionLog.Where(r => r.AssetId == assetId))
                    merged.TryAdd(record.RecordId, record);
            }

            return merged.Values.OrderByDescending(r => r.CreatedAtUtc).ToArray();
        }


        public static string Snapshot(object value)
            => value == null ? string.Empty : JsonConvert.SerializeObject(value, Formatting.None);
    }
}
