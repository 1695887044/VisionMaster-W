using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace VisionMaster.Scada
{
    /// <summary>
    /// 柱状图 / 饼图的一条数据项（设计期配置，存属性袋里的一条规范 JSON）。
    ///
    /// 为什么是"标签 + 变量名"而不是"变量 Id + 名字"
    /// ---------
    /// 图表的数据项是<b>给人配的展示项</b>（"周一 / 周二…"），不是可寻址的对象引用：
    /// 变量被删/改名时，图表应该显示"未解析"并让作者改配置，而不是像动作表那样按 Id 静默迁移。
    /// 与报表列的 <c>ScadaReportColumn.Source</c> 同一口径（按名字解析）。
    /// </summary>
    public sealed class ScadaChartItem
    {
        /// <summary>轴/图例上的文字</summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>取值变量名（按名字解析，空 = 这一项未配置）</summary>
        public string Variable { get; set; } = string.Empty;

        /// <summary>颜色（#RRGGBB / #AARRGGBB）；空 = 用图元的默认配色</summary>
        public string Color { get; set; } = string.Empty;

        public bool HasVariable => !string.IsNullOrWhiteSpace(Variable);
    }

    /// <summary>
    /// 图表数据项的编解码：属性袋里存的是规范 JSON（与 <see cref="ScadaReportCodec"/> 同一范式）。
    ///
    /// 规范化口径（写进袋子之前一律走这里，读回来也走这里）：
    /// · 标签去空白；空标签回落成变量名；两者都空的行丢弃；
    /// · 变量名去空白；
    /// · 颜色只接受 <c>#RGB</c> / <c>#RRGGBB</c> / <c>#AARRGGBB</c> 形态，其它一律清空（回落默认配色），
    ///   不因为一个手打错的颜色让整张图打不开；
    /// · <b>重复变量名不丢</b>：同一变量画两根柱（"今日/本周"用同一变量的不同区间）在图上是有意义的，
    ///   只在标签上体现差别即可。
    /// </summary>
    public static class ScadaChartCodec
    {
        /// <summary>柱状图数据项在属性袋里的键</summary>
        public const string BarKey = "BarSeries";

        /// <summary>饼图数据项在属性袋里的键</summary>
        public const string PieKey = "PieSlices";

        /// <summary>柱数上限：超过就建议改用数据表格（设计基线 Q1 = 24）</summary>
        public const int MaxBars = 24;

        /// <summary>饼图分片上限：超过自动并入「其他」（设计基线 §6）</summary>
        public const int MaxSlices = 8;

        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };

        public static IReadOnlyList<ScadaChartItem> Parse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return Array.Empty<ScadaChartItem>();

            List<ScadaChartItem>? raw;
            try
            {
                raw = JsonSerializer.Deserialize<List<ScadaChartItem>>(json, Options);
            }
            catch (JsonException)
            {
                // 配置坏了不该让整张画面打不开：当作"没有数据项"，由界面显示"未配置"
                return Array.Empty<ScadaChartItem>();
            }

            if (raw == null)
                return Array.Empty<ScadaChartItem>();

            var result = new List<ScadaChartItem>(raw.Count);
            foreach (var item in raw)
            {
                if (item == null)
                    continue;

                var variable = (item.Variable ?? string.Empty).Trim();
                var label = (item.Label ?? string.Empty).Trim();
                if (variable.Length == 0 && label.Length == 0)
                    continue;

                result.Add(new ScadaChartItem
                {
                    Label = label.Length == 0 ? variable : label,
                    Variable = variable,
                    Color = NormalizeColor(item.Color),
                });
            }

            return result;
        }

        public static string Format(IEnumerable<ScadaChartItem>? items)
            => JsonSerializer.Serialize((items ?? Enumerable.Empty<ScadaChartItem>()).ToList(), Options);

        /// <summary>颜色规范化：只留 #RGB / #RRGGBB / #AARRGGBB，其余清空</summary>
        public static string NormalizeColor(string? color)
        {
            var text = (color ?? string.Empty).Trim();
            if (text.Length == 0 || text[0] != '#')
                return string.Empty;

            for (var i = 1; i < text.Length; i++)
            {
                if (!Uri.IsHexDigit(text[i]))
                    return string.Empty;
            }

            return text.Length is 4 or 7 or 9 ? text.ToUpperInvariant() : string.Empty;
        }
    }

    /// <summary>
    /// 柱状图/饼图的纯计算：纵轴范围、排序、饼图合并与占比。
    ///
    /// 抽出来的理由与 <see cref="ScadaReportColumnImport"/> 相同：这些规则<b>必须能在断言里跑</b>，
    /// 而画布上的控件既没窗口也没数据源。绘制只消费这里算好的结果。
    /// </summary>
    public static class ScadaChartMath
    {
        /// <summary>柱状图数据项（值已解析：<c>null</c> = 变量未解析，不等于 0）</summary>
        public readonly record struct BarDatum(string Label, double? Value, string Color);

        public enum BarSort
        {
            None = 0,
            Descending = 1,
            Ascending = 2,
        }

        /// <summary>
        /// 纵轴范围：<b>一定包含 0</b>。
        ///
        /// 只按最大值算会把负柱画到框外（设计评审 V2）；<paramref name="fixedMax"/> &gt; 0 时上界用它，
        /// 但下界仍按真实最小值（固定上界不该把负值截掉）。
        /// </summary>
        public static (double Min, double Max) Range(IEnumerable<BarDatum> bars, double fixedMax)
        {
            var min = 0d;
            var max = 0d;
            var any = false;

            foreach (var bar in bars)
            {
                if (bar.Value is not { } value)
                    continue;

                any = true;
                if (value < min) min = value;
                if (value > max) max = value;
            }

            if (fixedMax > 0 && fixedMax > max)
                max = fixedMax;

            // 全 0 或全缺失：给一个 1 的跨度，避免除零（柱高算成 0，不是 NaN）
            if (!any || (max - min) <= 1e-9)
                max = min + 1;

            return (min, max);
        }

        /// <summary>
        /// 一根柱占绘图区的比例（0~1，相对 <see cref="Range"/> 的跨度）。
        /// 返回的是"从 0 基线到该值的长度"，调用方按 0 的位置分上下画。
        /// </summary>
        public static double Fraction(double value, (double Min, double Max) range)
        {
            var span = range.Max - range.Min;
            if (span <= 1e-9)
                return 0;

            return Math.Clamp((value - range.Min) / span, 0, 1);
        }

        /// <summary>0 基线在绘图区里的比例位置（0 = 顶部，1 = 底部）</summary>
        public static double ZeroFraction((double Min, double Max) range)
        {
            var span = range.Max - range.Min;
            if (span <= 1e-9)
                return 1;

            return Math.Clamp((range.Max - 0) / span, 0, 1);
        }

        public static IReadOnlyList<BarDatum> Sort(IEnumerable<BarDatum> bars, BarSort sort)
        {
            var list = bars.ToList();
            if (sort == BarSort.None)
                return list;

            // 未解析的柱永远排最后：它们没有可比的数值，混在中间会让人以为"值很小"
            return sort == BarSort.Descending
                ? list.OrderBy(b => b.Value.HasValue ? 0 : 1).ThenByDescending(b => b.Value ?? 0).ToList()
                : list.OrderBy(b => b.Value.HasValue ? 0 : 1).ThenBy(b => b.Value ?? 0).ToList();
        }

        /// <summary>饼图一片（值已解析；<c>null</c> = 未解析，不参与占比）</summary>
        public readonly record struct PieDatum(string Label, double? Value, string Color);

        /// <summary>饼图合并后的结果</summary>
        public readonly record struct PieSlice(string Label, double Value, double Percent, string Color, bool IsMerged);

        /// <summary>
        /// 饼图分片：忽略未解析与负值（占比里没有负数），按 <paramref name="mergeBelow"/> 把
        /// 小于该占比的片并入「其他」；片数超过 <see cref="ScadaChartCodec.MaxSlices"/> 时
        /// 强制按 2% 合并。
        /// </summary>
        public static IReadOnlyList<PieSlice> Slices(IEnumerable<PieDatum> data, double mergeBelow)
        {
            var valid = data
                .Where(d => d.Value is > 0)
                .Select(d => (d.Label, Value: d.Value!.Value, d.Color))
                .ToList();

            var total = valid.Sum(v => v.Value);
            if (total <= 0)
                return Array.Empty<PieSlice>();

            var threshold = Math.Max(mergeBelow, 0);

            if (valid.Count > ScadaChartCodec.MaxSlices && threshold <= 0)
                threshold = 0.02;

            List<(string Label, double Value, string Color)> kept = valid;
            double mergedValue = 0;
            int mergedCount = 0;

            if (threshold > 0)
            {
                kept = new List<(string, double, string)>();
                foreach (var item in valid)
                {
                    if (item.Value / total < threshold)
                    {
                        mergedValue += item.Value;
                        mergedCount++;
                    }
                    else
                    {
                        kept.Add(item);
                    }
                }
            }

            var result = new List<PieSlice>(kept.Count + 1);
            foreach (var item in kept)
            {
                result.Add(new PieSlice(item.Label, item.Value, item.Value / total * 100d, item.Color, false));
            }

            if (mergedCount > 0)
            {
                result.Add(new PieSlice($"其他（合并 {mergedCount} 项）", mergedValue,
                    mergedValue / total * 100d, "#CBD5E1", true));
            }

            return result;
        }

        /// <summary>按当前区域性无关的格式把数值转成显示文本（图表与断言共用）</summary>
        public static string Text(double value, string? format)
        {
            var f = string.IsNullOrWhiteSpace(format) ? "0.###" : format.Trim();
            try
            {
                return value.ToString(f, CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
                return value.ToString("0.###", CultureInfo.InvariantCulture);
            }
        }
    }
}
