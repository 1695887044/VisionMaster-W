using System;
using System.Linq;
using Core.Interfaces;
using Core.Interfaces.Assets;
using VisionMaster.Models;
using VisionMaster.Models.Assets;
using VisionMaster.Scada;

namespace VisionMaster.Services
{
    /// <summary>
    /// 项目资产的写操作闸门：权限、默认指针、冻结/解冻、版本历史与审计。
    ///
    /// 所有写操作只有这一个入口——资产页按钮与将来的导入、回滚都走它，
    /// 免得"页面能改、插件能改、审计只在其中一处"。
    /// </summary>
    public sealed class ProjectAssetService
    {
        /// <summary>资产写操作需要的权限 Key（复用系统权限目录里的「改结构」）。</summary>
        public const string WritePermission = ScadaPermissionKeys.EditStructure;

        private readonly SolutionAssetWriter _writer;
        private readonly IScadaAccessPolicy _policy;
        private readonly ScadaAuditWriter _audit;
        private readonly Func<string> _currentSolutionTag;

        public ProjectAssetService(
            SolutionAssetWriter writer,
            IScadaAccessPolicy policy,
            ScadaAuditWriter audit,
            Func<string>? currentSolutionTag = null)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _policy = policy ?? DefaultScadaAccessPolicy.Instance;
            _audit = audit;
            _currentSolutionTag = currentSolutionTag ?? (() => string.Empty);
        }

        public bool TrySetDefault(Guid assetId, out string error)
        {
            error = string.Empty;
            if (!TryAuthorize(assetId, "设为默认", out var asset, out var kind, out var scope, out error))
                return false;

            var solution = _writer.Solution;
            solution.AssetDefaults ??= new System.Collections.Generic.List<AssetDefaultPointer>();

            foreach (var pointer in solution.AssetDefaults.Where(p =>
                         string.Equals(p.Kind, kind, StringComparison.OrdinalIgnoreCase)
                         && string.Equals(p.Scope, scope, StringComparison.Ordinal)))
                pointer.AssetId = Guid.Empty;

            var existing = solution.AssetDefaults.FirstOrDefault(p =>
                string.Equals(p.Kind, kind, StringComparison.OrdinalIgnoreCase)
                && string.Equals(p.Scope, scope, StringComparison.Ordinal));
            if (existing == null)
            {
                existing = new AssetDefaultPointer { Kind = kind, Scope = scope };
                solution.AssetDefaults.Add(existing);
            }
            existing.AssetId = assetId;

            AssetVersionLog.Append(solution, assetId, kind, asset.Name, AssetVersion(assetId, asset),
                ContentHash(assetId, asset), _writer.SnapshotOf(assetId), "设为默认",
                _policy.CurrentUserName, "作用域：" + scope);

            Audit(asset.Name, "设为默认", ScadaAuditOutcome.Success, "作用域：" + scope);
            return true;
        }

        public bool TryToggleFreeze(Guid assetId, out bool nowFrozen, out string error)
        {
            nowFrozen = false;
            error = string.Empty;
            if (!TryAuthorize(assetId, "冻结/解冻", out var asset, out var kind, out _, out error))
                return false;

            var frozen = !IsFrozen(assetId);
            if (!TrySetFrozen(assetId, frozen, out error))
                return false;

            nowFrozen = frozen;
            var solution = _writer.Solution;
            AssetVersionLog.Append(solution, assetId, kind, asset.Name, AssetVersion(assetId, asset),
                ContentHash(assetId, asset), _writer.SnapshotOf(assetId),
                frozen ? "冻结" : "解冻", _policy.CurrentUserName);

            Audit(asset.Name, frozen ? "冻结" : "解冻", ScadaAuditOutcome.Success, null);
            return true;
        }

        /// <summary>导入、内容更新后记一条版本。冻结的资产拒绝写入；内容变化才递增版本。</summary>
        public bool TryRecordVersion(Guid assetId, string action, string note, out string error)
        {
            error = string.Empty;
            if (!TryAuthorize(assetId, action, out var asset, out var kind, out _, out error))
                return false;

            if (IsFrozen(assetId))
            {
                error = $"资产「{asset.Name}」已冻结，先解冻再修改";
                return false;
            }

            // 版本只在内容变化时递增；默认/冻结/解冻走的是另一条不递增的路径。
            var version = _writer.BumpVersion(assetId);

            AssetVersionLog.Append(_writer.Solution, assetId, kind, asset.Name, version,
                ContentHash(assetId, asset), _writer.SnapshotOf(assetId), action,
                _policy.CurrentUserName, note);

            Audit(asset.Name, action, ScadaAuditOutcome.Success, note);
            return true;
        }

        /// <summary>
        /// 回滚：只对「标定」做真实内容回滚（Transform 可 JSON 还原）；
        /// 文件资产的快照只有路径/哈希，回滚会明确拒绝而不是假装成功。
        /// </summary>
        public bool TryRollback(Guid assetId, Guid recordId, out string error)
        {
            error = string.Empty;
            if (!TryAuthorize(assetId, "回滚", out var asset, out var kind, out _, out error))
                return false;

            if (IsFrozen(assetId))
            {
                error = $"资产「{asset.Name}」已冻结，先解冻再回滚";
                return false;
            }

            if (!string.Equals(kind, "calibration", StringComparison.OrdinalIgnoreCase))
            {
                error = "当前只支持标定资产回滚；文件资产的旧版本需要保留文件本体后再开放";
                return false;
            }

            var record = _writer.Solution.AssetVersionLog?.FirstOrDefault(r => r.RecordId == recordId);
            if (record == null)
            {
                error = "找不到这条版本记录";
                return false;
            }

            try
            {
                var transform = Newtonsoft.Json.JsonConvert.DeserializeObject<CalibrationTransform>(record.SnapshotJson);
                if (transform == null)
                {
                    error = "版本快照无法解析";
                    return false;
                }

                var calibration = _writer.Solution.Calibrations.FirstOrDefault(c => c.CalibrationId == assetId);
                if (calibration == null)
                {
                    error = "标定资产已不存在";
                    return false;
                }

                calibration.Transform = transform;
                calibration.Version = Math.Max(1, calibration.Version) + 1;

                AssetVersionLog.Append(_writer.Solution, assetId, kind, calibration.Name, calibration.Version,
                    string.Empty, record.SnapshotJson, "回滚", _policy.CurrentUserName,
                    "回滚到记录 " + recordId.ToString("N")[..8]);

                Audit(calibration.Name, "回滚", ScadaAuditOutcome.Success, "记录 " + recordId.ToString("N")[..8]);
                return true;
            }
            catch (Exception ex)
            {
                error = "回滚失败：" + ex.Message;
                return false;
            }
        }

        /// <summary>归档 / 恢复。归档不删文件、不动版本号，只切生命周期状态。</summary>
        public bool TrySetArchived(Guid assetId, bool archived, out string error)
        {
            error = string.Empty;
            var action = archived ? "归档" : "恢复";
            if (!TryAuthorize(assetId, action, out var asset, out var kind, out _, out error))
                return false;

            if (archived && IsFrozen(assetId))
            {
                error = $"资产「{asset.Name}」已冻结，先解冻再归档";
                return false;
            }

            var solution = _writer.Solution;
            solution.AssetLifecycle ??= new List<AssetLifecycleState>();

            var state = solution.AssetLifecycle.FirstOrDefault(s => s.AssetId == assetId);
            if (state == null)
            {
                state = new AssetLifecycleState { AssetId = assetId, Kind = kind };
                solution.AssetLifecycle.Add(state);
            }

            state.Kind = kind;
            state.State = archived ? "已归档" : "有效";
            state.ChangedAtUtc = DateTime.UtcNow;
            state.ChangedBy = _policy.CurrentUserName;
            state.Note = archived ? "归档（文件保留）" : "恢复";

            AssetVersionLog.Append(solution, assetId, kind, asset.Name, AssetVersion(assetId, asset),
                ContentHash(assetId, asset), _writer.SnapshotOf(assetId), action,
                _policy.CurrentUserName, state.Note);

            Audit(asset.Name, action, ScadaAuditOutcome.Success, state.Note);
            return true;
        }

        /// <summary>
        /// 删除：从方案集合移除该资产，并清掉默认指针与生命周期记录。
        /// **不删磁盘文件**——资产文件可能被其它方案/人引用，物理删除必须是独立动作。
        /// </summary>
        public bool TryDelete(Guid assetId, out string deletedFilePath, out string error)
        {
            deletedFilePath = string.Empty;
            error = string.Empty;

            if (!TryAuthorize(assetId, "删除", out var asset, out var kind, out _, out error))
                return false;

            if (IsFrozen(assetId))
            {
                error = $"资产「{asset.Name}」已冻结，先解冻再删除";
                return false;
            }

            var solution = _writer.Solution;
            var removed = kind switch
            {
                "calibration" => solution.Calibrations.Remove(
                    solution.Calibrations.FirstOrDefault(c => c.CalibrationId == assetId)!),
                "recipe" => solution.EnsureRecipes().Remove(
                    solution.EnsureRecipes().FirstOrDefault(r => r.RecipeId == assetId)!),
                "template" => solution.TemplateAssets.RemoveAll(a => a.AssetId == assetId) > 0,
                "model" => solution.ModelAssets.RemoveAll(a => a.AssetId == assetId) > 0,
                "image" => solution.ImageAssets.RemoveAll(a => a.AssetId == assetId) > 0,
                _ => false,
            };

            if (!removed)
            {
                error = "资产不存在或已被移除";
                return false;
            }

            solution.AssetDefaults?.RemoveAll(p => p.AssetId == assetId);
            solution.AssetLifecycle?.RemoveAll(s => s.AssetId == assetId);

            deletedFilePath = asset.RelativePath ?? string.Empty;

            // 删除后历史仍保留（谁在什么时候删了哪份资产是审计事实），但不能靠资产 Id 反查，
            // 所以最后一条记录带名字与路径。
            AssetVersionLog.Append(solution, assetId, kind, asset.Name, AssetVersion(assetId, asset),
                asset.ContentHash ?? string.Empty, string.Empty, "删除", _policy.CurrentUserName,
                string.IsNullOrWhiteSpace(deletedFilePath) ? "已从方案移除" : "已从方案移除，文件保留：" + deletedFilePath);

            Audit(asset.Name, "删除", ScadaAuditOutcome.Success,
                string.IsNullOrWhiteSpace(deletedFilePath) ? "已从方案移除" : "文件保留：" + deletedFilePath);
            return true;
        }

        /// <summary>
        /// 两条版本记录的字段级差异。快照是 JSON：逐属性比对，
        /// 缺一边就标 `<新增>` / `<移除>`（不能当成空串，否则"字段被删"看起来像"改成空"）。
        /// </summary>
        public static IReadOnlyList<AssetFieldDiff> DiffSnapshots(string? beforeJson, string? afterJson)
        {
            var before = ParseFlat(beforeJson);
            var after = ParseFlat(afterJson);

            var keys = before.Keys.Union(after.Keys, StringComparer.Ordinal)
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToList();

            var diffs = new List<AssetFieldDiff>(keys.Count);
            foreach (var key in keys)
            {
                var hasBefore = before.TryGetValue(key, out var left);
                var hasAfter = after.TryGetValue(key, out var right);
                diffs.Add(new AssetFieldDiff(
                    key,
                    hasBefore ? left : "<新增>",
                    hasAfter ? right : "<移除>"));
            }

            return diffs;
        }

        /// <summary>把快照 JSON 摊平成 `a.b[0].c → 值`，数组按索引展开。</summary>
        private static Dictionary<string, string> ParseFlat(string? json)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(json)) return result;

            try
            {
                var token = Newtonsoft.Json.Linq.JToken.Parse(json);
                Flatten(token, string.Empty, result);
            }
            catch
            {
                // 快照损坏或不是 JSON：整段当一条，别把 diff 弄崩。
                result["<raw>"] = json!;
            }

            return result;
        }

        private static void Flatten(Newtonsoft.Json.Linq.JToken token, string prefix, Dictionary<string, string> sink)
        {
            switch (token)
            {
                case Newtonsoft.Json.Linq.JObject obj:
                    foreach (var property in obj.Properties())
                    {
                        var key = prefix.Length == 0 ? property.Name : prefix + "." + property.Name;
                        Flatten(property.Value, key, sink);
                    }
                    break;

                case Newtonsoft.Json.Linq.JArray array:
                    if (array.Count == 0)
                    {
                        sink[prefix] = "[]";
                        break;
                    }
                    for (var i = 0; i < array.Count; i++)
                        Flatten(array[i], prefix + "[" + i + "]", sink);
                    break;

                case Newtonsoft.Json.Linq.JValue value:
                    sink[prefix] = value.Type == Newtonsoft.Json.Linq.JTokenType.Null
                        ? "null"
                        : value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    break;

                default:
                    sink[prefix] = token.ToString(Newtonsoft.Json.Formatting.None);
                    break;
            }
        }

        public bool TryAuthorize(
            Guid assetId,            string action,
            out ProjectAssetRef asset,
            out string kind,
            out string scope,
            out string error)
        {
            asset = null!;
            kind = string.Empty;
            scope = string.Empty;
            error = string.Empty;

            if (!_writer.TryResolve(assetId, out asset, out kind, out scope, out error))
                return false;

            if (!_policy.Can(WritePermission, out var reason))
            {
                error = reason ?? "当前账号没有资产写权限";
                Audit(asset.Name, action, ScadaAuditOutcome.Denied, error);
                return false;
            }

            return true;
        }

        public bool IsFrozen(Guid assetId) => _writer.IsFrozen(assetId);

        /// <summary>只做权限判定（导入这类"还没有资产 Id"的写操作用）。</summary>
        public bool CheckWritePermission(string action, out string error)
        {
            error = string.Empty;
            if (_policy.Can(WritePermission, out var reason))
                return true;

            error = reason ?? "当前账号没有资产写权限";
            try
            {
                _audit?.Append(new ScadaAuditEntry(
                    _policy.CurrentUserName, "项目资产", "点击", action,
                    ScadaAuditOutcome.Denied, error));
            }
            catch { }
            return false;
        }

        public bool TrySetFrozen(Guid assetId, bool frozen, out string error)
            => _writer.TrySetFrozen(assetId, frozen, out error);

        public int AssetVersion(Guid assetId, ProjectAssetRef asset)
            => _writer.VersionOf(assetId);

        public string ContentHash(Guid assetId, ProjectAssetRef asset)
            => _writer.ContentHashOf(assetId);

        private void Audit(string subject, string action, ScadaAuditOutcome outcome, string? detail)
        {
            try
            {
                _audit?.Append(new ScadaAuditEntry(
                    _policy.CurrentUserName,
                    subject,
                    "点击",
                    action,
                    outcome,
                    string.IsNullOrWhiteSpace(detail)
                        ? null
                        : detail + (string.IsNullOrWhiteSpace(_currentSolutionTag()) ? string.Empty : " [" + _currentSolutionTag() + "]")));
            }
            catch
            {
                // 审计写不成不放行动作（与 ScadaAuditWriter 的既有口径一致）
            }
        }
    }
}
