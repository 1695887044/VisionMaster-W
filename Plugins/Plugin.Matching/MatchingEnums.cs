using System.ComponentModel.DataAnnotations;

namespace Plugin.Matching
{
    /// <summary>
    /// 配置界面信息栏的消息级别（只影响颜色，不参与任何匹配逻辑）。
    /// 与 BlobDetect 的同名枚举同语义、各插件自带一份（插件间不共享类型，避免跨程序集耦合）。
    /// </summary>
    public enum StatusLevel
    {
        /// <summary>正常（绿）：模板已创建、路径有效</summary>
        Info,

        /// <summary>提示（橙）：还没画模板/没载图等"还能继续"的状态</summary>
        Warning,

        /// <summary>错误（红）：载图失败、创建模板失败</summary>
        Error,
    }

    /// <summary>
    /// 匹配算法（每个模板条目独立选择：创建期定死，改它必须重新学习）。
    ///
    /// - <see cref="Shape"/>：基于形状（<c>create_shape_model</c> / <c>find_shape_model</c>），
    ///   认边缘几何、对光照变化稳健，能测旋转与缩放。**出厂默认，绝大多数场景用这个**。
    /// - <see cref="Ncc"/>：基于归一化互相关（<c>create_ncc_model</c> / <c>find_ncc_model</c>），
    ///   认灰度纹理，对**没有清晰边缘、只有灰度纹理差异**的目标（织物、印刷底色、低对比度膜面）
    ///   比形状匹配稳；缺点是对光照/对比度变化敏感，且**不能测缩放**（HALCON 无缩放版 NCC 模型）。
    ///
    /// 显示名走中文（下拉框给操作者看），传给 HALCON 的由 plugin 按成员 switch 生成。
    /// </summary>
    public enum MatchingAlgorithm
    {
        /// <summary>基于形状（默认）</summary>
        [Display(Name = "基于形状（边缘几何）")]
        Shape,

        /// <summary>基于灰度相关（NCC）</summary>
        [Display(Name = "基于灰度相关（NCC）")]
        Ncc,
    }
}
