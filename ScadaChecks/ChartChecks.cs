using System.Collections.Generic;
using System.Linq;
using VisionMaster.Scada;

namespace ScadaChecks
{
    internal partial class Program
    {
        /// <summary>
        /// [CH] 柱状图 / 饼图的纯计算层（编解码 + 轴范围 + 排序 + 分片合并）。
        ///
        /// 绘制部分不在这里测（要有窗口），但"画多高、排哪、哪片被合并"这些规则全在
        /// <see cref="ScadaChartMath"/> 里，所以这些断言能钉住图表最容易出错的口径。
        /// </summary>
        private static class ChartChecks
        {
            public static void Run()
            {
                Section("[CH] 柱状图 / 饼图：编解码 + 轴范围 + 排序 + 分片合并");

                // ---------------- 编解码 ----------------

                var items = ScadaChartCodec.Parse(
                    "[{\"Label\":\" 周一 \",\"Variable\":\" Qty1 \",\"Color\":\"#38bdf8\"}," +
                    "{\"Label\":\"\",\"Variable\":\"Qty2\"}," +
                    "{\"Label\":\"\",\"Variable\":\"  \"}," +
                    "{\"Label\":\"坏色\",\"Variable\":\"Qty3\",\"Color\":\"red\"}]");

                Check("[CH1] 数据项：标签/变量去空白；空标签回落变量名；全空行丢弃；非法颜色清空（回落默认配色）",
                    items.Count == 3
                    && items[0].Label == "周一" && items[0].Variable == "Qty1" && items[0].Color == "#38BDF8"
                    && items[1].Label == "Qty2"
                    && items[2].Label == "坏色" && items[2].Color == "",
                    string.Join(" | ", items.Select(i => $"{i.Label}/{i.Variable}/{i.Color}")));

                var roundTrip = ScadaChartCodec.Parse(ScadaChartCodec.Format(items));
                Check("[CH2] 编解码往返稳定（规范 JSON 读回来逐项相等）",
                    roundTrip.Count == items.Count
                    && roundTrip.Zip(items).All(p => p.First.Label == p.Second.Label
                                                     && p.First.Variable == p.Second.Variable
                                                     && p.First.Color == p.Second.Color),
                    ScadaChartCodec.Format(items));

                Check("[CH3] 坏 JSON 不抛：当作「没有数据项」（一张图配坏了不该让整张画面打不开）",
                    ScadaChartCodec.Parse("{oops").Count == 0 && ScadaChartCodec.Parse(null).Count == 0,
                    "");

                // ---------------- 轴范围（含负值） ----------------

                var bars = new List<ScadaChartMath.BarDatum>
                {
                    new("A", 120, ""),
                    new("B", -40, ""),
                    new("C", null, ""),
                };

                var range = ScadaChartMath.Range(bars, 0);
                var zero = ScadaChartMath.ZeroFraction(range);
                var fA = ScadaChartMath.Fraction(120, range);
                var fB = ScadaChartMath.Fraction(-40, range);

                Check("[CH4] 纵轴一定包含 0：范围 = [-40, 120]，0 基线在 25% 处；正值向上、负值向下都落在 [0,1]",
                    range.Min == -40 && range.Max == 120
                    && System.Math.Abs(zero - 0.75) < 1e-9
                    && fA > zero && fB < zero && fA <= 1 && fB >= 0,
                    $"range=[{range.Min},{range.Max}] zero={zero:0.###} fA={fA:0.###} fB={fB:0.###}");

                var flat = ScadaChartMath.Range(new[] { new ScadaChartMath.BarDatum("A", 0, "") }, 0);
                Check("[CH5] 全 0 / 全缺失：跨度回落到 1，不除零、不出 NaN",
                    flat.Max - flat.Min > 0
                    && !double.IsNaN(ScadaChartMath.Fraction(0, flat)),
                    $"range=[{flat.Min},{flat.Max}]");

                // ---------------- 排序 ----------------

                var sorted = ScadaChartMath.Sort(new[]
                {
                    new ScadaChartMath.BarDatum("缺", null, ""),
                    new ScadaChartMath.BarDatum("小", 10, ""),
                    new ScadaChartMath.BarDatum("大", 90, ""),
                }, ScadaChartMath.BarSort.Descending);

                Check("[CH6] 降序排序：大 → 小，未解析的柱永远排最后（它没有可比的数值）",
                    sorted.Select(b => b.Label).SequenceEqual(new[] { "大", "小", "缺" }),
                    string.Join(" → ", sorted.Select(b => b.Label)));

                // ---------------- 饼图分片 ----------------

                var slices = ScadaChartMath.Slices(new[]
                {
                    new ScadaChartMath.PieDatum("尺寸", 42, "#2563EB"),
                    new ScadaChartMath.PieDatum("外观", 26, "#38BDF8"),
                    new ScadaChartMath.PieDatum("漏装", 18, "#22C55E"),
                    new ScadaChartMath.PieDatum("其他", 2, "#94A3B8"),
                    new ScadaChartMath.PieDatum("负值", -3, ""),
                    new ScadaChartMath.PieDatum("未解析", null, ""),
                }, 0.03);

                Check("[CH7] 饼图：忽略负值与未解析；<3% 并入「其他」并注明合并了几项；占比合计 100%",
                    slices.Count == 4
                    && slices[^1].IsMerged && slices[^1].Label.Contains("合并 1 项")
                    && System.Math.Abs(slices.Sum(s => s.Percent) - 100d) < 1e-6
                    && slices.All(s => s.Value > 0),
                    string.Join(" | ", slices.Select(s => $"{s.Label}:{s.Value}({s.Percent:0.#}%)")));

                var empty = ScadaChartMath.Slices(new[] { new ScadaChartMath.PieDatum("零", 0, "") }, 0.03);
                Check("[CH8] 合计为 0：返回空集合（界面画空心圆 + 「没有计数」，不是画一个满圆）",
                    empty.Count == 0, "");

                Check("[CH9] 数值格式：空 = 0.###；非法格式串回落 0.###（不抛）",
                    ScadaChartMath.Text(12.5, null) == "12.5"
                    && ScadaChartMath.Text(12.5, "F1") == "12.5"
                    && ScadaChartMath.Text(12.5, "Q") == "12.5",
                    ScadaChartMath.Text(12.5, null) + " / " + ScadaChartMath.Text(12.5, "F1"));

                // 属性面板的"绑定渠道"：数据项必须走 ChartSeries 行（摘要 + 配置数据项… + 变量选择器），
                // 不能退化成 MultilineText 裸文本框——2026-10-25 用户实测反馈的正是这一点。
                var barProp = VisionMaster.Scada.Controls.ElementRegistry.Find("Hmi.BarChart")?
                    .Properties.FirstOrDefault(p => p.Key == ScadaChartCodec.BarKey);
                var pieProp = VisionMaster.Scada.Controls.ElementRegistry.Find("Hmi.PieChart")?
                    .Properties.FirstOrDefault(p => p.Key == ScadaChartCodec.PieKey);

                Check("[CH10] 柱/饼的「数据项」走 ChartSeries 行（面板给按钮 + 变量选择器，不是裸文本框）",
                    barProp?.Kind == VisionMaster.Scada.Controls.ElementPropertyKind.ChartSeries
                    && pieProp?.Kind == VisionMaster.Scada.Controls.ElementPropertyKind.ChartSeries,
                    $"bar={barProp?.Kind} / pie={pieProp?.Kind}");
            }
        }
    }
}
