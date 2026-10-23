using System.ComponentModel;

namespace Plugin.PreProcessing.Models
{
    /// <summary>
    /// 常数算术的作用对象。
    ///
    /// 为什么要这个枚举：链内算子只能拿到"当前这张图 + 一堆字符串参数"，拿不到第二张图
    /// （见 OperatorStepItem 的结构）。所以链内只能做"图像 与 常数"的算术；
    /// 真正的"图像 与 图像"差分必须由 Plugin.ImageDiff 这类双图输入插件承担。
    /// 这里把两种用途分清楚，避免用户以为在预处理链里能配出双图差分。
    /// </summary>
    public enum ConstArithmeticMode
    {
        [Description("与原值相加（更亮）")]
        Add = 0,

        [Description("与原值相减（更暗）")]
        Subtract = 1,

        [Description("取绝对值后输出（把带符号结果变成亮度）")]
        Absolute = 2,

        [Description("乘一个系数（增益，常用 1 以下做压暗）")]
        Multiply = 3,

        [Description("加一个偏移后整体缩放到 0~255（丢对比度的图救回来）")]
        Normalize = 4,
    }

    /// <summary>
    /// 差分结果的呈现方式。
    ///
    /// 差分的核心陷阱：byte 图做减法负值会被截成 0，于是"目标变暗"的缺陷整片消失，
    /// 而画面看起来"正常"。这里的选项把"要不要保留符号"变成显式选择，而不是让默认值偷偷决定。
    /// </summary>
    public enum DiffResultMode
    {
        [Description("绝对差（|A−B|，非负，最安全）")]
        Absolute = 0,

        [Description("带符号差 A−B 转 real 再归一化上屏（能区分变亮/变暗）")]
        SignedNormalized = 1,

        [Description("原始减法结果（byte 下负差截为 0，仅适合确认 A 恒亮于 B 的场合）")]
        RawSubtract = 2,
    }

    /// <summary>
    /// 差分后二值化的方向。
    /// </summary>
    public enum DiffThresholdPolarity
    {
        [Description("亮于阈值（差分大的像素为前景）")]
        Above = 0,

        [Description("暗于阈值（反向，用于检出本该变化却没变化的区域）")]
        Below = 1,

        [Description("不二值化，只输出灰度差分图")]
        None = 2,
    }
}
