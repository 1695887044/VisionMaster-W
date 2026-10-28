using HalconDotNet;
using Plugin.PreProcessing.Models;
using Plugin.PreProcessing.Services;
using UI.Attributes;

namespace Plugin.PreProcessing.Operators
{
    /// <summary>
    /// FFT 频域滤波：把图变换到频域，按"到直流的距离"保留/去掉某个频带，再变换回来。
    ///
    /// 与空域滤波（均值/中值/高斯）的分工：
    /// 空域滤波只能按"邻域"办事，去不掉整幅图上的周期性干扰（摩尔纹、纱窗纹、扫描条纹）；
    /// 那类干扰在频谱上是一条孤立的亮点，只能在频域里"点掉"（陷波）。
    ///
    /// 【频率口径】归一化频率 0..0.5 cycles/pixel，0.5 = Nyquist：
    /// 低通只用「高频截止」，高通只用「低频截止」，带通/带阻两端都用，陷波用中心 + 半径。
    /// 与图像尺寸无关 —— 同一块周期性纹理在不同分辨率下要填的频率不同（分辨率翻倍则频率翻倍）。
    /// </summary>
    [PreprocessOperator("fft_filter", "FFT 频域滤波", "滤波", Order = 75,
        Description = "傅里叶变换后在频域做低通/高通/带通/带阻/陷波：去周期性纹理、只保留指定频带")]
    public sealed class FftFilterOperator : PreprocessOperator
    {
        private FftFilterType _filterType = FftFilterType.Lowpass;
        private FftFilterShape _filterShape = FftFilterShape.Gaussian;
        private double _cutoffLow = 0.05;
        private double _cutoffHigh = 0.15;
        private double _notchRow;
        private double _notchCol = 0.15;
        private double _notchRadius = 0.03;
        private bool _keepDc = true;

        [SuperDisplay(Name = "滤波类型", Group = new string[] { "参数" }, Order = 1, ColSpan = 12,
            Description = "低通=只留低频（平滑去噪）/ 高通=只留高频（突出边缘）/ 带通=只留环带 / 带阻=去掉环带 / 陷波=点掉指定方向的周期干扰")]
        public FftFilterType FilterType
        {
            get => _filterType;
            set => SetParam(ref _filterType, value);
        }

        [SuperDisplay(Name = "过渡形状", Group = new string[] { "参数" }, Order = 2, ColSpan = 12,
            Description = "理想=硬截止（边界振铃明显）/ 高斯=平滑过渡（无振铃，截止处 -6 dB）/ 巴特沃斯=二阶（比高斯陡、振铃小）")]
        public FftFilterShape FilterShape
        {
            get => _filterShape;
            set => SetParam(ref _filterShape, value);
        }

        [SuperDisplay(Name = "低频截止", Group = new string[] { "参数" }, Order = 3, ColSpan = 6,
            Description = "归一化频率 0~0.5（0.5 = Nyquist）。高通只用它；带通/带阻是环带内边界")]
        [RangeValidation(0, 0.5, "低频截止需要在 0~0.5 之间（0.5 = Nyquist）")]
        public double CutoffLow
        {
            get => _cutoffLow;
            set => SetParam(ref _cutoffLow, value);
        }

        [SuperDisplay(Name = "高频截止", Group = new string[] { "参数" }, Order = 4, ColSpan = 6,
            Description = "归一化频率 0~0.5（0.5 = Nyquist）。低通只用它；带通/带阻是环带外边界")]
        [RangeValidation(0, 0.5, "高频截止需要在 0~0.5 之间（0.5 = Nyquist）")]
        public double CutoffHigh
        {
            get => _cutoffHigh;
            set => SetParam(ref _cutoffHigh, value);
        }

        [SuperDisplay(Name = "陷波中心行", Group = new string[] { "参数" }, Order = 5, ColSpan = 6,
            Description = "仅陷波：归一化中心 -0.5~0.5，0 = 直流所在行。图像上的竖条纹干扰在行方向为 0")]
        [RangeValidation(-0.5, 0.5, "陷波中心行需要在 -0.5~0.5 之间")]
        public double NotchRow
        {
            get => _notchRow;
            set => SetParam(ref _notchRow, value);
        }

        [SuperDisplay(Name = "陷波中心列", Group = new string[] { "参数" }, Order = 6, ColSpan = 6,
            Description = "仅陷波：归一化中心 -0.5~0.5，0 = 直流所在列。条纹越密，这个值越大")]
        [RangeValidation(-0.5, 0.5, "陷波中心列需要在 -0.5~0.5 之间")]
        public double NotchCol
        {
            get => _notchCol;
            set => SetParam(ref _notchCol, value);
        }

        [SuperDisplay(Name = "陷波半径", Group = new string[] { "参数" }, Order = 7, ColSpan = 6,
            Description = "仅陷波：归一化半径 0~0.5。太小去不掉干扰，太大连同有用细节一起挖掉")]
        [RangeValidation(0, 0.5, "陷波半径需要在 0~0.5 之间")]
        public double NotchRadius
        {
            get => _notchRadius;
            set => SetParam(ref _notchRadius, value);
        }

        [SuperDisplay(Name = "保留直流", Group = new string[] { "参数" }, Order = 8, ColSpan = 6,
            Description = "仅陷波：勾选时陷波圈不会挖掉直流（0 频）分量。默认勾选，避免整幅图变暗/变亮")]
        public bool KeepDc
        {
            get => _keepDc;
            set => SetParam(ref _keepDc, value);
        }

        /// <summary>
        /// 只做文档化的夹取：频率夹到 0~0.5、半径夹到 0~0.5、中心夹到 ±0.5，
        /// 低/高截止填反时**只交换、不缩放**（与二值化上下限同一口径）。
        /// 不按图像尺寸悄悄改频率 —— 那会让"我填的值"和"实际生效的值"对不上。
        /// </summary>
        protected override void Normalize()
        {
            CutoffLow = Clamp(CutoffLow, 0.0, 0.5);
            CutoffHigh = Clamp(CutoffHigh, 0.0, 0.5);
            if (CutoffLow > CutoffHigh)
            {
                (CutoffLow, CutoffHigh) = (CutoffHigh, CutoffLow);
            }

            NotchRow = Clamp(NotchRow, -0.5, 0.5);
            NotchCol = Clamp(NotchCol, -0.5, 0.5);
            NotchRadius = Clamp(NotchRadius, 0.0, 0.5);
        }

        protected override HImage Process(HImage input)
            => PreprocessHService.FftFilter(input, FilterType, CutoffLow, CutoffHigh,
                NotchRow, NotchCol, NotchRadius, KeepDc, FilterShape);

        public override string Summary => FilterType switch
        {
            FftFilterType.Lowpass => $"{Desc(FilterShape)} {Desc(FilterType)} ≤ {CutoffHigh:0.###}",
            FftFilterType.Highpass => $"{Desc(FilterShape)} {Desc(FilterType)} ≥ {CutoffLow:0.###}",
            FftFilterType.Notch => $"{Desc(FilterShape)} {Desc(FilterType)} r={NotchRadius:0.###}",
            _ => $"{Desc(FilterShape)} {Desc(FilterType)} {CutoffLow:0.###}~{CutoffHigh:0.###}",
        };
    }
}
