using System;
using System.Collections.Generic;
using System.Linq;
using Core.Interfaces;
using VisionMaster.Models;

namespace VisionMaster.Services
{
    /// <summary>
    /// 方案级标定资产目录的宿主实现：包一层 <see cref="SolutionModel.Calibrations"/>。
    ///
    /// 为什么包一层而不让插件直接拿 SolutionModel：插件只引用 Core.Interfaces，物理上够不到
    /// VisionMaster.Models；同时这里也是"保存资产"的唯一写入口，校验/版本递增/锁定判断
    /// 只有一份，不会出现 Calibration 插件与将来的管理面板各写一套规则。
    /// </summary>
    public sealed class SolutionCalibrationCatalog : ICalibrationCatalog
    {
        private readonly SolutionModel _solution;
        private readonly object _sync = new();

        public SolutionCalibrationCatalog(SolutionModel solution)
        {
            _solution = solution ?? throw new ArgumentNullException(nameof(solution));
        }

        public IReadOnlyList<CalibrationAsset> All
        {
            get
            {
                lock (_sync) return _solution.Calibrations.ToArray();
            }
        }

        public bool TryGet(Guid calibrationId, out CalibrationAsset? asset)
        {
            lock (_sync)
            {
                asset = calibrationId == Guid.Empty
                    ? null
                    : _solution.Calibrations.FirstOrDefault(x => x.CalibrationId == calibrationId);
                return asset != null;
            }
        }

        public bool TryGetByName(string name, out CalibrationAsset? asset)
        {
            var normalized = (name ?? string.Empty).Trim();
            lock (_sync)
            {
                asset = normalized.Length == 0
                    ? null
                    : _solution.Calibrations.FirstOrDefault(
                        x => string.Equals(x.Name, normalized, StringComparison.OrdinalIgnoreCase));
                return asset != null;
            }
        }

        public bool Save(CalibrationAsset asset, out string error)
        {
            error = string.Empty;
            if (asset == null) { error = "标定资产为空"; return false; }
            if (asset.Transform == null) { error = "标定资产缺少 Transform"; return false; }

            var name = (asset.Name ?? string.Empty).Trim();
            if (name.Length == 0) { error = "标定资产必须有名字"; return false; }

            lock (_sync)
            {
                var existing = asset.CalibrationId == Guid.Empty
                    ? null
                    : _solution.Calibrations.FirstOrDefault(x => x.CalibrationId == asset.CalibrationId);

                if (existing != null && existing.IsLocked)
                {
                    error = $"标定资产「{existing.Name}」已锁定，拒绝覆盖";
                    return false;
                }

                if (existing == null)
                {
                    asset.Name = name;
                    if (asset.CalibrationId == Guid.Empty) asset.CalibrationId = Guid.NewGuid();
                    if (asset.Version <= 0) asset.Version = 1;
                    _solution.Calibrations.Add(asset);
                    return true;
                }

                // 同 Id：只在内容真的变化时递增版本，避免每次运行把版本刷高
                var changed = !SameTransform(existing.Transform, asset.Transform)
                              || !string.Equals(existing.Name, name, StringComparison.Ordinal)
                              || !string.Equals(existing.Description, asset.Description, StringComparison.Ordinal);
                existing.Name = name;
                existing.Description = asset.Description ?? string.Empty;
                existing.Transform = asset.Transform;
                existing.IsLocked = asset.IsLocked;
                if (changed) existing.Version = Math.Max(1, existing.Version) + 1;
                return true;
            }
        }

        private static bool SameTransform(CalibrationTransform? a, CalibrationTransform? b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (a.Kind != b.Kind || Math.Abs(a.MmPerPixel - b.MmPerPixel) > 1e-12) return false;
            if (a.SourceImageWidth != b.SourceImageWidth || a.SourceImageHeight != b.SourceImageHeight) return false;
            if (!string.Equals(a.CameraSerial, b.CameraSerial, StringComparison.Ordinal)) return false;
            if (!SameArray(a.Matrix, b.Matrix)) return false;
            if (!SameArray(a.ProjectiveMatrix, b.ProjectiveMatrix)) return false;
            if (!SameArray(a.MeshNodes, b.MeshNodes)) return false;
            if (a.MeshSize != b.MeshSize) return false;
            if (!SameArray(a.HandEyeMatrix, b.HandEyeMatrix)) return false;
            return true;
        }

        private static bool SameArray(double[]? a, double[]? b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Length != b.Length) return false;
            for (var i = 0; i < a.Length; i++)
                if (Math.Abs(a[i] - b[i]) > 1e-12) return false;
            return true;
        }
    }
}
