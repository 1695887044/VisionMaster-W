using System.ComponentModel.DataAnnotations;

namespace Plugin.ImageDiff
{
    /// <summary>
    /// 差分算法：决定"两幅图怎么比"。
    ///
    /// 为什么把算法做成枚举而不是"一条链"：本插件是**双图**算术/差分，链式算子（如
    /// Plugin.PreProcessing 的算子树）每一步只有单图输入，承载不了第二张图与两图对齐；
    /// 一次性把"怎么比"选清楚，现场才看得懂结果口径。
    /// </summary>
    public enum DiffAlgorithm
    {
        /// <summary>
        /// 绝对灰度差：abs(image1 - image2)。最常用、最好解释——亮的地方就是"不一样"的地方，
        /// 与"谁减谁"无关（对称），因此不会因为换了基准图方向而改变结果。
        /// </summary>
        [Display(Name = "绝对差（abs_image1 - image2|）")]
        Absolute = 0,

        /// <summary>
        /// 有符号差：image1 - image2，再整体平移到 128 附近显示。
        /// 用在哪：要知道"是变亮了还是变暗了"的场合（如判断凹陷/凸起、判断印刷深浅偏移）。
        /// 注意：判定用绝对值，正负只影响显示与"最大值方向"的解读。
        /// </summary>
        [Display(Name = "有符号差（image1 − image2）")]
        Signed = 1,

        /// <summary>
        /// 相对差（按参考图归一化的百分比灰阶差）：|a−b| / max(ref, 1) × 100。
        /// 用在哪：参考图本身明暗跨度大（有高光也有阴影）时，绝对差会把"高光区的正常波动"
        /// 判成缺陷，相对差则按局部亮度自适应。**代价**：参考图接近全黑处分母趋 0，
        /// 那里会被放大成全白噪声，故先用插件参数 AmbientFloor（环境亮度下限）做分母保护。
        /// </summary>
        [Display(Name = "相对差（按参考图亮度归一化）")]
        Relative = 2,
    }

    /// <summary>
    /// 判定结果的极性：差分图上"亮"代表差异，但不同场合关心的方向不同。
    /// </summary>
    public enum DiffPolarity
    {
        /// <summary>双侧：任何方向的差异都算缺陷（默认）。</summary>
        [Display(Name = "双侧（变亮变暗都算）")]
        Both = 0,

        /// <summary>只算变亮：被测图比参考图亮的地方。</summary>
        [Display(Name = "只算变亮（被测比参考亮）")]
        Brighter = 1,

        /// <summary>只算变暗：被测图比参考图暗的地方（最常见的划痕/脏污/漏印）。</summary>
        [Display(Name = "只算变暗（被测比参考暗）")]
        Darker = 2,
    }

    /// <summary>
    /// 差异像元的聚合方式：决定"缺陷面积"这个数怎么算。
    /// </summary>
    public enum DiffAggregation
    {
        /// <summary>
        /// 连通域：把超阈值的像元按 8 连通聚成区域，逐个算面积/重心。
        /// 用在哪：要么知道"有几处、各在哪"，要么要按"单片面积"判（细小划痕）。
        /// </summary>
        [Display(Name = "连通域（按片统计）")]
        Components = 0,

        /// <summary>
        /// 总面积：所有超阈值像元累加，不区分是否连成片。
        /// 用在哪：只关心"总共差了多少"（如整体印刷深浅偏移、整片污渍），不在乎分布。
        /// **代价**：几百个孤立噪点会累加成一个大面积，对噪声更敏感。
        /// </summary>
        [Display(Name = "总面积（不分片）")]
        Total = 1,
    }

    /// <summary>
    /// 两图尺寸/通道不一致时的处理方式。
    ///
    /// 关键口径：**默认硬失败，绝不静默缩放**。差分是逐像元运算，
    /// 一旦尺寸不同就必须几何对齐（缩放会引入插值误差，旋转/平移要用标定或匹配），
    /// 这些都不是本插件该替用户猜的事——静默缩放会得到一张"看起来正常、数值全错"的差分图。
    /// </summary>
    public enum SizeMismatchPolicy
    {
        /// <summary>硬失败（默认）：报出两边尺寸与通道数，要求上游对齐。</summary>
        [Display(Name = "硬失败（推荐）")]
        Fail = 0,

        /// <summary>
        /// 按参考图缩放被测图（zoom_image_size，双线性）。
        /// 只在"两图只差整体缩放、且可以接受插值误差"时用；结果会记进日志与输出说明。
        /// </summary>
        [Display(Name = "缩放被测图到参考图尺寸")]
        ScaleTest = 1,

        /// <summary>
        /// 取两图的公共左上角区域（交集裁剪）。两图视场有偏移但同一比例尺时用，
        /// 结果会记进日志与输出说明。**不做任何插值**。
        /// </summary>
        [Display(Name = "裁到公共区域")]
        CropCommon = 2,
    }
}
