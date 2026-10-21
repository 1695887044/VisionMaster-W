using System;
using System.Globalization;
using Newtonsoft.Json;

namespace VisionMaster.Communications
{
    /// <summary>
    /// 单个数组元素的工程量换算参数（读：原始值 → 工程值；写：工程值 → 原始值，公式互逆）。
    ///
    /// <para><b>为什么不能复用 <see cref="DeviceAddressBase"/> 上的 Scale/Offset</b>：那里只有一份，
    /// 是"整个变量共用"。同型号多通道（50 路温度）用它没问题，但"第 0 项温度、第 1 项压力"
    /// 或"每路单独标定"就表达不了——一套系数套到所有元素上，读回来的工程量是错的。
    /// 数组的换算语义必须逐元素，所以档案挂在变量上（<c>NetworkVariableModel.ElementConversions</c>），
    /// 不挂在地址对象上：地址对象回答"怎么寻址"，元素档案回答"每个元素怎么解释"。</para>
    ///
    /// <para><b>Identity 的判定只看 Scale/Offset</b>：Scale=1 且 Offset=0 视为不换算
    /// （读/写都原样返回，不做舍入）；此时 Unit/DecimalPlaces 只影响界面显示，
    /// 用于"不换算但想显示 ℃ / 两位小数"的场景。</para>
    /// </summary>
    public sealed class ElementConversion
    {
        /// <summary>缩放系数（原始值 × 本系数 + 工程偏移 = 工程值）。0 = 非法（写入无法反算）。</summary>
        public double Scale { get; set; } = 1.0;

        /// <summary>工程偏移（缩放之后相加；反算时先减再除）</summary>
        public double Offset { get; set; }

        /// <summary>显示/读值保留的小数位（0~10；写回原始值时的整数舍入不受它影响）</summary>
        public int DecimalPlaces { get; set; } = 2;

        /// <summary>工程单位（仅显示，如 ℃ / MPa；空 = 不显示）</summary>
        public string? Unit { get; set; }

        /// <summary>是否"不换算"（Scale=1 且 Offset=0）</summary>
        [JsonIgnore]
        public bool IsIdentity => Scale == 1.0 && Offset == 0.0;

        /// <summary>深拷贝（草稿/持久化用：模型里的档案不被编辑草稿就地改掉）</summary>
        public ElementConversion Clone() => new()
        {
            Scale = Scale,
            Offset = Offset,
            DecimalPlaces = DecimalPlaces,
            Unit = Unit,
        };

        /// <summary>界面文案：×0.1 +5 ℃ / ×1 ℃ / —（identity 且无单位）</summary>
        public string Describe()
        {
            if (IsIdentity && string.IsNullOrEmpty(Unit))
                return "—";

            string scale = Scale.ToString("0.######", CultureInfo.InvariantCulture);
            string text = $"×{scale}";
            if (Offset != 0)
            {
                string sign = Offset > 0 ? "+" : "-";
                text += $" {sign}{Math.Abs(Offset).ToString("0.######", CultureInfo.InvariantCulture)}";
            }

            if (!string.IsNullOrEmpty(Unit))
                text += $" {Unit}";

            return text;
        }
    }

    /// <summary>
    /// 数组元素换算的纯函数：读/写共用同一份公式，避免"读用一套、写用另一套"的口径漂移。
    ///
    /// <para>只换算数值元素：bool / string / byte 原样返回（布尔没有量程、文本没有数值语义、
    /// byte[] 是原始块）。</para>
    /// </summary>
    public static class ArrayConversion
    {
        /// <summary>
        /// 原始值 → 工程值。返回 <c>true</c> 时 <paramref name="engineering"/> 是 double（数值元素）
        /// 或原值（非数值元素）；<c>false</c> 表示换算失败（调用方应退回原始值并留痕）。
        /// </summary>
        public static bool TryToEngineering(
            ElementConversion? conversion, object? raw, Type elementType,
            out object? engineering, out string? error)
        {
            engineering = raw;
            error = null;

            if (conversion == null || conversion.IsIdentity || raw == null)
                return true;

            if (!IsNumericElement(elementType))
                return true;

            try
            {
                double value = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                double scaled = value * conversion.Scale + conversion.Offset;
                engineering = Math.Round(scaled, ClampDecimals(conversion.DecimalPlaces));
                return true;
            }
            catch (Exception ex)
            {
                error = $"「{raw}」无法按换算参数（×{conversion.Scale} {conversion.Offset:+0;-0;0}）转换：{ex.Message}";
                engineering = raw;
                return false;
            }
        }

        /// <summary>
        /// 工程值 → 原始值（读路径的逆运算）。整数元素按原实现同一口径
        /// （<see cref="Math.Round(double, int)"/>）舍入，不静默截断。
        /// </summary>
        public static bool TryToRaw(
            ElementConversion? conversion, object? engineering, Type elementType,
            out object? raw, out string? error)
        {
            raw = engineering;
            error = null;

            if (conversion == null || conversion.IsIdentity || engineering == null)
                return true;

            if (!IsNumericElement(elementType))
                return true;

            if (conversion.Scale == 0)
            {
                error = "缩放系数为 0，无法把工程值反算成原始值（请填 1 或实际比例）";
                return false;
            }

            try
            {
                double value = Convert.ToDouble(engineering, CultureInfo.InvariantCulture);
                double unoffset = value - conversion.Offset;
                double result = unoffset / conversion.Scale;

                Type target = Nullable.GetUnderlyingType(elementType) ?? elementType;
                raw = Type.GetTypeCode(target) switch
                {
                    TypeCode.SByte => Convert.ToSByte(Math.Round(result, 0)),
                    TypeCode.Byte => Convert.ToByte(Math.Round(result, 0)),
                    TypeCode.Int16 => Convert.ToInt16(Math.Round(result, 0)),
                    TypeCode.UInt16 => Convert.ToUInt16(Math.Round(result, 0)),
                    TypeCode.Int32 => Convert.ToInt32(Math.Round(result, 0)),
                    TypeCode.UInt32 => Convert.ToUInt32(Math.Round(result, 0)),
                    TypeCode.Int64 => Convert.ToInt64(Math.Round(result, 0)),
                    TypeCode.UInt64 => Convert.ToUInt64(Math.Round(result, 0)),
                    TypeCode.Single => (float)result,
                    TypeCode.Double => result,
                    TypeCode.Decimal => (decimal)result,
                    _ => Convert.ChangeType(result, target, CultureInfo.InvariantCulture),
                };
                return true;
            }
            catch (Exception ex)
            {
                error = $"工程值「{engineering}」无法按换算参数（×{conversion.Scale} {conversion.Offset:+0;-0;0}）反算：{ex.Message}";
                raw = engineering;
                return false;
            }
        }

        /// <summary>
        /// 界面显示：有单位/小数位就按工程格式显示，否则按不变文化原样显示。
        /// <para>注意：这里只负责"怎么显示"，不负责换算——传进来的值应当已经是工程值。</para>
        /// </summary>
        public static string Format(ElementConversion? conversion, object? value)
        {
            if (value == null) return string.Empty;

            if (conversion != null && IsNumericElement(value.GetType())
                && (!conversion.IsIdentity || !string.IsNullOrEmpty(conversion.Unit)))
            {
                try
                {
                    double d = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                    string text = d.ToString($"F{ClampDecimals(conversion.DecimalPlaces)}", CultureInfo.InvariantCulture);
                    return string.IsNullOrEmpty(conversion.Unit) ? text : $"{text} {conversion.Unit}";
                }
                catch
                {
                    // 落到下面的原样显示
                }
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        /// <summary>数值元素判定（bool 不算：它没有量程，换算没有意义）</summary>
        public static bool IsNumericElement(Type elementType)
        {
            Type t = Nullable.GetUnderlyingType(elementType) ?? elementType;
            if (t == typeof(bool) || t == typeof(string) || t == typeof(byte[]))
                return false;

            return Type.GetTypeCode(t) is TypeCode.SByte or TypeCode.Byte or TypeCode.Int16 or TypeCode.UInt16
                or TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64
                or TypeCode.Single or TypeCode.Double or TypeCode.Decimal;
        }

        private static int ClampDecimals(int value) => value < 0 ? 0 : value > 10 ? 10 : value;
    }
}
