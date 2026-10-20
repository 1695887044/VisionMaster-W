using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Prism.Mvvm;
using VisionMaster.Scada;

namespace VisionMaster.Views.Controls
{
    /// <summary>
    /// 「配置列」编辑器：加 / 删 / 上移 / 下移 + 每列五个字段。
    ///
    /// 为什么没有单独的 ViewModel：它的生命周期只有一次弹窗（构造 → 编辑 → 读
    /// <see cref="ResultJson"/> → 丢弃），没有跨窗口状态，也没有命令要挂。
    /// 与轴选择弹窗那种"要调宿主、要预选"的场景不同，这里只是把一份 JSON 摊开再收回去。
    /// </summary>
    public partial class ScadaGridColumnsEditor : UserControl
    {
        public ScadaGridColumnsEditor() : this(null) { }

        public ScadaGridColumnsEditor(IReadOnlyList<ScadaGridColumn>? columns)
        {
            InitializeComponent();

            if (columns != null)
            {
                foreach (var column in columns)
                    Rows.Add(new ColumnEditRow(column));
            }

            DataContext = this;
        }

        /// <summary>正在编辑的列（双向绑定在每行的输入框上）</summary>
        public ObservableCollection<ColumnEditRow> Rows { get; } = new();

        /// <summary>确认后的规范 JSON（变量名为空的行由 codec 丢掉）</summary>
        public string ResultJson => ScadaGridColumnsCodec.Format(Rows.Select(row => row.ToColumn()));

        private void OnAdd(object sender, RoutedEventArgs e) => Rows.Add(new ColumnEditRow());

        private void OnRemove(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is ColumnEditRow row)
                Rows.Remove(row);
        }

        private void OnMoveUp(object sender, RoutedEventArgs e) => Move(sender, -1);

        private void OnMoveDown(object sender, RoutedEventArgs e) => Move(sender, +1);

        private void Move(object sender, int delta)
        {
            if ((sender as FrameworkElement)?.DataContext is not ColumnEditRow row)
                return;

            int index = Rows.IndexOf(row);
            int target = index + delta;

            if (index < 0 || target < 0 || target >= Rows.Count)
                return;

            Rows.Move(index, target);
        }
    }

    /// <summary>
    /// 一行可编辑的列。宽度用<b>文本</b>而不是 int：用户打到一半（空串、"12"）不该被
    /// 立刻纠正成 0；解析推迟到 <see cref="ToColumn"/>。
    ///
    /// <see cref="Monospace"/> 目前没有对应控件（v1 先保住往返，不在弹窗里改），
    /// 所以它从模型读出来、原样写回去——否则打开一次弹窗就把等宽设置抹掉了。
    /// </summary>
    public sealed class ColumnEditRow : BindableBase
    {
        private string _header = string.Empty;
        private string _variable = string.Empty;
        private string _width = "0";
        private string _align = "left";
        private string _format = string.Empty;
        private bool _monospace;

        public ColumnEditRow() { }

        public ColumnEditRow(ScadaGridColumn column)
        {
            _header = column.Header;
            _variable = column.Variable;
            _width = column.Width.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _align = column.Align;
            _format = column.Format;
            _monospace = column.Monospace;
        }

        public string Header
        {
            get => _header;
            set => SetProperty(ref _header, value ?? string.Empty);
        }

        public string Variable
        {
            get => _variable;
            set => SetProperty(ref _variable, value ?? string.Empty);
        }

        public string WidthText
        {
            get => _width;
            set => SetProperty(ref _width, value ?? string.Empty);
        }

        public string Align
        {
            get => _align;
            set => SetProperty(ref _align, string.IsNullOrWhiteSpace(value) ? "left" : value);
        }

        public string Format
        {
            get => _format;
            set => SetProperty(ref _format, value ?? string.Empty);
        }

        public ScadaGridColumn ToColumn()
            => new(Header, Variable, int.TryParse(WidthText, out var width) ? width : 0, Align, Format, _monospace);
    }
}
