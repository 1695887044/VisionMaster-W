using System;
using System.ComponentModel;
using System.Windows;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 依赖属性默认值的解析：把描述符里那串"给人看的默认值文本"变成依赖属性元数据要的对象。
    ///
    /// 为什么需要它
    /// ---------
    /// 生成器没法在编译期算出 <c>Brush</c> / <c>enum</c> / <c>double</c> 的实例，
    /// 只能把 <c>DefaultValue = "#FF3A3A3A"</c> 这段文本原样搬进生成代码里，
    /// 由本方法在<b>类型的静态构造期</b>解析一次（每个依赖属性一次，之后所有实例共用）。
    ///
    /// 两条口径：
    /// ① <b>冻结</b>：依赖属性的默认值被所有实例共享，未冻结的 Freezable 一旦被某个实例的样式/动画改到，
    ///    其余实例会跟着变（与 <see cref="ScadaBrushes"/> 注释里那条串色事故同一个理由）。
    /// ② <b>解析失败不抛异常</b>：这里是静态构造，抛出去会变成 <see cref="TypeInitializationException"/>——
    ///    一个"改错了一个颜色字面量"就升级成"类型加载失败"，而且看不出是哪个属性。
    ///    所以失败时退回该类型的默认值，把"报错"留给注册期的
    ///    <see cref="ElementDescriptor.Validate"/>（它能给出"哪个图元的哪个键、写的是什么、转不成什么"）。
    ///
    /// <c>public</c> 而不是 <c>internal</c>：生成代码在<b>别的程序集</b>里也要调它
    /// （如 VM.Scada.Controls.Charts 的曲线图元），internal 会让那边的生成文件编译不过。
    /// </summary>
    public static class ScadaPropertyDefaults
    {
        /// <summary>按目标类型解析默认值文本；解析不了时退回该类型的默认值（见类注释口径②）</summary>
        public static object? Resolve(Type targetType, string? text)
        {
            if (targetType == typeof(string))
                return text ?? string.Empty;

            if (string.IsNullOrEmpty(text))
                return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;

            try
            {
                var converted = TypeDescriptor.GetConverter(targetType).ConvertFromInvariantString(text!);
                if (converted is Freezable freezable && freezable.CanFreeze)
                    freezable.Freeze();

                return converted;
            }
            catch (Exception)
            {
                return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
            }
        }
    }
}
