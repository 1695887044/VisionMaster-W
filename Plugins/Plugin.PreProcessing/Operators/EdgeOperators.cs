using HalconDotNet;
using Plugin.PreProcessing.Models;
using Plugin.PreProcessing.Services;
using UI.Attributes;

namespace Plugin.PreProcessing.Operators
{
    /// <summary>
    /// 边缘检测算子的公共基类：只放"模板尺寸"这一个共同参数与收敛规则。
    /// 抽象类且不贴 [PreprocessOperator]，注册表会跳过它，不会出现在算子库。
    /// </summary>
    public abstract class EdgeOperatorBase : PreprocessOperator
    {
        private int _maskSize = 3;

        [SuperDisplay(Name = "模板尺寸", Group = new string[] { "参数" }, Order = 1, ColSpan = 6,
            Description = "滤波掩膜边长，3~11 的奇数（偶数自动 +1，超出夹取）")]
        [RangeValidation(3, 11, "模板尺寸需要在 3~11 之间（奇数）")]
        public int MaskSize
        {
            get => _maskSize;
            set => SetParam(ref _maskSize, value);
        }

        /// <summary>Halcon 的 sobel_amp / laplace 只接受 3/5/7/9/11；这里统一收敛成正奇数</summary>
        protected override void Normalize()
        {
            int size = OddAtLeast(MaskSize, 3);
            MaskSize = size > 11 ? 11 : size;
        }
    }

    /// <summary>
    /// Sobel 边缘：梯度幅值图。适合"边缘在哪、有多强"，
    /// 不负责亚像素定位——要测尺寸请接卡尺/几何查找。
    /// </summary>
    [PreprocessOperator("sobel_edge", "Sobel 边缘", "边缘检测", Order = 180,
        Description = "Sobel 梯度幅值边缘图；模板越大越平滑，sum_abs 比 sum 更稳")]
    public sealed class SobelEdgeOperator : EdgeOperatorBase
    {
        private SobelFilterType _filterType = SobelFilterType.SumAbs;

        [SuperDisplay(Name = "滤波类型", Group = new string[] { "参数" }, Order = 2, ColSpan = 6,
            Description = "绝对值和：|Gx|+|Gy|，抗抵消；代数和：Gx+Gy，可能抵消")]
        public SobelFilterType FilterType
        {
            get => _filterType;
            set => SetParam(ref _filterType, value);
        }

        protected override HImage Process(HImage input)
        {
            // 边缘图统一单通道：彩色图逐通道求梯度会得到三张边缘图，下游阈值/Blob 不好接。
            HImage mono = PreprocessHService.EnsureMono(input);
            try { return PreprocessHService.SobelAmp(mono, FilterType, MaskSize); }
            finally { if (!object.ReferenceEquals(mono, input)) mono.Dispose(); }
        }

        public override string Summary => $"{MaskSize}×{MaskSize} · {Desc(FilterType)}";
    }

    /// <summary>
    /// Laplace 边缘：二阶差分绝对图。对噪声比 Sobel 更敏感，
    /// 建议链上前一步先做高斯/中值滤波。
    /// </summary>
    [PreprocessOperator("laplace_edge", "Laplace 边缘", "边缘检测", Order = 181,
        Description = "Laplace 二阶差分绝对边缘图；对噪声更敏感，建议先滤波")]
    public sealed class LaplaceEdgeOperator : EdgeOperatorBase
    {
        private LaplaceFilterMask _filterMask = LaplaceFilterMask.N4;

        [SuperDisplay(Name = "邻域", Group = new string[] { "参数" }, Order = 2, ColSpan = 6,
            Description = "4 邻域（n_4）/ 8 邻域（n_8，更各向同性也更敏感）")]
        public LaplaceFilterMask FilterMask
        {
            get => _filterMask;
            set => SetParam(ref _filterMask, value);
        }

        protected override HImage Process(HImage input)
        {
            HImage mono = PreprocessHService.EnsureMono(input);
            try { return PreprocessHService.Laplace(mono, FilterMask, MaskSize); }
            finally { if (!object.ReferenceEquals(mono, input)) mono.Dispose(); }
        }

        public override string Summary => $"{MaskSize}×{MaskSize} · {Desc(FilterMask)}";
    }
}
