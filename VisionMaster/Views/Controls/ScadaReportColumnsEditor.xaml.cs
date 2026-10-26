using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Prism.Mvvm;
using VisionMaster.Scada;
using VisionMaster.Services;

namespace VisionMaster.Views.Controls
{
    /// <summary>
    /// 报表「配置列」编辑器：加 / 删 / 上移 / 下移 + 每列五个字段。
    ///
    /// 与 <see cref="ScadaGridColumnsEditor"/> 同构（同一套生命周期：构造 → 编辑 → 读
    /// <see cref="ResultJson"/> → 丢弃），差别只在字段：报表列绑的是<b>账本列名</b>，
    /// 且多一个"聚合方式"。
    /// </summary>
    public partial class ScadaReportColumnsEditor : UserControl
    {
        /// <summary>当前方案文件路径（用来定位 History 账本）；null = 方案还没保存过</summary>
        private readonly string? _solutionFilePath;

        public ScadaReportColumnsEditor() : this(null, null) { }

        public ScadaReportColumnsEditor(IReadOnlyList<ScadaReportColumn>? columns, string? solutionFilePath = null)
        {
            InitializeComponent();

            _solutionFilePath = solutionFilePath;

            if (columns != null)
            {
                foreach (var column in columns)
                    Rows.Add(new ReportColumnEditRow(column));
            }

            DataContext = this;
        }

        /// <summary>正在编辑的列（双向绑定在每行的输入框上）</summary>
        public ObservableCollection<ReportColumnEditRow> Rows { get; } = new();

        /// <summary>确认后的规范 JSON（来源列为空的行由 codec 丢掉）</summary>
        public string ResultJson => ScadaReportCodec.Format(Rows.Select(row => row.ToColumn()));

        /// <summary>
        /// 会被丢掉的行数（来源列为空）。
        /// 与数据表格的列编辑器同一条理由：codec 丢空列是配置容错，但用户点「确定」时
        /// 得知道"刚才填的那行没生效"，否则就是静默丢配置。
        /// </summary>
        public int DroppedCount => Rows.Count(row => !row.HasSource);

        private void OnAdd(object sender, RoutedEventArgs e) => Rows.Add(new ReportColumnEditRow());

        /// <summary>
        /// 「从账本导入列」：读 History 下最新的一个账本 CSV 的表头，一键生成整张列配置。
        ///
        /// 为什么不做成下拉候选：设计期宿主不一定有账本（方案还没跑过），空候选比"让用户手打"更误导。
        /// 做成按钮则是显式动作——有账本时一键铺满，没有时说清"还没有账本"，不会静默什么都不发生。
        /// </summary>
        private void OnImportFromLedger(object sender, RoutedEventArgs e)
        {
            if (!TryReadLatestLedgerHeader(out var header, out var fileName, out var reason))
            {
                MessageBox.Show(Window.GetWindow(this), reason, "从账本导入列",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var columns = ScadaReportColumnImport.FromHeader(header);
            if (columns.Count == 0)
            {
                MessageBox.Show(Window.GetWindow(this),
                    $"账本 {fileName} 的表头里没有可用的列名（全是空列或重名列）。", "从账本导入列",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (Rows.Count > 0)
            {
                var confirm = MessageBox.Show(Window.GetWindow(this),
                    $"将用账本 {fileName} 的表头替换当前 {Rows.Count} 列（当前列的标题/格式/聚合设置会丢失）。继续？",
                    "从账本导入列", MessageBoxButton.OKCancel, MessageBoxImage.Warning);

                if (confirm != MessageBoxResult.OK)
                    return;
            }

            Rows.Clear();
            foreach (var column in columns)
                Rows.Add(new ReportColumnEditRow(column));

            ImportHintText.Text = $"已从 {fileName} 导入 {columns.Count} 列（{string.Join(" / ", columns.Select(c => c.Source))}）";
        }

        /// <summary>
        /// 找 History 目录下最新的账本并读它的表头。失败原因一律给可直接照做的中文。
        /// </summary>
        private bool TryReadLatestLedgerHeader(out IReadOnlyList<string> header, out string fileName, out string reason)
        {
            header = Array.Empty<string>();
            fileName = string.Empty;

            var directory = SolutionPaths.GetHistoryDirectory(_solutionFilePath);
            if (string.IsNullOrWhiteSpace(directory) || !System.IO.Directory.Exists(directory))
            {
                reason = "当前方案还没有历史账本目录（History）。\n\n"
                         + "账本由「CSV 记录」链路在运行时写入：先让方案跑一次、产生采样记录，再回来导入。";
                return false;
            }

            System.IO.FileInfo? latest = null;
            try
            {
                latest = new System.IO.DirectoryInfo(directory)
                    .EnumerateFiles("*.csv", System.IO.SearchOption.TopDirectoryOnly)
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .FirstOrDefault()
                    ?? new System.IO.DirectoryInfo(directory)
                        .EnumerateFiles("*.csv", System.IO.SearchOption.AllDirectories)
                        .OrderByDescending(f => f.LastWriteTimeUtc)
                        .FirstOrDefault();
            }
            catch (Exception ex)
            {
                reason = $"读历史账本目录失败：{ex.Message}";
                return false;
            }

            if (latest == null)
            {
                reason = $"历史账本目录里还没有 .csv 文件：{directory}\n\n先让方案跑一次、产生采样记录。";
                return false;
            }

            string? firstLine;
            try
            {
                using var reader = new System.IO.StreamReader(latest.FullName, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                firstLine = reader.ReadLine();
            }
            catch (Exception ex)
            {
                reason = $"读账本 {latest.Name} 失败：{ex.Message}";
                return false;
            }

            header = CsvLedger.ParseLine(firstLine ?? string.Empty);
            if (header.Count == 0)
            {
                reason = $"账本 {latest.Name} 的第一行是空的（没有表头）。";
                return false;
            }

            fileName = latest.Name;
            reason = string.Empty;
            return true;
        }

        private void OnRemove(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is ReportColumnEditRow row)
                Rows.Remove(row);
        }

        private void OnMoveUp(object sender, RoutedEventArgs e) => Move(sender, -1);

        private void OnMoveDown(object sender, RoutedEventArgs e) => Move(sender, +1);

        private void Move(object sender, int delta)
        {
            if ((sender as FrameworkElement)?.DataContext is not ReportColumnEditRow row)
                return;

            int index = Rows.IndexOf(row);
            int target = index + delta;

            if (index < 0 || target < 0 || target >= Rows.Count)
                return;

            Rows.Move(index, target);
        }
    }

    /// <summary>
    /// 一行可编辑的报表列。宽度用<b>文本</b>而不是 int：用户打到一半（空串、"12"）不该被
    /// 立刻纠正成 0；解析推迟到 <see cref="ToColumn"/>。
    /// </summary>
    public sealed class ReportColumnEditRow : BindableBase
    {
        private string _source = string.Empty;
        private string _title = string.Empty;
        private string _format = string.Empty;
        private string _aggregate = nameof(ScadaReportAggregate.None);
        private string _width = "0";

        public ReportColumnEditRow() { }

        public ReportColumnEditRow(ScadaReportColumn column)
        {
            _source = column.Source;
            _title = column.Title;
            _format = column.Format;
            _aggregate = column.Aggregate.ToString();
            _width = column.Width.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>账本列名</summary>
        public string Source
        {
            get => _source;
            set
            {
                if (SetProperty(ref _source, value ?? string.Empty))
                    RaisePropertyChanged(nameof(HasSource));
            }
        }

        /// <summary>来源列填了没有（空 = 这一列还没配完，弹窗里标红、确认时被丢掉）</summary>
        public bool HasSource => !string.IsNullOrWhiteSpace(_source);

        /// <summary>报表列名（空 = 用来源列名）</summary>
        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value ?? string.Empty);
        }

        /// <summary>数值格式</summary>
        public string Format
        {
            get => _format;
            set => SetProperty(ref _format, value ?? string.Empty);
        }

        /// <summary>聚合方式（<see cref="ScadaReportAggregate"/> 的成员名）</summary>
        public string Aggregate
        {
            get => _aggregate;
            set => SetProperty(ref _aggregate, string.IsNullOrWhiteSpace(value) ? nameof(ScadaReportAggregate.None) : value);
        }

        public string WidthText
        {
            get => _width;
            set => SetProperty(ref _width, value ?? string.Empty);
        }

        public ScadaReportColumn ToColumn()
        {
            var aggregate = Enum.TryParse<ScadaReportAggregate>(Aggregate, ignoreCase: true, out var parsed)
                ? parsed
                : ScadaReportAggregate.None;

            return new ScadaReportColumn(
                Source,
                Title,
                Format,
                aggregate,
                int.TryParse(WidthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var width) ? width : 0);
        }
    }
}
