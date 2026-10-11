using System;
using System.Reflection;
using System.Windows;
using System.Windows.Data;

namespace UI.CustomControl.PropertyGrid
{
    /// <summary>
    /// 数值生成器：把 int / double 这类**数值属性**交给 <see cref="NumericBox"/>，而不是裸文本框。
    ///
    /// 【为什么 Priority = 110】高于 <see cref="StructValueGenerator"/>（100）—— 后者用 TextBox
    /// 渲染一切原始类型与 string，数值的钳制 / 小数位 / 单位 / 步进在那里没有落点，
    /// 而且它靠绑定引擎自己做类型转换，区域设置一变就静默失败（"1.5" 在 de-DE 机器上敲不进去）。
    ///
    /// 【钳制从哪来】读属性上**已有的** <see cref="RangeValidationAttribute"/>（[RangeValidation(min, max)]）：
    /// 不需要属性再标一套范围，数值框自动带上上下限（CoerceValue 夹，敲过头会被拉回来而不是报错）。
    ///
    /// 【小数位】整数类型固定 0 位；float/double/decimal 不强制位数 ——
    /// 强制 3 位会把 0.123456 悄悄四舍五入成 0.123，那是改用户的数据，不是显示。
    /// </summary>
    public class NumericGenerator : IControlGenerator
    {
        public int Priority => 110;

        public bool CanProcess(PropertyInfo prop, Type targetType, bool isReadOnly) => IsNumeric(targetType);

        public FrameworkElement Create(PropertyInfo prop, object bindingSource, bool isReadOnly)
        {
            var box = new NumericBox
            {
                IsReadOnly = isReadOnly,
                DecimalPlaces = IsIntegral(prop.PropertyType) ? 0 : -1,
            };

            var range = prop.GetCustomAttribute<RangeValidationAttribute>();
            if (range != null)
            {
                box.Minimum = range.Min;
                box.Maximum = range.Max;
            }

            box.SetBinding(NumericBox.ValueProperty, new Binding(prop.Name)
            {
                Source = bindingSource,
                // 只读属性只读回来：写回一个没有 setter 的属性会抛，绑定自己会先炸
                Mode = isReadOnly ? BindingMode.OneWay : BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            });

            return box;
        }

        /// <summary>
        /// 走数值框的类型。刻意用白名单而不是 <c>IsPrimitive</c>：
        /// 后者把 bool / char 也算原始类型，而 bool 归 BoolStateGenerator（开关）、char 保持文本更合适。
        /// </summary>
        private static bool IsNumeric(Type t) =>
            t == typeof(byte) || t == typeof(sbyte) ||
            t == typeof(short) || t == typeof(ushort) ||
            t == typeof(int) || t == typeof(uint) ||
            t == typeof(long) || t == typeof(ulong) ||
            t == typeof(float) || t == typeof(double) || t == typeof(decimal);

        private static bool IsIntegral(Type t) =>
            t == typeof(byte) || t == typeof(sbyte) ||
            t == typeof(short) || t == typeof(ushort) ||
            t == typeof(int) || t == typeof(uint) ||
            t == typeof(long) || t == typeof(ulong);
    }
}
