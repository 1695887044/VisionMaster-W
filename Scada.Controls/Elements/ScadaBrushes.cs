using System.Windows.Media;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 冻结画刷工厂：把颜色字符串/Color 变成 <b>已 Freeze</b> 的 <see cref="SolidColorBrush"/>。
    ///
    /// 为什么图元库需要这样一个小东西，而不是各处随手 new：
    /// ① <b>依赖属性的默认值会被所有实例共享</b>。未冻结的 Freezable 一旦被某个实例的样式或
    ///    动画改到，其余实例会跟着变——表现为"改了这一个图元，另一个图元也跟着变色"。
    ///    Freeze 之后 WPF 会拒绝任何写入，这类串色从"偶发"变成"根本不可能"。
    /// ② 冻结的画刷是跨线程只读的，将来后台线程准备渲染数据时不会踩到线程亲和性检查。
    /// ③ 同类代码原先在指示灯、诊断角标、画布占位符里各写了一份，行为还不一致
    ///    （有的吃颜色串、有的吃 Color、有的吃 RGB 三分量）。统一到一处，
    ///    新图元只要"要一个默认色"就来这里取，不必再决定"该抄哪一份"。
    ///
    /// 注意：这里只做"冻结"，不做缓存。冻结画刷本身极轻量，各图元把它存进自己的
    /// <c>static readonly</c> 字段即可；若将来出现"同色被上千个图元各存一份"的实测问题，
    /// 再在这里加一层按色缓存也不影响调用方。
    /// </summary>
    public static class ScadaBrushes
    {
        /// <summary>
        /// 按颜色字符串建冻结画刷，支持 <c>#AARRGGBB</c> / <c>#RRGGBB</c> / <c>#RGB</c>
        /// 以及 <c>Red</c> 这类 WPF 预定义颜色名（解析走 <see cref="ColorConverter"/>，
        /// 与图元把 <c>Fill</c> 属性从字符串换算成画刷时用的是同一套规则，不会出现
        /// "属性面板认这个写法、默认值不认"的分叉）。
        /// </summary>
        /// <param name="color">颜色字符串；无法解析时抛 <see cref="FormatException"/>（属编码期错误，不吞）</param>
        public static SolidColorBrush Frozen(string color)
            => Frozen((Color)ColorConverter.ConvertFromString(color)!);

        /// <summary>按 <see cref="Color"/> 建冻结画刷</summary>
        public static SolidColorBrush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        /// <summary>按 RGB 三分量建冻结画刷（不透明）</summary>
        public static SolidColorBrush Frozen(byte r, byte g, byte b) => Frozen(Color.FromRgb(r, g, b));
    }
}
