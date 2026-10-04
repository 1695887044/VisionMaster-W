namespace Core.Halcon.Models
{
    /// <summary>
    /// 插件注入图像时携带的一条键值信息（例如 "中心X" / "1234.5"、"判定" / "NG"）。
    ///
    /// 用途：画布第二行列表按这些行显示这张图的来历与结果——插件最清楚自己这张图
    /// 说明什么，不再依赖"步骤名/端口名"去猜。
    /// </summary>
    public sealed class ImageInfoRow
    {
        public ImageInfoRow()
        {
        }

        public ImageInfoRow(string label, string value)
        {
            Label = label;
            Value = value;
        }

        /// <summary>信息名（左，例如 "缺陷数"）</summary>
        public string Label { get; init; } = string.Empty;

        /// <summary>信息值（右，例如 "51"）</summary>
        public string Value { get; init; } = string.Empty;

        /// <summary>常用写法：<c>new("判定", "NG")</c></summary>
        public static ImageInfoRow Of(string label, object? value) =>
            new(label, value?.ToString() ?? string.Empty);

        public override string ToString() =>
            string.IsNullOrEmpty(Label) ? Value : Label + ": " + Value;
    }
}
