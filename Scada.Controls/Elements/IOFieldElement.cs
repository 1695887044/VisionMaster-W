using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 数值域图元（TypeKey = <c>Hmi.IOField</c>）：一个带边框的方框，框里一行
    /// 「说明字 + 数值 + 单位」——工业画面上出现频率最高的那一小块（温度 23.5 ℃、计数 1,204 pcs）。
    ///
    /// <b>和文本图元、棒图的边界在哪</b>
    /// ---------
    /// 文本图元（<see cref="TextElement"/>）显示的是"已经定型的字符串"，它不认识数值；
    /// 棒图（<see cref="ProgressBarElement"/>）把一个数值摊成"量程里的长度"，读者一眼看的是比例。
    /// 本图元填的是中间那一格：<b>读者要的是确切的数</b>，且这个数要按固定小数位显示、
    /// 要跟着单位、要有个框把它和背景分开。所以它保留棒图那套"值 + 格式串"，
    /// 丢掉量程与填充，换成"说明字 + 单位"这两段文字。
    ///
    /// <b>为什么值要算成只读依赖属性 <see cref="ValueText"/></b>
    /// ---------
    /// 与棒图把长度算成 <see cref="ProgressBarElement.FillLength"/> 同一套路子：
    /// "数值 → 显示串"这层换算只写一遍（含格式串写错时的兜底），模板只负责把结果贴到 TextBlock 上。
    /// 好处是断言能直接读这个串（"值 23.5 / 格式 0.0 / 单位 ℃ → 「23.5 ℃」"），不必去数像素。
    ///
    /// <b>为什么单位并进同一个串，而不是再放一个 TextBlock</b>
    /// ---------
    /// 分开摆就要处理"单位为空时那块 TextBlock 还占着宽度"的收尾，而两段字之间到底留几个像素
    /// 又会变成第二处可配项。并进 <see cref="ValueText"/> 之后，"单位留空 = 只显数值"是自然结果，
    /// 与时钟图元把标签留空就塌掉整行是同一个口径。
    ///
    /// 运行时的接法（S6）：把 <see cref="Value"/> 绑到工程变量上，框里的数就跟着变量走。
    /// </summary>
    public class IOFieldElement : ScadaElementBase
    {
        /// <summary>当前数值（绑到工程变量上）</summary>
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            nameof(Value), typeof(double), typeof(IOFieldElement),
            new FrameworkPropertyMetadata(
                0d, FrameworkPropertyMetadataOptions.AffectsRender, OnFieldInputChanged));

        /// <summary>数值的显示格式（标准 .NET 数字格式串，如 <c>0.0</c> / <c>F2</c> / <c>#,##0</c>）</summary>
        public static readonly DependencyProperty ValueFormatProperty = DependencyProperty.Register(
            nameof(ValueFormat), typeof(string), typeof(IOFieldElement),
            new FrameworkPropertyMetadata(
                "0.##", FrameworkPropertyMetadataOptions.AffectsRender, OnFieldInputChanged));

        /// <summary>单位（跟在数值后面的一小段字，如 ℃ / mm / pcs；留空则只显数值）</summary>
        public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
            nameof(Unit), typeof(string), typeof(IOFieldElement),
            new FrameworkPropertyMetadata(
                string.Empty, FrameworkPropertyMetadataOptions.AffectsRender, OnFieldInputChanged));

        private static readonly DependencyPropertyKey ValueTextPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(ValueText), typeof(string), typeof(IOFieldElement),
            new PropertyMetadata(string.Empty));

        /// <summary>框里显示的完整数值串（只读；= 数值按格式串格式化 + 单位）</summary>
        public static readonly DependencyProperty ValueTextProperty = ValueTextPropertyKey.DependencyProperty;

        static IOFieldElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(IOFieldElement),
                new FrameworkPropertyMetadata(typeof(IOFieldElement)));
        }

        public IOFieldElement()
        {
            // 先把第一帧算出来。三个输入的依赖属性默认值（0 / "0.##" / ""）与描述符声明的默认值完全一致，
            // 于是 ApplyTargetProperty 发现"值没变"会跳过 SetValue，那三个变更回调一个都不会响——
            // 不在这里算一次，单独 new 出来的数值域框里就是空的（时钟图元踩过同一个坑）。
            UpdateText();
        }

        /// <summary>当前数值（见 <see cref="ValueProperty"/>）</summary>
        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        /// <summary>数值显示格式（见 <see cref="ValueFormatProperty"/>）</summary>
        public string? ValueFormat
        {
            get => (string?)GetValue(ValueFormatProperty);
            set => SetValue(ValueFormatProperty, value);
        }

        /// <summary>单位（见 <see cref="UnitProperty"/>）</summary>
        public string? Unit
        {
            get => (string?)GetValue(UnitProperty);
            set => SetValue(UnitProperty, value);
        }

        /// <summary>框里显示的完整数值串（只读；见 <see cref="ValueTextProperty"/>）</summary>
        public string? ValueText => (string?)GetValue(ValueTextProperty);

        // 收尾钩子：模型刷新时三个输入多半"没变"（默认值就是那三个数），变更回调不会响，
        // 所以刷新末尾必须再算一次，保证"模型 → 控件"这条路每次都把显示串对齐。
        protected override void OnElementRefreshed() => UpdateText();

        private static void OnFieldInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((IOFieldElement)d).UpdateText();

        private void UpdateText()
        {
            var text = FormatValue();
            var unit = Unit;

            if (!string.IsNullOrEmpty(unit))
                text = text + " " + unit;

            SetValue(ValueTextPropertyKey, text);
        }

        private string FormatValue()
        {
            // 用不变文化：.vms 是跨机器交换的，显示串也不该跟着系统区域设置变
            // （德语系统上小数点会变成逗号，同一份方案在两地显示不一样）。
            var format = string.IsNullOrWhiteSpace(ValueFormat) ? null : ValueFormat;

            try
            {
                return Value.ToString(format, CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
                // 格式串写错（如 "0.0.0"）不该让整页渲染中断，退回通用格式
                return Value.ToString(CultureInfo.InvariantCulture);
            }
        }
    }
}
