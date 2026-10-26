using System;
using System.IO;
using System.Linq;
using Core.Interfaces;
using Core.Interfaces.Assets;
using VisionMaster.Models;
using VisionMaster.Models.Assets;

namespace VisionMaster.Services
{
    /// <summary>
    /// 资产写入的最小内核：按 Id 找到具体资产并读写冻结/版本/哈希。
    /// 只操作方案内存，不落盘；保存仍由 SolutionService 负责。
    /// </summary>
    public sealed class SolutionAssetWriter
    {
        public SolutionAssetWriter(SolutionModel solution)
        {
            Solution = solution ?? throw new ArgumentNullException(nameof(solution));
        }

        public SolutionModel Solution { get; }

        public bool TryResolve(
            Guid assetId,
            out ProjectAssetRef asset,
            out string kind,
            out string scope,
            out string error)
        {
            asset = null!;
            kind = string.Empty;
            scope = string.Empty;
            error = string.Empty;

            if (assetId == Guid.Empty)
            {
                error = "资产 Id 为空";
                return false;
            }

            var calibration = Solution.Calibrations.FirstOrDefault(c => c.CalibrationId == assetId);
            if (calibration != null)
            {
                kind = "calibration";
                scope = ScopeOfCalibration(calibration);
                asset = new ProjectAssetRef
                {
                    AssetId = assetId,
                    Kind = kind,
                    Name = calibration.Name,
                    Version = calibration.Version,
                };
                return true;
            }

            var recipe = Solution.RecipesOrEmpty().FirstOrDefault(r => r.RecipeId == assetId);
            if (recipe != null)
            {
                kind = "recipe";
                scope = recipe.Scope.ToString();
                asset = new ProjectAssetRef
                {
                    AssetId = assetId,
                    Kind = kind,
                    Name = recipe.Name,
                    Version = recipe.ContentVersion,
                };
                return true;
            }

            foreach (var item in Solution.TemplateAssets.Cast<SolutionAssetBase>())
                if (item.AssetId == assetId) return Found(item, "template", out asset, out kind, out scope, out error);

            foreach (var item in Solution.ModelAssets.Cast<SolutionAssetBase>())
                if (item.AssetId == assetId) return Found(item, "model", out asset, out kind, out scope, out error);

            foreach (var item in Solution.ImageAssets.Cast<SolutionAssetBase>())
                if (item.AssetId == assetId) return Found(item, "image", out asset, out kind, out scope, out error);

            error = $"未找到资产 {assetId}";
            return false;
        }

        private static bool Found(
            SolutionAssetBase item,
            string assetKind,
            out ProjectAssetRef asset,
            out string kind,
            out string scope,
            out string error)
        {
            kind = assetKind;
            scope = string.IsNullOrWhiteSpace(item.Scope) ? "整线" : item.Scope;
            error = string.Empty;
            asset = new ProjectAssetRef
            {
                AssetId = item.AssetId,
                Kind = assetKind,
                Name = item.Name,
                RelativePath = item.RelativePath,
                ContentHash = item.ContentHash,
                Version = item.Version,
            };
            return true;
        }

        public bool IsFrozen(Guid assetId)
        {
            return assetId != Guid.Empty && FindFreezable(assetId)?.IsFrozen == true;
        }

        public bool TrySetFrozen(Guid assetId, bool frozen, out string error)
        {
            error = string.Empty;
            var target = FindFreezable(assetId);
            if (target == null)
            {
                error = "找不到资产";
                return false;
            }

            target.IsFrozen = frozen;
            if (!frozen) return true;

            // 冻结视为内容状态变化：时间戳更新，版本由调用方决定是否递增
            target.UpdatedAtUtc = DateTime.UtcNow;
            return true;
        }

        public int VersionOf(Guid assetId)
        {
            if (Solution.Calibrations.FirstOrDefault(c => c.CalibrationId == assetId) is { } cal)
                return cal.Version;
            if (Solution.RecipesOrEmpty().FirstOrDefault(r => r.RecipeId == assetId) is { } recipe)
                return recipe.ContentVersion;
            return FindStored(assetId)?.Version ?? 0;
        }

        /// <summary>内容真正变化时版本 +1；返回新版本号（找不到资产返回 0）。</summary>
        public int BumpVersion(Guid assetId)
        {
            var calibration = Solution.Calibrations.FirstOrDefault(c => c.CalibrationId == assetId);
            if (calibration != null)
            {
                calibration.Version = Math.Max(1, calibration.Version) + 1;
                return calibration.Version;
            }

            var recipe = Solution.RecipesOrEmpty().FirstOrDefault(r => r.RecipeId == assetId);
            if (recipe != null)
            {
                recipe.ContentVersion = Math.Max(1, recipe.ContentVersion) + 1;
                return recipe.ContentVersion;
            }

            var stored = FindStored(assetId);
            if (stored == null) return 0;
            stored.Version = Math.Max(1, stored.Version) + 1;
            stored.UpdatedAtUtc = DateTime.UtcNow;
            return stored.Version;
        }

        public string ContentHashOf(Guid assetId)
            => FindStored(assetId)?.ContentHash ?? string.Empty;

        /// <summary>
        /// 可回滚快照：标定存 Transform（能真正还原）；配方存条目/点位（能看出改了哪条参数）；
        /// 文件资产只存路径/哈希（二进制不在快照里）。
        /// </summary>
        public string SnapshotOf(Guid assetId)
        {
            var calibration = Solution.Calibrations.FirstOrDefault(c => c.CalibrationId == assetId);
            if (calibration?.Transform != null)
                return AssetVersionLog.Snapshot(calibration.Transform);

            var recipe = Solution.RecipesOrEmpty().FirstOrDefault(r => r.RecipeId == assetId);
            if (recipe != null)
                return recipe.CreateAssetSnapshot();

            var stored = FindStored(assetId);
            return stored == null
                ? string.Empty
                : AssetVersionLog.Snapshot(new { stored.RelativePath, stored.ContentHash, stored.Version, stored.FileSize });
        }

        /// <summary>
        /// 把某个历史版本的文件复制回活动路径，并把版本号 +1（历史只追加，不回退编号）。
        /// 版本库里的文件是 `Assets/Versions/{kind}/{id}/v{n}{ext}`。
        /// </summary>
        public bool TryRestoreStoredVersion(Guid assetId, int sourceVersion, out string error)
        {
            error = string.Empty;

            var stored = FindStored(assetId);
            if (stored == null)
            {
                error = "找不到资产";
                return false;
            }

            var kind = KindOfStored(assetId);
            var root = SolutionPaths.GetSolutionRoot(Solution.SolutionFilePath);
            if (string.IsNullOrWhiteSpace(root))
            {
                error = "方案尚未保存，无法回滚文件";
                return false;
            }

            var extension = Path.GetExtension(stored.RelativePath ?? string.Empty);
            var relative = ProjectAssetImporter.VersionFileRelativePath(kind, assetId, sourceVersion, extension);
            var source = Path.Combine(root!, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(source))
            {
                error = $"v{Math.Max(1, sourceVersion)} 没有保留文件本体（早于版本库功能），无法回滚";
                return false;
            }

            var active = Path.GetFullPath(Path.Combine(root!, stored.RelativePath!));
            try
            {
                var folder = Path.GetDirectoryName(active);
                if (!string.IsNullOrWhiteSpace(folder)) Directory.CreateDirectory(folder!);

                var temp = active + ".tmp-" + Guid.NewGuid().ToString("N");
                try
                {
                    File.Copy(source, temp, overwrite: true);
                    File.Move(temp, active, overwrite: true);
                }
                finally
                {
                    if (File.Exists(temp))
                    {
                        try { File.Delete(temp); } catch { }
                    }
                }

                var info = new FileInfo(active);
                using var stream = File.OpenRead(active);
                using var sha = System.Security.Cryptography.SHA256.Create();
                stored.ContentHash = Convert.ToHexString(sha.ComputeHash(stream));
                stored.FileSize = info.Length;
                stored.Version = Math.Max(1, stored.Version) + 1;
                stored.UpdatedAtUtc = DateTime.UtcNow;
                stored.UpdatedBy = Environment.UserName;
                stored.Status = "有效";

                // 回滚后的内容也算一个版本，同样进版本库，下次才能再回滚到它。
                var targetRelative = ProjectAssetImporter.VersionFileRelativePath(
                    kind, assetId, stored.Version, extension);
                var target = Path.Combine(root!, targetRelative.Replace('/', Path.DirectorySeparatorChar));
                var targetFolder = Path.GetDirectoryName(target);
                if (!string.IsNullOrWhiteSpace(targetFolder)) Directory.CreateDirectory(targetFolder!);
                File.Copy(active, target, overwrite: true);

                return true;
            }
            catch (Exception ex)
            {
                error = "回滚文件失败：" + ex.Message;
                return false;
            }
        }

        private string KindOfStored(Guid assetId)
        {
            if (Solution.TemplateAssets.Any(a => a.AssetId == assetId)) return "template";
            if (Solution.ModelAssets.Any(a => a.AssetId == assetId)) return "model";
            if (Solution.ImageAssets.Any(a => a.AssetId == assetId)) return "image";
            return "asset";
        }

        private SolutionAssetBase? FindStored(Guid assetId)
        {
            foreach (var item in Solution.TemplateAssets.Cast<SolutionAssetBase>())
                if (item.AssetId == assetId) return item;
            foreach (var item in Solution.ModelAssets.Cast<SolutionAssetBase>())
                if (item.AssetId == assetId) return item;
            foreach (var item in Solution.ImageAssets.Cast<SolutionAssetBase>())
                if (item.AssetId == assetId) return item;
            return null;
        }

        private IFreezableAsset? FindFreezable(Guid assetId)
        {
            var calibration = Solution.Calibrations.FirstOrDefault(c => c.CalibrationId == assetId);
            if (calibration != null)
                return new CalibrationFreezeAdapter(calibration);

            // 配方也必须能冻结：它和标定一样是"被生产引用的内容"，
            // 之前只覆盖标定与文件资产，配方点冻结会得到"找不到资产"（2026-10-26 补）。
            var recipe = Solution.RecipesOrEmpty().FirstOrDefault(r => r.RecipeId == assetId);
            if (recipe != null)
                return new RecipeFreezeAdapter(recipe);

            var stored = FindStored(assetId);
            return stored == null ? null : new StoredFreezeAdapter(stored);
        }

        private static string ScopeOfCalibration(CalibrationAsset asset)
        {
            var name = asset.Name ?? string.Empty;
            return string.IsNullOrWhiteSpace(name) ? "方案级" : "方案级";
        }

        private interface IFreezableAsset
        {
            bool IsFrozen { get; set; }
            DateTime UpdatedAtUtc { get; set; }
        }

        private sealed class CalibrationFreezeAdapter : IFreezableAsset
        {
            private readonly CalibrationAsset _asset;
            public CalibrationFreezeAdapter(CalibrationAsset asset) => _asset = asset;
            public bool IsFrozen
            {
                get => _asset.IsLocked;
                set => _asset.IsLocked = value;
            }

            public DateTime UpdatedAtUtc
            {
                get => _asset.Transform?.CreatedAtUtc ?? default;
                set
                {
                    if (_asset.Transform != null) _asset.Transform.CreatedAtUtc = value;
                }
            }
        }

        private sealed class RecipeFreezeAdapter : IFreezableAsset
        {
            private readonly RecipeDefinition _recipe;
            public RecipeFreezeAdapter(RecipeDefinition recipe) => _recipe = recipe;
            public bool IsFrozen
            {
                get => _recipe.IsFrozen;
                set => _recipe.IsFrozen = value;
            }

            public DateTime UpdatedAtUtc
            {
                get => _recipe.UpdatedAtUtc ?? _recipe.CreatedAtUtc;
                set => _recipe.UpdatedAtUtc = value;
            }
        }

        private sealed class StoredFreezeAdapter : IFreezableAsset        {
            private readonly SolutionAssetBase _asset;
            public StoredFreezeAdapter(SolutionAssetBase asset) => _asset = asset;
            public bool IsFrozen
            {
                get => _asset.IsFrozen;
                set => _asset.IsFrozen = value;
            }

            public DateTime UpdatedAtUtc
            {
                get => _asset.UpdatedAtUtc;
                set => _asset.UpdatedAtUtc = value;
            }
        }
    }
}
