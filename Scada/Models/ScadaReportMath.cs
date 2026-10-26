using System;
using System.Collections.Generic;
using System.Globalization;

namespace VisionMaster.Scada
{
    /// <summary>
    /// 报表的算术与分桶（纯函数，无 IO、无 WPF）。
    ///
    /// 为什么单独一层
    /// ---------
    /// 这几条口径必须<b>只有一份</b>：数据源（宿主侧）用它聚合，断言用它核对，
    /// 将来 Web 端要复算时也得照这一份。散在实现里的话，"班次怎么算"
    /// 会变成两个地方各写一遍、还都觉得自己对。
    /// </summary>
    public static class ScadaReportMath
    {
        /// <summary>数值格式缺省值（最多三位小数，去掉尾随零）</summary>
        public const string DefaultNumberFormat = "0.###";

        /// <summary>
        /// 解析班次起点串（<c>08:00,20:00</c>）。容忍中文逗号、分号与空格；
        /// 非法项直接丢弃，去重后按时间升序。全非法时返回空表（调用方据此报"班次没配"）。
        /// </summary>
        public static IReadOnlyList<TimeSpan> ParseShiftStarts(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return Array.Empty<TimeSpan>();

            var result = new List<TimeSpan>();
            foreach (var part in text.Split(new[] { ',', '，', ';', '；', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!TimeSpan.TryParse(part.Trim(), CultureInfo.InvariantCulture, out var span))
                    continue;

                if (span < TimeSpan.Zero || span >= TimeSpan.FromDays(1))
                    continue;

                if (!result.Contains(span))
                    result.Add(span);
            }

            result.Sort();
            return result;
        }

        /// <summary>
        /// 求 <paramref name="time"/> 所属的桶。分组为 <see cref="ScadaReportGrouping.None"/> 时返回 false。
        ///
        /// 班次的边界语义：取"最后一个不晚于它的起点"；早于当天第一个起点时归前一天最后一班
        /// （跨零点的那一班），因此 23:59 与次日 00:01 落在同一个桶里。
        /// </summary>
        public static bool TryResolveBucket(
            DateTime time,
            ScadaReportGrouping grouping,
            IReadOnlyList<TimeSpan> shiftStarts,
            out DateTime bucketStart,
            out DateTime bucketEnd,
            out string label)
        {
            bucketStart = default;
            bucketEnd = default;
            label = string.Empty;

            switch (grouping)
            {
                case ScadaReportGrouping.Day:
                    bucketStart = time.Date;
                    bucketEnd = bucketStart.AddDays(1);
                    label = bucketStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    return true;

                case ScadaReportGrouping.Hour:
                    bucketStart = new DateTime(time.Year, time.Month, time.Day, time.Hour, 0, 0, time.Kind);
                    bucketEnd = bucketStart.AddHours(1);
                    label = bucketStart.ToString("yyyy-MM-dd HH:00", CultureInfo.InvariantCulture);
                    return true;

                case ScadaReportGrouping.Shift:
                    if (shiftStarts == null || shiftStarts.Count == 0)
                        return false;

                    var startIndex = -1;
                    for (var i = 0; i < shiftStarts.Count; i++)
                    {
                        if (shiftStarts[i] <= time.TimeOfDay)
                            startIndex = i;
                        else
                            break;
                    }

                    if (startIndex >= 0)
                    {
                        bucketStart = time.Date + shiftStarts[startIndex];
                        bucketEnd = startIndex + 1 < shiftStarts.Count
                            ? time.Date + shiftStarts[startIndex + 1]
                            : time.Date.AddDays(1) + shiftStarts[0];
                    }
                    else
                    {
                        bucketStart = time.Date.AddDays(-1) + shiftStarts[shiftStarts.Count - 1];
                        bucketEnd = time.Date + shiftStarts[0];
                    }

                    label = bucketStart.ToString("yyyy-MM-dd ", CultureInfo.InvariantCulture)
                            + FormatClock(bucketStart)
                            + "-"
                            + FormatClock(bucketEnd);
                    return true;

                default:
                    return false;
            }
        }

        private static string FormatClock(DateTime value)
            => value.ToString("HH:mm", CultureInfo.InvariantCulture);

        /// <summary>
        /// 单元格 → 数值。先按不变文化，再按当前文化（Excel 另存过的账本可能带本地小数逗号）。
        /// 空白/非数值返回 false——调用方按"整行跳过并计数"处理，不把 0 当成真值。
        /// </summary>
        public static bool TryParseNumber(string? text, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var trimmed = text.Trim();

            if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                && double.IsFinite(value))
            {
                return true;
            }

            if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
                && double.IsFinite(value))
            {
                return true;
            }

            value = 0;
            return false;
        }

        /// <summary>数值 → 文本。格式串非法时回落 <see cref="DefaultNumberFormat"/>，不抛。</summary>
        public static string FormatNumber(double value, string? format)
        {
            var text = string.IsNullOrWhiteSpace(format) ? DefaultNumberFormat : format!.Trim();

            try
            {
                return value.ToString(text, CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
                return value.ToString(DefaultNumberFormat, CultureInfo.InvariantCulture);
            }
        }

        /// <summary>单元格文本 → 展示文本：能解析成数值就按格式重写，否则原样返回。</summary>
        public static string FormatCell(string? text, string? format)
            => TryParseNumber(text, out var number) ? FormatNumber(number, format) : (text ?? string.Empty);
    }
}
