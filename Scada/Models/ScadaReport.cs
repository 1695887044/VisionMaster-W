using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace VisionMaster.Scada
{
    /// <summary>报表的两种形态：明细（逐行）与汇总（按班次/日/小时分组聚合）。</summary>
    public enum ScadaReportMode
    {
        /// <summary>逐行明细（可翻页、可打印）</summary>
        Detail = 0,

        /// <summary>按 <see cref="ScadaReportRequest.Grouping"/> 分组聚合</summary>
        Summary = 1,
    }

    /// <summary>汇总列用的聚合函数。明细模式忽略它。</summary>
    public enum ScadaReportAggregate
    {
        /// <summary>不聚合：明细取原值；汇总取该组第一行</summary>
        None = 0,
        Sum = 1,
        Average = 2,
        Min = 3,
        Max = 4,

        /// <summary>计数（对任意列有效：数非空单元格）</summary>
        Count = 5,

        /// <summary>该组最后一行（明细里常用来取"结束值"）</summary>
        Last = 6,
    }

    /// <summary>汇总分组口径。</summary>
    public enum ScadaReportGrouping
    {
        None = 0,

        /// <summary>按班次（起点由 <see cref="ScadaReportRequest.ShiftStarts"/> 给）</summary>
        Shift = 1,

        /// <summary>按自然日</summary>
        Day = 2,

        /// <summary>按小时</summary>
        Hour = 3,
    }

    /// <summary>
    /// 报表的一列（<b>设计期配置</b>，存属性袋里的一条规范 JSON，见 <see cref="ScadaReportCodec"/>）。
    ///
    /// 与 <see cref="ScadaGridColumn"/> 的分工：数据表格的列绑的是<b>变量名</b>（实时取值），
    /// 报表的列绑的是<b>CSV 表头列名</b>（历史账本里的列）。两者都是"配置"，都不认识业务类。
    /// </summary>
    public sealed class ScadaReportColumn
    {
        /// <summary>来源列名（CSV 表头里那一列；去空白、忽略大小写匹配）</summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>报表列名（空 = 用来源列名）</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>数值格式（如 <c>0.00</c> / <c>0</c>）；空 = 默认 <c>0.###</c>。只对可解析为数值的单元格生效</summary>
        public string Format { get; set; } = string.Empty;

        /// <summary>汇总模式下这一列怎么算</summary>
        public ScadaReportAggregate Aggregate { get; set; } = ScadaReportAggregate.None;

        /// <summary>列宽（像素）。<b>0 = 余量</b>：吃掉其余列剩下的宽度</summary>
        public int Width { get; set; }

        public ScadaReportColumn() { }

        public ScadaReportColumn(string source, string title = "", string format = "",
                                 ScadaReportAggregate aggregate = ScadaReportAggregate.None, int width = 0)
        {
            Source = source ?? string.Empty;
            Title = title ?? string.Empty;
            Format = format ?? string.Empty;
            Aggregate = aggregate;
            Width = width;
        }

        /// <summary>归一化：标题空则用来源列名；宽度夹到下限；聚合值越界回落 None</summary>
        public ScadaReportColumn Normalize()
        {
            var source = (Source ?? string.Empty).Trim();

            return new ScadaReportColumn
            {
                Source = source,
                Title = string.IsNullOrWhiteSpace(Title) ? source : Title.Trim(),
                Format = (Format ?? string.Empty).Trim(),
                Aggregate = Enum.IsDefined(typeof(ScadaReportAggregate), Aggregate) ? Aggregate : ScadaReportAggregate.None,
                Width = Width < 0 ? 0
                    : Width > 0 && Width < ScadaReportCodec.MinWidth ? ScadaReportCodec.MinWidth
                    : Width,
            };
        }
    }

    /// <summary>
    /// 一次报表构建的入参（<b>不可变</b>）。
    ///
    /// 时间用本地时间，与历史查询同一口径：现场看的就是"今天 08:00 到 20:00"。
    /// </summary>
    public sealed class ScadaReportRequest
    {
        public ScadaReportMode Mode { get; init; }

        public DateTime FromLocal { get; init; }

        public DateTime ToLocal { get; init; }

        public IReadOnlyList<ScadaReportColumn> Columns { get; init; } = Array.Empty<ScadaReportColumn>();

        /// <summary>时间列名（空 = 自动识别表头里含"时间/日期/time/date"的第一列）</summary>
        public string? TimestampColumn { get; init; }

        /// <summary>关键字（空 = 不过滤；匹配任意列，忽略大小写）</summary>
        public string? Keyword { get; init; }

        public ScadaReportGrouping Grouping { get; init; }

        /// <summary>班次起点（如 <c>08:00,20:00</c>）；<see cref="ScadaReportGrouping.Shift"/> 时必填</summary>
        public IReadOnlyList<string> ShiftStarts { get; init; } = Array.Empty<string>();

        /// <summary>明细模式的行数上限（超出截断并在结果里说明）</summary>
        public int MaxRows { get; init; } = 5000;
    }

    /// <summary>
    /// 报表构建的结果（<b>不可变</b>）。
    ///
    /// 状态复用 <see cref="ScadaHistoryStatus"/>：报表与历史是同一件事的两个视图，
    /// "该范围没有记录"和"数据源不可用"的界面文案必须一模一样。
    /// </summary>
    public sealed class ScadaReportResult
    {
        public ScadaHistoryStatus Status { get; init; }

        /// <summary>给人看的原因/提示（不可用原因、跳过行数、截断说明）</summary>
        public string? Message { get; init; }

        /// <summary>列头（= 配置里每列的 <see cref="ScadaReportColumn.Title"/>）</summary>
        public IReadOnlyList<string> Columns { get; init; } = Array.Empty<string>();

        /// <summary>行（已格式化文本；每行长度 = 列数）</summary>
        public IReadOnlyList<IReadOnlyList<string>> Rows { get; init; } = Array.Empty<IReadOnlyList<string>>();

        /// <summary>结果行数（明细 = 返回行数；汇总 = 分组数）</summary>
        public int RowCount { get; init; }

        /// <summary>实际扫描的账本行数（页脚"扫描 N 行"）</summary>
        public int ScannedRows { get; init; }

        public string? SourceName { get; init; }

        public DateTime GeneratedAt { get; init; }

        public static ScadaReportResult Ok(
            IReadOnlyList<string> columns,
            IReadOnlyList<IReadOnlyList<string>> rows,
            int scannedRows,
            string? sourceName,
            string? message = null,
            DateTime? generatedAt = null)
            => new()
            {
                Status = ScadaHistoryStatus.Ok,
                Columns = columns ?? Array.Empty<string>(),
                Rows = rows ?? Array.Empty<IReadOnlyList<string>>(),
                RowCount = rows?.Count ?? 0,
                ScannedRows = scannedRows,
                SourceName = sourceName,
                Message = message,
                GeneratedAt = generatedAt ?? DateTime.Now,
            };

        public static ScadaReportResult Empty(
            IReadOnlyList<string> columns, int scannedRows, string? sourceName, string? message = null)
            => new()
            {
                Status = ScadaHistoryStatus.Empty,
                Columns = columns ?? Array.Empty<string>(),
                ScannedRows = scannedRows,
                SourceName = sourceName,
                Message = message,
                GeneratedAt = DateTime.Now,
            };

        public static ScadaReportResult Unavailable(string message, string? sourceName = null)
            => new()
            {
                Status = ScadaHistoryStatus.Unavailable,
                Message = message,
                SourceName = sourceName,
                GeneratedAt = DateTime.Now,
            };

        public static ScadaReportResult InvalidRange(string message)
            => new() { Status = ScadaHistoryStatus.InvalidRange, Message = message, GeneratedAt = DateTime.Now };

        public static ScadaReportResult Timeout(string message, string? sourceName = null)
            => new()
            {
                Status = ScadaHistoryStatus.Timeout,
                Message = message,
                SourceName = sourceName,
                GeneratedAt = DateTime.Now,
            };

        public static ScadaReportResult Cancelled(string? sourceName = null)
            => new() { Status = ScadaHistoryStatus.Cancelled, SourceName = sourceName, GeneratedAt = DateTime.Now };
    }

    /// <summary>
    /// 报表数据源契约（由宿主实现，图元只认这个接口）。
    ///
    /// 为什么必须新开一条契约，而不是复用 <see cref="IScadaHistorySource"/>
    /// ---------
    /// 历史查询返回的是<b>当前页的字符串行</b>（<see cref="ScadaHistoryResult.Rows"/> 是
    /// <c>string</c>），既不能求和，也不给全量。报表要的是"扫一遍整个范围、边扫边聚合"，
    /// 只能在数据源内部做（那里才知道原始数值与文件布局）。
    ///
    /// 实现约束（与历史源逐条一致）
    /// ---------
    /// · <see cref="BuildAsync"/> 不抛异常：错误一律折进 <see cref="ScadaReportResult.Status"/>。
    /// · 必须尊重 <paramref name="cancellationToken"/>：切页/改范围/关窗时旧请求要能停。
    /// · 汇总模式的聚合在扫描过程中完成，<b>不把全量行搬进内存</b>。
    /// </summary>
    public interface IScadaReportSource
    {
        /// <summary>数据源显示名（页脚"来源：…"）</summary>
        string Name { get; }

        /// <summary>此刻是否可用（目录不存在、文件被占用等）</summary>
        bool IsAvailable { get; }

        /// <summary>不可用原因；可用时为 null</summary>
        string? UnavailableReason { get; }

        /// <summary>构建一张报表</summary>
        Task<ScadaReportResult> BuildAsync(ScadaReportRequest request, CancellationToken cancellationToken);
    }

    /// <summary>
    /// 报表导出契约（由宿主实现：xlsx 要 MiniExcel，CSV 要 UTF-8 BOM）。
    ///
    /// 为什么不让图元自己写文件：控件库刻意不引第三方表格库（与"不引 VM.Core"同一条边界），
    /// 而"导出的文件长什么样"恰恰是宿主的责任（表头、单位、操作者署名）。
    /// </summary>
    public interface IScadaReportExporter
    {
        /// <summary>支持的格式键（小写，如 <c>xlsx</c> / <c>csv</c>）</summary>
        IReadOnlyList<string> Formats { get; }

        /// <summary>
        /// 把结果写到 <paramref name="path"/>。<b>不抛异常</b>：失败返回 false + 中文原因。
        /// 实现必须"先写临时文件再覆盖移动"——写一半失败不能毁掉用户原来那份文件。
        /// </summary>
        bool TryExport(ScadaReportResult result, string title, string format, string path, out string error);
    }

    /// <summary>
    /// 报表列定义的落盘编解码。与 <see cref="ScadaGridColumnsCodec"/> 逐字同构：
    /// 规范 JSON 存在属性袋的一条键里，设计期面板、运行期图元、断言共用同一份规则。
    /// </summary>
    public static class ScadaReportCodec
    {
        /// <summary>属性袋里的键</summary>
        public const string Key = "ReportColumns";

        /// <summary>列宽下限（再窄就放不下一个汉字）</summary>
        public const int MinWidth = 40;

        /// <summary>列数上限：报表是"给人看的一页"，不是导出全表</summary>
        public const int MaxColumns = 16;

        /// <summary>解析列定义；空串/坏 JSON 一律返回空表，<b>不抛异常</b>。</summary>
        public static IReadOnlyList<ScadaReportColumn> Parse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return Array.Empty<ScadaReportColumn>();

            try
            {
                var list = JsonConvert.DeserializeObject<List<ScadaReportColumn>>(json);
                if (list == null || list.Count == 0)
                    return Array.Empty<ScadaReportColumn>();

                var result = new List<ScadaReportColumn>(Math.Min(list.Count, MaxColumns));

                foreach (var item in list)
                {
                    if (item == null) continue;
                    if (result.Count >= MaxColumns) break;

                    // 来源列为空的列是"还没配完"，留着只会在表头占一格却永远空白
                    if (string.IsNullOrWhiteSpace(item.Source)) continue;

                    result.Add(item.Normalize());
                }

                return result;
            }
            catch (JsonException)
            {
                return Array.Empty<ScadaReportColumn>();
            }
        }

        /// <summary>把列定义写成规范串（去空列、截到上限、字段归一）。</summary>
        public static string Format(IEnumerable<ScadaReportColumn>? columns)
        {
            if (columns == null)
                return string.Empty;

            var list = new List<ScadaReportColumn>();

            foreach (var column in columns)
            {
                if (column == null) continue;
                if (list.Count >= MaxColumns) break;
                if (string.IsNullOrWhiteSpace(column.Source)) continue;

                list.Add(column.Normalize());
            }

            return list.Count == 0 ? string.Empty : JsonConvert.SerializeObject(list);
        }
    }
}
