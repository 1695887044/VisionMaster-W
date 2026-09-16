using HalconDotNet;
using Plugin.PreProcessing.Models;
using Plugin.PreProcessing.Services;
using UI.Attributes;

namespace Plugin.PreProcessing.Operators
{
    /// <summary>
    /// 固定框裁剪：在算子链中间"插一刀"，只把框内那块图往后传。
    ///
    /// 和 Plugin.CreateRoi 的 ROI 裁剪不是一回事，别搞混：
    /// 那个是独立流程节点，输出端口叫 Crop_{ROI名}，只能整节点替换；
    /// 本算子是链内的一步，能放在去噪之后、二值化之前，后面每一步都只看框内。
    /// 尺寸会变（输出宽=框宽、高=框高），下游若拿它和原图做像素级对齐要自己留意。
    /// </summary>
    [PreprocessOperator("crop_box", "固定框裁剪", "几何调整", Order = 41,
        Description = "按画布上拖出的正框裁出子图；框未画（宽高为 0）时原样透传")]
    public sealed class CropBoxOperator : BoxOperatorBase
    {
        protected override HImage ProcessBox(HImage input, int imageWidth, int imageHeight)
        {
            // 框正好盖住整张图：裁剪是恒等变换，透传省一次全图拷贝
            if (EffectRow == 0 && EffectCol == 0 && EffectWidth == imageWidth && EffectHeight == imageHeight)
                return input;

            // 参数顺序是 (row, column, width, height) —— 与 HDevelop 文档写的 (Height, Width) 相反，
            // 封装层注释里有实测数据，别在这里"按文档"改回去
            return PreprocessHService.Crop(input, EffectRow, EffectCol, EffectWidth, EffectHeight);
        }
    }

    /// <summary>
    /// 框外屏蔽：保留框内像素，框外整片涂成指定灰度。尺寸不变。
    ///
    /// 和裁剪的取舍：只要"别让下游看见框外"、又希望下游输出的坐标仍是原图坐标时，用这个；
    /// 想少算一半像素、提速，用裁剪。两者可以连用（先屏蔽再裁剪也行，只是裁剪后屏蔽已无意义）。
    /// </summary>
    [PreprocessOperator("mask_box", "框外屏蔽", "几何调整", Order = 43,
        Description = "框内保留原像素，框外涂成指定灰度；图像尺寸不变，坐标系与原图一致")]
    public sealed class MaskBoxOperator : BoxOperatorBase
    {
        private double _outsideGray;

        [SuperDisplay(Name = "框外灰度", GroupPath = "参数", Order = 1, ColSpan = 12,
            Description = "框外填充的灰度值。彩色图会把 R/G/B 三个通道都填成这个值，即一种灰色")]
        [RangeValidation(0, 255, "框外灰度需要在 0~255 之间")]
        public double OutsideGray
        {
            get => _outsideGray;
            set => SetParam(ref _outsideGray, value);
        }

        protected override void Normalize()
        {
            base.Normalize();
            OutsideGray = Clamp(OutsideGray, 0, 255);
        }

        protected override HImage ProcessBox(HImage input, int imageWidth, int imageHeight)
        {
            // 框盖住整张图 = 没有任何像素要被涂掉，透传
            if (EffectRow == 0 && EffectCol == 0 && EffectWidth == imageWidth && EffectHeight == imageHeight)
                return input;

            return PreprocessHService.MaskOutside(input, EffectRow, EffectCol,
                EffectWidth, EffectHeight, OutsideGray);
        }

        public override string Summary => IsBoxEmpty ? base.Summary : $"框外 {OutsideGray:0.#}  {base.Summary}";
    }
}
