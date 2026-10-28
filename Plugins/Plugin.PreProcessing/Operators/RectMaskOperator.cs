using HalconDotNet;
using Plugin.PreProcessing.Models;
using Plugin.PreProcessing.Services;
using UI.Attributes;

namespace Plugin.PreProcessing.Operators
{
    /// <summary>
    /// "矩形模板 W×H"类算子的公共基类。
    ///
    /// 均值滤波和四个灰度形态学算子的参数长得一模一样。抄五遍的代价是：总有一遍把 Width/Height 写反
    /// （参考实现的灰度膨胀/灰度腐蚀就是这么接错的）。
    /// 把参数声明、纠偏、摘要统一放在这里，派生类只回答一个问题：
    /// "给你这张图和这两个尺寸，你调哪个 Halcon 算子"。
    /// 本类是抽象类且没贴 [PreprocessOperator]，注册表会自动跳过它，不会出现在算子菜单里。
    /// </summary>
    public abstract class RectMaskOperator : PreprocessOperator
    {
        private int _width = 5;
        private int _height = 5;

        [SuperDisplay(Name = "模板宽度", Group = new string[] { "参数" }, Order = 1, ColSpan = 6,
            Description = "矩形模板宽度（像素）")]
        [RangeValidation(1, 999, "模板宽度需要在 1~999 之间")]
        public int Width
        {
            get => _width;
            set => SetParam(ref _width, value);
        }

        [SuperDisplay(Name = "模板高度", Group = new string[] { "参数" }, Order = 2, ColSpan = 6,
            Description = "矩形模板高度（像素）")]
        [RangeValidation(1, 999, "模板高度需要在 1~999 之间")]
        public int Height
        {
            get => _height;
            set => SetParam(ref _height, value);
        }

        /// <summary>参数纠偏：模板尺寸最小 1，避免把 0 或负数喂给 Halcon</summary>
        protected override void Normalize()
        {
            Width = Clamp(Width, 1, 999);
            Height = Clamp(Height, 1, 999);
        }

        protected override HImage Process(HImage input)
        {
            // 不按图像尺寸悄悄缩小模板：HALCON 的 mean_image/形态学在模板过大时会抛 #3033/#1302，
            // 由插件的失败契约（FailedOperators / FailOnOperatorError）如实上抛。
            // 静默夹取会改变用户填的参数却不说，算出来的图与 HALCON 直接跑不一致。
            return ProcessMask(input, Width, Height);
        }

        /// <summary>派生类实现：宽高已由基类收敛，参数顺序由实现方在方法体内一次写对</summary>
        protected abstract HImage ProcessMask(HImage input, int width, int height);

        public override string Summary => $"{Width} × {Height}";
    }
}

