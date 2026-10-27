using System;
using System.Collections.Generic;
using System.IO;
using Core.Interfaces.Operations;
using Microsoft.Data.Sqlite;

namespace Core.Database
{
    /// <summary>
    /// 方案级资产版本历史（`&lt;方案目录&gt;\Logs\operations.db`）。
    ///
    /// 为什么搬出 `.vms`
    /// ---------
    /// `.vms` 里的 `AssetVersionLog` 每个资产最多 50 条，而且每存一次方案要整份重写——
    /// 历史越多，方案文件越大、保存越慢。历史是"只追加的事实"，放 sidecar 库更合适：
    /// 方案归档/换机时它跟着目录一起走，与 `.vms` 同生命周期。
    ///
    /// 与软件级库的区别
    /// ---------
    /// 软件级 `<程序目录>\Data\operations.db` 记"这台机器发生过什么"；
    /// 这里记"这套方案的资产被谁动过"。混在一起会让方案换机就断链。
    ///
    /// **不用 WAL**：现场常把方案放共享盘，SQLite 在 SMB 上 WAL 不可靠。
    /// </summary>
    public sealed class SqliteAssetHistoryStore : ISolutionAssetHistorySink, IDisposable
    {
        private const string TableName = "asset_events";
        private const int KeepPerAsset = 200;

        private readonly Func<string?> _solutionRootProvider;
        private readonly Action<string>? _warn;
        private readonly object _gate = new();

        private string _databasePath = string.Empty;
        private bool _initialized;
        private bool _disabled;
        private string _lastError = string.Empty;

        public SqliteAssetHistoryStore(Func<string?> solutionRootProvider, Action<string>? warn = null)
        {
            _solutionRootProvider = solutionRootProvider ?? throw new ArgumentNullException(nameof(solutionRootProvider));
            _warn = warn;
        }

        /// <summary>
        /// 当前方案的库路径。**未初始化时也算得出来**（由根目录 provider 现算）：
        /// 调用方要靠它判断"现在能不能写库"，返回空会让人误以为没接库而回退到 `.vms`。
        /// </summary>
        public string DatabasePath
            => _disabled
                ? string.Empty
                : (!string.IsNullOrWhiteSpace(_databasePath)
                    ? _databasePath
                    : PathFor(_solutionRootProvider() ?? string.Empty));

        /// <summary>`&lt;方案目录&gt;\Logs\operations.db`。</summary>
        public static string PathFor(string solutionRoot)
            => string.IsNullOrWhiteSpace(solutionRoot)
                ? string.Empty
                : Path.Combine(solutionRoot, "Logs", "operations.db");

        public void Append(AssetVersionEvent entry) => TryAppend(entry);

        public bool TryAppend(AssetVersionEvent entry)
        {
            if (entry == null || entry.AssetId == Guid.Empty) return false;

            try
            {
                lock (_gate)
                {
                    if (!EnsureInitialized()) return false;

                    using var connection = OpenConnection();
                    using var command = connection.CreateCommand();
                    command.CommandText =
                        "INSERT INTO " + TableName + " (" +
                        "record_id, asset_id, kind, name, version, content_hash, snapshot_json, action, actor, ts_utc, note" +
                        ") VALUES ($recordId, $assetId, $kind, $name, $version, $hash, $snapshot, $action, $actor, $ts, $note)" +
                        " ON CONFLICT(record_id) DO NOTHING;";

                    command.Parameters.AddWithValue("$recordId", entry.RecordId.ToString());
                    command.Parameters.AddWithValue("$assetId", entry.AssetId.ToString());
                    command.Parameters.AddWithValue("$kind", entry.Kind ?? string.Empty);
                    command.Parameters.AddWithValue("$name", entry.Name ?? string.Empty);
                    command.Parameters.AddWithValue("$version", entry.Version);
                    command.Parameters.AddWithValue("$hash", entry.ContentHash ?? string.Empty);
                    command.Parameters.AddWithValue("$snapshot", entry.SnapshotJson ?? string.Empty);
                    command.Parameters.AddWithValue("$action", entry.Action ?? string.Empty);
                    command.Parameters.AddWithValue("$actor", entry.Actor ?? string.Empty);
                    command.Parameters.AddWithValue("$ts", entry.CreatedAtUtc.ToString("O"));
                    command.Parameters.AddWithValue("$note", entry.Note ?? string.Empty);
                    command.ExecuteNonQuery();

                    TrimPerAsset(connection, entry.AssetId);
                }

                return true;
            }
            catch (Exception ex)
            {
                var message = ex.GetType().Name + ": " + ex.Message;
                if (!string.Equals(message, _lastError, StringComparison.Ordinal))
                {
                    _lastError = message;
                    _warn?.Invoke("[资产历史] 写入失败（已忽略，不影响资产操作）：" + message);
                }

                return false;
            }
        }

        public IReadOnlyList<AssetVersionEvent> Read(Guid assetId, int limit)
        {
            var result = new List<AssetVersionEvent>();
            if (assetId == Guid.Empty) return result;

            try
            {
                lock (_gate)
                {
                    if (!EnsureInitialized()) return result;

                    using var connection = OpenConnection();
                    using var command = connection.CreateCommand();
                    command.CommandText =
                        "SELECT record_id, asset_id, kind, name, version, content_hash, snapshot_json, action, actor, ts_utc, note " +
                        "FROM " + TableName + " WHERE asset_id = $assetId ORDER BY ts_utc DESC, rowid DESC LIMIT $limit;";
                    command.Parameters.AddWithValue("$assetId", assetId.ToString());
                    command.Parameters.AddWithValue("$limit", Math.Max(1, limit));

                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        result.Add(new AssetVersionEvent
                        {
                            RecordId = Guid.TryParse(reader.GetString(0), out var rid) ? rid : Guid.Empty,
                            AssetId = Guid.TryParse(reader.GetString(1), out var aid) ? aid : assetId,
                            Kind = reader.GetString(2),
                            Name = reader.GetString(3),
                            Version = reader.GetInt32(4),
                            ContentHash = reader.GetString(5),
                            SnapshotJson = reader.GetString(6),
                            Action = reader.GetString(7),
                            Actor = reader.GetString(8),
                            CreatedAtUtc = DateTime.TryParse(reader.GetString(9), null,
                                System.Globalization.DateTimeStyles.RoundtripKind, out var ts) ? ts : DateTime.UtcNow,
                            Note = reader.GetString(10),
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _warn?.Invoke("[资产历史] 读取失败：" + ex.Message);
            }

            return result;
        }

        /// <summary>每个资产只留最近 <see cref="KeepPerAsset"/> 条（远高于 `.vms` 里的 50）。</summary>
        private static void TrimPerAsset(SqliteConnection connection, Guid assetId)
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                "DELETE FROM " + TableName + " WHERE asset_id = $assetId AND record_id NOT IN (" +
                "SELECT record_id FROM " + TableName + " WHERE asset_id = $assetId " +
                "ORDER BY ts_utc DESC, rowid DESC LIMIT $keep);";
            command.Parameters.AddWithValue("$assetId", assetId.ToString());
            command.Parameters.AddWithValue("$keep", KeepPerAsset);
            command.ExecuteNonQuery();
        }

        private bool EnsureInitialized()
        {
            var root = _solutionRootProvider();
            var path = PathFor(root ?? string.Empty);

            if (string.IsNullOrWhiteSpace(path))
            {
                // 没有方案（或方案未保存）：静默停用，等有方案时再初始化。
                _initialized = false;
                _databasePath = string.Empty;
                return false;
            }

            if (_initialized && string.Equals(path, _databasePath, StringComparison.OrdinalIgnoreCase))
                return !_disabled;

            _initialized = false;
            _disabled = false;
            _databasePath = path;

            try
            {
                var folder = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(folder)) Directory.CreateDirectory(folder!);

                using var connection = OpenConnection();
                using var create = connection.CreateCommand();
                create.CommandText =
                    "CREATE TABLE IF NOT EXISTS " + TableName + " (" +
                    "record_id TEXT PRIMARY KEY," +
                    "asset_id TEXT NOT NULL," +
                    "kind TEXT NOT NULL DEFAULT ''," +
                    "name TEXT NOT NULL DEFAULT ''," +
                    "version INTEGER NOT NULL DEFAULT 1," +
                    "content_hash TEXT NOT NULL DEFAULT ''," +
                    "snapshot_json TEXT NOT NULL DEFAULT ''," +
                    "action TEXT NOT NULL DEFAULT ''," +
                    "actor TEXT NOT NULL DEFAULT ''," +
                    "ts_utc TEXT NOT NULL," +
                    "note TEXT NOT NULL DEFAULT ''" +
                    ");" +
                    "CREATE INDEX IF NOT EXISTS ix_asset_events_asset_ts ON " + TableName + "(asset_id, ts_utc);";
                create.ExecuteNonQuery();

                _initialized = true;
                return true;
            }
            catch (Exception ex)
            {
                _disabled = true;
                _warn?.Invoke("[资产历史] 初始化失败，本次运行不再记录：" + ex.Message);
                return false;
            }
        }

        private SqliteConnection OpenConnection()
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = _databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                DefaultTimeout = 5,
            };

            var connection = new SqliteConnection(builder.ToString());
            connection.Open();
            return connection;
        }

        public void Dispose() => _disabled = true;
    }
}
