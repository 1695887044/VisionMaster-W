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

        /// <summary>
        /// 内联内容（只有配方这类"内容就在方案里"的资产有值；文件资产读 AbsolutePath）。
        /// 文本配方返回原文，参数配方返回 JSON 快照。
        /// </summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>
        /// <see cref="Content"/> 的形态：<c>text</c>（文本配方原文）/ <c>json</c>（参数配方快照）/
        /// 空（没有内联内容）。**必须显式给**：只靠"能不能解析"判断，会让参数配方被当成
        /// 空文本配方，报出"配方表里没有这个芯数"这种完全指错方向的错。
        /// </summary>
        public string ContentKind { get; set; } = string.Empty;
    }

    public interface IProjectAssetResolver
    {
        bool TryResolve(Guid assetId, out ProjectAssetRef? asset, out string? error);
    }

    /// <summary>插件配置壳可识别的资产字段（template / model）。</summary>
    public interface IAssetIdConfig
    {
        string AssetKind { get; }
        string AssetId { get; set; }
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
        /// <summary>
        /// 读"内容就在方案里"的资产（配方这类没有独立文件的）：返回 Content 原文。
        /// 与 <see cref="TryResolve"/> 分开是因为文件资产没有内容可读，
        /// 混成一个口会让调用方在"路径为空"和"内容为空"之间分不清。
        /// </summary>
        public static bool TryResolveContent(
            IExecutionContext? context,
            string? assetId,
            string expectedKind,
            out string content,
            out string error)
            => TryResolveContent(context, assetId, expectedKind, null, out content, out error);

        /// <summary>
        /// 同上，但可要求内容形态（<c>text</c>/<c>json</c>）。传 null = 不限。
        /// </summary>
        public static bool TryResolveContent(
            IExecutionContext? context,
            string? assetId,
            string expectedKind,
            string? expectedContentKind,
            out string content,
            out string error)
        {
            content = string.Empty;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(assetId))
            {
                error = "资产 Id 为空";
                return false;
            }

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

            if (string.IsNullOrWhiteSpace(asset.Content))
            {
                error = $"资产 {asset.Name} 没有内联内容（文件资产请用路径解析）";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(expectedContentKind)
                && !string.Equals(asset.ContentKind, expectedContentKind, StringComparison.OrdinalIgnoreCase))
            {
                error = $"资产 {asset.Name} 不是文本配方（内容形态 {asset.ContentKind}）；" +
                        "请在项目资产页导入文本配方，或改选其它资产";
                return false;
            }

            content = asset.Content;
            return true;
        }

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
