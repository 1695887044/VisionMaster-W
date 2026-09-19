using System;
using System.Globalization;

namespace VisionMaster.Helpers
{
    /// <summary>
    /// 变量值转换：把"界面/组态里的一串文本"转成变量的 <see cref="IVariable.DataType"/>。
    ///
    /// 为什么需要它：写变量有三个入口——变量管理弹窗的"写值"、组态动作 WriteVariable、
    /// 以及后续图元输入框反向写。三个入口拿到的都是字符串，若各自写一套
    /// <c>Convert.ChangeType</c>，规则迟早分叉（尤其布尔：PLC 习惯用 1/0，
    /// 而 <c>Convert.ChangeType("1", typeof(bool))</c> 会直接抛异常）。
    /// 所以规则只在这里定义一份。
    ///
    /// 规则（与运行态读方向的取值规则对齐，见决策四"标量直转"）：
    /// - 布尔：true/false、1/0、是/否、on/off（忽略大小写）都认；其余按"非零数算真"兜底；
    /// - 字符串：原样；
    /// - 数值/日期：交给 <c>Convert.ChangeType</c>（不变文化，避免小数点随区域漂移）；
    /// - 数组：不支持（组态动作与界面输入框都是单值场景）。
    /// </summary>
    public static class VariableValueConverter
    {
        /// <summary>
        /// 尝试把文本转成目标类型。失败时 <paramref name="error"/> 为可直接展示的中文原因。
        /// </summary>
        public static bool TryConvert(string? text, Type dataType, out object? value, out string? error)
        {
            error = null;
            value = null;

            var target = Nullable.GetUnderlyingType(dataType) ?? dataType;
            var raw = text ?? string.Empty;

            if (target == typeof(string))
            {
                value = raw;
                return true;
            }

            if (target == typeof(bool))
                return TryConvertBool(raw, out value, out error);

            if (target.IsArray)
            {
                error = $"不支持把文本写入数组类型（{target.Name}）";
                return false;
            }

            if (string.IsNullOrWhiteSpace(raw))
            {
                error = $"值不能为空（目标类型 {target.Name}）";
                return false;
            }

            try
            {
                value = Convert.ChangeType(raw, target, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception ex)
            {
                error = $"「{raw}」无法转换为 {target.Name}：{ex.Message}";
                return false;
            }
        }

        private static bool TryConvertBool(string raw, out object? value, out string? error)
        {
            value = null;
            error = null;

            var text = raw.Trim();
            if (text.Length == 0)
            {
                error = "布尔值不能为空";
                return false;
            }

            switch (text.ToLowerInvariant())
            {
                case "true": case "1": case "是": case "on": case "yes":
                    value = true;
                    return true;
                case "false": case "0": case "否": case "off": case "no":
                    value = false;
                    return true;
            }

            // 兜底：能当数读的，非零算真（治 PLC 直接给 2、-1 这类"非 0 即真"的值）
            if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var number))
            {
                value = Math.Abs(number) > double.Epsilon;
                return true;
            }

            error = $"「{raw}」无法识别为布尔值（可用 true/false、1/0、是/否、on/off）";
            return false;
        }
    }
}
