using System.ComponentModel.DataAnnotations;

namespace Plugin.PoseTransform
{
    #region 枚举

    /// <summary>
    /// 坐标变换模式。
    /// 两种模式的数学都是"刚体/仿射作用"，区别在作用对象与数据来源：
    ///   · 位姿跟随：基准 ROI（模板坐标系）× 当前位姿 → 当前坐标系 ROI（不吃标定，接 BlobDetect.MaskRegion）；
    ///   · 像素→机械：接「标定」插件的 Transform，把像素点/角度换算成机械坐标（引导/纠偏的最后一公里）。
    /// </summary>
    public enum TransformMode
    {
        /// <summary>位姿跟随：基准 ROI + (位姿 − 模板参考点) → 跟随 ROI（可选输出对齐图）</summary>
        [Display(Name = "位姿跟随")]
        FollowRoi = 0,

        /// <summary>像素 ↔ 机械：用九点标定矩阵把点/角度换成机械坐标（含反向回显自校验）</summary>
        [Display(Name = "像素→机械")]
        ToMechanical = 1
    }

    /// <summary>
    /// 状态消息级别：驱动配置界面信息栏/标定信息块的颜色（绿 / 橙 / 红）。
    /// 只描述"这条消息有多严重"，不参与任何流程逻辑（与「图像采集」「标定」同一口径）。
    /// </summary>
    public enum StatusLevel
    {
        /// <summary>正常信息（绿）</summary>
        Info,

        /// <summary>警告（橙）：还能继续，但需要注意</summary>
        Warning,

        /// <summary>错误（红）：当前配置不可用</summary>
        Error
    }

    #endregion
}
