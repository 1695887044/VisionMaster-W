using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace VisionMaster.Helpers
{
    /// <summary>
    /// 工具箱分类的「专属图标 + 专属色」映射表。
    ///
    /// 为什么要单独一张表：分类名来自插件 <c>DisplayAttribute.GroupName</c>（见
    /// <c>Engine\PluginService.cs</c> 的 <c>Category = att.GroupName</c>），是**插件侧**的自由文本——
    /// 面板不能把颜色写进每个条目模板里按名字硬编码，也不能让插件自己带颜色
    /// （插件只引用 Core.Interfaces，够不到主题令牌）。折中是宿主侧维护一张"名字 → 观感"的表：
    /// 插件只管报名字，长什么样由面板定；表里没有的名字一律回落"通用图标 + 灰"，
    /// 所以**新增一个分类不需要改这个文件**，只是暂时拿不到专属色。
    ///
    /// 图标码点是 Font Awesome 6 Pro Solid 的私用区字符（与各插件 <c>ShortName</c> 同一套字体，
    /// 见 App.xaml 的 <c>Icon</c> 令牌），这里挑的是与分类语义最贴的那几个。
    /// 颜色用于**图标前景**（浅色底上写字，全部按对比度挑过），不要拿去当大面积底色。
    ///
    /// 画刷一律冻结后缓存：条目模板每次实例化都会取一次，不缓存就是每行一个新画刷对象。
    /// </summary>
    public static class ToolboxCategoryVisuals
    {
        /// <summary>未知分类的兜底图标（cubes：中性、不指向任何具体语义）</summary>
        public const string FallbackIcon = "\uf1b3";

        /// <summary>收藏分组的图标（实心星）</summary>
        public const string FavoriteIcon = "\uf005";

        /// <summary>收藏分组的专属色（琥珀金，与 ScadaWarningBrush 同值但各守各的域）</summary>
        public static readonly Color FavoriteColor = Color.FromRgb(0xE6, 0xA2, 0x3C);

        /// <summary>未知分类的兜底色（与 ScadaSecondaryTextBrush 同值）</summary>
        public static readonly Color FallbackColor = Color.FromRgb(0x90, 0x93, 0x99);

        /// <summary>
        /// 分类 →（图标, 颜色）。键必须与插件 <c>GroupName</c> 逐字一致（序数比较）。
        /// 13 条 = 当前全部模块算子分类（不含"相机/相机驱动/运动卡"这三个设备类分组——
        /// 它们走 CameraPlugins / MotionPlugins，不进工具箱面板）。
        /// </summary>
        private static readonly Dictionary<string, (string Icon, Color Color)> Map = new(StringComparer.Ordinal)
        {
            ["逻辑控制"] = ("\uf0e8", Color.FromRgb(0x63, 0x66, 0xF1)),
            ["流程控制"] = ("\uf126", Color.FromRgb(0x0E, 0xA5, 0xE9)),
            ["逻辑判断"] = ("\uf1b2", Color.FromRgb(0x8B, 0x5C, 0xF6)),
            ["变量操作"] = ("\uf044", Color.FromRgb(0x14, 0xB8, 0xA6)),
            ["图像处理"] = ("\uf1c5", Color.FromRgb(0x3B, 0x82, 0xF6)),
            ["缺陷检测"] = ("\uf188", Color.FromRgb(0xEF, 0x44, 0x44)),
            ["测量"] = ("\uf545", Color.FromRgb(0xF5, 0x9E, 0x0B)),
            ["标定"] = ("\uf05b", Color.FromRgb(0x06, 0xB6, 0xD4)),
            ["几何工具"] = ("\uf140", Color.FromRgb(0x10, 0xB9, 0x81)),
            ["定位"] = ("\uf3c5", Color.FromRgb(0xF9, 0x73, 0x16)),
            ["数据处理"] = ("\uf1c0", Color.FromRgb(0x64, 0x74, 0x8B)),
            ["常用工具"] = ("\uf080", Color.FromRgb(0x6B, 0x72, 0x80)),
            ["运动控制"] = ("\uf0d1", Color.FromRgb(0x65, 0xA3, 0x0D)),
        };

        /// <summary>已知分类名（升序无关，按 Map 声明顺序）。断言用它逐条核对"没有漏配"。</summary>
        public static IReadOnlyCollection<string> Categories => Map.Keys;

        private static readonly Dictionary<string, Brush> BrushCache = new(StringComparer.Ordinal);
        private static readonly object BrushGate = new();

        /// <summary>分类图标码点；未知分类回落 <see cref="FallbackIcon"/>。</summary>
        public static string IconFor(string? category)
            => category != null && Map.TryGetValue(category, out var v) ? v.Icon : FallbackIcon;

        /// <summary>分类专属色；未知分类回落 <see cref="FallbackColor"/>。</summary>
        public static Color ColorFor(string? category)
            => category != null && Map.TryGetValue(category, out var v) ? v.Color : FallbackColor;

        /// <summary>分类专属画刷（已冻结、按分类缓存）；未知分类回落灰。</summary>
        public static Brush BrushFor(string? category)
        {
            var key = category != null && Map.ContainsKey(category) ? category : string.Empty;

            lock (BrushGate)
            {
                if (BrushCache.TryGetValue(key, out var cached)) return cached;

                var brush = new SolidColorBrush(ColorFor(category));
                brush.Freeze();
                BrushCache[key] = brush;
                return brush;
            }
        }

        /// <summary>收藏分组的画刷（已冻结、单例）。</summary>
        public static Brush FavoriteBrush { get; } = CreateFrozen(FavoriteColor);

        private static Brush CreateFrozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }

    /// <summary>
    /// 分类名 → 分类专属画刷（给"悬停预览浮层"里的分类色图标/胶囊用）。
    ///
    /// 为什么预览浮层不能像条目模板那样直接绑组 VM 的 AccentBrush：
    /// ToolTip 的内容活在独立的 Popup 可视化树里，<c>RelativeSource AncestorType=...</c>
    /// 从浮层内部**走不到**条目的祖先链（能找到的祖先只有 Popup 自己），
    /// 所以只能从条目自身的 <c>Category</c> 反查——查的就是上面那张表。
    /// </summary>
    public sealed class CategoryAccentBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => ToolboxCategoryVisuals.BrushFor(value as string);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
