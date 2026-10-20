using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace VisionMaster.Scada
{
    /// <summary>
    /// 数据表格图元的一列（<b>设计期配置</b>，存属性袋里的一条规范 JSON）。
    ///
    /// 为什么不存 CLR 类型 / 不反射"用户自定义类"
    /// ---------
    /// 列是"表头 + 绑哪个变量 + 怎么显示"，是<b>数据</b>不是类型。存类型名会引出
    /// .vms 的类型白名单、程序集版本、跨机器缺类这一整串问题（本仓的 TypeKey 刻意
    /// 用字符串、不用 <c>$type</c>，同一条理由）。列定义存成数据之后，运行期只需要
    /// "按名字找变量、按格式转文本"，图元完全不认识任何业务类。
    /// </summary>
    public sealed class ScadaGridColumn
    {
        /// <summary>表头文字（空则显示变量名）</summary>
        public string Header { get; set; } = string.Empty;

        /// <summary>
        /// 取值变量名。
        ///
        /// 存<b>名字</b>而不是 Id：列定义是手写的配置，用户看到的就是变量名；
        /// 与 <see cref="ScadaBinding"/> 的"Id 优先、名字兜底"不同——那里是连线（有选择器），
        /// 这里是配置（手填）。运行期按名字解析，改名的后果是这一列显示"变量不存在"，
        /// 不会悄悄读到别的变量（名字精确匹配，Ordinal）。
        /// </summary>
        public string Variable { get; set; } = string.Empty;

        /// <summary>列宽（像素）。<b>0 = 余量</b>：吃掉其余列剩下的宽度（通常给最后一列）</summary>
        public int Width { get; set; }

        /// <summary>对齐：<c>left</c> / <c>center</c> / <c>right</c>（不认识的一律按 left）</summary>
        public string Align { get; set; } = "left";

        /// <summary>格式串（如 <c>F2</c> / <c>yyyy-MM-dd</c>）；空 = 默认。只对文本化有意义</summary>
        public string Format { get; set; } = string.Empty;

        /// <summary>等宽显示（数字/时间/SN 用；等宽 + 右对齐才扫得动一列数字）</summary>
        public bool Monospace { get; set; }

        public ScadaGridColumn() { }

        public ScadaGridColumn(string header, string variable, int width = 0, string align = "left",
                               string format = "", bool monospace = false)
        {
            Header = header ?? string.Empty;
            Variable = variable ?? string.Empty;
            Width = width;
            Align = string.IsNullOrWhiteSpace(align) ? "left" : align.Trim().ToLowerInvariant();
            Format = format ?? string.Empty;
            Monospace = monospace;
        }

        /// <summary>归一化：表头空则用变量名；宽度夹到合理区间；对齐归一</summary>
        public ScadaGridColumn Normalize()
        {
            var column = new ScadaGridColumn
            {
                Header = string.IsNullOrWhiteSpace(Header) ? (Variable ?? string.Empty) : Header.Trim(),
                Variable = (Variable ?? string.Empty).Trim(),
                Width = Width < 0 ? 0 : (Width > 0 && Width < ScadaGridColumnsCodec.MinWidth ? ScadaGridColumnsCodec.MinWidth : Width),
                Align = NormalizeAlign(Align),
                Format = (Format ?? string.Empty).Trim(),
                Monospace = Monospace,
            };

            return column;
        }

        internal static string NormalizeAlign(string? align)
            => (align ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "center" or "centre" => "center",
                "right" => "right",
                _ => "left",
            };
    }

    /// <summary>
    /// 列定义的落盘编解码。与 <see cref="ScadaAxisOptionsCodec"/> 逐字同构：
    /// 规范 JSON 存在属性袋的一条键里，设计期面板、运行期图元、离线校验共用同一份规则。
    ///
    /// 为什么不是 <c>"时间|SN|角度"</c> 这种分隔串：表头是用户自由文本，完全可能含竖线；
    /// JSON 的转义是现成的、可读的，Newtonsoft 本来就在本工程里。
    /// </summary>
    public static class ScadaGridColumnsCodec
    {
        /// <summary>属性袋里的键（与 <see cref="ScadaElement.Properties"/> 同一命名空间）</summary>
        public const string Key = "GridColumns";

        /// <summary>列宽下限（再窄就放不下一个汉字）</summary>
        public const int MinWidth = 40;

        /// <summary>列数上限：再多就横向塞不下，也不该拿一张表当报表用</summary>
        public const int MaxColumns = 12;

        /// <summary>
        /// 解析列定义。空串/坏 JSON 一律返回空表，<b>不抛异常</b>——
        /// 一条坏配置不该让整张画面打不开（与轴候选、运行态"坏值只打角标"同一口径）。
        /// </summary>
        public static IReadOnlyList<ScadaGridColumn> Parse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return Array.Empty<ScadaGridColumn>();

            try
            {
                var list = JsonConvert.DeserializeObject<List<ScadaGridColumn>>(json);
                if (list == null || list.Count == 0)
                    return Array.Empty<ScadaGridColumn>();

                var result = new List<ScadaGridColumn>(Math.Min(list.Count, MaxColumns));

                foreach (var item in list)
                {
                    if (item == null) continue;
                    if (result.Count >= MaxColumns) break;

                    // 变量名为空的列是"还没配完"，留着只会在表头占一格却永远空白
                    if (string.IsNullOrWhiteSpace(item.Variable)) continue;

                    result.Add(item.Normalize());
                }

                return result;
            }
            catch (JsonException)
            {
                return Array.Empty<ScadaGridColumn>();
            }
        }

        /// <summary>把列定义写成规范串（去空列、截到上限、字段归一）。</summary>
        public static string Format(IEnumerable<ScadaGridColumn>? columns)
        {
            if (columns == null)
                return string.Empty;

            var list = new List<ScadaGridColumn>();

            foreach (var column in columns)
            {
                if (column == null) continue;
                if (list.Count >= MaxColumns) break;
                if (string.IsNullOrWhiteSpace(column.Variable)) continue;

                list.Add(column.Normalize());
            }

            return list.Count == 0 ? string.Empty : JsonConvert.SerializeObject(list);
        }
    }

    /// <summary>
    /// 运行期的一行快照（<b>不可变</b>）。
    ///
    /// 为什么是快照而不是"活对象 + 变更通知"：行一旦采到就不该再变（历史记录的语义）。
    /// 可变行会逼着图元去订阅每一行，而表格只负责"把最近 N 条摆出来"。
    /// </summary>
    public sealed class ScadaGridRow
    {
        public ScadaGridRow(IReadOnlyList<string> cells, int errorCell, DateTime timestamp)
        {
            Cells = cells ?? Array.Empty<string>();
            ErrorCell = errorCell;
            Timestamp = timestamp;
        }

        /// <summary>各列已格式化的文本（长度 = 列数；缺值补空串）</summary>
        public IReadOnlyList<string> Cells { get; }

        /// <summary>第一个"取不到值"的列序号；<c>-1</c> = 整行都好</summary>
        public int ErrorCell { get; }

        /// <summary>采行时刻（本地时间，界面直接显示）</summary>
        public DateTime Timestamp { get; }
    }
}
