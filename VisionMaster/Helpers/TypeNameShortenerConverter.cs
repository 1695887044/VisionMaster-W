using System;
using System.Globalization;
using System.Windows.Data;

namespace VisionMaster.Helpers
{
    /// <summary>
    /// 把端口定义里的类型名缩短成"人能读"的短名。
    ///
    /// 为什么需要它：<c>PortDefinition.DataTypeName</c> 存的是
    /// <c>Type.AssemblyQualifiedName</c>（见 Engine\PluginService.cs 的端口组装），
    /// 形如 <c>"HalconDotNet.HImage, halcondotnet, Version=25.5.0.0, Culture=neutral, PublicKeyToken=..."</c>——
    /// 直接上屏会把悬浮预览撑成一行程序集清单。这里只取类型短名：
    /// 去程序集限定（首个逗号起）、去泛型实参（首个 '[' 起）、取最后一段、去泛型元数反引号。
    /// 认不出的一律返回空串，绝不抛。
    /// </summary>
    public sealed class TypeNameShortenerConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => Shorten(value as string);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();

        /// <summary>
        /// 缩短类型名（转换器与断言共用同一份实现，避免"两处口径不一致"）。
        /// 例：<c>System.Double</c> → <c>Double</c>；
        /// <c>HalconDotNet.HImage, halcondotnet, Version=…</c> → <c>HImage</c>；
        /// <c>System.Collections.Generic.List`1[[System.Double, …]], …</c> → <c>List</c>。
        /// </summary>
        public static string Shorten(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

            var s = raw.Trim();

            int comma = s.IndexOf(',');       // 去程序集限定（AssemblyQualifiedName 的尾巴）
            if (comma >= 0) s = s.Substring(0, comma);

            int bracket = s.IndexOf('[');     // 去泛型实参
            if (bracket >= 0) s = s.Substring(0, bracket);

            int dot = s.LastIndexOf('.');     // 只留最后一段（命名空间全去掉）
            if (dot >= 0) s = s.Substring(dot + 1);

            int tick = s.IndexOf('`');        // 去泛型元数（List`1 → List）
            if (tick >= 0) s = s.Substring(0, tick);

            return s;
        }
    }
}
