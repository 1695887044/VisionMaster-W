using HalconDotNet;
using Plugin.PreProcessing.Models;
using Plugin.PreProcessing.Services;
using UI.Attributes;

namespace Plugin.PreProcessing.Operators
{
    /// <summary>全局阈值二值化：整张图用同一个灰度区间判决</summary>
    [PreprocessOperator("threshold", "二值化", "二值化", Order = 160,
        Description = "灰度落在 [下限, 上限] 内的像素置为白，其余置为黑。光照均匀时用它")]
    public sealed class ThresholdOperator : PreprocessOperator
    {
        private double _low = 128;
        private double _high = 255;
        private bool _reverse;

        [SuperDisplay(Name = "灰度下限", Group = new string[] { "参数" }, Order = 1, ColSpan = 4,
            Description = "参与判决的灰度区间起点（按图像位深填，uint2 可到 65535）")]
        public double Low
        {
            get => _low;
            set => SetParam(ref _low, value);
        }

        [SuperDisplay(Name = "灰度上限", Group = new string[] { "参数" }, Order = 2, ColSpan = 4,
            Description = "参与判决的灰度区间终点（按图像位深填，uint2 可到 65535）")]
        public double High
        {
            get => _high;
            set => SetParam(ref _high, value);
        }

        [SuperDisplay(Name = "反转", Group = new string[] { "参数" }, Order = 3, ColSpan = 4,
            Description = "勾选后：命中区间变黑、背景变白")]
        public bool Reverse
        {
            get => _reverse;
            set => SetParam(ref _reverse, value);
        }

        protected override void Normalize()
        {
            // 不按 0~255 夹：uint2/real 图的合法灰度可以超过 255，夹了会静默改写用户阈值
            // （实测 uint2 1000~16300 被夹成 255 后只选到恰为 255 的像素）。
            if (Low > High)
            {
                var t = Low;
                Low = High;
                High = t;
            }
        }

        protected override HImage Process(HImage input)
            => PreprocessHService.Threshold(input, Low, High, Reverse);

        public override string Summary => $"{Low:0.#} ~ {High:0.#}{(Reverse ? " 反转" : "")}";
    }

    /// <summary>均值二值化（局部阈值）：每个像素跟自己邻域的均值比</summary>
    [PreprocessOperator("var_threshold", "均值二值化", "二值化", Order = 170,
        Description = "以局部均值为基准判决，适合光照不均的图。模板要大于目标特征尺寸")]
    public sealed class VarThresholdOperator : PreprocessOperator
    {
        private int _maskWidth = 15;
        private int _maskHeight = 15;
        private double _stdDevScale = 0.2;
        private double _absThreshold = 2;
        private VarThresholdMode _mode = VarThresholdMode.Light;

        [SuperDisplay(Name = "模板宽度", Group = new string[] { "参数" }, Order = 1, ColSpan = 4,
            Description = "计算局部均值的邻域宽度，需大于目标尺寸")]
        [RangeValidation(3, 999, "模板宽度需要在 3~999 之间")]
        public int MaskWidth
        {
            get => _maskWidth;
            set => SetParam(ref _maskWidth, value);
        }

        [SuperDisplay(Name = "模板高度", Group = new string[] { "参数" }, Order = 2, ColSpan = 4,
            Description = "计算局部均值的邻域高度，需大于目标尺寸")]
        [RangeValidation(3, 999, "模板高度需要在 3~999 之间")]
        public int MaskHeight
        {
            get => _maskHeight;
            set => SetParam(ref _maskHeight, value);
        }

        [SuperDisplay(Name = "标准差倍数", Group = new string[] { "参数" }, Order = 3, ColSpan = 4,
            Description = "判决偏移 = 局部标准度 × 本系数，越大越难被判为前景")]
        [RangeValidation(0, 10, "标准差倍数需要在 0~10 之间")]
        public double StdDevScale
        {
            get => _stdDevScale;
            set => SetParam(ref _stdDevScale, value);
        }

        [SuperDisplay(Name = "绝对阈值", Group = new string[] { "参数" }, Order = 4, ColSpan = 4,
            Description = "偏移量下限：局部平坦处至少差这么多才算前景（按图像位深填）")]
        public double AbsThreshold
        {
            get => _absThreshold;
            set => SetParam(ref _absThreshold, value);
        }

        [SuperDisplay(Name = "比较方式", Group = new string[] { "参数" }, Order = 5, ColSpan = 4,
            Description = "取比局部均值亮/暗的像素")]
        public VarThresholdMode Mode
        {
            get => _mode;
            set => SetParam(ref _mode, value);
        }

        protected override void Normalize()
        {
            MaskWidth = Clamp(MaskWidth, 3, 999);
            MaskHeight = Clamp(MaskHeight, 3, 999);
            StdDevScale = Clamp(StdDevScale, 0, 10);
            // AbsThreshold 不按 0~255 夹：uint2/real 的局部差值可以超过 255
        }

        protected override HImage Process(HImage input)
            => PreprocessHService.VarThreshold(input, MaskWidth, MaskHeight, StdDevScale, AbsThreshold, Mode);

        public override string Summary => $"{MaskWidth} × {MaskHeight} / {Desc(Mode)}";
    }
}

