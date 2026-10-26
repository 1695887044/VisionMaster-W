using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VisionMaster.Scada;

namespace VisionMaster.Services
{
    /// <summary>
    /// 方案历史目录（<c>&lt;方案根&gt;\History\*.csv</c>）上的<b>报表数据源</b>。
    ///
    /// 与 <see cref="CsvFolderHistorySource"/> 的分工
    /// ---------
    /// 历史查询是"翻页看最近 N 条"（返回当前页的字符串行）；报表是"扫一遍整个范围，
    /// 按班次/日/小时分组聚合，再给人看/打印/导出"。后者必须在数据源内部完成——
    /// 只有这里拿得到原始数值，也才不会把全量行搬进内存。
    ///
    /// 两条模式
    /// ---------
    /// · <b>明细</b>：范围内逐行（最新在前），可截断到 <see cref="ScadaReportRequest.MaxRows"/>。
    /// · <b>汇总</b>：按 <see cref="ScadaReportRequest.Grouping"/> 分桶，每列按自己的
    ///   <see cref="ScadaReportColumn.Aggregate"/> 算；桶按时间升序（报表是从上往下读的）。
    ///
    /// 数值口径
    /// ---------
    /// 非数值单元格<b>只跳过那一格</b>（不是整行）：同一行里别的列还有效，整行丢掉会让
    /// 汇总凭空少数据。跳过的格数会写进结果的 Message，不静默。
    /// </summary>
    public sealed class CsvReportSource : IScadaReportSource
    {
        /// <summary>默认超时（毫秒）。比历史查询宽：报表扫的是整段范围</summary>
        public const int DefaultTimeoutMs = 15_000;

        /// <summary>单次构建最多扫多少行（防止误指到几十万行的目录把界面卡死）</summary>
        public const int MaxScanRows = 200_000;

        /// <summary>时间跨度上限（天）：再长就不该在画面里跑，去用外部报表工具</summary>
        public const int MaxRangeDays = 366;

        /// <summary>明细模式默认行数上限</summary>
        public const int DefaultMaxRows = 5000;

        private readonly string? _folder;
        private readonly int _timeoutMs;

        public CsvReportSource(string? folder, int timeoutMs = DefaultTimeoutMs)
        {
            _folder = string.IsNullOrWhiteSpace(folder) ? null : folder;
            _timeoutMs = timeoutMs <= 0 ? DefaultTimeoutMs : timeoutMs;
        }

        /// <summary>历史目录（可为 null：方案还没保存过）</summary>
        public string? Folder => _folder;

        public string Name => "方案历史目录";

        public bool IsAvailable => _folder != null && Directory.Exists(_folder);

        public string? UnavailableReason
        {
            get
            {
                if (_folder == null)
                    return "方案还没有保存过，没有历史目录。";

                return Directory.Exists(_folder) ? null : $"历史目录不存在：{_folder}";
            }
        }

        public async Task<ScadaReportResult> BuildAsync(
            ScadaReportRequest request, CancellationToken cancellationToken)
        {
            if (request == null)
                return ScadaReportResult.Unavailable("报表参数为空。", Name);

            if (!IsAvailable)
                return ScadaReportResult.Unavailable(UnavailableReason ?? "历史目录不可用。", Name);

            if (request.FromLocal >= request.ToLocal)
                return ScadaReportResult.InvalidRange("时间范围无效：起点必须早于终点。");

            if ((request.ToLocal - request.FromLocal).TotalDays > MaxRangeDays)
                return ScadaReportResult.InvalidRange($"时间跨度超过 {MaxRangeDays} 天：请缩小范围。");

            if (request.Columns == null || request.Columns.Count == 0)
                return ScadaReportResult.InvalidRange("还没有配置报表列：先在属性面板里加列。");

            if (request.Mode == ScadaReportMode.Summary)
            {
                if (request.Grouping == ScadaReportGrouping.None)
                    return ScadaReportResult.InvalidRange("汇总报表必须选分组（班次 / 日 / 小时）。");

                if (request.Grouping == ScadaReportGrouping.Shift
                    && ScadaReportMath.ParseShiftStarts(string.Join(",", request.ShiftStarts)).Count == 0)
                {
                    return ScadaReportResult.InvalidRange("班次起点没配：填成 08:00,20:00 这样。");
                }
            }

            try
            {
                return await Task.Run(() => BuildCore(request, cancellationToken), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return ScadaReportResult.Cancelled(Name);
            }
            catch (Exception ex)
            {
                // 契约：构建不抛。目录权限、文件被占用、磁盘掉线都在这里收口。
                return ScadaReportResult.Unavailable($"读取历史目录失败：{ex.Message}", Name);
            }
        }

        private ScadaReportResult BuildCore(ScadaReportRequest request, CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            var files = CsvLedger.EnumerateFiles(_folder!);
            if (files.Length == 0)
                return ScadaReportResult.Empty(
                    Array.Empty<string>(), 0, Name,
                    "History 目录里还没有 CSV 账本。把「CSV 记录」插件的目录指到 <方案>\\History 即可。");

            var columns = request.Columns.Select(c => c.Normalize()).ToArray();
            var titles = columns.Select(c => c.Title).ToArray();
            var shiftStarts = ScadaReportMath.ParseShiftStarts(string.Join(",", request.ShiftStarts));

            IReadOnlyList<string>? header = null;
            var columnIndexes = new int[columns.Length];
            var timeIndex = -1;

            var scanned = 0;
            var skippedFiles = 0;
            var nonNumericCells = 0;
            var truncated = false;

            var detail = request.Mode == ScadaReportMode.Detail
                ? new List<(DateTime Time, string[] Cells)>()
                : null;

            // 汇总：桶 → 每列一个累加器。用有序字典保插入序，最后再按桶起点排序。
            var groups = request.Mode == ScadaReportMode.Summary
                ? new Dictionary<DateTime, Accumulator[]>()
                : null;

            var maxRows = request.MaxRows <= 0 ? DefaultMaxRows : request.MaxRows;

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (stopwatch.ElapsedMilliseconds > _timeoutMs)
                    return ScadaReportResult.Timeout(
                        $"报表构建超过 {_timeoutMs} ms：目录里文件太多或磁盘太慢，请缩小范围。", Name);

                string[] lines;
                try
                {
                    lines = File.ReadAllLines(file, Encoding.UTF8);
                }
                catch (Exception)
                {
                    skippedFiles++;
                    continue;
                }

                if (lines.Length == 0)
                    continue;

                var fileHeader = CsvLedger.ParseLine(lines[0]);
                if (fileHeader.Count == 0)
                {
                    skippedFiles++;
                    continue;
                }

                if (header == null)
                {
                    header = fileHeader;
                    timeIndex = CsvLedger.ResolveTimeIndex(header, request.TimestampColumn);
                    if (timeIndex < 0)
                    {
                        return ScadaReportResult.InvalidRange(
                            $"找不到时间列。表头：{string.Join(" / ", header)}；可在「时间列」里指定列名。");
                    }

                    for (var i = 0; i < columns.Length; i++)
                    {
                        var index = CsvLedger.ResolveColumnIndex(header, columns[i].Source);
                        if (index < 0)
                        {
                            return ScadaReportResult.InvalidRange(
                                $"账本里没有列「{columns[i].Source}」。现有列：" + string.Join(" / ", header));
                        }

                        columnIndexes[i] = index;
                    }
                }
                else if (!CsvLedger.HeaderMatches(header, fileHeader))
                {
                    skippedFiles++;
                    continue;
                }

                for (var i = 1; i < lines.Length; i++)
                {
                    if ((i & 0xFF) == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (stopwatch.ElapsedMilliseconds > _timeoutMs)
                            return ScadaReportResult.Timeout(
                                $"报表构建超过 {_timeoutMs} ms：已扫描 {scanned} 行。", Name);
                    }

                    var line = lines[i];
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    var cells = CsvLedger.ParseLine(line);
                    if (cells.Count <= timeIndex)
                        continue;

                    if (!CsvLedger.TryParseTime(cells[timeIndex], out var time))
                        continue;

                    if (time < request.FromLocal || time >= request.ToLocal)
                        continue;

                    if (!string.IsNullOrWhiteSpace(request.Keyword) && !CsvLedger.RowContains(cells, request.Keyword!))
                        continue;

                    scanned++;
                    if (scanned > MaxScanRows)
                    {
                        truncated = true;
                        break;
                    }

                    if (detail != null)
                    {
                        var values = new string[columnIndexes.Length];
                        for (var c = 0; c < columnIndexes.Length; c++)
                        {
                            var index = columnIndexes[c];
                            var raw = index < cells.Count ? cells[index] : string.Empty;
                            values[c] = ScadaReportMath.FormatCell(raw, columns[c].Format);
                        }

                        detail.Add((time, values));

                        if (detail.Count > maxRows)
                        {
                            detail.RemoveAt(detail.Count - 1);
                            truncated = true;
                            break;
                        }
                    }
                    else if (groups != null)
                    {
                        if (!ScadaReportMath.TryResolveBucket(
                                time, request.Grouping, shiftStarts,
                                out var bucketStart, out _, out _))
                        {
                            continue;
                        }

                        if (!groups.TryGetValue(bucketStart, out var accumulators))
                        {
                            accumulators = new Accumulator[columnIndexes.Length];
                            for (var c = 0; c < accumulators.Length; c++)
                                accumulators[c] = new Accumulator();
                            groups[bucketStart] = accumulators;
                        }

                        for (var c = 0; c < columnIndexes.Length; c++)
                        {
                            var index = columnIndexes[c];
                            var raw = index < cells.Count ? cells[index] : string.Empty;

                            if (!accumulators[c].Add(raw))
                                nonNumericCells++;
                        }
                    }
                }

                if (truncated)
                    break;
            }

            if (header == null)
                return ScadaReportResult.Empty(Array.Empty<string>(), 0, Name, "目录里的 CSV 都不可读。");

            var notes = BuildNotes(skippedFiles, nonNumericCells, truncated);

            if (detail != null)
            {
                // 最新在前：与历史查询、实时表格同一口径
                detail.Sort((a, b) => b.Time.CompareTo(a.Time));

                if (detail.Count == 0)
                {
                    return ScadaReportResult.Empty(
                        titles, scanned, Name,
                        $"{request.FromLocal:MM-dd HH:mm} ~ {request.ToLocal:MM-dd HH:mm} 没有记录。{notes}");
                }

                return ScadaReportResult.Ok(
                    titles,
                    detail.Select(d => (IReadOnlyList<string>)d.Cells).ToList(),
                    scanned, Name,
                    string.IsNullOrWhiteSpace(notes) ? null : notes.TrimStart('；', ' '));
            }

            if (groups != null)
            {
                if (groups.Count == 0)
                {
                    return ScadaReportResult.Empty(
                        SummaryTitles(columns, request.Grouping), scanned, Name,
                        $"{request.FromLocal:MM-dd HH:mm} ~ {request.ToLocal:MM-dd HH:mm} 没有记录。{notes}");
                }

                var rows = groups
                    .OrderBy(g => g.Key)
                    .Select(g =>
                    {
                        var row = new string[g.Value.Length + 1];
                        row[0] = ScadaReportMath.TryResolveBucket(
                            g.Key, request.Grouping, shiftStarts, out _, out _, out var label)
                            ? label
                            : g.Key.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

                        for (var c = 0; c < g.Value.Length; c++)
                            row[c + 1] = g.Value[c].Result(columns[c].Aggregate, columns[c].Format);

                        return (IReadOnlyList<string>)row;
                    })
                    .ToList();

                return ScadaReportResult.Ok(
                    SummaryTitles(columns, request.Grouping), rows, scanned, Name,
                    string.IsNullOrWhiteSpace(notes) ? null : notes.TrimStart('；', ' '));
            }

            return ScadaReportResult.Empty(titles, scanned, Name, "报表模式无效。");
        }

        private static string[] SummaryTitles(ScadaReportColumn[] columns, ScadaReportGrouping grouping)
        {
            var titles = new string[columns.Length + 1];
            titles[0] = grouping switch
            {
                ScadaReportGrouping.Shift => "班次",
                ScadaReportGrouping.Day => "日期",
                ScadaReportGrouping.Hour => "小时",
                _ => "分组",
            };

            for (var i = 0; i < columns.Length; i++)
                titles[i + 1] = columns[i].Title;

            return titles;
        }

        private static string BuildNotes(int skippedFiles, int nonNumericCells, bool truncated)
        {
            var parts = new List<string>(3);

            if (skippedFiles > 0)
                parts.Add($"跳过 {skippedFiles} 个表头不一致或不可读的文件");

            if (nonNumericCells > 0)
                parts.Add($"{nonNumericCells} 个单元格不是数值（未计入求和/平均/极值）");

            if (truncated)
                parts.Add($"已扫描到上限（{MaxScanRows} 行或明细行数上限），请缩小范围");

            return parts.Count == 0 ? string.Empty : "；" + string.Join("；", parts);
        }

        /// <summary>一列的累加器（汇总模式）。非数值单元格不参与数值聚合，但仍参与计数与首末值。</summary>
        private sealed class Accumulator
        {
            private double _sum;
            private double _min = double.MaxValue;
            private double _max = double.MinValue;
            private int _numericCount;
            private int _nonEmptyCount;
            private string _first = string.Empty;
            private string _last = string.Empty;
            private bool _hasFirst;

            /// <summary>收一格；返回 false 表示"这格不是数值"（调用方据此计数提示）</summary>
            public bool Add(string? raw)
            {
                var text = raw ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(text))
                    _nonEmptyCount++;

                if (!_hasFirst)
                {
                    _first = text;
                    _hasFirst = true;
                }

                _last = text;

                if (!ScadaReportMath.TryParseNumber(text, out var value))
                    return false;

                _sum += value;
                if (value < _min) _min = value;
                if (value > _max) _max = value;
                _numericCount++;
                return true;
            }

            public string Result(ScadaReportAggregate aggregate, string? format) => aggregate switch
            {
                ScadaReportAggregate.Sum => _numericCount == 0 ? string.Empty : ScadaReportMath.FormatNumber(_sum, format),
                ScadaReportAggregate.Average => _numericCount == 0
                    ? string.Empty
                    : ScadaReportMath.FormatNumber(_sum / _numericCount, format),
                ScadaReportAggregate.Min => _numericCount == 0 ? string.Empty : ScadaReportMath.FormatNumber(_min, format),
                ScadaReportAggregate.Max => _numericCount == 0 ? string.Empty : ScadaReportMath.FormatNumber(_max, format),
                ScadaReportAggregate.Count => _nonEmptyCount.ToString(CultureInfo.InvariantCulture),
                ScadaReportAggregate.Last => _last,
                _ => _first,
            };
        }
    }
}
