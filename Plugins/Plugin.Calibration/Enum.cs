using System;
using System.ComponentModel.DataAnnotations;

namespace Plugin.Calibration
{
    #region 枚举

    /// <summary>
    /// 标定模式。
    ///
    /// 三种模式的产出都是 <see cref="Core.Interfaces.CalibrationTransform"/>，
    /// 区别只在"用什么方法得到变换"：
    ///   · 像素当量：只要 mm/px（不做机械坐标）——卡尺/Blob 的毫米换算来源；
    ///   · 九点标定：N×N 组 (机械坐标 ↔ 图像点) 求 2D 仿射——引导/纠偏类场景必需；
    ///   · 透视标定（二期①）：N 组点求 3×3 投影——相机倾斜/大视场下"近大远小"的场合（至少 4 点，推荐 3×3/4×4）；
    ///   · 网格标定（二期②）：N×N 节点三角化后逐格仿射——镜头畸变的工程近似（要求 N×N 全取点）。
    ///
    /// 注意：**点数（N×N）只是采样密度，不是模型**。仿射只有 6 个自由度，3 点即唯一解；
    /// 加密点数提高抗噪余量、也让残差更能暴露畸变，但不会把畸变"拟合掉"（见方案 §十一）。
    /// </summary>
    public enum CalibrationMode
    {
        /// <summary>像素当量：量一段已知长度，得到 mm/px</summary>
        [Display(Name = "像素当量")]
        PixelScale = 0,

        /// <summary>九点标定：N×N 组点求仿射（习惯叫"九点"，默认 3×3）</summary>
        [Display(Name = "九点标定")]
        NinePoint = 1,

        /// <summary>透视标定：N 组点求 3×3 投影（至少 4 点；覆盖视野四角+中心，推荐 3×3/4×4）</summary>
        [Display(Name = "透视标定")]
        Perspective = 2,

        /// <summary>
        /// 网格标定（分段仿射，工程近似）：N×N 节点三角化后逐格仿射，局部吸收镜头畸变。
        /// 与九点/透视的差别：**要求 N×N 每行都取点**（缺一个节点整格不可用）；
        /// 节点处精确通过（残差恒 0、无信息量）——质量区显示的是"同一批点上全局仿射基线"的残差，
        /// 用来判读畸变有多大、网格值不值得用（界面文案已写明）。
        /// </summary>
        [Display(Name = "网格标定")]
        Mesh = 3
    }

    /// <summary>
    /// 状态消息级别：驱动配置界面信息栏的颜色（绿 / 橙 / 红）。
    /// 只描述"这条消息有多严重"，不参与任何流程逻辑（与「图像采集」插件同一口径）。
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
