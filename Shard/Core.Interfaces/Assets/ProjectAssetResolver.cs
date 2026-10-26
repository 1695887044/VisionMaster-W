using System;

namespace Core.Interfaces.Assets
{
    /// <summary>
    /// 方案资产的只读解析结果。插件只拿到“这是什么、文件在哪、版本/哈希”，
    /// 不直接引用 SolutionModel 或 UI。
    /// </summary>
    public sealed class ProjectAssetRef
    {
        public Guid AssetId { get; init; }
        public string Kind { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public string AbsolutePath { get; set; } = string.Empty;
        public string ContentHash { get; set; } = string.Empty;
        public int Version { get; set; }
    }

    public interface IProjectAssetResolver
    {
        bool TryResolve(Guid assetId, out ProjectAssetRef? asset, out string? error);
    }

    public sealed class NullProjectAssetResolver : IProjectAssetResolver
    {
        public static readonly NullProjectAssetResolver Instance = new();

        private NullProjectAssetResolver() { }

        public bool TryResolve(Guid assetId, out ProjectAssetRef? asset, out string? error)
        {
            asset = null;
            error = "当前执行环境没有方案资产目录";
            return false;
        }
    }

    /// <summary>插件侧共用的“AssetId 优先、旧路径回退”解析口径。</summary>
    public static class ProjectAssetPathResolver
    {
        public static bool TryResolve(
            IExecutionContext? context,
            string? assetId,
            string? fallbackPath,
            string expectedKind,
            out string path,
            out string error)
        {
            path = fallbackPath ?? string.Empty;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(assetId))
                return true;

            if (!Guid.TryParse(assetId.Trim(), out var id))
            {
                error = $"资产 Id 不是合法 Guid：{assetId}";
                return false;
            }

            if (context == null)
            {
                error = "资产 Id 只能在运行期解析（当前没有执行上下文）";
                return false;
            }

            if (!context.Assets.TryResolve(id, out var asset, out error) || asset == null)
                return false;

            if (!string.Equals(asset.Kind, expectedKind, StringComparison.OrdinalIgnoreCase))
            {
                error = $"资产 {asset.Name} 的类型是 {asset.Kind}，这里需要 {expectedKind}";
                return false;
            }

            if (string.IsNullOrWhiteSpace(asset.AbsolutePath))
            {
                error = $"资产 {asset.Name} 没有可读取的文件路径";
                return false;
            }

            path = asset.AbsolutePath;
            return true;
        }
    }
}
