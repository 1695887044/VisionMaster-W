using System;

namespace VisionMaster.Models.Assets
{
    /// <summary>
    /// 方案级内容资产的公共元数据。标定和配方已有自己的类，不强行并入；
    /// 模板/模型/图像使用本基类，具体集合仍是具体类型（避免多态序列化）。
    /// </summary>
    public abstract class SolutionAssetBase
    {
        public Guid AssetId { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Scope { get; set; } = "整线";
        public string Camera { get; set; } = "—";
        public int Version { get; set; } = 1;
        public string Status { get; set; } = "有效";
        public bool IsFrozen { get; set; }

        /// <summary>相对方案根目录的路径（例如 Assets\Templates\foo.json）。</summary>
        public string RelativePath { get; set; } = string.Empty;

        public string ContentHash { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
        public string CreatedBy { get; set; } = string.Empty;
        public string UpdatedBy { get; set; } = string.Empty;
    }

    public sealed class TemplateAsset : SolutionAssetBase
    {
        public string TemplateKind { get; set; } = "几何模板";
        public string PreviewRelativePath { get; set; } = string.Empty;
    }

    public sealed class ModelAsset : SolutionAssetBase
    {
        public string ModelKind { get; set; } = "通用模型";
        public int InputWidth { get; set; }
        public int InputHeight { get; set; }
        public string Labels { get; set; } = string.Empty;
    }

    public sealed class ImageAsset : SolutionAssetBase
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public string ImageKind { get; set; } = "采集图";
        public string SourceCamera { get; set; } = string.Empty;
    }
}
