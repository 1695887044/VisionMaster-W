using System.ComponentModel.DataAnnotations;

namespace Plugin.BlobDetect
{
    /// <summary>
    /// 二值化方式。
    ///
    /// 为什么要让用户选"方式"而不是直接暴露算子名：
    /// 三种分割思路分别对应工业现场三类场景（固定阈值看稳定打光的良品/不良品灰度差、
    /// 自动阈值面对来料灰度整体漂移、动态阈值面对光照不均），用户只需要回答
    /// "我的图是哪种情况"，不需要知道底层调用的是 threshold / binary_threshold / var_threshold。
    /// </summary>
    public enum ThresholdMode
    {
        /// <summary>固定阈值：直接给定灰度区间 [MinGray, MaxGray]</summary>
        [Display(Name = "固定阈值")]
        Fixed,

        /// <summary>自动阈值：最大类间方差法（Otsu）自动求分割点，无需额外参数</summary>
        [Display(Name = "自动阈值")]
        Auto,

        /// <summary>动态阈值：按局部邻域统计量分割，能吃掉光照不均/背景渐变</summary>
        [Display(Name = "动态阈值")]
        Dynamic,
    }

    /// <summary>
    /// 检测目标（缺陷的明暗极性）。
    ///
    /// 设计目的：把"亮缺陷/暗缺陷"这一条用户能理解的概念，统一映射到各方式自己的极性参数
    /// （binary_threshold / var_threshold 的 'light'/'dark'），
    /// 用户不必理解为什么不同算子对同一个"亮"要用不同的写法。
    /// 固定阈值方式下不需要它：灰度区间的上下限本身就把极性表达清楚了。
    /// </summary>
    public enum DetectTarget
    {
        /// <summary>亮缺陷：比背景亮的划痕/亮点</summary>
        [Display(Name = "亮缺陷")]
        Bright,

        /// <summary>暗缺陷：比背景暗的划痕/暗斑</summary>
        [Display(Name = "暗缺陷")]
        Dark,
    }

    /// <summary>
    /// 缺陷输出顺序。
    ///
    /// 为什么要有这一项：HALCON 区域数组本身没有"顺序"约定，
    /// 下游按索引取"第 N 个缺陷"时拿到的其实是算子内部顺序（不可预期、也不保证跨版本一致）。
    /// 现场报表/上位机常要求"最大缺陷排第一"或"按行列顺序编号"，所以在插件里定死顺序语义。
    /// </summary>
    public enum DefectSortMode
    {
        /// <summary>不排序：保持 HALCON 区域顺序（最快，但顺序不可预期）</summary>
        [Display(Name = "不排序（区域顺序）")]
        None,

        /// <summary>按面积从大到小：报表里最关心的最大缺陷排第一</summary>
        [Display(Name = "面积从大到小")]
        AreaDescending,

        /// <summary>从上到下、从左到右（先行后列）：与"读数顺序"一致，便于人工核对编号</summary>
        [Display(Name = "从上到下、从左到右")]
        RowColumn,
    }

    /// <summary>
    /// 配置界面信息栏的消息级别（只影响颜色，不参与任何流程逻辑）。
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
