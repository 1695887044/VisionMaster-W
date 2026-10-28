using UI.Attributes;

namespace Plugin.ImageAlign
{
    /// <summary>
    /// 画布视图的「参数…」弹窗模型：只收标量/枚举配置，点对由画布/表格管理。
    /// 用 [SuperDisplay] 让 UI 的 FlatPropertyGrid 直接渲染，不重复手写 13 个参数控件。
    /// </summary>
    public sealed class ImageAlignConfigModel
    {
        [SuperDisplay(Group = new[] { "对齐参数" }, Order = 1, Name = "对齐模式",
            Description = "基准点（2~12 点）/ 手动四点（必须 4 对）/ 上游位姿（消费 HomMat2D 或 Pose）")]
        public AlignMode Mode { get; set; }

        [SuperDisplay(Group = new[] { "对齐参数" }, Order = 2, Name = "插值方式",
            Description = "双线性（推荐）/ 最近邻（标签、二值图）")]
        public AlignInterpolation Interpolation { get; set; }

        [SuperDisplay(Group = new[] { "对齐参数" }, Order = 3, Name = "模板参考 Row",
            Description = "上游位姿模式：模板参考点 Row（像素）")]
        public double TemplateRefRow { get; set; }

        [SuperDisplay(Group = new[] { "对齐参数" }, Order = 4, Name = "模板参考 Column",
            Description = "上游位姿模式：模板参考点 Column（像素）")]
        public double TemplateRefColumn { get; set; }

        [SuperDisplay(Group = new[] { "对齐参数" }, Order = 5, Name = "残差上限(px)",
            Description = "验证点残差超过它直接失败；0 = 不做硬校验")]
        public double ResidualLimit { get; set; }

        [SuperDisplay(Group = new[] { "对齐参数" }, Order = 6, Name = "分数参考(px)",
            Description = "Score = 1 − 残差/该值（夹取 0~1）")]
        public double ScoreRef { get; set; }

        [SuperDisplay(Group = new[] { "对齐参数" }, Order = 7, Name = "RANSAC 内点阈值(px)",
            Description = ">4 对点时，残差 ≤ 该值的点算内点；默认 2px")]
        public double InlierThreshold { get; set; }

        [SuperDisplay(Group = new[] { "阈值判定" }, Order = 8, Name = "启用阈值判定",
            Description = "关闭时只输出指标；打开后按最低分数/残差上限/有效区比例判 IsOk")]
        public bool EnableThreshold { get; set; }

        [SuperDisplay(Group = new[] { "阈值判定" }, Order = 9, Name = "最低分数",
            Description = "Score 下限（0~1）；仅启用阈值时生效")]
        public double MinScore { get; set; }

        [SuperDisplay(Group = new[] { "阈值判定" }, Order = 10, Name = "最低有效区比例",
            Description = "ValidAreaRatio 下限（0~1）；仅启用阈值时生效")]
        public double MinValidAreaRatio { get; set; }

        public ImageAlignConfigModel() { }

        public ImageAlignConfigModel(ImageAlignPlugin plugin)
        {
            Mode = plugin.Mode;
            Interpolation = plugin.Interpolation;
            TemplateRefRow = plugin.TemplateRefRow;
            TemplateRefColumn = plugin.TemplateRefColumn;
            ResidualLimit = plugin.ResidualLimit;
            ScoreRef = plugin.ScoreRef;
            InlierThreshold = plugin.InlierThreshold;
            EnableThreshold = plugin.EnableThreshold;
            MinScore = plugin.MinScore;
            MinValidAreaRatio = plugin.MinValidAreaRatio;
        }

        public void ApplyTo(ImageAlignPlugin plugin)
        {
            plugin.Mode = Mode;
            plugin.Interpolation = Interpolation;
            plugin.TemplateRefRow = TemplateRefRow;
            plugin.TemplateRefColumn = TemplateRefColumn;
            plugin.ResidualLimit = ResidualLimit;
            plugin.ScoreRef = ScoreRef;
            plugin.InlierThreshold = InlierThreshold;
            plugin.EnableThreshold = EnableThreshold;
            plugin.MinScore = MinScore;
            plugin.MinValidAreaRatio = MinValidAreaRatio;
        }
    }
}
