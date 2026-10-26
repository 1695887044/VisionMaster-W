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

        public bool TryAuthorize(
            Guid assetId,
            string action,
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
