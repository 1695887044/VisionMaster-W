using HalconDotNet;
using Plugin.PreProcessing.Models;
using Plugin.PreProcessing.Services;
using UI.Attributes;

namespace Plugin.PreProcessing.Operators
{
    /// <summary>均值滤波：W×H 邻域内取平均，平滑噪声同时会糊掉细节</summary>
    [PreprocessOperator("mean", "均值滤波", "滤波", Order = 50,
        Description = "模板内灰度求平均，抑制噪声但会模糊边缘")]
    public sealed class MeanOperator : RectMaskOperator
    {
        protected override HImage ProcessMask(HImage input, int width, int height)
            => PreprocessHService.Mean(input, width, height);
    }

    /// <summary>中值滤波：模板内取中值，去椒盐噪声且不糊边缘</summary>
    [PreprocessOperator("median", "中值滤波", "滤波", Order = 60,
        Description = "按模板半径取邻域中值，去椒盐噪声的同时保留边缘")]
    public sealed class MedianOperator : PreprocessOperator
    {
        private MedianMaskType _maskType = MedianMaskType.Square;
        private int _radius = 5;
        private MaskMarginMode _margin = MaskMarginMode.Mirrored;

        [SuperDisplay(Name = "模板形状", GroupPath = "参数", Order = 1, ColSpan = 4,
            Description = "Halcon 支持方形 / 圆形 / 菱形")]
        public MedianMaskType MaskType
        {
            get => _maskType;
            set => SetParam(ref _maskType, value);
        }

        [SuperDisplay(Name = "模板半径", GroupPath = "参数", Order = 2, ColSpan = 4,
            Description = "邻域半径，实际边长约为 2×半径+1")]
        [RangeValidation(1, 50, "模板半径需要在 1~50 之间")]
        public int Radius
        {
            get => _radius;
            set => SetParam(ref _radius, value);
        }

        [SuperDisplay(Name = "边缘扩充", GroupPath = "参数", Order = 3, ColSpan = 4,
            Description = "图像边界外的像素怎么补：镜像或循环")]
        public MaskMarginMode Margin
        {
            get => _margin;
            set => SetParam(ref _margin, value);
        }

        protected override void Normalize() => Radius = AtLeast(Radius, 1);

        protected override HImage Process(HImage input)
            => PreprocessHService.Median(input, MaskType, Radius, Margin);

        public override string Summary => $"{Desc(MaskType)} r={Radius}";
    }

    /// <summary>高斯滤波：加权平滑，比均值更自然</summary>
    [PreprocessOperator("gauss", "高斯滤波", "滤波", Order = 70,
        Description = "高斯核平滑，模板尺寸必须是 ≥3 的奇数")]
    public sealed class GaussOperator : PreprocessOperator
    {
        private int _size = 5;

        [SuperDisplay(Name = "模板尺寸", GroupPath = "参数", Order = 1, ColSpan = 12,
            Description = "必须是奇数，填偶数会自动 +1")]
        [RangeValidation(3, 101, "模板尺寸需要在 3~101 之间")]
        public int Size
        {
            get => _size;
            set => SetParam(ref _size, value);
        }

        protected override void Normalize() => Size = OddAtLeast(Size, 3);

        protected override HImage Process(HImage input) => PreprocessHService.Gauss(input, Size);

        public override string Summary => Size.ToString();
    }
}
