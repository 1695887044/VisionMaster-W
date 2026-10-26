using System;
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
    /// 柱状图 / 饼图的「数据项」编辑器：加 / 删 / 上移 / 下移 + 每项三个字段。
    ///
    /// 与 <see cref="ScadaReportColumnsEditor"/> 同构（同一套生命周期：构造 → 编辑 → 读
    /// <see cref="ResultJson"/> → 丢弃），差别只在字段与"没有聚合/格式"。
    /// 抽成独立控件而不复用一个：报表列绑的是账本列名，图表项绑的是工程变量，
    /// 字段一旦合并，两边都会被逼着显示对方才用得上的东西。
    /// </summary>
    public partial class ScadaChartSeriesEditor : UserControl
    {
        /// <summary>变量选择弹窗（宿主注入）；null = 只能手填（断言/无宿主场景）</summary>
        private readonly VisionMaster.Services.IScadaVariablePicker? _picker;

        public ScadaChartSeriesEditor() : this(null, null) { }

        public ScadaChartSeriesEditor(IReadOnlyList<ScadaChartItem>? items,
                                      VisionMaster.Services.IScadaVariablePicker? picker = null)
        {
            InitializeComponent();

            _picker = picker;

            if (items != null)
            {
                foreach (var item in items)
                    Rows.Add(new ChartSeriesEditRow(item));
            }

            DataContext = this;
        }

        /// <summary>正在编辑的数据项（双向绑定在每行的输入框上）</summary>
        public ObservableCollection<ChartSeriesEditRow> Rows { get; } = new();

        /// <summary>颜色候选：值仍是 #RRGGBB 字符串，只把字面量从 XAML 移到代码侧（R140）。</summary>
        public IReadOnlyList<ChartColorOption> ColorOptions { get; } = new[]
        {
            new ChartColorOption("自动", string.Empty),
            new ChartColorOption("蓝", "#2563EB"),
            new ChartColorOption("青", "#38BDF8"),
            new ChartColorOption("绿", "#22C55E"),
            new ChartColorOption("橙", "#F59E0B"),
            new ChartColorOption("紫", "#8B5CF6"),
            new ChartColorOption("红", "#E03A2B"),
        };

        /// <summary>确认后的规范串（变量名为空的行由 codec 丢掉）</summary>
        public string ResultJson => ScadaChartCodec.Format(Rows.Select(row => row.ToItem()));

        /// <summary>会被丢掉的行数（变量名为空）——确认时提示用户"刚才填的那行没生效"</summary>
        public int DroppedCount => Rows.Count(row => !row.HasVariable);

        private void OnAdd(object sender, RoutedEventArgs e) => Rows.Add(new ChartSeriesEditRow());

        /// <summary>
        /// 「选…」：打开工程变量选择弹窗，选中后把变量名写回该行。
        ///
        /// 图表项只存变量名（与报表列同一口径：按名字解析、改名后显示"未解析"），
        /// 所以这里丢掉选择器给的 Id，只取 Name——不要以为没存 Id 是漏存。
        /// </summary>
        private void OnPickVariable(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not ChartSeriesEditRow row)
                return;

            if (_picker == null)
            {
                MessageBox.Show(Window.GetWindow(this),
                    "当前环境没有变量选择器（断言/无宿主），请直接手填变量名。",
                    "选择变量", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _picker.Pick(Guid.Empty, row.Variable, (_, name) =>
            {
                if (!string.IsNullOrWhiteSpace(name))
                    row.Variable = name!;
            });
        }

        private void OnRemove(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is ChartSeriesEditRow row)
                Rows.Remove(row);
        }

        private void OnMoveUp(object sender, RoutedEventArgs e) => Move(sender, -1);

        private void OnMoveDown(object sender, RoutedEventArgs e) => Move(sender, +1);

        private void Move(object sender, int delta)
        {
            if ((sender as FrameworkElement)?.DataContext is not ChartSeriesEditRow row)
                return;

            int index = Rows.IndexOf(row);
            int target = index + delta;

            if (index < 0 || target < 0 || target >= Rows.Count)
                return;

            Rows.Move(index, target);
        }
    }

    /// <summary>一行可编辑的数据项（标签 / 变量名 / 颜色）</summary>
    public sealed class ChartColorOption
    {
        public ChartColorOption(string label, string tag) { Label = label; Tag = tag; }
        public string Label { get; }
        public string Tag { get; }
    }

    public sealed class ChartSeriesEditRow : BindableBase
    {
        private string _label = string.Empty;
        private string _variable = string.Empty;
        private string _color = string.Empty;

        public ChartSeriesEditRow() { }

        public ChartSeriesEditRow(ScadaChartItem item)
        {
            _label = item.Label;
            _variable = item.Variable;
            _color = item.Color;
        }

        public string Label
        {
            get => _label;
            set => SetProperty(ref _label, value ?? string.Empty);
        }

        /// <summary>变量名（空 = 这一项还没配完，标红、确认时被丢掉）</summary>
        public string Variable
        {
            get => _variable;
            set
            {
                if (SetProperty(ref _variable, value ?? string.Empty))
                    RaisePropertyChanged(nameof(HasVariable));
            }
        }

        public bool HasVariable => !string.IsNullOrWhiteSpace(_variable);

        /// <summary>颜色（#RRGGBB）；非法值由 codec 清空并回落自动配色</summary>
        public string Color
        {
            get => _color;
            set => SetProperty(ref _color, value ?? string.Empty);
        }

        public ScadaChartItem ToItem() => new()
        {
            Label = Label,
            Variable = Variable,
            Color = Color,
        };
    }
}
