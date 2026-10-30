using System;
using System.ComponentModel.DataAnnotations;

namespace Core.Interfaces
{
    /// <summary>
    /// 「声明一个变量」时可选的类型（跨插件共用的最小类型集合）。
    ///
    /// 为什么放在 <c>Core.Interfaces</c> 而不是某个插件里：
    /// <see cref="Plugin.Util"/>（变量赋值/变量定义与赋值）与 <c>Plugin.CSharpScript</c>
    /// 各有一套自己的变量声明表，两边都要"类型 → CLR Type → 端口"的映射。
    /// 放在插件里就会变成两份需要手工同步的表（以后加一个类型漏一处 = 静默不兼容），
    /// 放在契约程序集里则只有一份。
    ///
    /// **与 <c>Plugin.CSharpScript.ScriptVarType</c> 的关系**：那是脚本自己的声明类型，
    /// 多出 Object/HTuple/HImage/HRegion/HXld/HObject 等 HALCON 图形类型。本枚举只收
    /// 「能被动态输出端口承载、且能被下游普通插件的 InputPort 消费」的标量类型；
    /// 图形类型不在这里——变量的值来自上游端口（HImage 等），不走"声明一个图形变量"这条路。
    /// </summary>
    public enum VariableValueType
    {
        /// <summary>任意对象（不做类型检查；下游连 int 也不会在编译期报错）</summary>
        [Display(Name = "任意对象")]
        Object = 0,

        /// <summary>整数（int）</summary>
        [Display(Name = "整数")]
        Int = 1,

        /// <summary>浮点数（double）</summary>
        [Display(Name = "浮点数")]
        Double = 2,

        /// <summary>布尔（bool）</summary>
        [Display(Name = "布尔")]
        Bool = 3,

        /// <summary>字符串（string）</summary>
        [Display(Name = "字符串")]
        String = 4,
    }

    /// <summary>
    /// <see cref="VariableValueType"/> 与 CLR 类型的双向映射（唯一一份）。
    ///
    /// 新增类型时只改这里 + 枚举本身；两处插件都自动跟随。
    /// </summary>
    public static class VariableValueTypes
    {
        /// <summary>声明类型 → 动态端口的 CLR 类型。</summary>
        public static Type ToClrType(this VariableValueType type) => type switch
        {
            VariableValueType.Int => typeof(int),
            VariableValueType.Double => typeof(double),
            VariableValueType.Bool => typeof(bool),
            VariableValueType.String => typeof(string),
            _ => typeof(object),
        };

        /// <summary>CLR 类型 → 声明类型（不认识的一律 Object，不抛）。</summary>
        public static VariableValueType FromClrType(Type? type)
        {
            if (type == null) return VariableValueType.Object;
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (type == typeof(int)) return VariableValueType.Int;
            if (type == typeof(double)) return VariableValueType.Double;
            if (type == typeof(bool)) return VariableValueType.Bool;
            if (type == typeof(string)) return VariableValueType.String;
            return VariableValueType.Object;
        }

        /// <summary>
        /// 运行期把值转成声明类型；转不动返回 false 并给中文原因（不抛）。
        ///
        /// 与「变量赋值」既有的宽进口径一致：能转就转（文本按不变区域性解析），
        /// 转不了必须报清被拒原值，不许静默写个错值。
        /// </summary>
        public static bool TryCoerce(object? value, VariableValueType target, out object? coerced, out string? error)
        {
            coerced = value;
            error = null;

            if (target == VariableValueType.Object) return true;
            if (value == null)
            {
                // 引用类型保持 null；值类型给默认值（与端口默认值一致）
                var clr = target.ToClrType();
                coerced = clr.IsValueType ? Activator.CreateInstance(clr) : null;
                return true;
            }

            var want = target.ToClrType();
            if (want.IsInstanceOfType(value)) return true;

            try
            {
                if (want == typeof(string))
                {
                    coerced = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
                    return true;
                }
                if (value is string text && (want == typeof(int) || want == typeof(double)))
                {
                    // 数值文本：不变区域性、拒绝千分位（与 GlobalVariableWriter 同口径）
                    if (text.Contains(','))
                    {
                        error = $"值「{text}」含千分位逗号，不能按 {want.Name} 解析（请去掉逗号）";
                        return false;
                    }
                    coerced = Convert.ChangeType(text, want, System.Globalization.CultureInfo.InvariantCulture);
                    return true;
                }
                coerced = Convert.ChangeType(value, want, System.Globalization.CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception ex)
            {
                error = $"值「{value}」（{value.GetType().Name}）无法按声明类型 {want.Name} 解析：{ex.Message}";
                coerced = value;
                return false;
            }
        }
    }
}
