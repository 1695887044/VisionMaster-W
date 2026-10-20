using System;
using System.Windows;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// KPI 看板图元（TypeKey = <c>Hmi.KpiBoard</c>）：把 2~4 组「数值 + 标签 + 单位」排成一块卡片。
    ///
    /// <b>它解决的是重复劳动，不是新能力</b>
    /// ---------
    /// 现场做一块 OEE/产量/良率/停机看板，过去要摆四个数值域、四个文本、再手调间距——
    /// 四组东西各改一次字号、各对齐一次，改一次版面要动十几处。本图元把四组数据收进一个控件，
    /// 模板只负责排版，四组的显示口径天然一致。
    ///
    /// <b>为什么不做成四个独立属性（Value1~4 / Label1~4 / Unit1~4）</b>
    /// ---------
    /// 每组数据在 .vms 里是三个字符串，做成数组/子对象会让属性面板多一层树，
    /// 绑定引擎与序列化也要为"嵌套属性"另开一条路。四组平铺属性最直白：
    /// 属性面板上就是十二行，每行都能单独绑变量（<see cref="Value1"/>~<see cref="Value4"/> 可绑），
    /// 标签与单位是设计期语言、不参与绑定。
    ///
    /// <b>Columns 的越界收敛</b>
    /// ---------
    /// 属性面板已经把列数限制在 2~4，但 .vms 是手工可改的文本。为了让模板拿到的一定是合法值，
    /// 本控件另给一个只读的 <see cref="EffectiveColumns"/>（2~4），模板应绑它而不是绑原始
    /// <see cref="Columns"/>——原始值仍按用户所写保留在模型里（不悄悄改用户配置）。
    ///
    /// 运行时的接法：把 <see cref="Value1"/>~<see cref="Value4"/> 分别绑到工程变量上，
    /// 模板里的四组文字就会跟着变量刷新。
    /// </summary>
    public partial class KpiBoardElement : ScadaElementBase
    {
        // 声明次序 = 属性面板里的行序（生成器按源码次序收集）：先四组数值，再四组标签，
        // 再四组单位，最后外观。同组内按 1~4 排列，与看板上的阅读顺序一致。

        /// <summary>第 1 格数值（绑变量）</summary>
        [ScadaProperty(Key = "Value1", DisplayName = "数值 1", Kind = ElementPropertyKind.Text,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "", IsBindable = true,
                       Description = "第 1 格的显示文本；运行态把工程变量绑到这里（变量是数字也按文本显示）")]
        public partial string? Value1 { get; set; }

        /// <summary>第 2 格数值（绑变量）</summary>
        [ScadaProperty(Key = "Value2", DisplayName = "数值 2", Kind = ElementPropertyKind.Text,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "", IsBindable = true,
                       Description = "第 2 格的显示文本；运行态把工程变量绑到这里")]
        public partial string? Value2 { get; set; }

        /// <summary>第 3 格数值（绑变量）</summary>
        [ScadaProperty(Key = "Value3", DisplayName = "数值 3", Kind = ElementPropertyKind.Text,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "", IsBindable = true,
                       Description = "第 3 格的显示文本；运行态把工程变量绑到这里")]
        public partial string? Value3 { get; set; }

        /// <summary>第 4 格数值（绑变量）</summary>
        [ScadaProperty(Key = "Value4", DisplayName = "数值 4", Kind = ElementPropertyKind.Text,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "", IsBindable = true,
                       Description = "第 4 格的显示文本；运行态把工程变量绑到这里")]
        public partial string? Value4 { get; set; }

        /// <summary>第 1 格标签</summary>
        [ScadaProperty(Key = "Label1", DisplayName = "标签 1", Kind = ElementPropertyKind.Text,
                       Group = ScadaPropertyGroups.Text, DefaultValue = "",
                       Description = "第 1 格的说明字（如「OEE」「产量」）；设计期语言，不参与变量绑定")]
        public partial string? Label1 { get; set; }

        /// <summary>第 2 格标签</summary>
        [ScadaProperty(Key = "Label2", DisplayName = "标签 2", Kind = ElementPropertyKind.Text,
                       Group = ScadaPropertyGroups.Text, DefaultValue = "",
                       Description = "第 2 格的说明字；设计期语言，不参与变量绑定")]
        public partial string? Label2 { get; set; }

        /// <summary>第 3 格标签</summary>
        [ScadaProperty(Key = "Label3", DisplayName = "标签 3", Kind = ElementPropertyKind.Text,
                       Group = ScadaPropertyGroups.Text, DefaultValue = "",
                       Description = "第 3 格的说明字；设计期语言，不参与变量绑定")]
        public partial string? Label3 { get; set; }

        /// <summary>第 4 格标签</summary>
        [ScadaProperty(Key = "Label4", DisplayName = "标签 4", Kind = ElementPropertyKind.Text,
                       Group = ScadaPropertyGroups.Text, DefaultValue = "",
                       Description = "第 4 格的说明字；设计期语言，不参与变量绑定")]
        public partial string? Label4 { get; set; }

        /// <summary>第 1 格单位</summary>
        [ScadaProperty(Key = "Unit1", DisplayName = "单位 1", Kind = ElementPropertyKind.Text,
                       Group = ScadaPropertyGroups.Text, DefaultValue = "",
                       Description = "第 1 格的单位（如「件/分」「%」）；留空则只显数值与标签")]
        public partial string? Unit1 { get; set; }

        /// <summary>第 2 格单位</summary>
        [ScadaProperty(Key = "Unit2", DisplayName = "单位 2", Kind = ElementPropertyKind.Text,
                       Group = ScadaPropertyGroups.Text, DefaultValue = "",
                       Description = "第 2 格的单位；留空则只显数值与标签")]
        public partial string? Unit2 { get; set; }

        /// <summary>第 3 格单位</summary>
        [ScadaProperty(Key = "Unit3", DisplayName = "单位 3", Kind = ElementPropertyKind.Text,
                       Group = ScadaPropertyGroups.Text, DefaultValue = "",
                       Description = "第 3 格的单位；留空则只显数值与标签")]
        public partial string? Unit3 { get; set; }

        /// <summary>第 4 格单位</summary>
        [ScadaProperty(Key = "Unit4", DisplayName = "单位 4", Kind = ElementPropertyKind.Text,
                       Group = ScadaPropertyGroups.Text, DefaultValue = "",
                       Description = "第 4 格的单位；留空则只显数值与标签")]
        public partial string? Unit4 { get; set; }

        /// <summary>列数（2 = 2×2，4 = 4×1）</summary>
        [ScadaProperty(Key = "Columns", DisplayName = "列数", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Appearance, DefaultValue = "2", Min = 2, Max = 4,
                       Description = "2 = 2×2 四宫格，3 = 3 列（第 4 格换行），4 = 4×1 一行四格；" +
                                     "越界值按 2~4 收敛（模板绑 EffectiveColumns）")]
        public partial int Columns { get; set; }

        private static readonly DependencyPropertyKey EffectiveColumnsPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(EffectiveColumns), typeof(int), typeof(KpiBoardElement), new PropertyMetadata(2));

        /// <summary>
        /// 收敛后的列数（2~4，只读）。
        ///
        /// 模板应把 <c>UniformGrid.Columns</c> 绑到这一位而不是原始 <see cref="Columns"/>：
        /// .vms 是手工可改的文本，原始值可能越界；这里只影响排版，不改用户写在模型里的配置。
        /// </summary>
        public static readonly DependencyProperty EffectiveColumnsProperty =
            EffectiveColumnsPropertyKey.DependencyProperty;

        static KpiBoardElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(KpiBoardElement),
                new FrameworkPropertyMetadata(typeof(KpiBoardElement)));
        }

        public KpiBoardElement()
        {
            // 构造即算第一帧：断言环境（不挂可视树、不跑布局）里也能读到收敛后的列数。
            UpdateColumns();
        }

        protected override void OnElementRefreshed() => UpdateColumns();

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            UpdateColumns();
        }

        /// <summary>收敛后的列数（只读，见 <see cref="EffectiveColumnsProperty"/>）</summary>
        public int EffectiveColumns => (int)GetValue(EffectiveColumnsProperty);

        partial void OnColumnsPropertyChanged() => UpdateColumns();

        private void UpdateColumns()
            => SetValue(EffectiveColumnsPropertyKey, Math.Clamp(Columns, 2, 4));
    }
}
