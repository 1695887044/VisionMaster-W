using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Plugin.ImageAlign
{
    /// <summary>对齐模式。v1 只做“点对”和“消费上游位姿”，不自己跑模板匹配。</summary>
    public enum AlignMode
    {
        /// <summary>2~4 对点：2 点刚性、3 点仿射、4 点透视，按点数自动选。</summary>
        [Display(Name = "基准点（2~4 点对）")]
        [Description("基准点（2~12 点对）")]
        PointPairs = 0,

        /// <summary>手动四点：必须正好 4 对点，只走透视；少于 4 点直接失败，不退化成仿射。</summary>
        [Display(Name = "手动四点（透视）")]
        [Description("手动四点（透视，必须 4 对）")]
        ManualFourPoint = 1,

        /// <summary>消费上游位姿：接 HomMat2D（参考→当前）或 PoseRow/Column/Angle（弧度）。</summary>
        [Display(Name = "上游位姿（刚性）")]
        [Description("上游位姿（刚性）")]
        TemplatePose = 2,
    }

    /// <summary>重采样插值方式。</summary>
    public enum AlignInterpolation
    {
        /// <summary>双线性：边缘更平滑，推荐。</summary>
        [Display(Name = "双线性")]
        [Description("双线性（推荐）")]
        Bilinear = 0,

        /// <summary>最近邻：不引入新灰度，适合标签/二值图。</summary>
        [Display(Name = "最近邻")]
        [Description("最近邻（标签、二值图）")]
        NearestNeighbor = 1,
    }
}

