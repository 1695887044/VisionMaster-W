using System.Globalization;
using System.Windows.Controls;

namespace Core.Controls
{
    /// <summary>
    /// 数值输入校验规则（插件配置界面公共用）：一次同时管"能不能解析成数字"和"是否落在允许区间"，
    /// 失败返回中文提示。
    ///
    /// 为什么从各插件收编到这里：同一个类此前在 Plugin.BlobDetect / Plugin.CaliperMeasure /
    /// Plugin.Matching 各有一份（逐字相同），再加一个新插件就是第四份。校验文案不统一的后果是
    /// "同一个非法输入在不同插件里提示不一样"，而这类文案是给操作员看的。
    ///
    /// 为什么用 ValidationRule 而不是在 setter 里 if/else 兜底：
    /// WPF 的校验规则跑在"字符串还没有写回绑定源"之前，校验不通过时源属性根本不会被改写，
    /// 于是插件里的参数永远停在最近一次合法值——界面填错字只会显示红框 + 提示，
    /// 绝不会把非法数值带进 HALCON 算子（"非法值不能炸"这条硬要求就落在这里）。
    /// 若改在 setter 里判，值已经写进去了，还得再想办法回滚，麻烦且容易留脏值。
    /// </summary>
    public class NumberValidationRule : ValidationRule
    {
        /// <summary>允许的最小值（含）</summary>
        public double Min { get; set; } = double.MinValue;

        /// <summary>允许的最大值（含）</summary>
        public double Max { get; set; } = double.MaxValue;

        /// <summary>字段中文名，用于拼提示语（如"面积下限"）</summary>
        public string FieldName { get; set; } = "数值";

        /// <summary>是否只允许整数（像素、半径、个数这类参数不允许小数）</summary>
        public bool IntegerOnly { get; set; }

        public override ValidationResult Validate(object value, CultureInfo cultureInfo)
        {
            // ★ 2026-10-10：绑定目标可能是 Text（字符串，老写法），也可能换成 NumericBox 的 Value（double）。
            //   目标是 Text 时这里是用户输入的原始文本；目标是 Value 时是**已转换过的数值**。
            //   只按字符串处理（value as string ?? ""）会让数值目标恒判"不能为空" ——
            //   界面全红，而且校验拒绝写回源、用户输入进不了模型（Plugin.Matching 迁移时踩到过）。
            double number;
            string display;

            if (value is string raw)
            {
                display = raw.Trim();
                if (display.Length == 0)
                    return new ValidationResult(false, $"{FieldName}不能为空");

                // 用当前界面区域解析：中文系统下小数点就是 "."，与既有插件的手工输入习惯一致
                if (!double.TryParse(display, NumberStyles.Float, cultureInfo, out number))
                    return new ValidationResult(false, $"{FieldName}必须是数字，当前输入：{display}");
            }
            else if (value is IConvertible convertible)
            {
                try
                {
                    number = convertible.ToDouble(cultureInfo);
                }
                catch
                {
                    return new ValidationResult(false, $"{FieldName}不是有效数值");
                }

                display = number.ToString(cultureInfo);
            }
            else
            {
                return new ValidationResult(false, $"{FieldName}不能为空");
            }

            if (double.IsNaN(number) || double.IsInfinity(number))
                return new ValidationResult(false, $"{FieldName}不是有效数值");

            if (IntegerOnly && Math.Abs(number - Math.Round(number)) > 1e-9)
                return new ValidationResult(false, $"{FieldName}必须是整数");

            if (number < Min || number > Max)
                return new ValidationResult(false, $"{FieldName}应在 {Min}~{Max} 之间，当前：{display}");

            return ValidationResult.ValidResult;
        }
    }
}
