using System.Collections.Generic;
using System.Windows;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 报表里的一格（视图模型，模板直绑）。宽度在 <see cref="ReportElement"/> 里按
    /// "定宽列 + 余量均分"算好——模板不做换算，与数据表格同一套做法。
    /// </summary>
    public sealed class ReportCellView
    {
        public ReportCellView(string text, double width, bool isHeader, bool alignRight)
        {
            Text = text ?? string.Empty;
            Width = width;
            IsHeader = isHeader;
            AlignRight = alignRight;
        }

        public string Text { get; }

        public double Width { get; }

        /// <summary>表头格（换字色/字号）</summary>
        public bool IsHeader { get; }

        /// <summary>右对齐（数值列；表头跟随同一列，避免表头与数据错位）</summary>
        public bool AlignRight { get; }

        /// <summary>
        /// 直接给模板用的对齐枚举。
        ///
        /// 为什么不写转换器：模板里只为一个 bool → TextAlignment 引一个转换器，
        /// 会让"这个格子怎么对齐"分散到两处（模型 + 资源字典），换算放这里最省。
        /// </summary>
        public TextAlignment Alignment => AlignRight ? TextAlignment.Right : TextAlignment.Left;
    }

    /// <summary>报表里的一行（视图模型）。</summary>
    public sealed class ReportRowView
    {
        public ReportRowView(IReadOnlyList<ReportCellView> cells)
        {
            Cells = cells;
        }

        public IReadOnlyList<ReportCellView> Cells { get; }
    }
}
