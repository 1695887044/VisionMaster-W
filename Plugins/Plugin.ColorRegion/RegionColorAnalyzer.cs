using System;
using System.Collections.Generic;
using System.Linq;
using Core.Halcon.Color;

namespace Plugin.ColorRegion
{
    /// <summary>一块区域的颜色结论：主色、占比、中位色、各色票数，以及判定结果</summary>
    internal sealed class RegionColorResult
    {
        /// <summary>主色（票数最多的颜色词）</summary>
        internal string Dominant { get; set; } = string.Empty;

        /// <summary>主色占比（0~1）</summary>
        internal double Share { get; set; }

        /// <summary>区域的中位色（排查与示教用）</summary>
        internal int R { get; set; }
        internal int G { get; set; }
        internal int B { get; set; }

        /// <summary>实际参与投票的采样点数</summary>
        internal int SampleCount { get; set; }

        /// <summary>各颜色词的票数（降序）</summary>
        internal List<KeyValuePair<string, int>> Tally { get; } = new();

        internal bool Passed { get; set; }
        internal string Verdict { get; set; } = string.Empty;
        internal List<string> Warnings { get; } = new();

        /// <summary>各色占比明细（给日志、界面、投射用同一份文本）</summary>
        internal string TableText()
            => SampleCount == 0
                ? "(没有采样点)"
                : string.Join("、", Tally.Select(t => $"{t.Key}:{t.Value * 100.0 / SampleCount:0}%"));
    }

    /// <summary>
    /// 区域颜色分析：一块区域是什么颜色 + 与期望比。
    ///
    /// 为什么是"逐点分类再投票"而不是"取区域中位色分类一次"：
    /// 区域里若一半红一半蓝，中位色可能是个"谁都不像"的颜色，却还是会被报出一个词 ——
    /// 现场看不出它其实没把握。逐点投票会直接告诉你"主色红只占 41%"，这是"不纯"，不是"红"。
    ///
    /// 本类不碰 WPF、不碰 Halcon 句柄，输入是纯数组 —— 断言可以直接喂构造数据。
    /// 采样（ROI → 点 → 逐点 RGB）由内核 Core.Halcon.Color.ColorSampler 负责。
    /// </summary>
    internal static class RegionColorAnalyzer
    {
        /// <summary>把主色占比低于这个值时提示"区域颜色不纯"（与判定阈值无关，只是提醒）</summary>
        private const double ImpureHintShare = 0.60;

        /// <summary>
        /// 逐点分类 → 投票 → 主色与占比。
        /// channels 是 [R,G,B] 三个数组（由内核的采样器给出），三个长度必须一致。
        /// </summary>
        internal static bool Analyze(int[][] channels, ColorThresholds thresholds,
            out RegionColorResult result, out string error)
        {
            result = new RegionColorResult();
            error = string.Empty;

            if (channels == null || channels.Length < 3)
            {
                error = $"区域颜色检查要彩色图，当前只有 {channels?.Length ?? 0} 个通道";
                return false;
            }

            int count = channels[0].Length;
            if (count == 0)
            {
                error = "区域里取不到采样点：请重新画一个框";
                return false;
            }

            var tally = new Dictionary<string, int>();
            for (int i = 0; i < count; i++)
            {
                string name = ColorVocabulary.Classify(channels[0][i], channels[1][i], channels[2][i], thresholds);
                tally[name] = tally.TryGetValue(name, out int n) ? n + 1 : 1;
            }

            // 票数降序；票数相同时按词表顺序定序 —— 保证同一块区域每次跑出来的主色都一样
            var ordered = tally
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => Array.IndexOf(ColorVocabulary.AllNames, kv.Key))
                .ToList();

            result.Tally.AddRange(ordered);
            result.SampleCount = count;
            result.Dominant = ordered[0].Key;
            result.Share = (double)ordered[0].Value / count;
            result.R = ColorSampler.Median(channels[0]);
            result.G = ColorSampler.Median(channels[1]);
            result.B = ColorSampler.Median(channels[2]);

            if (result.Share < ImpureHintShare && ordered.Count > 1)
            {
                result.Warnings.Add($"区域颜色不纯：主色「{result.Dominant}」只占 {result.Share * 100:0}%，"
                                  + $"明细 {result.TableText()}；确认采样区是否框到了背景或多种颜色");
            }

            return true;
        }

        /// <summary>
        /// 与期望比：**主色在期望里** 且 **占比 ≥ 最小占比** 才算合格。
        ///
        /// 期望为空 → 报错（与「颜色序列检查」的"配方缺失"同一口径）：
        /// 没配期望就是工程配置问题，不该静默通过、也不该判成产品不良。
        /// 期望里写 `*` → 这一块只报颜色不判定（这是用户显式写的"我要不检"）。
        /// </summary>
        internal static bool Judge(RegionColorResult result, string[] expected, double minShare, out string error)
        {
            error = string.Empty;

            if (expected == null || expected.Length == 0)
            {
                error = "尚未配置期望颜色：请在节点配置里填「期望颜色」（多个用逗号分隔，写 * 表示只报颜色不判定）";
                return false;
            }

            bool anyColor = expected.Contains(ColorVocabulary.Any);
            if (anyColor)
            {
                result.Passed = true;
                result.Verdict = $"只报颜色不判定：{result.Dominant}（占 {result.Share * 100:0}%，"
                               + $"中位色 R{result.R} G{result.G} B{result.B}）";
                return true;
            }

            var unknown = expected.Where(e => !ColorVocabulary.IsKnown(e)).ToArray();
            if (unknown.Length > 0)
            {
                error = $"期望颜色里有认不出的词：{string.Join("、", unknown)}；"
                      + $"颜色词只有 {string.Join("、", ColorVocabulary.AllNames)}（或写 {ColorVocabulary.Any} 表示不判定）";
                return false;
            }

            string expectedText = string.Join(",", expected);
            bool colorOk = expected.Contains(result.Dominant);
            bool shareOk = result.Share >= minShare;

            if (colorOk && shareOk)
            {
                result.Passed = true;
                result.Verdict = $"颜色正确：{result.Dominant}（占 {result.Share * 100:0}%，期望 {expectedText}）";
                return true;
            }

            result.Passed = false;
            result.Verdict = colorOk
                ? $"颜色不纯：主色 {result.Dominant} 只占 {result.Share * 100:0}%（要求 ≥ {minShare * 100:0}%），明细 {result.TableText()}"
                : $"颜色错误：实测 {result.Dominant}（占 {result.Share * 100:0}%），期望 {expectedText}；明细 {result.TableText()}";
            return true;
        }
    }
}
