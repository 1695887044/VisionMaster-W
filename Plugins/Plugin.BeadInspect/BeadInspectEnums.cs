using System.ComponentModel.DataAnnotations;

namespace Plugin.BeadInspect
{
    /// <summary>
    /// 对齐策略（方案说明书 §2 决策 4：三模式覆盖「固定 → 刚性 → 透视」全谱）。
    /// </summary>
    public enum BeadAlignMode
    {
        /// <summary>固定相机/治具：不做对齐，输入图即检测图</summary>
        [Display(Name = "固定相机（不对齐）")]
        None,

        /// <summary>接匹配插件的 AlignedImage（上游已完成位姿归一化）</summary>
        [Display(Name = "匹配位姿")]
        PoseFromMatching,

        /// <summary>内置平面可变形（投影）对齐：参考图建 planar 模型，运行期 find + projective_trans_image</summary>
        [Display(Name = "平面可变形（投影）")]
        PlanarDeformable,
    }

    /// <summary>
    /// 胶的明暗极性（HALCON create_bead_inspection_model 的 polarity 只能二选一，不支持同检两种，
    /// 见方案说明书第十章硬约束 3）。
    /// </summary>
    public enum BeadPolarity
    {
        /// <summary>暗胶：亮加工面上的一条暗线（范例口径）</summary>
        [Display(Name = "暗胶")]
        Dark,

        /// <summary>亮胶：暗底上的一条亮线</summary>
        [Display(Name = "亮胶")]
        Light,
    }

    /// <summary>
    /// 长度输出单位（模型参数永远以像素计，mm 换算只发生在输出层，见 §5.4）。
    /// </summary>
    public enum BeadUnit
    {
        /// <summary>像素（默认）</summary>
        [Display(Name = "像素")]
        Pixel,

        /// <summary>毫米：优先接标定插件的 Transform，回退 PixelSizeMm；两者皆无则 Fail</summary>
        [Display(Name = "毫米")]
        Mm,
    }
}
