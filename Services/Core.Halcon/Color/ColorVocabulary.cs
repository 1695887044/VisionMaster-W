using System;

namespace Core.Halcon.Color
{
    /// <summary>
    /// 颜色词表与分类：把一个 RGB 归到一个**颜色词**。
    ///
    /// 为什么要做成"颜色词"而不是直接给 RGB：
    /// 现场说的是"黑、棕、玫红、红、黄"，不是"R=200 G=30 B=40"。
    /// 判定、配方、日志、画面标签全都用同一套词，才能对得上话。
    ///
    /// 判据的层次（与线序脚本版一致，那一套已经在样本图上验过）：
    ///   先判"有没有颜色"：暗到没色相 → 黑；亮且不彩 → 白；不彩但偏暖又偏暗 → 棕；
    ///   剩下的不彩情况只能给灰（灰是中性色，R/G/B 基本齐平）。
    ///   有颜色的按色相分色系；红与玫红只差十几度，分界取 350 度。
    ///
    /// 阈值全部来自 <see cref="ColorThresholds"/>（可调），本类不藏任何魔数。
    /// </summary>
    public static class ColorVocabulary
    {
        public const string Black = "黑";
        public const string White = "白";
        public const string Gray = "灰";
        public const string Brown = "棕";
        public const string Red = "红";
        public const string Rose = "玫红";
        public const string Orange = "橙";
        public const string Yellow = "黄";
        public const string Green = "绿";
        public const string Blue = "蓝";
        public const string Purple = "紫";

        /// <summary>通配符：这一位不检</summary>
        public const string Any = "*";

        /// <summary>全部颜色词（配方里能写的词就是这些）</summary>
        public static readonly string[] AllNames =
        {
            Black, White, Gray, Brown, Red, Rose, Orange, Yellow, Green, Blue, Purple
        };

        /// <summary>这套词认不认得它（配方里写错词时给现场提示用）</summary>
        public static bool IsKnown(string name)
            => name == Any || Array.IndexOf(AllNames, name) >= 0;

        /// <summary>把 RGB 归到一个颜色词</summary>
        public static string Classify(int r, int g, int b, ColorThresholds thresholds)
        {
            double mx = Math.Max(r, Math.Max(g, b));
            double mn = Math.Min(r, Math.Min(g, b));
            double d = mx - mn;
            double s = mx <= 0 ? 0 : d / mx;   // 饱和度
            double v = mx;                     // 明度

            if (s < thresholds.ColorlessSaturation)
            {
                if (v < thresholds.BlackValue) return Black;
                if (s < thresholds.WhiteSaturation && v > thresholds.WhiteValue) return White;
                if ((r - g) >= thresholds.BrownWarmth && (r - b) >= thresholds.BrownWarmth
                    && v < thresholds.BrownValue) return Brown;
                return Gray;
            }

            double h;
            if (d == 0) h = 0;
            else if (mx == r) h = 60 * (((g - b) / d) % 6);
            else if (mx == g) h = 60 * (((b - r) / d) + 2);
            else h = 60 * (((r - g) / d) + 4);
            if (h < 0) h += 360;

            if (h >= thresholds.RoseHueFrom && h < thresholds.RedHueFrom) return Rose;
            if (h >= thresholds.RedHueFrom || h < thresholds.RedHueTo) return Red;
            if (h < thresholds.OrangeHueTo) return Orange;
            if (h < thresholds.YellowHueTo) return Yellow;
            if (h < thresholds.GreenHueTo) return Green;
            if (h < thresholds.BlueHueTo) return Blue;
            return Purple;
        }

        /// <summary>色相（度）；无彩时返回 0</summary>
        public static double Hue(int r, int g, int b)
        {
            double mx = Math.Max(r, Math.Max(g, b));
            double mn = Math.Min(r, Math.Min(g, b));
            double d = mx - mn;
            if (d == 0) return 0;

            double h;
            if (mx == r) h = 60 * (((g - b) / d) % 6);
            else if (mx == g) h = 60 * (((b - r) / d) + 2);
            else h = 60 * (((r - g) / d) + 4);
            return h < 0 ? h + 360 : h;
        }

        /// <summary>饱和度（0~1）</summary>
        public static double Saturation(int r, int g, int b)
        {
            double mx = Math.Max(r, Math.Max(g, b));
            return mx <= 0 ? 0 : (mx - Math.Min(r, Math.Min(g, b))) / mx;
        }
    }
}
