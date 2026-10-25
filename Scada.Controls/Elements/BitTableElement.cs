using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 位表 / I/O 监视（TypeKey = <c>Hmi.BitTable</c>，另有 <c>Hmi.IoMonitor</c> / <c>Hmi.VariableMonitor</c>
    /// 两个"同控件不同默认值"的预设）。
    ///
    /// 一个字节 8 位是现场最常见的形态：把 <see cref="Value"/> 绑到一个字（字节/字/双字都行），
    /// 图元按 <see cref="BitCount"/> 与 <see cref="StartBit"/> 把每一位摊成格子。
    /// 监视类图元<b>只读</b>：要改某一位请用「位按钮」——那条路有权限判定、写回通道与失败回执，
    /// 在位表里做"点格子写位"等于把两套写入口摆在同一屏上，改坏一处没人查得出来。
    ///
    /// 为什么格子是代码建的，不是 ItemsControl
    /// ---------
    /// 格子数最多 32 个、结构固定（序号 + 值 + 标签），用 ItemsControl 反而要处理
    /// "ItemsPanel 里拿不到 TemplatedParent""UniformGrid.Columns 要跨层绑定"这些坑。
    /// 代码建的好处是：结构变更（位数/起始位/列数/显示项）走 <see cref="Rebuild"/>，
    /// 值变更（每拍一次）走 <see cref="UpdateValues"/> 只改文字与底色，<b>不重建元素</b>。
    ///
    /// 外观（边框色/粗细/文字色/字号）走绑定，不参与重建：属性面板改颜色能立刻看到，
    /// 不必等下一次 <see cref="ScadaElementBase.Refresh"/>。
    /// </summary>
    [TemplatePart(Name = PartCells, Type = typeof(UniformGrid))]
    [TemplatePart(Name = PartTitle, Type = typeof(TextBlock))]
    public partial class BitTableElement : ScadaElementBase
    {
        /// <summary>模板部件名：格子容器</summary>
        public const string PartCells = "PART_Cells";

        /// <summary>模板部件名：标题</summary>
        public const string PartTitle = "PART_Title";

        /// <summary>被监视的那个字（绑变量）</summary>
        [ScadaProperty(Key = "Value", DisplayName = "监视值", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "0", IsBindable = true,
                       Description = "绑到一个字/字节变量；按位拆开显示。小数部分忽略（只取整数位）")]
        public partial double Value { get; set; }

        /// <summary>显示多少位</summary>
        [ScadaProperty(Key = "BitCount", DisplayName = "位数", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "8", Min = 1, Max = 32,
                       Description = "一个字节 8 位；字 16 位；双字 32 位")]
        public partial int BitCount { get; set; }

        /// <summary>从哪一位开始（0 = 最低位）</summary>
        [ScadaProperty(Key = "StartBit", DisplayName = "起始位", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "0", Min = 0, Max = 31,
                       Description = "从第几位开始取；例如只看高字节时填 8")]
        public partial int StartBit { get; set; }

        /// <summary>每行几格</summary>
        [ScadaProperty(Key = "Columns", DisplayName = "每行格数", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "8", Min = 1, Max = 32,
                       Description = "8 = 一行八位（一个字节）；16 位可排两行八格")]
        public partial int Columns { get; set; }

        /// <summary>显示位序号</summary>
        [ScadaProperty(Key = "ShowIndex", DisplayName = "显示位号", Kind = ElementPropertyKind.Bool,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "True",
                       Description = "格子上方显示 bit 序号（从起始位算起）")]
        public partial bool ShowIndex { get; set; }

        /// <summary>显示 0/1</summary>
        [ScadaProperty(Key = "ShowValue", DisplayName = "显示值", Kind = ElementPropertyKind.Bool,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "True",
                       Description = "格子里显示 0 / 1；关掉只靠底色判断（远看更清楚）")]
        public partial bool ShowValue { get; set; }

        /// <summary>显示自定义标签</summary>
        [ScadaProperty(Key = "ShowLabels", DisplayName = "显示标签", Kind = ElementPropertyKind.Bool,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "False",
                       Description = "打开后在格子下方显示「标签」里对应的名字（I0.0、急停…）")]
        public partial bool ShowLabels { get; set; }

        /// <summary>标签清单（逗号分隔，按显示次序对号入座）</summary>
        [ScadaProperty(Key = "Labels", DisplayName = "标签", Kind = ElementPropertyKind.Text,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "", IsBindable = false,
                       Description = "逗号分隔，按位序对应：I0.0,I0.1,I0.2…；少于位数时空着，多了忽略")]
        public partial string? Labels { get; set; }

        /// <summary>位为 1 时的格子底色</summary>
        [ScadaProperty(Key = "OnColor", DisplayName = "置位色", Kind = ElementPropertyKind.Color,
                       Group = ScadaPropertyGroups.State, DefaultValue = "#FF34C759",
                       Description = "位为 1 的格子底色")]
        public partial Brush? OnColor { get; set; }

        /// <summary>位为 0 时的格子底色</summary>
        [ScadaProperty(Key = "OffColor", DisplayName = "复位色", Kind = ElementPropertyKind.Color,
                       Group = ScadaPropertyGroups.State, DefaultValue = "#FF2A3441",
                       Description = "位为 0 的格子底色")]
        public partial Brush? OffColor { get; set; }

        /// <summary>值文字颜色（叠在置位/复位底色上，建议浅色）</summary>
        [ScadaProperty(Key = "ValueForeground", DisplayName = "值文字色", Kind = ElementPropertyKind.Color,
                       Group = ScadaPropertyGroups.Text, DefaultValue = "#FFFFFFFF",
                       Description = "0/1 与位号的颜色；要能在两种格子底色上都看得清")]
        public partial Brush? ValueForeground { get; set; }

        private UniformGrid? _cells;
        private TextBlock? _title;
        private readonly List<CellVisual> _cellVisuals = new();

        static BitTableElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(BitTableElement), new FrameworkPropertyMetadata(typeof(BitTableElement)));
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            _cells = GetTemplateChild(PartCells) as UniformGrid;
            _title = GetTemplateChild(PartTitle) as TextBlock;
            Rebuild();
        }

        protected override void OnElementRefreshed()
        {
            base.OnElementRefreshed();
            Rebuild();
        }

        partial void OnValuePropertyChanged() => UpdateValues();
        partial void OnBitCountPropertyChanged() => Rebuild();
        partial void OnStartBitPropertyChanged() => Rebuild();
        partial void OnColumnsPropertyChanged() => Rebuild();
        partial void OnShowIndexPropertyChanged() => Rebuild();
        partial void OnShowValuePropertyChanged() => Rebuild();
        partial void OnShowLabelsPropertyChanged() => Rebuild();
        partial void OnLabelsPropertyChanged() => Rebuild();

        /// <summary>第 <paramref name="offset"/> 个格子当前是 0 还是 1（只读，供断言与探针）</summary>
        public bool GetBit(int offset)
        {
            long word = (long)Math.Truncate(Value);
            int bit = StartBit + offset;
            if (bit < 0 || bit > 63)
                return false;

            return ((ulong)word >> bit & 1UL) != 0UL;
        }

        /// <summary>当前显示的格数（只读，供断言）</summary>
        public int CellCount => _cellVisuals.Count;

        private void Rebuild()
        {
            if (_title != null)
            {
                _title.Text = Text ?? string.Empty;
                _title.Visibility = string.IsNullOrWhiteSpace(Text) ? Visibility.Collapsed : Visibility.Visible;
            }

            if (_cells == null)
                return;

            _cells.Children.Clear();
            _cellVisuals.Clear();
            _cells.Columns = Math.Max(1, Columns);

            int count = Math.Clamp(BitCount, 1, 32);
            var labels = SplitLabels(Labels);

            for (int i = 0; i < count; i++)
            {
                var visual = CreateCell(i, i < labels.Count ? labels[i] : null);
                _cellVisuals.Add(visual);
                _cells.Children.Add(visual.Root);
            }

            UpdateValues();
        }

        private CellVisual CreateCell(int offset, string? label)
        {
            var root = new Border
            {
                Margin = new Thickness(2),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                SnapsToDevicePixels = true,
            };

            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

            TextBlock? index = null;
            if (ShowIndex)
            {
                index = new TextBlock
                {
                    Text = (StartBit + offset).ToString(),
                    FontSize = 9,
                    Opacity = 0.75,
                    HorizontalAlignment = HorizontalAlignment.Center,
                };
                BindForeground(index);
                stack.Children.Add(index);
            }

            TextBlock? value = null;
            if (ShowValue)
            {
                value = new TextBlock
                {
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                };
                BindForeground(value);
                stack.Children.Add(value);
            }

            TextBlock? labelText = null;
            if (ShowLabels && !string.IsNullOrWhiteSpace(label))
            {
                labelText = new TextBlock
                {
                    Text = label,
                    FontSize = 9,
                    Opacity = 0.8,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                BindForeground(labelText);
                stack.Children.Add(labelText);
            }

            root.Child = stack;
            BindAppearance(root);

            return new CellVisual(root, value);
        }

        /// <summary>
        /// 外观绑定到本图元（而不是建格子时读一次值）：属性面板改边框色/值文字色能立刻生效。
        /// 用 AncestorType 而不是 TemplatedParent——代码建的格子不在模板的 TemplatedParent 链上。
        /// </summary>
        private void BindAppearance(Border border)
        {
            border.SetBinding(Border.BorderBrushProperty, Self(nameof(Stroke)));
            border.SetBinding(Border.BorderThicknessProperty, Self(nameof(StrokeThickness)));
        }

        private void BindForeground(TextBlock text)
            => text.SetBinding(TextBlock.ForegroundProperty, Self(nameof(ValueForeground)));

        private Binding Self(string path) => new(path)
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(BitTableElement), 1),
        };

        private void UpdateValues()
        {
            for (int i = 0; i < _cellVisuals.Count; i++)
            {
                var visual = _cellVisuals[i];
                bool on = GetBit(i);
                visual.Root.Background = (on ? OnColor : OffColor) ?? Brushes.Transparent;
                if (visual.Value != null)
                    visual.Value.Text = on ? "1" : "0";
            }
        }

        private static List<string> SplitLabels(string? text)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
                return result;

            foreach (var part in text.Split(new[] { ',', '，', ';' }, StringSplitOptions.RemoveEmptyEntries))
                result.Add(part.Trim());

            return result;
        }

        private sealed record CellVisual(Border Root, TextBlock? Value);
    }
}
