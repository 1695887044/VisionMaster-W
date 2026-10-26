using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using VisionMaster.Scada;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 报表打印排版：把查询结果排成 <see cref="FlowDocument"/>，再交给 <c>PrintDialog</c>。
    ///
    /// 为什么用 FlowDocument 而不是自己算分页
    /// ---------
    /// 表格行高随内容变（长文本换行），自己算分页必然在"某一行刚好跨页"处出错。
    /// FlowDocument 的分页器按内容自动断页，我们只需要在每页底部盖一行页脚
    /// （<see cref="ReportPagePaginator"/>）。
    ///
    /// 纸张：A4（96 DPI 下的 793.7 × 1122.5），四边 48；方向由调用方给。
    /// </summary>
    public static class ReportPrintBuilder
    {
        /// <summary>A4 宽（96 DPI）</summary>
        public const double A4Width = 793.7;

        /// <summary>A4 高（96 DPI）</summary>
        public const double A4Height = 1122.5;

        /// <summary>页边距</summary>
        public const double PagePadding = 48;

        /// <summary>构建可打印文档（不弹任何 UI，断言可直接调）。</summary>
        public static FlowDocument Build(
            ScadaReportResult result, string? title, string? rangeText, string? operatorName, bool landscape)
        {
            ArgumentNullException.ThrowIfNull(result);

            var document = new FlowDocument
            {
                FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI"),
                FontSize = 10,
                PageWidth = landscape ? A4Height : A4Width,
                PageHeight = landscape ? A4Width : A4Height,
                PagePadding = new Thickness(PagePadding),
                ColumnWidth = double.PositiveInfinity, // 单栏：表格跨栏会断得莫名其妙
            };

            var heading = new Paragraph(new Run(string.IsNullOrWhiteSpace(title) ? "报表" : title))
            {
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 2),
            };
            document.Blocks.Add(heading);

            var meta = new Paragraph
            {
                FontSize = 9,
                Foreground = Brushes.DimGray,
                Margin = new Thickness(0, 0, 0, 10),
            };
            meta.Inlines.Add(new Run(
                $"{rangeText}    来源：{result.SourceName ?? "—"}    生成：{result.GeneratedAt:yyyy-MM-dd HH:mm:ss}"
                + $"    操作者：{operatorName ?? "未登录"}"));

            if (!string.IsNullOrWhiteSpace(result.Message))
                meta.Inlines.Add(new Run("    " + result.Message) { Foreground = Brushes.DarkOrange });

            document.Blocks.Add(meta);

            if (result.Rows.Count == 0)
            {
                document.Blocks.Add(new Paragraph(new Run("（没有数据）")) { Foreground = Brushes.Gray });
                return document;
            }

            document.Blocks.Add(BuildTable(result));
            return document;
        }

        private static Table BuildTable(ScadaReportResult result)
        {
            var columnCount = Math.Max(1, result.Columns.Count);

            var table = new Table { CellSpacing = 0, BorderBrush = Brushes.Gainsboro, BorderThickness = new Thickness(0.5) };

            for (var i = 0; i < columnCount; i++)
                table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });

            var group = new TableRowGroup();
            table.RowGroups.Add(group);

            var header = new TableRow { Background = Brushes.WhiteSmoke, FontWeight = FontWeights.SemiBold };
            for (var i = 0; i < columnCount; i++)
            {
                header.Cells.Add(Cell(i < result.Columns.Count ? result.Columns[i] : string.Empty, header: true));
            }
            group.Rows.Add(header);

            var rowIndex = 0;
            foreach (var row in result.Rows)
            {
                var tableRow = new TableRow();
                if (rowIndex++ % 2 == 1)
                    tableRow.Background = Brushes.WhiteSmoke;

                for (var i = 0; i < columnCount; i++)
                    tableRow.Cells.Add(Cell(i < row.Count ? row[i] : string.Empty, header: false));

                group.Rows.Add(tableRow);
            }

            return table;
        }

        private static TableCell Cell(string? text, bool header)
            => new(new Paragraph(new Run(text ?? string.Empty))
            {
                Margin = new Thickness(0),
                FontSize = header ? 9.5 : 9.5,
            })
            {
                BorderBrush = Brushes.Gainsboro,
                BorderThickness = new Thickness(0.5),
                Padding = new Thickness(4, 2, 4, 2),
            };

        /// <summary>给分页器套一层页脚（"操作者 / 第 x 页 共 y 页"）。</summary>
        public static DocumentPaginator WithFooter(DocumentPaginator inner, string footer)
            => new ReportPagePaginator(inner, footer);

        /// <summary>
        /// 在每页底部盖页脚的分页器。
        ///
        /// 为什么不改文档本身：页脚是"每页一份"的东西，往 FlowDocument 里塞只会在末尾出现一次。
        /// 分页器是唯一知道"这一页多高、底部在哪"的地方。
        /// </summary>
        private sealed class ReportPagePaginator : DocumentPaginator
        {
            private readonly DocumentPaginator _inner;
            private readonly string _footer;

            public ReportPagePaginator(DocumentPaginator inner, string footer)
            {
                _inner = inner ?? throw new ArgumentNullException(nameof(inner));
                _footer = footer ?? string.Empty;
            }

            public override bool IsPageCountValid => _inner.IsPageCountValid;

            public override int PageCount => _inner.PageCount;

            public override Size PageSize
            {
                get => _inner.PageSize;
                set => _inner.PageSize = value;
            }

            public override IDocumentPaginatorSource Source => _inner.Source;

            public override DocumentPage GetPage(int pageNumber)
            {
                var page = _inner.GetPage(pageNumber);
                if (ReferenceEquals(page, DocumentPage.Missing) || page.Visual == null)
                    return page;

                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    dc.DrawRectangle(Brushes.White, null, new Rect(new Point(), page.Size));

                    var brush = new VisualBrush(page.Visual)
                    {
                        Stretch = Stretch.None,
                        AlignmentX = AlignmentX.Left,
                        AlignmentY = AlignmentY.Top,
                    };
                    dc.DrawRectangle(brush, null, new Rect(new Point(), page.Size));

                    var text = new FormattedText(
                        $"{_footer}    第 {pageNumber + 1} / {PageCount} 页",
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface("Microsoft YaHei UI"),
                        8.5,
                        Brushes.Gray,
                        1.0);

                    dc.DrawText(text, new Point(PagePadding, page.Size.Height - PagePadding + 16));
                }

                return new DocumentPage(visual, page.Size, new Rect(new Point(), page.Size), new Rect(new Point(), page.Size));
            }
        }
    }
}
