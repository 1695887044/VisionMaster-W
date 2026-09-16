using HalconDotNet;
using Plugin.PreProcessing.Models;
using Plugin.PreProcessing.Services;
using UI.Attributes;

namespace Plugin.PreProcessing.Operators
{
    /// <summary>图像镜像（左右 / 上下 / 对角翻转）</summary>
    [PreprocessOperator("mirror", "图像镜像", "几何调整", Order = 20,
        Description = "按水平、垂直或对角方向翻转图像")]
    public sealed class MirrorOperator : PreprocessOperator
    {
        private MirrorMode _mode = MirrorMode.Horizontal;

        [SuperDisplay(Name = "镜像方式", GroupPath = "参数", Order = 1, ColSpan = 12,
            Description = "对角镜像会把宽高互换")]
        public MirrorMode Mode
        {
            get => _mode;
            set => SetParam(ref _mode, value);
        }

        protected override HImage Process(HImage input) => PreprocessHService.Mirror(input, Mode);

        public override string Summary => Desc(Mode);
    }

    /// <summary>图像旋转（90 / 180 / 270 度整角度）</summary>
    [PreprocessOperator("rotate", "图像旋转", "几何调整", Order = 30,
        Description = "按 90 度整数倍旋转。Halcon 的 rotate_image 不改画布大小，90/270 度时超出原画布的部分会被裁掉")]
    public sealed class RotateOperator : PreprocessOperator
    {
        private RotateAngle _angle = RotateAngle.Angle180;

        [SuperDisplay(Name = "旋转角度", GroupPath = "参数", Order = 1, ColSpan = 12,
            Description = "旋转角度；90/270 度需留意图像四角被裁切")]
        public RotateAngle Angle
        {
            get => _angle;
            set => SetParam(ref _angle, value);
        }

        protected override HImage Process(HImage input) => PreprocessHService.Rotate(input, (int)Angle);

        public override string Summary => $"{(int)Angle}°";
    }

    /// <summary>修改图像尺寸（放大补边 / 缩小裁边，不做缩放）</summary>
    [PreprocessOperator("change_format", "修改图像尺寸", "几何调整", Order = 40,
        Description = "把画布改成指定宽高：超出部分裁掉，不足部分补 0。填 0 表示该方向保持原尺寸")]
    public sealed class ChangeFormatOperator : PreprocessOperator
    {
        private int _width;
        private int _height;

        [SuperDisplay(Name = "目标宽度", GroupPath = "参数", Order = 1, ColSpan = 6,
            Description = "0 = 沿用原图宽度")]
        [RangeValidation(0, 100000, "目标宽度不合法")]
        public int Width
        {
            get => _width;
            set => SetParam(ref _width, value);
        }

        [SuperDisplay(Name = "目标高度", GroupPath = "参数", Order = 2, ColSpan = 6,
            Description = "0 = 沿用原图高度")]
        [RangeValidation(0, 100000, "目标高度不合法")]
        public int Height
        {
            get => _height;
            set => SetParam(ref _height, value);
        }

        protected override HImage Process(HImage input)
        {
            PreprocessHService.ImageSize(input, out var srcW, out var srcH);
            var w = Width > 0 ? Width : srcW;
            var h = Height > 0 ? Height : srcH;

            // 尺寸没变就是空操作：直接透传，不白造一张图
            if (w == srcW && h == srcH) return input;

            return PreprocessHService.ChangeFormat(input, w, h);
        }

        public override string Summary => $"{(Width > 0 ? Width.ToString() : "原宽")} × " +
                                         $"{(Height > 0 ? Height.ToString() : "原高")}";
    }

    /// <summary>
    /// 按比例缩放（zoom_image_factor）：行列方向各用独立比例。
    ///
    /// 【为什么拆成两个因子】现场最常见的两类需求根本不是一个数：
    /// 纵向线扫图像常在"行方向"过采样，只需压行；而做细节检测时会把局部双向放大。
    /// 单一比例因子满足不了，拆开后各管各的，也不影响"只想等比缩放"时把两个填成一样。
    ///
    /// 【实测坑】封装层收到的第一个数是 scaleWidth（作用在列/宽度上），
    /// 这里对外仍按"行因子 / 列因子"表达，映射在 PreprocessHService.Zoom 内部完成。
    /// </summary>
    [PreprocessOperator("zoom_factor", "按比例缩放", "几何调整", Order = 42,
        Description = "行、列方向分别按比例缩放。缩小常用于提速，放大用于看细节")]
    public sealed class ZoomFactorOperator : PreprocessOperator
    {
        // 实测：因子填 0 或负数直接抛 #1301，所以夹取下界给到 0.01 而不是 0
        private const double MinFactor = 0.01;
        private const double MaxFactor = 20.0;

        private double _rowFactor = 1.0;
        private double _colFactor = 1.0;
        private ZoomInterpolation _interpolation = ZoomInterpolation.Bilinear;

        [SuperDisplay(Name = "行方向因子", GroupPath = "参数", Order = 1, ColSpan = 4,
            Description = "垂直方向（高度）缩放比例，>1 放大、<1 缩小")]
        [RangeValidation(MinFactor, MaxFactor, "行方向因子需要在 0.01~20 之间")]
        public double RowFactor
        {
            get => _rowFactor;
            set => SetParam(ref _rowFactor, value);
        }

        [SuperDisplay(Name = "列方向因子", GroupPath = "参数", Order = 2, ColSpan = 4,
            Description = "水平方向（宽度）缩放比例，>1 放大、<1 缩小")]
        [RangeValidation(MinFactor, MaxFactor, "列方向因子需要在 0.01~20 之间")]
        public double ColFactor
        {
            get => _colFactor;
            set => SetParam(ref _colFactor, value);
        }

        [SuperDisplay(Name = "插值方式", GroupPath = "参数", Order = 3, ColSpan = 4,
            Description = "放大看细节选双三次，纯提速缩小用最近邻即可")]
        public ZoomInterpolation Interpolation
        {
            get => _interpolation;
            set => SetParam(ref _interpolation, value);
        }

        protected override void Normalize()
        {
            RowFactor = Clamp(RowFactor, MinFactor, MaxFactor);
            ColFactor = Clamp(ColFactor, MinFactor, MaxFactor);
        }

        protected override HImage Process(HImage input)
        {
            // 两个因子都是 1：缩放是恒等变换，透传省一张全图
            if (System.Math.Abs(RowFactor - 1) < 1e-9 && System.Math.Abs(ColFactor - 1) < 1e-9)
                return input;

            return PreprocessHService.Zoom(input, RowFactor, ColFactor, Interpolation);
        }

        public override string Summary => $"行 ×{RowFactor:0.###}  列 ×{ColFactor:0.###}  {Desc(Interpolation)}";
    }
}
