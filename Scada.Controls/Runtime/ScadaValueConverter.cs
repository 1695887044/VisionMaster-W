using System;
using System.Globalization;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 运行态值转换：把一个<b>工程变量的值</b>（任意 CLR 对象）换算成某个图元属性要的类型。
    ///
    /// 为什么单独成类而不是塞进数据泵：它是整个 S6 里<b>唯一</b>一条"错就错在数据上"的路径，
    /// 也是最容易被现场问"为什么这个值不显示"的地方。做成纯函数（无状态、不碰可视树）才能
    /// 在断言工程里把整张换算表逐条钉死，而不是只能靠真机看现象。
    ///
    /// 换算顺序（越靠前越优先，顺序本身就是设计）：
    /// <code>
    /// ① 目标就是 string   → 有格式串按格式串，否则按不变文化 ToString（文本图元的正路）
    /// ② 类型本来就对      → 直接用（bool→bool、double→double，零开销）
    /// ③ 目标是 bool       → 认 True/False、1/0、是/否、On/Off、非零算真（治 PLC 的整数开关量）
    /// ④ 目标是枚举        → 文本按名字，数值按序号（治下位机上报的整型状态字）
    /// ⑤ 目标是数值/日期   → 交给 IConvertible（文本 "12.5" 也能进 double）
    /// ⑥ 其余（Brush 等）  → 借 WPF 现成的类型转换器，与设计期读 .vms 走的是同一条路
    /// </code>
    ///
    /// 为什么最后一步要复用 <see cref="ScadaElementBase.ConvertFromString"/>：
    /// "#FF0000" → Brush、FontWeights.Bold → FontWeight 这些规则 WPF 早就写好了，
    /// 我们自己再写一份解析，等于让"设计期配的颜色"与"运行期变量给的颜色"走两条解析路，
    /// 迟早出现"手填能显示、变量给了同一个串却显示不出来"。
    /// </summary>
    public static class ScadaValueConverter
    {
        /// <summary>
        /// 尝试换算。<b>不抛异常</b>：失败时返回 false 并给出可直接展示给操作员的中文原因。
        /// </summary>
        /// <param name="value">变量当前值（可为 null）</param>
        /// <param name="targetType">目标属性的 CLR 类型（取自依赖属性的 PropertyType）</param>
        /// <param name="format">显示格式串（如 "F2" / "yyyy-MM-dd"），只对文本目标有意义</param>
        /// <param name="result">换算结果</param>
        /// <param name="error">失败原因（中文，含具体值，便于现场对号入座）</param>
        public static bool TryConvert(object? value, Type targetType, string? format, out object? result, out string? error)
        {
            result = null;
            error = null;

            if (targetType is null)
            {
                error = "目标类型未知";
                return false;
            }

            // 空值：文本目标当"清空显示"处理（变量没值就显示空白，是操作员期待的行为），
            // 其它类型（数值/颜色/布尔）没有"空"这个语义，如实报错而不是悄悄写个 0 上去。
            if (value is null)
            {
                if (targetType == typeof(string))
                {
                    result = string.Empty;
                    return true;
                }

                error = "变量当前值为空";
                return false;
            }

            if (IsNonFinite(value))
            {
                error = $"变量值 {value} 不是有效数字（NaN/Infinity）";
                return false;
            }

            // ① 文本目标
            if (targetType == typeof(string))
            {
                result = Format(value, format);
                return true;
            }

            // ② 类型本来就对（含继承：SolidColorBrush → Brush）
            if (targetType.IsInstanceOfType(value))
            {
                result = value;
                return true;
            }

            // ③ 布尔目标
            if (targetType == typeof(bool))
                return TryConvertToBool(value, out result, out error);

            // ④ 枚举目标
            if (targetType.IsEnum)
                return TryConvertToEnum(value, targetType, out result, out error);

            // ⑤ 数值/日期：IConvertible 一条路全包（含 "12.5" → 12.5）
            if (value is IConvertible && IsConvertibleTarget(targetType))
            {
                try
                {
                    var converted = Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);

                    if (IsNonFinite(converted))
                    {
                        error = $"值「{value}」不是有效数字（NaN/Infinity）";
                        return false;
                    }

                    result = converted;
                    return true;
                }
                catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException or ArgumentException)
                {
                    error = $"值「{value}」转不成 {targetType.Name}";
                    return false;
                }
            }

            // ⑥ 其余交给 WPF 的类型转换器（与设计期同一条路）
            try
            {
                result = ScadaElementBase.ConvertFromString(targetType, Format(value, format));
                return true;
            }
            catch (Exception ex)
            {
                error = $"值「{value}」转不成 {targetType.Name}：{ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// 按格式串转文本。
        /// 用不变文化：.vms 与变量值都是跨机器交换的，小数点不能跟着系统区域设置走
        /// （德语系统上 1.5 会显示成 1,5，再喂给别的解析器就变成 15）。
        /// </summary>
        private static string Format(object value, string? format)
        {
            if (value is IFormattable formattable)
                return formattable.ToString(string.IsNullOrWhiteSpace(format) ? null : format, CultureInfo.InvariantCulture);

            return value.ToString() ?? string.Empty;
        }

        /// <summary>
        /// 布尔换算：工业现场的开关量什么形态都有——PLC 给整数 0/1、文本型设备给 "ON"/"OFF"、
        /// 中文界面给 "是"/"否"。只认 bool.Parse 的话，一个指示灯绑定就会因为下位机给的是 1
        /// 而永远不亮，且看不出哪里错了。
        /// </summary>
        private static bool TryConvertToBool(object value, out object? result, out string? error)
        {
            switch (value)
            {
                case bool flag:
                    result = flag;
                    error = null;
                    return true;

                case string text when TryParseBool(text, out bool parsed):
                    result = parsed;
                    error = null;
                    return true;

                case string text:
                    error = $"文本「{text}」不是可识别的布尔值（True/False、1/0、是/否、On/Off）";
                    result = null;
                    return false;

                default:
                    if (IsNumeric(value))
                    {
                        result = Convert.ToDouble(value, CultureInfo.InvariantCulture) != 0d;
                        error = null;
                        return true;
                    }

                    error = $"值「{value}」转不成布尔";
                    result = null;
                    return false;
            }
        }

        /// <summary>布尔文本识别（大小写不敏感；非零数字一律算真）</summary>
        private static bool TryParseBool(string text, out bool value)
        {
            value = false;

            var trimmed = text.Trim();
            if (trimmed.Length == 0)
                return false;

            if (bool.TryParse(trimmed, out value))
                return true;

            if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
            {
                value = number != 0d; // PLC 的 1/0，也认 "1.0"
                return true;
            }

            switch (trimmed.ToLowerInvariant())
            {
                case "是":
                case "真":
                case "开":
                case "on":
                case "yes":
                case "y":
                    value = true;
                    return true;

                case "否":
                case "假":
                case "关":
                case "off":
                case "no":
                case "n":
                    value = false;
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 枚举换算：文本按名字（大小写不敏感），数值按序号。
        /// 数值走 <see cref="Enum.IsDefined(Type, object)"/> 而不是直接 <c>Enum.ToObject</c>：
        /// 序号越界时 ToObject 会造出一个"名字不存在"的枚举值，落到属性上就是查不到分支的怪状态，
        /// 宁可当场报错。
        /// </summary>
        private static bool TryConvertToEnum(object value, Type targetType, out object? result, out string? error)
        {
            var names = string.Join("/", Enum.GetNames(targetType));

            if (value is string text)
            {
                try
                {
                    result = Enum.Parse(targetType, text.Trim(), ignoreCase: true);
                    error = null;
                    return true;
                }
                catch (Exception ex) when (ex is ArgumentException or OverflowException)
                {
                    error = $"文本「{text}」不是 {targetType.Name} 的合法取值（{names}）";
                    result = null;
                    return false;
                }
            }

            try
            {
                var candidate = Enum.ToObject(targetType, Convert.ToInt64(value, CultureInfo.InvariantCulture));

                if (Enum.IsDefined(targetType, candidate))
                {
                    result = candidate;
                    error = null;
                    return true;
                }

                error = $"数值 {value} 不是 {targetType.Name} 的合法取值（{names}）";
                result = null;
                return false;
            }
            catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
            {
                error = $"值「{value}」转不成 {targetType.Name}（{names}）";
                result = null;
                return false;
            }
        }

        /// <summary>是不是可以走 <see cref="Convert.ChangeType(object, Type, IFormatProvider)"/> 的目标类型</summary>
        private static bool IsConvertibleTarget(Type type)
            => type.IsPrimitive
            || type == typeof(decimal)
            || type == typeof(DateTime)
            || type == typeof(DateTimeOffset);

        /// <summary>是不是数值形态（布尔换算要用；不含 char）</summary>
        private static bool IsNumeric(object value)
            => value is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal;

        /// <summary>NaN / Infinity：算得出来的"数字"写进依赖属性会让整个布局变成空白，必须挡在门外</summary>
        private static bool IsNonFinite(object? value)
            => (value is double d && !double.IsFinite(d))
            || (value is float f && !float.IsFinite(f));
    }
}
