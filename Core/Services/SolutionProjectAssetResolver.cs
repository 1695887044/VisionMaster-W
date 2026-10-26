using System;
using System.IO;
using System.Linq;
using Core.Interfaces.Assets;
using VisionMaster.Models;
using VisionMaster.Models.Assets;

namespace VisionMaster.Services
{
    /// <summary>
    /// 方案内容资产的宿主解析器：插件只给 AssetId，这里返回绝对路径/版本/哈希。
    /// 标定与配方没有外部文件，返回空路径但 Kind/Name/Version 可用于诊断。
    /// </summary>
    public sealed class SolutionProjectAssetResolver : IProjectAssetResolver
    {
        private readonly SolutionModel _solution;

        public SolutionProjectAssetResolver(SolutionModel solution)
        {
            _solution = solution ?? throw new ArgumentNullException(nameof(solution));
        }

        public bool TryResolve(Guid assetId, out ProjectAssetRef? asset, out string? error)
        {
            asset = null;
            error = null;
            if (assetId == Guid.Empty)
            {
                error = "资产 Id 为空";
                return false;
            }

            // 归档 = 退出生产。这里必须拒绝：归档只改界面的活，运行期照用的话
            // "已归档"就成了一句没有后果的标签（2026-10-26 补）。
            if (IsArchived(assetId, out var archivedName))
            {
                error = $"资产「{archivedName}」已归档，不能用于运行；请在项目资产页恢复后再跑";
                return false;
            }

            var calibration = _solution.Calibrations.FirstOrDefault(x => x.CalibrationId == assetId);
            if (calibration != null)
            {
                asset = new ProjectAssetRef
                {
                    AssetId = calibration.CalibrationId,
                    Kind = "calibration",
                    Name = calibration.Name,
                    Version = calibration.Version,
                };
                return true;
            }

            var recipe = _solution.RecipesOrEmpty().FirstOrDefault(x => x.RecipeId == assetId);
            if (recipe != null)
            {
                asset = new ProjectAssetRef
                {
                    AssetId = recipe.RecipeId,
                    Kind = "recipe",
                    Name = recipe.Name,
                    Version = recipe.ContentVersion,
                    // 配方内容在方案里：文本配方给原文，参数配方给快照 JSON。
                    // 插件拿 Content 自行解析（文件资产仍然只给路径）。
                    Content = string.IsNullOrWhiteSpace(recipe.TextContent)
                        ? recipe.CreateAssetSnapshot()
                        : recipe.TextContent,
                    ContentKind = string.IsNullOrWhiteSpace(recipe.TextContent) ? "json" : "text",
                };
                return true;
            }

            if (_solution.TemplateAssets.FirstOrDefault(x => x.AssetId == assetId) is { } template)
                return TryCreateFileRef(template, "template", out asset, out error);

            if (_solution.ModelAssets.FirstOrDefault(x => x.AssetId == assetId) is { } model)
                return TryCreateFileRef(model, "model", out asset, out error);

            if (_solution.ImageAssets.FirstOrDefault(x => x.AssetId == assetId) is { } image)
                return TryCreateFileRef(image, "image", out asset, out error);

            error = $"未找到资产 {assetId}";
            return false;
        }

        private bool IsArchived(Guid assetId, out string name)
        {
            name = string.Empty;
            var state = _solution.AssetLifecycle?.FirstOrDefault(s => s.AssetId == assetId);
            if (state == null || !string.Equals(state.State, "已归档", StringComparison.Ordinal))
                return false;

            // 名字只为把错误写清楚：优先取资产本名，取不到就退回生命周期记录里的。
            name = _solution.Calibrations.FirstOrDefault(x => x.CalibrationId == assetId)?.Name
                ?? _solution.RecipesOrEmpty().FirstOrDefault(x => x.RecipeId == assetId)?.Name
                ?? _solution.TemplateAssets.FirstOrDefault(x => x.AssetId == assetId)?.Name
                ?? _solution.ModelAssets.FirstOrDefault(x => x.AssetId == assetId)?.Name
                ?? _solution.ImageAssets.FirstOrDefault(x => x.AssetId == assetId)?.Name
                ?? assetId.ToString("N")[..8];
            return true;
        }

        private bool TryCreateFileRef(
            SolutionAssetBase source,
            string kind,
            out ProjectAssetRef? asset,
            out string? error)
        {
            asset = null;
            error = null;

            var root = SolutionPaths.GetSolutionRoot(_solution.SolutionFilePath);
            if (string.IsNullOrWhiteSpace(root))
            {
                error = "方案尚未保存，无法解析资产文件路径";
                return false;
            }

            if (string.IsNullOrWhiteSpace(source.RelativePath))
            {
                error = $"资产 {source.Name} 没有绑定文件";
                return false;
            }

            string absolute;
            try
            {
                absolute = Path.GetFullPath(Path.Combine(root!, source.RelativePath));
            }
            catch (Exception ex)
            {
                error = $"资产路径无效：{ex.Message}";
                return false;
            }

            var rootFull = Path.GetFullPath(root!);
            if (!absolute.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(absolute, rootFull, StringComparison.OrdinalIgnoreCase))
            {
                error = $"资产路径越出方案目录：{source.RelativePath}";
                return false;
            }

            if (!File.Exists(absolute))
            {
                error = $"资产文件不存在：{source.RelativePath}";
                return false;
            }

            asset = new ProjectAssetRef
            {
                AssetId = source.AssetId,
                Kind = kind,
                Name = source.Name,
                RelativePath = source.RelativePath,
                AbsolutePath = absolute,
                ContentHash = source.ContentHash,
                Version = source.Version,
            };
            return true;
        }
    }
}
