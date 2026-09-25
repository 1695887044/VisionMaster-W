using System.ComponentModel.DataAnnotations;

namespace Plugin.CaliperMeasure
{
    /// <summary>
    /// 测量类型（第二批起共 5 种：前两种不涉及拟合，后三种都要先做几何拟合）。
    /// </summary>
    public enum MeasureKind
    {
        /// <summary>宽度/间隙：1 个搜索区，measure_pairs 直接给双边距离</summary>
        [Display(Name = "宽度/间隙")]
        Width,

        /// <summary>两点距：2 个搜索区，各用 measure_pos 找一个边缘点，再 distance_pp</summary>
        [Display(Name = "两点距")]
        PointToPoint,

        /// <summary>
        /// 点到线距：2 个搜索区——区1 出"点"（各卡尺边点取平均成代表点），
        /// 区2 的 N 个边点拟合直线（fit_line_contour_xld），distance_pl 取点到直线的垂距。
        /// </summary>
        [Display(Name = "点到线距")]
        PointToLineDistance,

        /// <summary>
        /// 圆直径：1 个圆形搜索区 + 环宽参数。沿圆周径向均布 N 把卡尺（扫描方向=径向，
        /// 圆的边缘垂直于径向，与"边缘垂直于 L1 轴"的实证语义自洽）→ fit_circle_contour_xld → 直径=2×半径。
        /// </summary>
        [Display(Name = "圆直径")]
        CircleDiameter,

        /// <summary>
        /// 角度：2 个搜索区各拟合一条直线 → angle_ll → 归一化到 0~180°（工业习惯）。
        /// 结果单位是"度"，是量纲无关量，不受像素当量影响。
        /// </summary>
        [Display(Name = "角度")]
        Angle,
    }

    /// <summary>
    /// 直线/圆拟合的鲁棒算法（fit_line_contour_xld / fit_circle_contour_xld 的 Algorithm 参数）。
    ///
    /// 默认 Tukey 的依据：卡尺偶尔扫到毛刺会产生离群点，最小二乘（regression）会被一个坏点
    /// 拽歪整条线/圆；Tukey 对残差超限的点大幅降权（坏点几乎不参与拟合），是工业拟合的通用默认。
    /// </summary>
    public enum FitAlgorithmKind
    {
        /// <summary>最小二乘（HALCON 'regression'）：所有点等权，最快但对离群点零抵抗</summary>
        [Display(Name = "最小二乘(regression)")]
        Regression,

        /// <summary>Huber（HALCON 'huber'）：小残差线性降权、大残差常数限幅，折中方案</summary>
        [Display(Name = "Huber(抗离群)")]
        Huber,

        /// <summary>Tukey（HALCON 'tukey'）：大残差点几乎不参与拟合，抗毛刺能力最强（默认）</summary>
        [Display(Name = "Tukey(抗离群,推荐)")]
        Tukey,
    }

    /// <summary>
    /// 边缘极性：沿卡尺扫描方向，允许哪种明暗跳变。
    ///
    /// 为什么用"暗→亮/亮→暗"这种说法而不是直接暴露 HALCON 的 positive/negative：
    /// 操作员看图只会判断"灰的变白的还是白的变灰的"，不需要知道算子里叫 positive 还是 negative。
    /// 内部统一映射到 measure 的 Transition 参数。
    /// </summary>
    public enum EdgePolarity
    {
        /// <summary>暗→亮（HALCON transition = 'positive'）</summary>
        [Display(Name = "暗→亮")]
        DarkToLight,

        /// <summary>亮→暗（HALCON transition = 'negative'）</summary>
        [Display(Name = "亮→暗")]
        LightToDark,

        /// <summary>全部（HALCON transition = 'all'）：不确定极性时的起点</summary>
        [Display(Name = "全部")]
        All,
    }

    /// <summary>
    /// 每条卡尺上有多条边缘时的取舍：取第一条 / 最后一条 / 全部。
    /// 对应 measure 算子的 Select 参数——直接决定"一条卡尺出几个点"。
    /// </summary>
    public enum EdgeSelect
    {
        /// <summary>第一条（默认）：按扫描方向遇到的第一个跳变</summary>
        [Display(Name = "第一条")]
        First,

        /// <summary>最后一条</summary>
        [Display(Name = "最后一条")]
        Last,

        /// <summary>全部：一条卡尺可能给出多个点（配合"全部"极性要留意结果口径）</summary>
        [Display(Name = "全部")]
        All,
    }

    /// <summary>
    /// 亚像素插值方式（直接对应 HALCON gen_measure_rectangle2 的 Interpolation 参数）。
    /// 默认 bilinear：精度与速度的通用折中；nearest 最快但只有整像素，bicubic 最平滑但最慢。
    /// </summary>
    public enum EdgeInterpolation
    {
        /// <summary>最近邻（整像素，最快）</summary>
        [Display(Name = "nearest")]
        Nearest,

        /// <summary>双线性（亚像素，通用默认）</summary>
        [Display(Name = "bilinear")]
        Bilinear,

        /// <summary>双三次（亚像素，最平滑）</summary>
        [Display(Name = "bicubic")]
        Bicubic,
    }

    /// <summary>
    /// 配置界面信息栏的消息级别（只影响颜色，不参与流程逻辑）。
    /// </summary>
    public enum StatusLevel
    {
        /// <summary>正常（绿）：预览成功、判定信息</summary>
        Info,

        /// <summary>提示（橙）：还没接图像等"还能继续"的状态</summary>
        Warning,

        /// <summary>错误（红）：输入非法、算法失败</summary>
        Error,
    }
}
