using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MiniExcelLibs;
using VisionMaster.Scada;
using VisionMaster.Scada.Controls;
using VisionMaster.Services;

namespace ScadaChecks
{
    internal partial class Program
    {
        /// <summary>
        /// [RP] 报表：列定义编解码 / 聚合数学 / CSV 报表源 / 导出 / 打印排版 / 图元交互。
        ///
        /// 断言口径：数据源部分用<b>真临时目录 + 真 CSV</b>（不 mock 文件系统），
        /// 因为最容易错的正是"表头识别、范围过滤、分桶边界"这些只有真文件才暴露的东西。
        /// </summary>
        private static class ReportChecks
        {
            public static void Run() => RunOnSta(RunChecks);

            private static void RunChecks()
            {
                Section("[RP] 报表：列定义 / 聚合 / CSV 源 / 导出 / 打印 / 图元");

                CheckCodec();
                CheckMath();

                var root = Path.Combine(Path.GetTempPath(), "scada-report-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(root);

                try
                {
                    WriteLedger(root);
                    CheckDetailSource(root);
                    CheckSummarySource(root);
                    CheckSourceErrors(root);
                    CheckExporter(root);
                    CheckPrinter();
                    CheckElement(root);
                }
                finally
                {
                    try { Directory.Delete(root, recursive: true); } catch { }
                }
            }

            // ---------------- 列定义编解码 ----------------

            private static void CheckCodec()
            {
                var json = ScadaReportCodec.Format(new[]
                {
                    new ScadaReportColumn("时间", "时间", "", ScadaReportAggregate.None, 120),
                    new ScadaReportColumn("尺寸", "尺寸(mm)", "0.00", ScadaReportAggregate.Average),
                });

                var parsed = ScadaReportCodec.Parse(json);
                Check("[RP1] 列定义往返：两列、标题/格式/聚合/宽度逐项回来",
                    parsed.Count == 2
                    && parsed[0].Source == "时间" && parsed[0].Width == 120
                    && parsed[1].Title == "尺寸(mm)" && parsed[1].Format == "0.00"
                    && parsed[1].Aggregate == ScadaReportAggregate.Average,
                    json);

                Check("[RP2] 列定义容错：坏 JSON / 空来源列 → 空表或跳过，不抛",
                    ScadaReportCodec.Parse("{not json").Count == 0
                    && ScadaReportCodec.Parse("").Count == 0
                    && ScadaReportCodec.Parse(null).Count == 0
                    && ScadaReportCodec.Parse("[{\"Source\":\"\"},{\"Source\":\"温度\"}]").Count == 1,
                    "");

                Check("[RP3] 列定义上限：超过 16 列截断（报表是一页，不是导出全表）",
                    ScadaReportCodec.Parse(ScadaReportCodec.Format(
                        Enumerable.Range(0, 20).Select(i => new ScadaReportColumn("c" + i)))).Count == 16,
                    "");
            }

            // ---------------- 聚合数学与分桶 ----------------

            private static void CheckMath()
            {
                Check("[RP4] 数值解析：不变文化优先、再按当前文化；空白/非数值返回 false",
                    ScadaReportMath.TryParseNumber("10.5", out var a) && Math.Abs(a - 10.5) < 1e-9
                    && ScadaReportMath.TryParseNumber(" 7 ", out var b) && Math.Abs(b - 7) < 1e-9
                    && !ScadaReportMath.TryParseNumber("", out _)
                    && !ScadaReportMath.TryParseNumber("OK", out _),
                    "");

                Check("[RP5] 数值格式：按格式串输出；非法格式回落 0.### 不抛",
                    ScadaReportMath.FormatNumber(10.256, "0.00") == "10.26"
                    && ScadaReportMath.FormatNumber(10.256, "0.###") == "10.256"
                    && ScadaReportMath.FormatNumber(10.256, "Q") == "10.256",
                    ScadaReportMath.FormatNumber(10.256, "Q"));

                var starts = ScadaReportMath.ParseShiftStarts("08:00,20:00");
                Check("[RP6] 班次起点解析：逗号/中文逗号都认，去重排序；非法项丢弃",
                    starts.Count == 2 && starts[0] == TimeSpan.FromHours(8) && starts[1] == TimeSpan.FromHours(20)
                    && ScadaReportMath.ParseShiftStarts("20:00;08:00;08:00").Count == 2
                    && ScadaReportMath.ParseShiftStarts("abc").Count == 0,
                    string.Join(",", starts));

                bool early = ScadaReportMath.TryResolveBucket(
                    new DateTime(2026, 10, 25, 23, 59, 0), ScadaReportGrouping.Shift, starts,
                    out var bucketStart, out var bucketEnd, out var earlyLabel);

                bool late = ScadaReportMath.TryResolveBucket(
                    new DateTime(2026, 10, 26, 0, 1, 0), ScadaReportGrouping.Shift, starts,
                    out var nextStart, out _, out var lateLabel);

                Check("[RP7] 班次跨零点回绕：23:59 与次日 00:01 落在同一班（20:00-08:00）",
                    early && late && bucketStart == nextStart
                    && bucketStart == new DateTime(2026, 10, 25, 20, 0, 0)
                    && bucketEnd == new DateTime(2026, 10, 26, 8, 0, 0)
                    && earlyLabel == lateLabel
                    && earlyLabel.Contains("20:00-08:00"),
                    earlyLabel + " / " + lateLabel);

                ScadaReportMath.TryResolveBucket(
                    new DateTime(2026, 10, 25, 12, 0, 0), ScadaReportGrouping.Day, starts,
                    out var dayStart, out _, out var dayLabel);

                ScadaReportMath.TryResolveBucket(
                    new DateTime(2026, 10, 25, 12, 34, 0), ScadaReportGrouping.Hour, starts,
                    out var hourStart, out _, out var hourLabel);

                Check("[RP8] 日/小时分组标签：日期与整点",
                    dayStart == new DateTime(2026, 10, 25) && dayLabel == "2026-10-25"
                    && hourStart == new DateTime(2026, 10, 25, 12, 0, 0) && hourLabel == "2026-10-25 12:00",
                    dayLabel + " / " + hourLabel);
            }

            // ---------------- 真 CSV 账本 ----------------

            /// <summary>造两天的账本：列 = 时间,SN,结果,尺寸,温度</summary>
            private static void WriteLedger(string root)
            {
                var day1 = new StringBuilder();
                day1.AppendLine("时间,SN,结果,尺寸,温度");
                day1.AppendLine("2026-10-24 09:00:00,SN-1,OK,10.0,20");
                day1.AppendLine("2026-10-24 21:00:00,SN-2,NG,11.0,30");
                File.WriteAllText(Path.Combine(root, "2026-10-24.csv"), day1.ToString(), new UTF8Encoding(false));

                var day2 = new StringBuilder();
                day2.AppendLine("时间,SN,结果,尺寸,温度");
                day2.AppendLine("2026-10-25 08:30:00,SN-3,OK,12.0,40");
                day2.AppendLine("2026-10-25 09:30:00,SN-4,OK,abc,50");
                day2.AppendLine("2026-10-25 10:30:00,SN-5,NG,14.0,60");
                File.WriteAllText(Path.Combine(root, "2026-10-25.csv"), day2.ToString(), new UTF8Encoding(false));
            }

            private static ScadaReportRequest DetailRequest(params string[] sources) => new()
            {
                Mode = ScadaReportMode.Detail,
                FromLocal = new DateTime(2026, 10, 24),
                ToLocal = new DateTime(2026, 10, 26),
                Columns = sources.Select(s => new ScadaReportColumn(s)).ToArray(),
                MaxRows = 100,
            };

            private static void CheckDetailSource(string root)
            {
                var source = new CsvReportSource(root);

                var all = source.BuildAsync(DetailRequest("时间", "SN", "尺寸"), CancellationToken.None)
                    .GetAwaiter().GetResult();

                Check("[RP9] 明细：5 行全取到、最新在前、列按配置顺序",
                    all.Status == ScadaHistoryStatus.Ok
                    && all.Rows.Count == 5
                    && all.Columns.SequenceEqual(new[] { "时间", "SN", "尺寸" })
                    && all.Rows[0][1] == "SN-5"
                    && all.Rows[4][1] == "SN-1"
                    && all.ScannedRows == 5,
                    $"{all.Status} rows={all.Rows.Count} first={all.Rows.FirstOrDefault()?[1]} scanned={all.ScannedRows}");

                var ranged = source.BuildAsync(new ScadaReportRequest
                {
                    Mode = ScadaReportMode.Detail,
                    FromLocal = new DateTime(2026, 10, 25, 9, 0, 0),
                    ToLocal = new DateTime(2026, 10, 25, 11, 0, 0),
                    Columns = new[] { new ScadaReportColumn("SN") },
                }, CancellationToken.None).GetAwaiter().GetResult();

                Check("[RP10] 明细：范围过滤（09:00~11:00 → SN-4 / SN-5，终点不含 08:30）",
                    ranged.Rows.Count == 2
                    && ranged.Rows[0][0] == "SN-5"
                    && ranged.Rows[1][0] == "SN-4",
                    string.Join(",", ranged.Rows.Select(r => r[0])));

                var keyword = source.BuildAsync(new ScadaReportRequest
                {
                    Mode = ScadaReportMode.Detail,
                    FromLocal = new DateTime(2026, 10, 24),
                    ToLocal = new DateTime(2026, 10, 26),
                    Columns = new[] { new ScadaReportColumn("SN"), new ScadaReportColumn("结果") },
                    Keyword = "ng",
                }, CancellationToken.None).GetAwaiter().GetResult();

                Check("[RP11] 明细：关键字匹配任意列、忽略大小写（ng → 2 行）",
                    keyword.Rows.Count == 2 && keyword.Rows.All(r => r[1] == "NG"),
                    string.Join(",", keyword.Rows.Select(r => r[0])));

                var capped = source.BuildAsync(new ScadaReportRequest
                {
                    Mode = ScadaReportMode.Detail,
                    FromLocal = new DateTime(2026, 10, 24),
                    ToLocal = new DateTime(2026, 10, 26),
                    Columns = new[] { new ScadaReportColumn("SN") },
                    MaxRows = 2,
                }, CancellationToken.None).GetAwaiter().GetResult();

                Check("[RP12] 明细：超过最多行数截断，并在提示里说明",
                    capped.Rows.Count == 2
                    && capped.Message != null
                    && capped.Message.Contains("上限"),
                    capped.Message ?? "(null)");

                var formatted = source.BuildAsync(new ScadaReportRequest
                {
                    Mode = ScadaReportMode.Detail,
                    FromLocal = new DateTime(2026, 10, 25),
                    ToLocal = new DateTime(2026, 10, 26),
                    Columns = new[] { new ScadaReportColumn("尺寸", "尺寸", "0.00") },
                }, CancellationToken.None).GetAwaiter().GetResult();

                Check("[RP13] 明细：数值列按格式串重写，非数值原样保留",
                    formatted.Rows.Select(r => r[0]).Contains("12.00")
                    && formatted.Rows.Select(r => r[0]).Contains("abc"),
                    string.Join(" | ", formatted.Rows.Select(r => r[0])));
            }

            private static void CheckSummarySource(string root)
            {
                var source = new CsvReportSource(root);

                var summary = source.BuildAsync(new ScadaReportRequest
                {
                    Mode = ScadaReportMode.Summary,
                    FromLocal = new DateTime(2026, 10, 24),
                    ToLocal = new DateTime(2026, 10, 26),
                    Grouping = ScadaReportGrouping.Day,
                    Columns = new[]
                    {
                        new ScadaReportColumn("尺寸", "尺寸合计", "0.##", ScadaReportAggregate.Sum),
                        new ScadaReportColumn("尺寸", "尺寸平均", "0.##", ScadaReportAggregate.Average),
                        new ScadaReportColumn("尺寸", "最小", "0.##", ScadaReportAggregate.Min),
                        new ScadaReportColumn("尺寸", "最大", "0.##", ScadaReportAggregate.Max),
                        new ScadaReportColumn("SN", "条数", "", ScadaReportAggregate.Count),
                        new ScadaReportColumn("结果", "最后一条", "", ScadaReportAggregate.Last),
                    },
                }, CancellationToken.None).GetAwaiter().GetResult();

                Check("[RP14] 汇总：按日分组 2 桶、桶按时间升序、首列是分组标签",
                    summary.Status == ScadaHistoryStatus.Ok
                    && summary.Rows.Count == 2
                    && summary.Columns[0] == "日期"
                    && summary.Rows[0][0] == "2026-10-24"
                    && summary.Rows[1][0] == "2026-10-25",
                    string.Join(" | ", summary.Rows.Select(r => r[0])));

                Check("[RP15] 汇总：求和/平均/最小/最大/计数/末值 六种聚合都对",
                    summary.Rows[0][1] == "21"          // 10 + 11
                    && summary.Rows[0][2] == "10.5"
                    && summary.Rows[0][3] == "10"
                    && summary.Rows[0][4] == "11"
                    && summary.Rows[0][5] == "2"
                    && summary.Rows[0][6] == "NG"
                    && summary.Rows[1][1] == "26"        // 12 + 14（abc 那行跳过）
                    && summary.Rows[1][5] == "3",
                    string.Join(" | ", summary.Rows[0].Select(c => c)));

                Check("[RP16] 汇总：非数值单元格只跳过那一格，并在提示里报数",
                    summary.Message != null && summary.Message.Contains("不是数值"),
                    summary.Message ?? "(null)");

                var byShift = source.BuildAsync(new ScadaReportRequest
                {
                    Mode = ScadaReportMode.Summary,
                    FromLocal = new DateTime(2026, 10, 24),
                    ToLocal = new DateTime(2026, 10, 26),
                    Grouping = ScadaReportGrouping.Shift,
                    ShiftStarts = new[] { "08:00", "20:00" },
                    Columns = new[] { new ScadaReportColumn("SN", "条数", "", ScadaReportAggregate.Count) },
                }, CancellationToken.None).GetAwaiter().GetResult();

                Check("[RP17] 汇总：按班次分组（09:00 与 21:00 不同班；08:30 与 10:30 同班）",
                    byShift.Rows.Count == 3
                    && byShift.Rows[0][0].Contains("08:00-20:00")
                    && byShift.Rows[1][0].Contains("20:00-08:00")
                    && byShift.Rows[2][0].Contains("08:00-20:00"),
                    string.Join(" | ", byShift.Rows.Select(r => r[0] + "=" + r[1])));
            }

            private static void CheckSourceErrors(string root)
            {
                var source = new CsvReportSource(root);

                var missingColumn = source.BuildAsync(DetailRequest("不存在的列"), CancellationToken.None)
                    .GetAwaiter().GetResult();
                Check("[RP18] 列名对不上账本 → InvalidRange，并列出账本现有列",
                    missingColumn.Status == ScadaHistoryStatus.InvalidRange
                    && missingColumn.Message != null
                    && missingColumn.Message.Contains("不存在的列")
                    && missingColumn.Message.Contains("SN"),
                    missingColumn.Message ?? "(null)");

                var emptyRange = source.BuildAsync(new ScadaReportRequest
                {
                    Mode = ScadaReportMode.Detail,
                    FromLocal = new DateTime(2020, 1, 1),
                    ToLocal = new DateTime(2020, 1, 2),
                    Columns = new[] { new ScadaReportColumn("SN") },
                }, CancellationToken.None).GetAwaiter().GetResult();
                Check("[RP19] 范围内没有记录 → Empty（不是 Ok + 0 行）",
                    emptyRange.Status == ScadaHistoryStatus.Empty,
                    emptyRange.Status.ToString());

                var invalid = source.BuildAsync(new ScadaReportRequest
                {
                    Mode = ScadaReportMode.Detail,
                    FromLocal = new DateTime(2026, 10, 26),
                    ToLocal = new DateTime(2026, 10, 25),
                    Columns = new[] { new ScadaReportColumn("SN") },
                }, CancellationToken.None).GetAwaiter().GetResult();
                Check("[RP20] 起点晚于终点 → InvalidRange",
                    invalid.Status == ScadaHistoryStatus.InvalidRange, invalid.Message ?? "");

                var noColumns = source.BuildAsync(new ScadaReportRequest
                {
                    Mode = ScadaReportMode.Detail,
                    FromLocal = new DateTime(2026, 10, 24),
                    ToLocal = new DateTime(2026, 10, 26),
                }, CancellationToken.None).GetAwaiter().GetResult();
                Check("[RP21] 没配列 → InvalidRange（提示去配列）",
                    noColumns.Status == ScadaHistoryStatus.InvalidRange
                    && noColumns.Message != null && noColumns.Message.Contains("列"),
                    noColumns.Message ?? "");

                var shiftMissing = source.BuildAsync(new ScadaReportRequest
                {
                    Mode = ScadaReportMode.Summary,
                    FromLocal = new DateTime(2026, 10, 24),
                    ToLocal = new DateTime(2026, 10, 26),
                    Grouping = ScadaReportGrouping.Shift,
                    Columns = new[] { new ScadaReportColumn("SN") },
                }, CancellationToken.None).GetAwaiter().GetResult();
                Check("[RP22] 汇总按班次但没配起点 → InvalidRange（给示例）",
                    shiftMissing.Status == ScadaHistoryStatus.InvalidRange
                    && shiftMissing.Message != null && shiftMissing.Message.Contains("08:00"),
                    shiftMissing.Message ?? "");

                var missingDir = new CsvReportSource(Path.Combine(root, "nope"));
                var unavailable = missingDir.BuildAsync(DetailRequest("SN"), CancellationToken.None)
                    .GetAwaiter().GetResult();
                Check("[RP23] 目录不存在 → Unavailable（不是空表）",
                    unavailable.Status == ScadaHistoryStatus.Unavailable,
                    unavailable.Message ?? "");

                using var cts = new CancellationTokenSource();
                cts.Cancel();
                var cancelled = source.BuildAsync(DetailRequest("SN"), cts.Token).GetAwaiter().GetResult();
                Check("[RP24] 已取消的令牌 → Cancelled，不抛",
                    cancelled.Status == ScadaHistoryStatus.Cancelled,
                    cancelled.Status.ToString());
            }

            // ---------------- 导出 ----------------

            private static void CheckExporter(string root)
            {
                var result = ScadaReportResult.Ok(
                    new[] { "SN", "尺寸" },
                    new List<IReadOnlyList<string>>
                    {
                        new[] { "SN-1", "10.5" },
                        new[] { "SN,2", "11" },
                    },
                    2, "断言源");

                var exporter = new ScadaReportExporter();

                var xlsx = Path.Combine(root, "out.xlsx");
                bool okXlsx = exporter.TryExport(result, "测试报表", "xlsx", xlsx, out var xlsxError);
                var rows = okXlsx ? MiniExcel.Query(xlsx, useHeaderRow: true, sheetName: ScadaReportExporter.DataSheetName).Cast<IDictionary<string, object>>().ToList() : null;

                Check("[RP25] 导出 xlsx：两个工作表、数据行能读回、表头是列名",
                    okXlsx
                    && rows != null && rows.Count == 2
                    && Convert.ToString(rows[0]["SN"]) == "SN-1"
                    && Convert.ToString(rows[1]["SN"]) == "SN,2"
                    && MiniExcel.GetSheetNames(xlsx).Contains(ScadaReportExporter.DataSheetName)
                    && MiniExcel.GetSheetNames(xlsx).Contains(ScadaReportExporter.NoteSheetName),
                    xlsxError);

                var csv = Path.Combine(root, "out.csv");
                bool okCsv = exporter.TryExport(result, "测试报表", "csv", csv, out var csvError);
                var bytes = okCsv ? File.ReadAllBytes(csv) : Array.Empty<byte>();
                var text = okCsv ? File.ReadAllText(csv, Encoding.UTF8) : string.Empty;

                Check("[RP26] 导出 csv：UTF-8 带 BOM、含逗号的字段被引号包裹",
                    okCsv
                    && bytes.Length > 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF
                    && text.Contains("SN,尺寸")
                    && text.Contains("\"SN,2\""),
                    csvError);

                Check("[RP27] 导出：不支持的格式 → false + 中文原因（不抛）",
                    !exporter.TryExport(result, "x", "pdf", Path.Combine(root, "x.pdf"), out var badFormat)
                    && badFormat.Contains("xlsx"),
                    badFormat);

                Check("[RP28] 导出：没有数据 → 拒绝并说明（不生成空文件）",
                    !exporter.TryExport(ScadaReportResult.Empty(new[] { "SN" }, 0, "x"), "x", "csv",
                        Path.Combine(root, "empty.csv"), out var emptyError)
                    && emptyError.Contains("还没有数据")
                    && !File.Exists(Path.Combine(root, "empty.csv")),
                    emptyError);

                Check("[RP29] 导出：临时文件不残留（成功/失败都不留 .tmp）",
                    !Directory.GetFiles(root, "*.tmp.*").Any(),
                    string.Join(",", Directory.GetFiles(root, "*.tmp.*")));
            }

            // ---------------- 打印排版 ----------------

            private static void CheckPrinter()
            {
                var small = ScadaReportResult.Ok(
                    new[] { "SN" },
                    new List<IReadOnlyList<string>> { new[] { "SN-1" } },
                    1, "断言源");

                var bigRows = new List<IReadOnlyList<string>>();
                for (var i = 0; i < 400; i++)
                    bigRows.Add(new[] { "SN-" + i });

                var big = ScadaReportResult.Ok(new[] { "SN" }, bigRows, 400, "断言源");

                var smallDoc = ReportPrintBuilder.Build(small, "小报表", "范围", "操作员", landscape: false);
                var bigDoc = ReportPrintBuilder.Build(big, "大报表", "范围", "操作员", landscape: false);

                smallDoc.PagePadding = new Thickness(ReportPrintBuilder.PagePadding);
                bigDoc.PagePadding = new Thickness(ReportPrintBuilder.PagePadding);

                int smallPages = PageCountOf(smallDoc);
                int bigPages = PageCountOf(bigDoc);

                Check("[RP30] 打印排版：A4 尺寸、行多则页多（1 行 1 页、400 行 > 1 页）",
                    Math.Abs(smallDoc.PageWidth - ReportPrintBuilder.A4Width) < 0.01
                    && smallPages >= 1 && bigPages > smallPages,
                    $"small={smallPages} big={bigPages}");                var landscapeDoc = ReportPrintBuilder.Build(small, "横版", "范围", "操作员", landscape: true);
                Check("[RP31] 打印排版：横向时宽高互换",
                    Math.Abs(landscapeDoc.PageWidth - ReportPrintBuilder.A4Height) < 0.01
                    && Math.Abs(landscapeDoc.PageHeight - ReportPrintBuilder.A4Width) < 0.01,
                    $"{landscapeDoc.PageWidth}×{landscapeDoc.PageHeight}");

                var emptyDoc = ReportPrintBuilder.Build(
                    ScadaReportResult.Empty(new[] { "SN" }, 0, "断言源"), "空报表", "范围", "操作员", false);
                Check("[RP32] 打印排版：没数据也出一页（标题 + 「没有数据」），不是空白页",
                    emptyDoc.Blocks.Count >= 3,
                    emptyDoc.Blocks.Count.ToString());
            }

            // ---------------- 图元 ----------------

            private static void CheckElement(string root)
            {
                // 时间文本解析（图元的范围配置）
                var now = new DateTime(2026, 10, 25, 12, 0, 0);
                Check("[RP33] 图元时间解析：now / -1d / -8h / 08:00 / 绝对时间",
                    ReportElement.TryResolveMoment("now", now, out var t1) && t1 == now
                    && ReportElement.TryResolveMoment("-1d", now, out var t2) && t2 == now.AddDays(-1)
                    && ReportElement.TryResolveMoment("-8h", now, out var t3) && t3 == now.AddHours(-8)
                    && ReportElement.TryResolveMoment("08:00", now, out var t4) && t4 == now.Date.AddHours(8)
                    && ReportElement.TryResolveMoment("2026-10-24 09:30", now, out var t5)
                    && t5 == new DateTime(2026, 10, 24, 9, 30, 0)
                    && !ReportElement.TryResolveMoment("下周", now, out _),
                    "");

                var model = ElementRegistry.CreateElement("Hmi.Report")!;
                model.Width = 760;
                model.Height = 380;
                model.SetProperty("ReportColumns", ScadaReportCodec.Format(new[]
                {
                    new ScadaReportColumn("时间", "时间", "", ScadaReportAggregate.None, 160),
                    new ScadaReportColumn("SN", "SN"),
                }));
                model.SetProperty("From", "2026-10-24");
                model.SetProperty("To", "2026-10-26");
                model.SetProperty("AutoQuery", "False");

                var element = (ReportElement)ElementRegistry.CreateControl(model)!;
                element.Style = ThemeStyle();
                Layout(element, 760, 380);

                Check("[RP34] 图元设计期：不查数据，列头按配置画出来，状态行给提示",
                    element.Result == null
                    && element.HeaderCells.Count == 2
                    && element.HeaderCells[0].Text == "时间"
                    && element.StatusText.Contains("设计期")
                    && element.HasRows == false,
                    $"headers={element.HeaderCells.Count} status={element.StatusText}");

                // 运行态：给一个假报表源，点查询
                var engine = new ScadaAlarmEngine(new ScadaDocument(), null);
                var beat = new ScadaBeatSource(Dispatcher.CurrentDispatcher);
                var fake = new FakeReportSource();
                var context = new ScadaRuntimeContext(engine, beat, reports: fake,
                    reportExporter: new ScadaReportExporter(), audit: new FakeAuditSink());

                element.RuntimeContext = context;
                element.QueryCommand.Execute();
                PumpDispatcher(400);

                Check("[RP35] 图元运行态：查询出 45 行、单页显示（默认每页 200 行）",
                    element.Result != null
                    && element.Result.Rows.Count == 45
                    && element.RowViews.Count == 45
                    && element.PageText.Contains("共 45 行"),
                    $"rows={element.Result?.Rows.Count} page={element.PageText} status={element.StatusText}");

                model.SetProperty("PageSize", "20");
                element.Refresh();
                element.QueryCommand.Execute();
                PumpDispatcher(400);

                Check("[RP36] 图元分页：每页 20 行 → 3 页，当前页只实例化 20 行",
                    element.PageCount == 3 && element.RowViews.Count == 20 && element.PageText.Contains("1 / 3"),
                    $"pages={element.PageCount} rows={element.RowViews.Count} text={element.PageText}");

                element.NextPageCommand.Execute();
                Check("[RP37] 图元翻页：下一页 → 第 2 页、行内容变化",
                    element.PageIndex == 1 && element.RowViews.Count == 20 && element.PageText.Contains("2 / 3"),
                    $"index={element.PageIndex} text={element.PageText}");

                // 导出权限：Operator 不能导出（RequiredRole = Engineer）
                var policy = new FakeAccessPolicy { CurrentRole = ScadaRole.Operator };
                var guarded = new ScadaRuntimeContext(engine, beat,
                    accessPolicy: policy,
                    reports: fake,
                    reportExporter: new ScadaReportExporter(),
                    audit: new FakeAuditSink());
                element.RuntimeContext = guarded;
                model.RequiredRole = ScadaRole.Engineer;
                element.Refresh();
                element.QueryCommand.Execute();
                PumpDispatcher(400);

                Check("[RP38] 导出权限：操作员 + 要求工程师 → 导出命令不可执行",
                    element.Result != null && !element.ExportXlsxCommand.CanExecute(),
                    $"canExport={element.ExportXlsxCommand.CanExecute()}");

                policy.CurrentRole = ScadaRole.Engineer;
                element.Refresh();
                element.QueryCommand.Execute();
                PumpDispatcher(400);
                Check("[RP39] 导出权限：换成工程师 → 导出命令可执行（含宿主导出器）",
                    element.ExportXlsxCommand.CanExecute() && element.PrintCommand.CanExecute(),
                    $"export={element.ExportXlsxCommand.CanExecute()} print={element.PrintCommand.CanExecute()}");
            }

            private static Style ThemeStyle()
            {
                var theme = new ResourceDictionary
                {
                    Source = new Uri("/VM.Scada.Controls;component/Themes/Generic.xaml", UriKind.Relative),
                };
                return (Style)theme[typeof(ReportElement)];
            }

            /// <summary>算 FlowDocument 的页数（ComputePageCount 是 void，页数从 PageCount 读）</summary>
            private static int PageCountOf(System.Windows.Documents.FlowDocument document)
            {
                var paginator = ((System.Windows.Documents.IDocumentPaginatorSource)document).DocumentPaginator;
                paginator.ComputePageCount();
                return paginator.PageCount;
            }

            private static void Layout(Control control, double width, double height)
            {
                control.ApplyTemplate();
                control.Measure(new Size(width, height));
                control.Arrange(new Rect(0, 0, width, height));
                control.UpdateLayout();
            }

            private static void RunOnSta(Action action)
            {
                Exception? error = null;
                var thread = new Thread(() =>
                {
                    try { action(); }
                    catch (Exception ex) { error = ex; }
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join();

                if (error != null)
                    Check("报表断言全程未抛异常", false, error.ToString());
            }

            /// <summary>假报表源：固定 5 行，用来测图元的分页/状态（数据源本身在 [RP9~RP24] 用真 CSV 测）。</summary>
            private sealed class FakeReportSource : IScadaReportSource
            {
                public string Name => "断言报表源";
                public bool IsAvailable => true;
                public string? UnavailableReason => null;

                public System.Threading.Tasks.Task<ScadaReportResult> BuildAsync(
                    ScadaReportRequest request, CancellationToken cancellationToken)
                {
                    // 45 行：默认每页 200 行 = 单页；配成每页 20 行 = 3 页（分页断言用）
                    var rows = Enumerable.Range(0, 45)
                        .Select(i => (IReadOnlyList<string>)new[] { $"2026-10-25 09:{i:00}:00", "SN-" + i })
                        .ToList();

                    return System.Threading.Tasks.Task.FromResult(
                        ScadaReportResult.Ok(new[] { "时间", "SN" }, rows, 45, Name));
                }
            }

            /// <summary>假审计出口：只记调用次数与最后一条，供断言核对。</summary>
            private sealed class FakeAuditSink : IScadaAuditSink
            {
                public int Count { get; private set; }
                public string LastAction { get; private set; } = string.Empty;
                public string LastDetail { get; private set; } = string.Empty;

                public bool Append(string actor, string sessionId, string clientAddress, string subject,
                                   string eventText, string action, string result, string detail)
                {
                    Count++;
                    LastAction = action;
                    LastDetail = detail;
                    return true;
                }
            }
        }
    }
}
