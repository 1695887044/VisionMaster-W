using System;
using System.Windows;
using System.Windows.Media;

namespace VM.Charts
{
    /// <summary>
    /// 图表主题:所有颜色优先取 Fluent 令牌(换肤自动跟随),
    /// 宿主没有合并 Fluent 字典时退回内置的 Fluent 同款色值。
    /// </summary>
    public static class ChartTheme
    {
        // 画布
        public static Color FigureBackground = Color.FromRgb(0xFF, 0xFF, 0xFF);
        public static Color DataBackground = Color.FromRgb(0xFF, 0xFF, 0xFF);
        public static Color Grid = Color.FromRgb(0xE0, 0xE0, 0xE0);
        public static Color Tick = Color.FromRgb(0x60, 0x60, 0x60);
        public static Color AxisLabel = Color.FromRgb(0x30, 0x30, 0x30);
        public static Color TitleLabel = Color.FromRgb(0x30, 0x30, 0x30);

        // 系列自动配色(A=0 的系列按创建顺序从这里取色)
        public static readonly Color[] SeriesPalette =
        {
            Color.FromRgb(0x0F, 0x6C, 0xBD),   // Accent
            Color.FromRgb(0x0E, 0x8A, 0x5F),   // Success
            Color.FromRgb(0xD9, 0x73, 0x0D),   // Warning
            Color.FromRgb(0xC5, 0x0F, 0x1F),   // Danger
            Color.FromRgb(0x7A, 0x50, 0xC8),   // 紫
            Color.FromRgb(0x00, 0x99, 0xBC),   // 青
        };

        /// <summary>从应用资源重载主题色(键不存在时保持当前值)。加载/主题切换后调用。</summary>
        public static void Reload()
        {
            FigureBackground = Resource("FluentBackgroundBrush", FigureBackground);
            DataBackground = Resource("FluentSurfaceBrush", DataBackground);
            Grid = Resource("FluentBorderBrush", Grid);
            Tick = Resource("FluentTextSecondaryBrush", Tick);
            AxisLabel = Resource("FluentTextPrimaryBrush", AxisLabel);
            TitleLabel = Resource("FluentTextPrimaryBrush", TitleLabel);

            var accent = Resource("FluentAccentBrush", SeriesPalette[0]);
            var success = Resource("FluentSuccessBrush", SeriesPalette[1]);
            var warning = Resource("FluentWarningBrush", SeriesPalette[2]);
            var danger = Resource("FluentDangerBrush", SeriesPalette[3]);
            SeriesPalette[0] = accent;
            SeriesPalette[1] = success;
            SeriesPalette[2] = warning;
            SeriesPalette[3] = danger;
        }

        private static Color Resource(string key, Color fallback)
        {
            var brush = Application.Current?.TryFindResource(key) as SolidColorBrush;
            return brush != null ? brush.Color : fallback;
        }

        internal static System.Drawing.Color ToDrawing(Color c)
        {
            return System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B);
        }

        /// <summary>给"A=0(未指定颜色)"的系列分配色板色。</summary>
        internal static Color AutoColor(int index)
        {
            return SeriesPalette[index % SeriesPalette.Length];
        }
    }
}
