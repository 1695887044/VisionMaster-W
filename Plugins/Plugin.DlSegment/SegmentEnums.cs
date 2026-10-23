using System.ComponentModel.DataAnnotations;

namespace Plugin.DlSegment
{
    /// <summary>
    /// 缩放模式：怎么把任意尺寸的输入图塞进模型的固定输入尺寸。
    ///
    /// 【判据】默认 letterbox（等比缩放 + 补边）。分割是逐像素判定，
    /// stretch 会把长宽比改掉——同一块缺陷在原图是圆的、被拉伸后模型看到的是椭圆，
    /// 与训练时的形状分布不一致，边缘像素容易判错。只有在模型训练时就是用 stretch 喂的
    /// （很多语义分割框架默认就是直接 resize）才切过去。
    /// </summary>
    public enum SegmentResizeMode
    {
        /// <summary>等比缩放后补边到目标尺寸，保持长宽比。推荐。</summary>
        [Display(Name = "Letterbox · 等比补边（推荐）")]
        Letterbox = 0,

        /// <summary>直接拉伸到目标尺寸，不补边。长宽比会变。</summary>
        [Display(Name = "Stretch · 拉伸填充")]
        Stretch = 1
    }

    /// <summary>
    /// 类别图的反向缩放插值。
    ///
    /// 【判据】默认 nearest。类别图里每个像素值是"类别序号"，不是连续量：
    /// 双线性插值会把 0 和 1 之类的序号插出 0.5 这种不存在的类别，
    /// 让下游按类别面积统计时凭空多出一类。只有在只关心轮廓、不关心面积的场合才考虑双线性。
    /// </summary>
    public enum SegmentInterpolation
    {
        /// <summary>最近邻。类别序号不会被打乱。推荐。</summary>
        [Display(Name = "Nearest · 最近邻（推荐）")]
        Nearest = 0,

        /// <summary>双线性。会在类别边界上插出不存在的中间值，慎用。</summary>
        [Display(Name = "Bilinear · 双线性")]
        Bilinear = 1
    }

    /// <summary>
    /// 输出区域的口径：模型看到的是整幅图，但我们可能只关心某个类别的区域。
    ///
    /// 【判据】默认 AllClasses（把所有非背景类都合成一个前景区域）。
    /// 想只取某一类时用 SingleClass + ClassIndex。
    /// </summary>
    public enum SegmentRegionMode
    {
        /// <summary>所有非背景类的像素合并成一个区域。默认。</summary>
        [Display(Name = "全部前景（所有非背景类合并）")]
        AllClasses = 0,

        /// <summary>只取指定类别（ClassIndex）的像素。</summary>
        [Display(Name = "只取指定类别")]
        SingleClass = 1
    }
}
