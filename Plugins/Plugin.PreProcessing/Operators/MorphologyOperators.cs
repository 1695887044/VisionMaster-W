using HalconDotNet;
using Plugin.PreProcessing.Models;
using Plugin.PreProcessing.Services;

namespace Plugin.PreProcessing.Operators
{
    // 灰度形态学四兄弟：参数完全一样（矩形模板 W×H），差别只在调哪一个算子。
    //
    // 【参考实现在这里有个必须记住的 bug】
    // 旧 PerProcessingViewModel 的 switch 里：
    //     case 灰度膨胀: → 调的是 GrayErosion(…)
    //     case 灰度腐蚀: → 调的是 GrayDilation(…)
    // 两个分支的算法整个接反了，而且参数名（m_GrayDilationWidth / m_GrayErosionWidth）也跟着错位，
    // 所以从界面看不出毛病——只有拿标准图对比结果才会发现。
    // 现在一个算子一个类，膨胀类里只可能调到膨胀，从结构上消灭这类错误。

    /// <summary>灰度膨胀：邻域取最大值，亮区扩大（细纹变粗、暗孔被封）</summary>
    [PreprocessOperator("gray_dilation", "灰度膨胀", "灰度形态学", Order = 80,
        Description = "邻域内取最大值：亮区扩张，可用于填补暗色裂纹")]
    public sealed class GrayDilationOperator : RectMaskOperator
    {
        protected override HImage ProcessMask(HImage input, int width, int height)
            => PreprocessHService.GrayDilation(input, width, height);
    }

    /// <summary>灰度腐蚀：邻域取最小值，暗区扩大（亮点被吃掉）</summary>
    [PreprocessOperator("gray_erosion", "灰度腐蚀", "灰度形态学", Order = 90,
        Description = "邻域内取最小值：暗区扩张，可用于去掉细小亮点噪声")]
    public sealed class GrayErosionOperator : RectMaskOperator
    {
        protected override HImage ProcessMask(HImage input, int width, int height)
            => PreprocessHService.GrayErosion(input, width, height);
    }

    /// <summary>灰度开运算：先腐蚀后膨胀，去亮点噪声而不改变目标外形</summary>
    [PreprocessOperator("gray_opening", "灰度开运算", "灰度形态学", Order = 100,
        Description = "先腐蚀后膨胀：滤掉比模板小的亮结构（白点噪声）")]
    public sealed class GrayOpeningOperator : RectMaskOperator
    {
        protected override HImage ProcessMask(HImage input, int width, int height)
            => PreprocessHService.GrayOpening(input, width, height);
    }

    /// <summary>灰度闭运算：先膨胀后腐蚀，去暗点噪声</summary>
    [PreprocessOperator("gray_closing", "灰度闭运算", "灰度形态学", Order = 110,
        Description = "先膨胀后腐蚀：填掉比模板小的暗结构（黑点、细裂缝）")]
    public sealed class GrayClosingOperator : RectMaskOperator
    {
        protected override HImage ProcessMask(HImage input, int width, int height)
            => PreprocessHService.GrayClosing(input, width, height);
    }

    /// <summary>灰度顶帽：原图 − 开运算，只留下被开运算清掉的亮结构</summary>
    [PreprocessOperator("gray_top_hat", "灰度顶帽", "灰度形态学", Order = 115,
        Description = "原图 − 开运算：提取比模板小的亮结构（亮斑、毛刺、亮点），输出的是残差图")]
    public sealed class GrayTopHatOperator : RectMaskOperator
    {
        protected override HImage ProcessMask(HImage input, int width, int height)
            => PreprocessHService.GrayTopHat(input, width, height);
    }

    /// <summary>灰度黑帽：闭运算 − 原图，只留下被闭运算填掉的暗结构</summary>
    [PreprocessOperator("gray_black_hat", "灰度黑帽", "灰度形态学", Order = 116,
        Description = "闭运算 − 原图：提取比模板小的暗结构（暗点、裂纹、黑斑），输出的是残差图")]
    public sealed class GrayBlackHatOperator : RectMaskOperator
    {
        protected override HImage ProcessMask(HImage input, int width, int height)
            => PreprocessHService.GrayBlackHat(input, width, height);
    }
}
