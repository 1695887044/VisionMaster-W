using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VisionMaster.Scada
{
    /// <summary>
    /// 历史查询的结果状态。<b>三态必须齐</b>（设计基线 §历史数据）：
    /// 有数据 / 该范围没有记录 / 数据源不可用，三者的界面文案完全不同——
    /// 把"没有记录"说成"不可用"会让人去修设备，把"不可用"说成"没有记录"会让人以为设备没生产。
    /// </summary>
    public enum ScadaHistoryStatus
    {
        /// <summary>查到记录</summary>
        Ok = 0,

        /// <summary>范围合法、数据源可用，但该范围内没有记录</summary>
        Empty = 1,

        /// <summary>数据源不可用（目录不存在、文件被占用、解析失败）</summary>
        Unavailable = 2,

        /// <summary>时间范围非法（起点晚于终点、跨度过大）</summary>
        InvalidRange = 3,

        /// <summary>查询超时（文件太大 / 磁盘慢）</summary>
        Timeout = 4,

        /// <summary>查询被取消（切页、关窗、改范围）</summary>
        Cancelled = 5,
    }

    /// <summary>
    /// 一次历史查询的入参（<b>不可变</b>）。
    ///
    /// 时间用本地时间：现场配置与查看都是"今天/昨天"这种本地口径，
    /// 落盘文件里的时间列也是本地时间（数据记录插件按本地时间写）。
    /// 转 UTC 只在需要跨时区比对时做，不在这一层统一转——那会让"今天"在文件里对不上。
    /// </summary>
    public sealed class ScadaHistoryQuery
    {
        /// <summary>范围起点（含）</summary>
        public DateTime FromLocal { get; set; }

        /// <summary>范围终点（不含）</summary>
        public DateTime ToLocal { get; set; }

        /// <summary>页码（0 基）</summary>
        public int PageIndex { get; set; }

        /// <summary>每页条数（1~500）</summary>
        public int PageSize { get; set; } = 50;

        /// <summary>时间列名（空 = 自动识别表头里含"时间/日期/time/date"的列）</summary>
        public string? TimestampColumn { get; set; }

        /// <summary>关键字（空 = 不过滤；匹配任意列，忽略大小写）</summary>
        public string? Keyword { get; set; }

        /// <summary>把页码与页大小夹到合法区间（数据源实现共用，避免各写一遍）</summary>
        public (int PageIndex, int PageSize) NormalizePaging()
        {
            var pageIndex = PageIndex < 0 ? 0 : PageIndex;
            var pageSize = PageSize < 1 ? 1 : (PageSize > 500 ? 500 : PageSize);
            return (pageIndex, pageSize);
        }
    }

    /// <summary>
    /// 历史查询的结果（<b>不可变</b>）。行是纯文本：列映射与格式化归数据源，
    /// 表格只负责摆出来——与 <see cref="ScadaGridRow"/> 的分工一致。
    /// </summary>
    public sealed class ScadaHistoryResult
    {
        public ScadaHistoryStatus Status { get; init; }

        /// <summary>给人看的原因/提示（不可用原因、空态建议、跳过的坏文件数）</summary>
        public string? Message { get; init; }

        /// <summary>列头（顺序即显示顺序）</summary>
        public IReadOnlyList<string> Columns { get; init; } = Array.Empty<string>();

        /// <summary>本页的行（每行长度 = 列数）</summary>
        public IReadOnlyList<IReadOnlyList<string>> Rows { get; init; } = Array.Empty<IReadOnlyList<string>>();

        /// <summary>过滤后的总条数（不是本页条数）</summary>
        public int TotalCount { get; init; }

        /// <summary>本页页码（0 基；数据源已夹紧后的值）</summary>
        public int PageIndex { get; init; }

        /// <summary>每页条数（数据源已夹紧后的值）</summary>
        public int PageSize { get; init; } = 50;

        /// <summary>数据源名字（脚注显示"来源：方案历史目录"）</summary>
        public string? SourceName { get; init; }

        public int PageCount => TotalCount <= 0 ? 0 : (TotalCount + PageSize - 1) / PageSize;

        public static ScadaHistoryResult Ok(
            IReadOnlyList<string> columns,
            IReadOnlyList<IReadOnlyList<string>> rows,
            int totalCount,
            int pageIndex,
            int pageSize,
            string? sourceName,
            string? message = null)
            => new()
            {
                Status = ScadaHistoryStatus.Ok,
                Columns = columns,
                Rows = rows,
                TotalCount = totalCount,
                PageIndex = pageIndex,
                PageSize = pageSize,
                SourceName = sourceName,
                Message = message,
            };

        public static ScadaHistoryResult Empty(
            IReadOnlyList<string> columns,
            int pageIndex,
            int pageSize,
            string? sourceName,
            string? message = null)
            => new()
            {
                Status = ScadaHistoryStatus.Empty,
                Columns = columns,
                TotalCount = 0,
                PageIndex = pageIndex,
                PageSize = pageSize,
                SourceName = sourceName,
                Message = message,
            };

        public static ScadaHistoryResult Unavailable(string message, string? sourceName = null)
            => new() { Status = ScadaHistoryStatus.Unavailable, Message = message, SourceName = sourceName };

        public static ScadaHistoryResult InvalidRange(string message)
            => new() { Status = ScadaHistoryStatus.InvalidRange, Message = message };

        public static ScadaHistoryResult Timeout(string message, string? sourceName = null)
            => new() { Status = ScadaHistoryStatus.Timeout, Message = message, SourceName = sourceName };

        public static ScadaHistoryResult Cancelled(string? sourceName = null)
            => new() { Status = ScadaHistoryStatus.Cancelled, SourceName = sourceName };
    }

    /// <summary>
    /// 历史数据源契约（设计基线 §历史数据）。
    ///
    /// 为什么放在领域层、由宿主实现
    /// ---------
    /// 图元库刻意不引用 VM.Core（与轴源、值源同一条边界）：它不认识"数据记录插件把文件写在哪"。
    /// 契约在这里，宿主把"方案目录 + CSV 账本"接进来；图元只认 <see cref="QueryAsync"/>。
    ///
    /// 实现约束
    /// ---------
    /// · <see cref="QueryAsync"/> 不抛异常：错误一律折进 <see cref="ScadaHistoryResult.Status"/>，
    ///   否则图元每次查询都要写一遍 try/catch，而"忘了 catch"的后果是整个运行窗口崩掉。
    /// · 必须尊重 <paramref name="cancellationToken"/>：切页/关窗/改范围时旧请求要能停。
    /// · 返回的 <see cref="ScadaHistoryResult.Rows"/> 只含<b>当前页</b>；总数放
    ///   <see cref="ScadaHistoryResult.TotalCount"/>，不要为了分页把全量行搬进内存。
    /// </summary>
    public interface IScadaHistorySource
    {
        /// <summary>数据源显示名（脚注"来源：…"）</summary>
        string Name { get; }

        /// <summary>此刻是否可用（目录不存在、文件被占用等）</summary>
        bool IsAvailable { get; }

        /// <summary>不可用原因；可用时为 null</summary>
        string? UnavailableReason { get; }

        /// <summary>查询一页历史</summary>
        Task<ScadaHistoryResult> QueryAsync(ScadaHistoryQuery query, CancellationToken cancellationToken);
    }
}
