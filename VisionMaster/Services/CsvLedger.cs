using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace VisionMaster.Services
{
    /// <summary>
    /// CSV 账本的读取原语（<c>&lt;方案根&gt;\History\*.csv</c>）。
    ///
    /// 为什么单独一层
    /// ---------
    /// 历史查询（<see cref="CsvFolderHistorySource"/>，分页取字符串行）与报表
    /// （<see cref="CsvReportSource"/>，全范围流式聚合）读的是<b>同一批文件</b>，
    /// 时间列识别、表头比对、引号解析这些规则只能有一份——两处各写一遍，
    /// 迟早出现"历史能查、报表查不到"这种只在一个入口复现的故障。
    ///
    /// 口径（与历史查询逐字一致，改这里等于同时改两条路）
    /// ---------
    /// · 时间列：显式指定优先（列名精确匹配、忽略大小写、去首尾空白）；否则取表头里
    ///   含「时间 / 日期 / time / date」的第一列。找不到就报错，<b>不猜</b>。
    /// · 表头比对：列数相同 + 逐列去空白后忽略大小写相等。
    /// · 行解析：支持双引号包裹与 <c>""</c> 转义；不支持引号内换行。
    /// </summary>
    internal static class CsvLedger
    {
        private static readonly string[] TimeFormats =
        {
            "yyyy-MM-dd HH:mm:ss.fff",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd HH:mm",
            "yyyy-MM-dd",
            "yyyy/MM/dd HH:mm:ss.fff",
            "yyyy/MM/dd HH:mm:ss",
            "yyyy/MM/dd HH:mm",
            "yyyy/MM/dd",
            "yyyy-MM-ddTHH:mm:ss.fff",
            "yyyy-MM-ddTHH:mm:ss",
        };

        /// <summary>目录下的全部 CSV（递归、按路径升序：文件名通常是日期，升序即时间序）</summary>
        public static string[] EnumerateFiles(string folder)
        {
            var files = Directory.GetFiles(folder, "*.csv", SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            return files;
        }

        public static bool HeaderMatches(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
        {
            if (expected.Count != actual.Count)
                return false;

            for (var i = 0; i < expected.Count; i++)
            {
                if (!string.Equals(expected[i].Trim(), actual[i].Trim(), StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }

        public static int ResolveTimeIndex(IReadOnlyList<string> header, string? explicitColumn)
        {
            if (!string.IsNullOrWhiteSpace(explicitColumn))
                return ResolveColumnIndex(header, explicitColumn!);

            for (var i = 0; i < header.Count; i++)
            {
                var text = header[i] ?? string.Empty;

                if (text.Contains("时间", StringComparison.Ordinal)
                    || text.Contains("日期", StringComparison.Ordinal)
                    || text.Contains("time", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("date", StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>按列名找下标（去空白、忽略大小写、精确匹配）；找不到返回 -1</summary>
        public static int ResolveColumnIndex(IReadOnlyList<string> header, string name)
        {
            var target = (name ?? string.Empty).Trim();
            if (target.Length == 0)
                return -1;

            for (var i = 0; i < header.Count; i++)
            {
                if (string.Equals((header[i] ?? string.Empty).Trim(), target, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return -1;
        }

        public static bool RowContains(IReadOnlyList<string> cells, string keyword)
        {
            foreach (var cell in cells)
            {
                if (cell != null && cell.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        public static bool TryParseTime(string? text, out DateTime time)
        {
            time = default;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var value = text.Trim();

            return DateTime.TryParseExact(
                       value, TimeFormats, CultureInfo.InvariantCulture,
                       DateTimeStyles.AssumeLocal, out time)
                   || DateTime.TryParse(
                       value, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out time);
        }

        /// <summary>CSV 一行 → 单元格（支持双引号包裹与 <c>""</c> 转义）</summary>
        public static IReadOnlyList<string> ParseLine(string line)
        {
            var cells = new List<string>();
            var builder = new StringBuilder();
            var quoted = false;

            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];

                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            builder.Append('"');
                            i++;
                        }
                        else
                        {
                            quoted = false;
                        }
                    }
                    else
                    {
                        builder.Append(c);
                    }
                }
                else if (c == '"')
                {
                    quoted = true;
                }
                else if (c == ',')
                {
                    cells.Add(builder.ToString());
                    builder.Clear();
                }
                else
                {
                    builder.Append(c);
                }
            }

            cells.Add(builder.ToString());
            return cells;
        }
    }
}
