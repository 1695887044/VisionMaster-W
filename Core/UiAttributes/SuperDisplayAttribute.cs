using System;
using System.Text.RegularExpressions;

namespace UI.Attributes
{
    #region 核心展示特性
    [AttributeUsage(AttributeTargets.Property)]
    public class SuperDisplayAttribute : Attribute
    {
        public bool IsReadOnly { get; set; }
        public string GroupPath { get; set; } = "默认分组";
        public int Order { get; set; }
        public string GroupOrder { get; set; } = "0";
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int ColSpan { get; set; } = 12;

        /// <summary>
        /// <para>该字段是否在属性面板中显示，默认 true（不写就等于显示，老字段行为不变）。</para>
        /// <para>用途：子类隐藏"继承来但对自己无意义"的字段。做法是子类用 <c>override</c> 重贴
        /// <c>[SuperDisplay(Visible = false)]</c>。</para>
        /// <para>⚠️ 必须用 <c>override</c> 而不是 <c>new</c>：<c>GetProperties()</c> 会把基类与子类
        /// 两个同名属性都反射出来（基类那份仍带 <see cref="SuperDisplayAttribute"/>），
        /// 结果是同一字段被渲染两行。</para>
        /// </summary>
        public bool Visible { get; set; } = true;

        /// <summary>
        /// 当该属性的值发生变化时，是否强制重新生成整个表单（多态切换核心）
        /// </summary>
        public bool RequireRefresh { get; set; } = false;
    }
    #endregion

    #region 扩展功能特性
    [AttributeUsage(AttributeTargets.Property)]
    public class IconAttribute : Attribute { public string IconCode { get; set; } = string.Empty; }

    [AttributeUsage(AttributeTargets.Property)]
    public class CommandAttribute : Attribute
    {
        public string Command { get; set; } = string.Empty;
        public object? CommandParam { get; set; }
        public System.Windows.Data.RelativeSourceMode Mode { get; set; } = System.Windows.Data.RelativeSourceMode.Self;
        public Type? AncestorType { get; set; }
        public int AncestorLevel { get; set; } = 1;
    }

    [AttributeUsage(AttributeTargets.Property)]
    public class PermissionAttribute : Attribute
    {
        public string RequiredRole { get; set; } = "Admin";
        public bool HideIfDenied { get; set; } = false;
    }

    [AttributeUsage(AttributeTargets.Property)]
    public class PropertyItemAttribute : Attribute { public Type Type { get; set; } = null!; }
    #endregion

    #region 🌟 强大的数据验证特性 (Validation)

    /// <summary>
    /// 自定义验证基类，所有具体的验证特性都继承它
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
    public abstract class ValidationBaseAttribute : Attribute
    {
        public string ErrorMessage { get; set; } = "输入格式有误";
        public abstract bool IsValid(object? value);
    }

    /// <summary>
    /// 正则表达式验证 (例如 IP地址、MAC地址)
    /// </summary>
    public class RegexValidationAttribute : ValidationBaseAttribute
    {
        public string Pattern { get; }
        public RegexValidationAttribute(string pattern, string errorMsg = "格式不符合要求")
        {
            Pattern = pattern;
            ErrorMessage = errorMsg;
        }

        public override bool IsValid(object? value)
        {
            if (value == null) return true; // 空值校验交给 [Required]，这里不处理
            return Regex.IsMatch(value.ToString() ?? "", Pattern);
        }
    }

    /// <summary>
    /// 数值范围验证
    /// </summary>
    public class RangeValidationAttribute : ValidationBaseAttribute
    {
        public double Min { get; }
        public double Max { get; }

        public RangeValidationAttribute(double min, double max, string errorMsg = null!)
        {
            Min = min;
            Max = max;
            ErrorMessage = errorMsg ?? $"数值必须在 {min} 到 {max} 之间";
        }

        public override bool IsValid(object? value)
        {
            if (value == null) return true;
            if (double.TryParse(value.ToString(), out double num))
            {
                return num >= Min && num <= Max;
            }
            return false;
        }
    }
    #endregion
}