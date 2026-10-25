using System;
using System.Collections.Generic;
using System.Windows;
using VisionMaster.Scada;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 配方只读面板图元（TypeKey = <c>Hmi.RecipePanel</c>）：把"当前生效配方"整块显示出来。
    ///
    /// <b>它解决的是配置成本，不是新数据</b>
    /// ---------
    /// 配方数据本身已经由宿主投影成 6 条只读系统变量（<c>System.Recipe.*</c>）。
    /// 但现场要的是"一块牌子"：配方名、版本、状态、以及逐项参数。用现有图元拼一遍要摆
    /// 一个文本 + 一个数值域 + 一张数据表格，而数据表格是"按采行信号攒行"的记录控件，
    /// 用它显示配方参数属于误用（它等的是上升沿，不是一份静态清单）。
    /// 本图元一次把这几样画齐，且<b>不需要在设计期一条条绑变量</b>。
    ///
    /// <b>三条边界</b>
    /// ---------
    /// ① <b>只读</b>：不写任何变量，也没有编辑态。改配方只能走主软件的配方管理器，
    ///    切换只能走按钮上的「应用配方」动作。
    /// ② <b>变量名是硬约定</b>：直接读 <c>System.Recipe.*</c>，名字与格式化口径都收在
    ///    <see cref="ScadaRecipePanelData"/> 一处（Web 运行态也调它，两端不会各说各话）。
    /// ③ <b>设计态显示占位</b>：没有运行上下文时不显示 0/空配方，而是"配方数据未接入"，
    ///    与轴面板"未接入运动数据"同一口径（不把"不知道"伪装成一个看起来正常的值）。
    ///
    /// 刷新走全画面统一节拍（与数据表格同一手法）：每拍现解析变量、现格式化，
    /// 只有文本真的变了才写依赖属性——配方本身很少变，但"偏离数"会随参数被改而变。
    /// </summary>
    public partial class RecipePanelElement : ScadaElementBase
    {
        // 声明次序 = 属性面板行序：标题 → 数据开关 → 行数。

        /// <summary>面板标题（如「当前配方」）</summary>
        [ScadaProperty(Key = "Title", DisplayName = "标题", Kind = ElementPropertyKind.Text,
                       Group = ScadaPropertyGroups.Text, DefaultValue = "当前配方",
                       Description = "面板左上角的一行字；留空则整行收起，只显示配方名与状态")]
        public partial string? Title { get; set; }

        /// <summary>是否显示逐项参数明细</summary>
        [ScadaProperty(Key = "ShowDetails", DisplayName = "显示参数明细", Kind = ElementPropertyKind.Bool,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "True",
                       Description = "打开时在状态行下面列出配方里的每一项参数；" +
                                     "关掉只留「配方名 · 版本 + 状态」，适合只想要一块小铭牌的场合")]
        public partial bool ShowDetails { get; set; }

        /// <summary>参数明细最多显示多少行</summary>
        [ScadaProperty(Key = "MaxRows", DisplayName = "最多显示行数", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "12",
                       Min = ScadaRecipePanelData.MinRows, Max = ScadaRecipePanelData.MaxRowsLimit,
                       Description = "超出的部分折成一行「… 共 N 项」；配方参数通常十几项，12 行够看")]
        public partial int MaxRows { get; set; }

        private static readonly DependencyPropertyKey HeaderTextPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(HeaderText), typeof(string), typeof(RecipePanelElement), new PropertyMetadata(string.Empty));

        /// <summary>主行文案：配方名 · 版本（无配方时是「未应用配方」/「配方已丢失」）</summary>
        public static readonly DependencyProperty HeaderTextProperty = HeaderTextPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey StatusTextPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(StatusText), typeof(string), typeof(RecipePanelElement), new PropertyMetadata(string.Empty));

        /// <summary>状态文案（已生效 / 偏离 N / 未应用 / 未启用 / 配方悬空）</summary>
        public static readonly DependencyProperty StatusTextProperty = StatusTextPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey StatusKeyPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(StatusKey), typeof(string), typeof(RecipePanelElement),
            new PropertyMetadata(ScadaRecipePanelData.StatusKeyMuted));

        /// <summary>状态键（模板据此挑色）：ok / warn / danger / muted</summary>
        public static readonly DependencyProperty StatusKeyProperty = StatusKeyPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey RowsPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(Rows), typeof(IReadOnlyList<ScadaRecipePanelLine>), typeof(RecipePanelElement),
            new PropertyMetadata(Array.Empty<ScadaRecipePanelLine>()));

        /// <summary>参数明细行（已按 <see cref="MaxRows"/> 截断）</summary>
        public static readonly DependencyProperty RowsProperty = RowsPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey HasRowsPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(HasRows), typeof(bool), typeof(RecipePanelElement), new PropertyMetadata(false));

        /// <summary>有没有明细行可显示（空态提示据此显隐）</summary>
        public static readonly DependencyProperty HasRowsProperty = HasRowsPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey EmptyHintPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(EmptyHint), typeof(string), typeof(RecipePanelElement), new PropertyMetadata(string.Empty));

        /// <summary>空态提示（没有配方 / 没有明细 / 数据未接入）</summary>
        public static readonly DependencyProperty EmptyHintProperty = EmptyHintPropertyKey.DependencyProperty;

        static RecipePanelElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(RecipePanelElement),
                new FrameworkPropertyMetadata(typeof(RecipePanelElement)));
        }

        public string HeaderText => (string)GetValue(HeaderTextProperty);

        public string StatusText => (string)GetValue(StatusTextProperty);

        public string StatusKey => (string)GetValue(StatusKeyProperty);

        public IReadOnlyList<ScadaRecipePanelLine> Rows => (IReadOnlyList<ScadaRecipePanelLine>)GetValue(RowsProperty);

        public bool HasRows => (bool)GetValue(HasRowsProperty);

        public string EmptyHint => (string)GetValue(EmptyHintProperty);

        /// <summary>
        /// 运行上下文换人：挂上新的节拍并立刻刷一次，退掉旧的。
        /// 与数据表格同一理由（订阅跟着上下文走，不跟着 Loaded 走——断言宿主没有 Loaded）。
        /// </summary>
        protected override void OnRuntimeContextChanged(ScadaRuntimeContext? oldContext, ScadaRuntimeContext? newContext)
        {
            if (oldContext != null)
                oldContext.Beat.Beat -= OnBeat;

            if (newContext != null)
                newContext.Beat.Beat += OnBeat;

            Refresh();
        }

        protected override void OnElementRefreshed() => Refresh();

        partial void OnShowDetailsPropertyChanged() => Refresh();
        partial void OnMaxRowsPropertyChanged() => Refresh();
        partial void OnTitlePropertyChanged() => Refresh();

        private void OnBeat(TimeSpan elapsed) => Refresh();

        /// <summary>
        /// 从当前运行上下文读一次配方并重画。
        ///
        /// <b>public 是给断言用的</b>：读值 → 组文案 → 截断全在一次里；
        /// 让断言直接推一拍，比在断言宿主里养一个真定时器可靠得多（与数据表格的 <c>Sample</c> 同一口径）。
        /// </summary>
        public void Refresh()
        {
            var snapshot = ScadaRecipePanelData.Read(RuntimeContext?.Values, ShowDetails, MaxRows);

            Apply(snapshot.Header, snapshot.Status, snapshot.StatusKey, snapshot.Rows, snapshot.Hint);
        }

        private void Apply(string header, string status, string key,
                           IReadOnlyList<ScadaRecipePanelLine> rows, string hint)
        {
            if (!string.Equals(HeaderText, header, StringComparison.Ordinal))
                SetValue(HeaderTextPropertyKey, header);

            if (!string.Equals(StatusText, status, StringComparison.Ordinal))
                SetValue(StatusTextPropertyKey, status);

            if (!string.Equals(StatusKey, key, StringComparison.Ordinal))
                SetValue(StatusKeyPropertyKey, key);

            if (!RowsEqual(Rows, rows))
            {
                SetValue(RowsPropertyKey, rows);
                SetValue(HasRowsPropertyKey, rows.Count > 0);
            }

            if (!string.Equals(EmptyHint, hint, StringComparison.Ordinal))
                SetValue(EmptyHintPropertyKey, hint);
        }

        private static bool RowsEqual(IReadOnlyList<ScadaRecipePanelLine> left, IReadOnlyList<ScadaRecipePanelLine> right)
        {
            if (left.Count != right.Count) return false;

            for (int i = 0; i < left.Count; i++)
            {
                if (!string.Equals(left[i].Text, right[i].Text, StringComparison.Ordinal)
                    || left[i].IsTruncated != right[i].IsTruncated)
                    return false;
            }

            return true;
        }
    }
}
