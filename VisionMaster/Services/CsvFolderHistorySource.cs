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
    /// 方案历史目录（<c>&lt;方案根&gt;\History\*.csv</c>）上的历史数据源。
    ///
    /// 为什么读 CSV 而不是引一个数据库
    /// ---------
    /// 现场已经在用「CSV 记录」插件写账本（一行一次流程结果，按天滚动），
    /// 历史查询要的就是同一批文件。为了一张表格再引一个嵌入式数据库，等于让
    /// "记录"和"查看"两套存储各自演化——迟早出现"账本里有、历史里没有"。
    ///
    /// 查询口径
    /// ---------
    /// · 时间列：显式指定优先；否则取表头里含「时间 / 日期 / time / date」的第一列。
    ///   找不到就返回 <see cref="ScadaHistoryStatus.InvalidRange"/>，<b>不猜</b>——
    ///   拿 SN 列当时间过滤会安静地返回空表，比报错更难查。
    /// · 排序：按行时间倒序（最新在前），与实时表格的「新行在上」一致。
    /// · 分页：只把当前页的行装进结果；总数单独给，不把全量搬进内存。
    /// · 坏文件：表头与第一份不一致的直接跳过，并在 Message 里报条数（旁路文件显式警告）。
    /// · 超时：默认 5 秒；超了返回 Timeout，不无限占着 UI 线程。
    ///
    /// 已知边界
    /// ---------
    /// 不支持引号内的换行（CSV 规范允许，但账本插件从不写这种行；真遇到会按两行处理，
    /// 第二行因列数不足被计入坏行）。需要支持时再换完整解析器，别在这里加半个状态机。
    /// </summary>
    public sealed class CsvFolderHistorySource : IScadaHistorySource
    {
        /// <summary>默认查询超时（毫秒）</summary>
        public const int DefaultTimeoutMs = 5000;

        /// <summary>单次查询最多扫多少行（防止误指到几十万行的目录把界面卡死）</summary>
        public const int MaxRowsScanned = 200_000;

        private readonly string? _folder;
        private readonly int _timeoutMs;

        public CsvFolderHistorySource(string? folder, int timeoutMs = DefaultTimeoutMs)
        {
            _folder = string.IsNullOrWhiteSpace(folder) ? null : folder;
            _timeoutMs = timeoutMs <= 0 ? DefaultTimeoutMs : timeoutMs;
        }

        /// <summary>历史目录（可为 null：方案还没保存过）</summary>
        public string? Folder => _folder;

        public string Name => "方案历史目录";

        public bool IsAvailable
            => _folder != null && Directory.Exists(_folder);

        public string? UnavailableReason
        {
            get
            {
                if (_folder == null)
                    return "方案还没有保存过，没有历史目录。";

                return Directory.Exists(_folder) ? null : $"历史目录不存在：{_folder}";
            }
        }

        public async Task<ScadaHistoryResult> QueryAsync(ScadaHistoryQuery query, CancellationToken cancellationToken)
        {
            if (query == null)
                return ScadaHistoryResult.Unavailable("查询参数为空。", Name);

            if (!IsAvailable)
                return ScadaHistoryResult.Unavailable(UnavailableReason ?? "历史目录不可用。", Name);

            if (query.FromLocal >= query.ToLocal)
                return ScadaHistoryResult.InvalidRange("时间范围无效：起点必须早于终点。");

            try
            {
                return await Task.Run(() => QueryCore(query, cancellationToken), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return ScadaHistoryResult.Cancelled(Name);
            }
            catch (Exception ex)
            {
                // 契约：查询不抛。目录权限、文件被占用、磁盘掉线都在这里收口。
                return ScadaHistoryResult.Unavailable($"读取历史目录失败：{ex.Message}", Name);
            }
        }

        private ScadaHistoryResult QueryCore(ScadaHistoryQuery query, CancellationToken cancellationToken)
        {
            var (pageIndex, pageSize) = query.NormalizePaging();
            var stopwatch = Stopwatch.StartNew();

            var files = CsvLedger.EnumerateFiles(_folder!);
            if (files.Length == 0)
                return ScadaHistoryResult.Empty(
                    Array.Empty<string>(), pageIndex, pageSize, Name,
                    "History 目录里还没有 CSV 账本。把「CSV 记录」插件的目录指到 <方案>\\History 即可。");

            IReadOnlyList<string>? header = null;
            var timeIndex = -1;
            var matched = new List<(DateTime Time, IReadOnlyList<string> Cells)>();
            var skippedFiles = 0;
            var badRows = 0;
            var capped = false;

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (stopwatch.ElapsedMilliseconds > _timeoutMs)
                    return ScadaHistoryResult.Timeout(
                        $"历史查询超过 {_timeoutMs} ms：目录里文件太多或磁盘太慢，请缩小范围。", Name);

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
                    timeIndex = CsvLedger.ResolveTimeIndex(header, query.TimestampColumn);

                    if (timeIndex < 0)
                    {
                        return ScadaHistoryResult.InvalidRange(
                            $"找不到时间列。表头：{string.Join(" / ", header)}；" +
                            "可在「历史时间列」里指定列名。");
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
                            return ScadaHistoryResult.Timeout(
                                $"历史查询超过 {_timeoutMs} ms：已扫描到 {matched.Count} 条匹配记录。", Name);
                    }

                    var line = lines[i];
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    var cells = CsvLedger.ParseLine(line);
                    if (cells.Count <= timeIndex)
                    {
                        badRows++;
                        continue;
                    }

                    if (!CsvLedger.TryParseTime(cells[timeIndex], out var time))
                    {
                        badRows++;
                        continue;
                    }

                    if (time < query.FromLocal || time >= query.ToLocal)
                        continue;

                    if (!string.IsNullOrWhiteSpace(query.Keyword) && !CsvLedger.RowContains(cells, query.Keyword!))
                        continue;

                    matched.Add((time, cells));

                    if (matched.Count >= MaxRowsScanned)
                    {
                        capped = true;
                        break;
                    }
                }

                if (capped)
                    break;
            }

            if (header == null)
                return ScadaHistoryResult.Empty(
                    Array.Empty<string>(), pageIndex, pageSize, Name, "目录里的 CSV 都不可读。");

            // 最新在前：与实时表格「新行在上」同一口径，操作员不用记两种排序。
            matched.Sort((a, b) => b.Time.CompareTo(a.Time));

            var total = matched.Count;
            if (total == 0)
            {
                return ScadaHistoryResult.Empty(
                    header, pageIndex, pageSize, Name,
                    $"{query.FromLocal:MM-dd HH:mm} ~ {query.ToLocal:MM-dd HH:mm} 没有记录。" +
                    BuildSkipMessage(skippedFiles, badRows));
            }

            var pageCount = (total + pageSize - 1) / pageSize;
            if (pageIndex >= pageCount)
                pageIndex = pageCount - 1;

            var rows = matched
                .Skip(pageIndex * pageSize)
                .Take(pageSize)
                .Select(m => m.Cells)
                .ToList();

            var message = BuildSkipMessage(skippedFiles, badRows);
            if (capped)
                message += $"；已扫描到 {MaxRowsScanned} 条上限，请缩小范围";

            return ScadaHistoryResult.Ok(
                header, rows, total, pageIndex, pageSize, Name,
                string.IsNullOrWhiteSpace(message) ? null : message.TrimStart('；', ' '));
        }

        private static string BuildSkipMessage(int skippedFiles, int badRows)
        {
            var parts = new List<string>(2);

            if (skippedFiles > 0)
                parts.Add($"跳过 {skippedFiles} 个表头不一致或不可读的文件");

            if (badRows > 0)
                parts.Add($"{badRows} 行时间无法解析");

            return parts.Count == 0 ? string.Empty : "；" + string.Join("；", parts);
        }

        /// <summary>
        /// CSV 一行 → 单元格（支持双引号包裹、<c>""</c> 转义；不支持引号内换行，见类注释）。
        /// 实现已下沉到 <see cref="CsvLedger"/>（报表走同一套解析）；这里保留公开入口，
        /// 既有断言与调用方不受影响。
        /// </summary>
        public static IReadOnlyList<string> ParseCsvLine(string line) => CsvLedger.ParseLine(line);
    }
}
