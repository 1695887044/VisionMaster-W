using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace UI.AttachedProperties
{
    public enum IconStyle
    {
        Light,
        Regular,
        Solid,
        Thin
    }

    public static class IconHelper
    {
        public static readonly DependencyProperty StyleProperty =
            DependencyProperty.RegisterAttached(
                "Style",
                typeof(IconStyle),
                typeof(IconHelper),
                new PropertyMetadata(IconStyle.Regular, OnStyleChanged));

        public static IconStyle GetStyle(DependencyObject obj) =>
            (IconStyle)obj.GetValue(StyleProperty);

        public static void SetStyle(DependencyObject obj, IconStyle value) =>
            obj.SetValue(StyleProperty, value);

        // ==================== Code 附加属性 ====================
        public static readonly DependencyProperty CodeProperty =
            DependencyProperty.RegisterAttached(
                "Code",
                typeof(string),
                typeof(IconHelper),
                new PropertyMetadata(null, OnCodeChanged));

        public static string GetCode(DependencyObject obj) =>
            (string)obj.GetValue(CodeProperty);

        public static void SetCode(DependencyObject obj, string value) =>
            obj.SetValue(CodeProperty, value);

        // ==================== 变化回调 ====================
        private static void OnStyleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TextBlock tb)
            {
                tb.FontFamily = GetFontFamily((IconStyle)e.NewValue);
            }
        }

        private static void OnCodeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TextBlock tb && e.NewValue is string code)
            {
                tb.Text = ParseIconCode(code);
            }
        }

        // ==================== 辅助方法 ====================
        private static FontFamily GetFontFamily(IconStyle style)
        {
            var resourceKey = style switch
            {
                IconStyle.Light => "FA.Light",
                IconStyle.Solid => "FA.Solid",
                IconStyle.Thin => "FA.Thin",
                _ => "FA.Regular"
            };

            if (Application.Current?.TryFindResource(resourceKey) is FontFamily fontFamily)
                return fontFamily;

            // 兜底：尝试直接返回 null，WPF 会使用默认字体
            return null;
        }

        private static string ParseIconCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return string.Empty;

            // 支持多种格式：
            // "f007"       -> 纯十六进制
            // "\uf007"     -> C# 转义
            // "&#xf007;"   -> XAML 转义
            // "0xf007"     -> 带 0x 前缀

            var cleanCode = code
                .Replace("&#x", "")
                .Replace(";", "")
                .Replace("\\u", "")
                .Replace("0x", "");

            if (int.TryParse(cleanCode, System.Globalization.NumberStyles.HexNumber, null, out int unicode))
            {
                return char.ConvertFromUtf32(unicode);
            }

            // 解析失败，原样返回（可能是直接输入的 Unicode 字符）
            return code;
        }
    }
}
