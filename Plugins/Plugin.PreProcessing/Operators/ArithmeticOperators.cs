using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using HalconDotNet;
using Plugin.PreProcessing.Models;
using Plugin.PreProcessing.Services;
using UI.Attributes;

namespace Plugin.PreProcessing.Operators
{
    /// <summary>
    /// 「算术运算」算子组的公共基类。
    ///
    /// 这一组和滤波/形态学的根本差别：它改变的是**灰度语义**（加一个偏移、乘一个增益），
    /// 而不是"用邻域重算某个像素"。所以它放在链尾（去判定/上屏之前），Order 从 90 起，
    /// 与"几何调整"的 41、"滤波"的 50 明确分开。
    ///
    /// 为什么没有"图像 − 图像"算子：链内算子只能读到当前图和字符串参数，拿不到第二张图
    /// （见 OperatorStepItem 的结构）。双图差分由 Plugin.ImageDiff 插件承担，那里有真正的双输入端口。
    /// 本类及其派生类都是常数算术，Summary 里固定带 "常数"，避免用户误读成双图运算。
    /// </summary>
    public abstract class ConstArithmeticOperator : PreprocessOperator
    {
        private double _value;

        [SuperDisplay(Name = "常数", Group = new string[] { "参数" }, Order = 1, ColSpan = 6,
            Description = "参与运算的常数（像素灰度值；乘法时是增益系数，如 1.2 提亮、0.8 压暗）")]
        public double Value
        {
            get => _value;
            set => SetParam(ref _value, value);
        }

        /// <summary>常数本身合法（NaN/Infinity 一律当 0，避免把脏值喂进 Halcon 得到整片随机）</summary>
        protected override void Normalize()
        {
            if (double.IsNaN(Value) || double.IsInfinity(Value)) Value = 0;
        }

        public override string Summary => $"常数 {Value:R}";
    }

    /// <summary>
    /// 灰度加常数：out = image + value。
    /// 典型用途：整体提亮暗图、把背景压平到某个基准灰阶方便后续固定阈值。
    /// byte 图超过 255 会被饱和截断（Halcon 行为），不是错误，但要知道。
    /// </summary>
    [PreprocessOperator("add_const", "灰度加常数", "算术运算", Order = 90,
        Description = "整幅图逐像素加上一个常数（提亮 / 抬基准）。超过 255 的像素饱和到 255。")]
    public sealed class AddConstOperator : ConstArithmeticOperator
    {
        protected override HImage Process(HImage input) => PreprocessHService.AddConst(input, Value);
    }

    /// <summary>
    /// 灰度减常数：out = image − value。
    ///
    /// 【陷阱】byte 图是**无符号**的，负结果会被截成 0 ——
    /// 所以"减得多一点"不会得到一片负值，而是得到一片纯黑且信息全丢。
    /// 需要保留负差请改用「差分归一化」或 ImageDiff 插件的带符号模式。
    /// </summary>
    [PreprocessOperator("sub_const", "灰度减常数", "算术运算", Order = 91,
        Description = "整幅图逐像素减去一个常数（压暗 / 降基准）。注意 byte 图负值会被截为 0，信息会丢。")]
    public sealed class SubConstOperator : ConstArithmeticOperator
    {
        protected override HImage Process(HImage input) => PreprocessHService.SubConst(input, Value);
    }

    /// <summary>
    /// 灰度乘增益：out = image × value。
    /// 与"灰度加常数"的区别：乘法是**相对**调整（亮处变化大），加法是绝对平移。
    /// 高光区域容易被推到 255 饱和，做对比度提升时用 1.1~1.3 之间的值更安全。
    /// </summary>
    [PreprocessOperator("mult_const", "灰度乘增益", "算术运算", Order = 92,
        Description = "整幅图逐像素乘一个增益（对比度整体拉伸）。大于 1 提亮、小于 1 压暗，高光易饱和。")]
    public sealed class MultConstOperator : ConstArithmeticOperator
    {
        protected override HImage Process(HImage input) => PreprocessHService.MultImageConst(input, Value);
    }

    /// <summary>
    /// 转 real 图（不做灰度变换，只换类型）。
    ///
    /// 为什么单独做成一个算子：链内所有算子都吃 byte 图，负值信息在进链那一刻就已经丢了。
    /// 如果用户想在自己的链里做"先减后放大"这类操作，必须先把类型升到 real，
    /// 否则每一步都在 0 处截断。这个算子是给这种场合准备的显式开关，
    /// 而不是让某个算子偷偷改类型。
    /// </summary>
    [PreprocessOperator("to_real", "转浮点图", "算术运算", Order = 93,
        Description = "把图像类型转成 real（保留负值与超出 255 的亮度）。链路默认按 byte 处理，需要保留差值时必须先加这一步。")]
    public sealed class ToRealOperator : PreprocessOperator
    {
        protected override HImage Process(HImage input) => PreprocessHService.ToReal(input);
    }

    /// <summary>
    /// 归一化到 0~255：按全图真实极值线性拉伸。
    /// 典型用途：real 图做完整条链后没法直接上屏（动态范围常常是 -30000~30000），
    /// 归一化后再看才是人眼能判读的灰度图。
    /// 全图灰阶一致（无对比信息）时输出全 0 图 —— 刻意的选择：塞噪声更难判读。
    /// </summary>
    [PreprocessOperator("normalize_gray", "灰度归一化", "算术运算", Order = 94,
        Description = "按全图最小/最大灰度线性拉伸到 0~255。全图灰度一致时输出全黑图（无对比信息）。")]
    public sealed class NormalizeGrayOperator : PreprocessOperator
    {
        protected override HImage Process(HImage input) => PreprocessHService.RealToByteNormalized(input);
    }

    /// <summary>
    /// 为什么这里**没有**"绝对差"或"图像 − 图像"算子（刻意的缺口，不是漏做）：
    /// 链内算子拿不到第二张图，任何"绝对差"在这里都只能退化成 |image − value|，
    /// 语义是"亮度离某个基准值有多远"，跟用户心里的"两张图差多少"完全不是一回事，
    /// 摆进算子菜单只会被误用。真正的双图差分在 Plugin.ImageDiff（有真双输入端口）。
    /// </summary>
    internal static class ArithmeticOperatorNotes
    {
        public const string NoBinaryArithmeticReason =
            "链内算子只有单图输入；双图算术请使用 ImageDiff 插件。";

        public static IReadOnlyList<string> OperatorKeys { get; } = new[]
        {
            "add_const", "sub_const", "mult_const", "to_real", "normalize_gray",
        };
    }
}
