using HalconDotNet;
using Plugin.PreProcessing.Models;
using Plugin.PreProcessing.Services;
using UI.Attributes;

namespace Plugin.PreProcessing.Operators
{
    /// <summary>锐化（emphasize）：邻域内做非锐化掩模，把边缘"抠"出来</summary>
    [PreprocessOperator("emphasize", "锐化", "图像增强", Order = 120,
        Description = "邻域非锐化掩模，增强边缘。因子过大画面会出现白边")]
    public sealed class SharpenOperator : PreprocessOperator
    {
        private int _width = 7;
        private int _height = 7;
        private double _factor = 1.0;

        [SuperDisplay(Name = "模板宽度", GroupPath = "参数", Order = 1, ColSpan = 4,
            Description = "必须是奇数")]
        [RangeValidation(3, 201, "模板宽度需要在 3~201 之间")]
        public int Width
        {
            get => _width;
            set => SetParam(ref _width, value);
        }

        [SuperDisplay(Name = "模板高度", GroupPath = "参数", Order = 2, ColSpan = 4,
            Description = "必须是奇数")]
        [RangeValidation(3, 201, "模板高度需要在 3~201 之间")]
        public int Height
        {
            get => _height;
            set => SetParam(ref _height, value);
        }

        [SuperDisplay(Name = "增强因子", GroupPath = "参数", Order = 3, ColSpan = 4,
            Description = "越大越锐，参考范围 0.3~20")]
        [RangeValidation(0, 50, "增强因子需要在 0~50 之间")]
        public double Factor
        {
            get => _factor;
            set => SetParam(ref _factor, value);
        }

        protected override void Normalize()
        {
            Width = OddAtLeast(Width, 3);
            Height = OddAtLeast(Height, 3);
            Factor = Clamp(Factor, 0.0, 50.0);
        }

        protected override HImage Process(HImage input)
            => PreprocessHService.Emphasize(input, Width, Height, Factor);

        public override string Summary => $"{Width} × {Height} / {Factor:0.##}";
    }

    /// <summary>对比度（illuminate）：局部均值增强，改善明暗不均</summary>
    [PreprocessOperator("illuminate", "对比度", "图像增强", Order = 130,
        Description = "局部光照增强，改善打光不均。因子必须落在 0~1 之间（Halcon 限制）")]
    public sealed class ContrastOperator : PreprocessOperator
    {
        private int _width = 101;
        private int _height = 101;
        private double _factor = 0.7;

        [SuperDisplay(Name = "模板宽度", GroupPath = "参数", Order = 1, ColSpan = 4,
            Description = "取远大于目标特征的尺寸，必须是奇数")]
        [RangeValidation(3, 999, "模板宽度需要在 3~999 之间")]
        public int Width
        {
            get => _width;
            set => SetParam(ref _width, value);
        }

        [SuperDisplay(Name = "模板高度", GroupPath = "参数", Order = 2, ColSpan = 4,
            Description = "取远大于目标特征的尺寸，必须是奇数")]
        [RangeValidation(3, 999, "模板高度需要在 3~999 之间")]
        public int Height
        {
            get => _height;
            set => SetParam(ref _height, value);
        }

        [SuperDisplay(Name = "增强因子", GroupPath = "参数", Order = 3, ColSpan = 4,
            Description = "Halcon 要求 0~1，界面旧版给到 5 会直接报错，这里自动收敛")]
        [RangeValidation(0, 1, "增强因子需要在 0~1 之间")]
        public double Factor
        {
            get => _factor;
            set => SetParam(ref _factor, value);
        }

        protected override void Normalize()
        {
            Width = OddAtLeast(Width, 3);
            Height = OddAtLeast(Height, 3);
            // Halcon 的 illuminate 只接受开区间 (0,1)，端点会抛异常
            Factor = Clamp(Factor, 0.01, 0.99);
        }

        protected override HImage Process(HImage input)
            => PreprocessHService.Illuminate(input, Width, Height, Factor);

        public override string Summary => $"{Width} × {Height} / {Factor:0.##}";
    }

    /// <summary>亮度调节（scale_image）：out = in × 倍数 + 偏移</summary>
    [PreprocessOperator("brightness", "亮度调节", "图像增强", Order = 140,
        Description = "线性变换：倍数控制对比度，偏移控制明暗。超出 0~255 会被截断")]
    public sealed class BrightnessOperator : PreprocessOperator
    {
        private double _multiplier = 1.0;
        private double _addend;

        [SuperDisplay(Name = "倍数(Mult)", GroupPath = "参数", Order = 1, ColSpan = 6,
            Description = "灰度线性放大倍数，>1 提对比、<1 压对比")]
        [RangeValidation(0, 100, "倍数需要在 0~100 之间")]
        public double Multiplier
        {
            get => _multiplier;
            set => SetParam(ref _multiplier, value);
        }

        [SuperDisplay(Name = "偏移(Add)", GroupPath = "参数", Order = 2, ColSpan = 6,
            Description = "整体加一个灰度值，正数变亮、负数变暗")]
        [RangeValidation(-255, 255, "偏移需要在 -255~255 之间")]
        public double Addend
        {
            get => _addend;
            set => SetParam(ref _addend, value);
        }

        protected override HImage Process(HImage input)
            => PreprocessHService.ScaleIntensity(input, Multiplier, Addend);

        public override string Summary => $"×{Multiplier:0.###}  {Addend:+0.##;-0.##;0}";
    }

    /// <summary>反色（invert_image）：灰度取反，亮暗互换</summary>
    [PreprocessOperator("invert", "反色", "图像增强", Order = 150,
        Description = "灰度取反：暗背景上的亮目标可转成亮背景，便于后续阈值处理")]
    public sealed class InvertOperator : PreprocessOperator
    {
        protected override HImage Process(HImage input) => PreprocessHService.Invert(input);

        public override string Summary => "亮暗互换";
    }

    /// <summary>
    /// 全局直方图均衡（equ_histo_image）：把灰度分布摊开到整个 0~255 动态范围。
    ///
    /// 什么时候用：来料光照忽明忽暗、目标与背景的灰度差被"挤"在很窄的一段里，
    /// 阈值怎么调都不干净 —— 先均衡，把有效灰度差拉满，再二值化就轻松了。
    /// 什么时候别用：图像本身对比度已经足够，均衡会把噪声一起放大。
    /// </summary>
    [PreprocessOperator("equ_histo", "直方图均衡", "图像增强", Order = 145,
        Description = "全局直方图均衡，拉伸灰度动态范围；彩色图三个通道各自均衡，可能轻微偏色")]
    public sealed class EquHistoOperator : PreprocessOperator
    {
        // 无可调参数：equ_histo_image 本身就是"零参数"算子，硬凑一个只会误导现场
        protected override HImage Process(HImage input) => PreprocessHService.EquHisto(input);

        public override string Summary => "灰度动态范围拉伸";
    }
}
