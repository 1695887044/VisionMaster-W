using System.ComponentModel.DataAnnotations;

namespace Plugin.GeometryFinder
{
    /// <summary>几何查找类型。</summary>
    public enum GeometryFindKind
    {
        /// <summary>直线查找：亚像素轮廓拟合直线</summary>
        [Display(Name = "直线")]
        Line = 0,

        /// <summary>圆查找：亚像素轮廓拟合圆</summary>
        [Display(Name = "圆")]
        Circle = 1,

        /// <summary>椭圆查找：亚像素轮廓拟合椭圆</summary>
        [Display(Name = "椭圆")]
        Ellipse = 2,
    }

    /// <summary>
    /// 拟合鲁棒算法（line 与 circle 的 HALCON 值集不同，内部各自映射）。
    /// 默认 Tukey：对毛刺离群点大幅降权，是工业拟合的通用默认。
    ///
    /// 显示名一律中文在前：HALCON 的 'regression'/'huber'/'tukey' 只留在上面的注释里 ——
    /// 下拉框是给现场操作者看的，不是给查 HALCON 文档的人看的（Huber/Tukey 是人名，无通用译名，保留原字）。
    /// </summary>
    public enum FitAlgorithmKind
    {
        [Display(Name = "最小二乘")]
        Regression = 0,

        [Display(Name = "Huber 抗离群")]
        Huber = 1,

        [Display(Name = "Tukey 抗离群（推荐）")]
        Tukey = 2,
    }
}
