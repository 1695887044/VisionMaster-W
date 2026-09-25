using System.Globalization;
using System.Windows.Controls;

namespace Plugin.CaliperMeasure
{
    /// <summary>
    /// 数值输入校验规则：一次同时管"能不能解析成数字"和"是否落在允许区间"，失败返回中文提示。
    ///
    /// 为什么用 ValidationRule 而不是在 setter 里 if/else 兜底：
    /// WPF 的校验规则跑在"字符串还没有写回绑定源"之前，校验不通过时源属性根本不会被改写，
    /// 于是插件里的参数永远停在最近一次合法值——界面填错字只会显示红框 + 提示，
    /// 绝不会把非法数值带进 HALCON 算子（"非法值不能炸"这条硬要求就落在这里）。
    /// 与 Plugin.BlobDetect 的同类规则保持同一套行为，避免两个插件对"0.5 该不该收"给出不同答案。
    /// </summary>
    public class NumberValidationRule : ValidationRule
    {
        /// <summary>允许的最小值（含）</summary>
        public double Min { get; set; } = double.MinValue;

        /// <summary>允许的最大值（含）</summary>
        public double Max { get; set; } = double.MaxValue;

        /// <summary>字段中文名，用于拼提示语（如"卡尺数"）</summary>
        public string FieldName { get; set; } = "数值";

        /// <summary>是否只允许整数（像素、个数这类参数不允许小数）</summary>
        public bool IntegerOnly { get; set; }

        public override ValidationResult Validate(object value, CultureInfo cultureInfo)
        {
            var text = (value as string ?? string.Empty).Trim();

            if (text.Length == 0)
                return new ValidationResult(false, $"{FieldName}不能为空");

            // 用当前界面区域解析：中文系统下小数点就是 "."，与既有插件的手工输入习惯一致
            if (!double.TryParse(text, NumberStyles.Float, cultureInfo, out var number))
                return new ValidationResult(false, $"{FieldName}必须是数字，当前输入：{text}");

            if (double.IsNaN(number) || double.IsInfinity(number))
                return new ValidationResult(false, $"{FieldName}不是有效数值");

            if (IntegerOnly && Math.Abs(number - Math.Round(number)) > 1e-9)
                return new ValidationResult(false, $"{FieldName}必须是整数");

            if (number < Min || number > Max)
                return new ValidationResult(false, $"{FieldName}应在 {Min}~{Max} 之间，当前：{number}");

            return ValidationResult.ValidResult;
        }
    }
}
