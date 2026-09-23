using System;
using System.Globalization;

namespace VisionMaster.Scada
{
    /// <summary>
    /// 运行期"变量值 → 判定/展示"的公共转换：把 <see cref="object"/> 值宽松地当数值看、
    /// 当布尔看、或者转成人话文本。
    ///
    /// 为什么要有这么一个东西
    /// ---------
    /// 运行态有两个消费者要问同一个问题："这个值算不算超限"（报警引擎、变量事件引擎）
    /// 和"这个值显示成什么"（报警记录、日志）。若各自写一份，
    /// 两处对"字符串 '3.5' 算不算数""null 该当假还是当判不了"的口径早晚会分叉——
    /// 而分叉的表现是"报警说超限了，变量事件却没触发"这种没法解释的现象。
    /// 转换口径只有一份实现，是本类存在的全部理由。
    ///
    /// 现场为什么必须"宽松"
    /// ---------
    /// 变量类型来自 PLC / 通讯层，同一个物理量在不同驱动下可能是 <c>short</c> / <c>double</c> /
    /// <c>string</c>；布尔量也常被存成 0/1（PLC 侧本来就是位）。判定若只认某一种 CLR 类型，
    /// 用户看到的就是"同一个配置换个驱动就不生效"。
    /// 所以：数值 / 布尔 / 可解析的字符串<b>都收</b>，失败<b>不抛</b>只返回 false。
    ///
    /// 拿不到值时为什么返回 null 而不是 false
    /// ---------
    /// <see cref="ToBoolean"/> 返回 <c>null</c> 表示"<b>判不了</b>"，与"<b>是假</b>"是两件事。
    /// 若把"没值"当成 false，"值为假"这类边沿条件会在变量断线时误报一片——
    /// 宁可漏一条本来也说不清的触发，也不要刷一堆假动作。
    /// </summary>
    internal static class ScadaValueConvert
    {
        /// <summary>
        /// 值的人话文本（报警记录里"报的时候是多少"）。拿不到值时返回 <c>null</c>，<b>不编造 0</b>。
        ///
        /// 布尔转成"真"/"假"而不是 True/False：这份文本要落到报警历史文件里给人看。
        /// </summary>
        public static string? ValueText(object? value)
        {
            if (value == null)
                return null;

            if (value is bool flag)
                return flag ? "真" : "假";

            // 用不变文化格式化：小数点在中文/德文环境下会变成逗号，
            // 而这份文本会被写进 CSV，逗号就是列分隔符——一条 23,5 能把整行列数冲乱。
            if (value is IFormattable formattable)
                return formattable.ToString(null, CultureInfo.InvariantCulture);

            return value.ToString();
        }

        /// <summary>
        /// 把值当布尔看。现场常把布尔量存成 0/1（PLC 侧本来就是位），所以数值也照收；
        /// 拿不到或解释不了时返回 <c>null</c>（= 判不了），而不是 false——
        /// 返回 false 会让"值为假"这类条件在"没值"时误报。
        /// </summary>
        public static bool? ToBoolean(object? value)
        {
            if (value == null)
                return null;

            if (value is bool flag)
                return flag;

            return TryToDouble(value, out var number) ? number != 0 : null;
        }

        /// <summary>宽松数值转换：数值 / 布尔 / 可解析的字符串都收，失败返回 false 且不抛</summary>
        public static bool TryToDouble(object? value, out double result)
        {
            result = 0;

            if (value == null)
                return false;

            if (value is bool flag)
            {
                result = flag ? 1 : 0;
                return true;
            }

            try
            {
                result = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
            catch (InvalidCastException)
            {
                return false;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        /// <summary>
        /// 两个值算不算"同一个值"（用于边沿判定：旧值 → 新值是否真的变了）。
        ///
        /// 为什么不用 <c>object.Equals</c>：PLC 侧同一个量常在 <c>short</c> / <c>int</c> / <c>double</c>
        /// 之间换壳（驱动重连后类型可能变），而 <c>Equals(1, 1.0)</c> 是 <c>false</c>——
        /// 那会让"更改数值"在值其实没动的时候也触发一次。
        /// 所以两边都能当数看时<b>按数比</b>，比不了才退回引用/等值比较。
        /// </summary>
        public static bool SameValue(object? left, object? right)
        {
            if (ReferenceEquals(left, right))
                return true;

            if (left == null || right == null)
                return false;

            if (left is bool || right is bool)
                return ToBoolean(left) == ToBoolean(right);

            if (TryToDouble(left, out var a) && TryToDouble(right, out var b))
                return a.Equals(b);

            return left.Equals(right);
        }
    }
}
