using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace VisionMaster.Helpers
{
    /// <summary>
    /// 组态图元分类的「专属图标 + 专属色」映射表（图元工具箱用）。
    ///
    /// 为什么不复用 <see cref="ToolboxCategoryVisuals"/>：那张表的键是<b>插件</b>的
    /// <c>DisplayAttribute.GroupName</c>，而且有一条断言把它钉成"表内条数 == 插件分类数"
    /// （`FlowCanvasChecks` 的分类色覆盖检查）。把组态图元的八个分类塞进去，那条断言会红，
    /// 而它守的是插件侧"新增分类必须配色"的纪律，不该为了这里放松。
    /// 两张表各守各的域：插件算子一套，组态图元一套，互不影响。
    ///
    /// 颜色只用于<b>图标前景 + 浅底</b>（22×22 的圆角块），不铺大面积；
    /// 与 `design.html` 的 `.cat-*` 分类色同一套观感，但按组态自己的八个分类重排。
    /// 画刷一律冻结后缓存：条目模板每实例化一行就取一次，不缓存就是每行一个新对象。
    /// </summary>
    public static class ScadaCategoryVisuals
    {
        /// <summary>未知分类的兜底图标（cubes：中性、不指向任何具体语义）</summary>
        public const string FallbackIcon = "\uf1b3";

        /// <summary>未知分类的兜底色（与 ScadaSecondaryTextBrush 同值）</summary>
        public static readonly Color FallbackColor = Color.FromRgb(0x90, 0x93, 0x99);

        /// <summary>「我的模板」那一组的图标（复制/图层，与图元分类区分开）</summary>
        public const string TemplateIcon = "\uf1c0";

        /// <summary>
        /// 分类 →（图标, 颜色）。键必须与 <c>ElementDescriptor.Category</c> 逐字一致（序数比较）。
        /// 图标码点全部是 Font Awesome 6 Pro Solid 私用区字符，且都已在别的视图里用过
        /// （不是从文档抄来的——抄错一个码点就是一枚豆腐块，而且只在真机上才看得见）。
        /// </summary>
        private static readonly Dictionary<string, (string Icon, Color Color)> Map = new(StringComparer.Ordinal)
        {
            ["基础"] = ("\uf0c8", Color.FromRgb(0x02, 0x84, 0xC7)),
            ["操作"] = ("\uf1e6", Color.FromRgb(0x7C, 0x3A, 0xED)),
            ["指示"] = ("\uf0e7", Color.FromRgb(0x16, 0xA3, 0x4A)),
            ["数值"] = ("\uf080", Color.FromRgb(0xB4, 0x53, 0x09)),
            ["工艺"] = ("\uf0e4", Color.FromRgb(0x4F, 0x46, 0xE5)),
            ["报警"] = ("\uf0f3", Color.FromRgb(0xDC, 0x26, 0x26)),
            ["运动"] = ("\uf013", Color.FromRgb(0x0E, 0x74, 0x90)),
            ["数据"] = ("\uf0ce", Color.FromRgb(0x47, 0x55, 0x69)),
        };

        /// <summary>已知分类名（断言用它逐条核对"注册表里的每个分类都配了色"）</summary>
        public static IReadOnlyCollection<string> Categories => Map.Keys;

        /// <summary>
        /// 图元类型键 → 专属图标。
        ///
        /// 为什么不能只按分类取：同一分类下的图元形态差得远（基础组里矩形/椭圆/文本/底板/图像显示
        /// 长的是五样东西），只按分类上色会让整组图标一模一样——用户一眼就看出来"这些图标是假的"。
        /// 设计稿 `design.html` 的图元工具箱也是逐个图元一枚字形。
        ///
        /// 码点全部先用 `GlyphTypeface.CharacterToGlyphMap` 核过"字库里真有这个字形"
        /// （`_ScadaUiProbe --glyphs`），不靠记忆：图标字体里缺码点会渲染成豆腐块，
        /// 而编译期、静态扫描全绿，只有真机那一眼才看得见。
        /// </summary>
        private static readonly Dictionary<string, string> IconMap = new(StringComparer.OrdinalIgnoreCase)
        {
            // 基础
            ["Hmi.Rectangle"] = "\uf0c8",   // square
            ["Hmi.Ellipse"] = "\uf111",     // circle
            ["Hmi.Text"] = "\uf031",        // font
            ["Hmi.Panel"] = "\uf5fd",       // layer-group（容器）
            ["Hmi.ImageView"] = "\uf1c5",   // image

            // 操作
            ["Hmi.Button"] = "\uf205",      // toggle-on
            ["Hmi.BitButton"] = "\uf14a",   // check-square
            ["Hmi.SelectorSwitch"] = "\uf1de", // sliders

            // 指示
            ["Hmi.Indicator"] = "\uf0eb",   // lightbulb
            ["Hmi.Lamp"] = "\uf0e7",        // bolt
            ["Hmi.Clock"] = "\uf017",       // clock

            // 数值
            ["Hmi.ProgressBar"] = "\uf080", // chart-bar
            ["Hmi.IOField"] = "\uf304",     // pen（可写数值域）
            ["Hmi.Gauge"] = "\uf0e4",       // tachometer
            ["Hmi.KpiBoard"] = "\uf201",    // chart-line
            ["Hmi.TextField"] = "\uf120",   // terminal（文本输入）
            ["Hmi.ComboBox"] = "\uf0d7",    // caret-down

            // 工艺
            ["Hmi.Valve"] = "\uf0e8",       // sitemap
            ["Hmi.Pump"] = "\uf04b",        // play（叶轮）
            ["Hmi.Motor"] = "\uf013",       // gear
            ["Hmi.Pipe"] = "\uf337",        // arrows-alt-h
            ["Hmi.Tank"] = "\uf1c0",        // database（柱状容器）

            // 报警
            ["Hmi.AlarmBanner"] = "\uf0f3", // bell
            ["Hmi.AckButton"] = "\uf00c",   // check

            // 运动
            ["Hmi.AxisConsole"] = "\uf11b", // gamepad（控台）
            ["Hmi.AxisPanel"] = "\uf5fe",   // diagram-successor（面板）
            ["Hmi.AxisPointTable"] = "\uf00b", // table-list（点位表）

            // 数据
            ["Hmi.DataGrid"] = "\uf0ce",    // table
            ["Hmi.RecipePanel"] = "\uf0ca", // list（与顶栏配方入口同一枚图标）
        };

        /// <summary>图元类型键的专属图标；没登记的图元回落分类图标，再回落兜底。</summary>
        public static string TypeKeyIconFor(string? typeKey, string? category)
            => typeKey != null && IconMap.TryGetValue(typeKey, out var icon)
                ? icon
                : IconFor(category);

        private static readonly Dictionary<string, Brush> AccentCache = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, Brush> SoftCache = new(StringComparer.Ordinal);
        private static readonly object Gate = new();

        /// <summary>分类图标码点；未知分类回落 <see cref="FallbackIcon"/>。</summary>
        public static string IconFor(string? category)
            => category != null && Map.TryGetValue(category, out var v) ? v.Icon : FallbackIcon;

        /// <summary>分类专属色；未知分类回落 <see cref="FallbackColor"/>。</summary>
        public static Color ColorFor(string? category)
            => category != null && Map.TryGetValue(category, out var v) ? v.Color : FallbackColor;

        /// <summary>分类专属画刷（已冻结、按分类缓存）——图标前景用。</summary>
        public static Brush BrushFor(string? category)
        {
            string key = category != null && Map.ContainsKey(category) ? category : string.Empty;

            lock (Gate)
            {
                if (AccentCache.TryGetValue(key, out var cached)) return cached;

                var brush = new SolidColorBrush(ColorFor(category));
                brush.Freeze();
                AccentCache[key] = brush;
                return brush;
            }
        }

        /// <summary>分类专属<b>浅底</b>（与白色混 88%）——22×22 图标块的背景。</summary>
        public static Brush SoftBrushFor(string? category)
        {
            string key = category != null && Map.ContainsKey(category) ? category : string.Empty;

            lock (Gate)
            {
                if (SoftCache.TryGetValue(key, out var cached)) return cached;

                var c = ColorFor(category);
                byte Mix(byte channel) => (byte)Math.Round(channel + (255 - channel) * 0.88);
                var brush = new SolidColorBrush(Color.FromRgb(Mix(c.R), Mix(c.G), Mix(c.B)));
                brush.Freeze();
                SoftCache[key] = brush;
                return brush;
            }
        }
    }

    /// <summary>分类名 → 图标码点（图元工具箱条目左侧那枚字形）。</summary>
    public sealed class ScadaCategoryIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => ScadaCategoryVisuals.IconFor(value as string);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// 图元（<c>ElementDescriptor</c>）→ 专属图标码点。
    ///
    /// 绑定的是<b>整条图元</b>而不是它的 TypeKey：查表要两把键——先按 TypeKey 找专属图标，
    /// 没登记再回落它的 Category。只传 TypeKey 的话，回落那一步就丢了分类信息，
    /// 未登记的新图元会一律变成同一个兜底图标（正是这次要修的那个毛病）。
    /// </summary>
    public sealed class ScadaElementIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is VisionMaster.Scada.Controls.ElementDescriptor descriptor
                ? ScadaCategoryVisuals.TypeKeyIconFor(descriptor.TypeKey, descriptor.Category)
                : ScadaCategoryVisuals.FallbackIcon;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>分类名 → 专属色画刷（图标前景）。</summary>
    public sealed class ScadaCategoryBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => ScadaCategoryVisuals.BrushFor(value as string);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>分类名 → 专属浅底画刷（图标块背景）。</summary>
    public sealed class ScadaCategorySoftBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => ScadaCategoryVisuals.SoftBrushFor(value as string);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
