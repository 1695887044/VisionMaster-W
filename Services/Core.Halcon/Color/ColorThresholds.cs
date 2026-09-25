namespace Core.Halcon.Color
{
    /// <summary>
    /// 颜色判据阈值。
    ///
    /// 为什么是"每个算子各带一份"而不是全局一份：
    /// 不同工位、不同产品的打光与材质不一样，"玫红从哪里算起"本来就可能不同；
    /// 全局一份色卡会变成束缚（要么大家互相迁就，要么再支持多份、复杂度又上一层）。
    /// 所以内核只提供**默认值**，各算子在界面上放一份可覆盖的副本，随方案落盘。
    ///
    /// 这一套默认值是在 cable1 / cable2 两张样本图上验过的（10 芯与 5 芯都判对）。
    /// </summary>
    public sealed class ColorThresholds
    {
        // ---- 无彩 / 有彩：饱和度低于它就算"没有颜色"（黑/白/灰/棕都住在这条线以下）----
        /// <summary>无彩/有彩分界（饱和度）</summary>
        public double ColorlessSaturation { get; set; } = 0.45;

        // ---- 无彩区里再细分 ----
        /// <summary>无彩且明度低于它 → 黑</summary>
        public double BlackValue { get; set; } = 80;

        /// <summary>无彩且饱和度低于它、明度高于 WhiteValue → 白</summary>
        public double WhiteSaturation { get; set; } = 0.15;

        /// <summary>无彩且明度高于它 → 白</summary>
        public double WhiteValue { get; set; } = 195;

        /// <summary>无彩但偏暖（红比绿蓝都高出这么多）且明度低于 BrownValue → 棕</summary>
        public int BrownWarmth { get; set; } = 12;

        /// <summary>棕的明度上限（比它亮就不是棕，是浅棕/米色，归灰）</summary>
        public double BrownValue { get; set; } = 175;

        // ---- 有彩区按色相分色系（单位：度，0~360）----
        /// <summary>玫红区间起点</summary>
        public double RoseHueFrom { get; set; } = 330;

        /// <summary>红区间起点（玫红区间的终点也是它）</summary>
        public double RedHueFrom { get; set; } = 350;

        /// <summary>红的色相上限（红跨 0 度，所以它的区间是 [RedHueFrom,360) ∪ [0,RedHueTo)）</summary>
        public double RedHueTo { get; set; } = 20;

        /// <summary>橙的色相上限</summary>
        public double OrangeHueTo { get; set; } = 45;

        /// <summary>黄的色相上限</summary>
        public double YellowHueTo { get; set; } = 70;

        /// <summary>绿的色相上限</summary>
        public double GreenHueTo { get; set; } = 160;

        /// <summary>蓝的色相上限（比它高就是紫）</summary>
        public double BlueHueTo { get; set; } = 230;

        /// <summary>复制一份（算子从内核取默认值时用它，免得改到内核里的那份）</summary>
        public ColorThresholds Clone() => (ColorThresholds)MemberwiseClone();
    }
}
