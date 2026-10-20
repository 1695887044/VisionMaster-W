using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 文本输入域图元（TypeKey = <c>Hmi.TextField</c>）：一行字符串的显示与就地编辑。
    ///
    /// <b>为什么要有它（数值域不是已经能输入了吗）</b>
    /// ---------
    /// 数值域（<see cref="IOFieldElement"/>）只收数字——它要解析、要换算、要判上下限。
    /// 现场还有一整类"字符串"要人填：批号、操作备注、配方名、工单号。硬塞进数值域只会得到
    /// "「B20261020-01」不是有效数值"；用文本图元加脚本又绕过了权限与写通道。本图元就是那一格。
    ///
    /// <b>与数值域完全一致的写值口径</b>
    /// ---------
    /// 控件不自己改 <see cref="Value"/>：提交时把 trim 后的文本交给 <see cref="IScadaValueWriter"/>，
    /// 成功才退出编辑态并发 <see cref="ScadaEventType.InputCompleted"/>；失败留在编辑态、
    /// 把原因放进 <see cref="EditError"/>——操作员敲的字还在框里，改一下就能重提。
    ///
    /// <b>空串是合法的</b>
    /// ---------
    /// 清空文本框再提交 = 把变量清成空串（"把批号擦掉"是现场真实需求），
    /// 与数值域"清空 = 放弃输入"刻意不同。放弃输入请按 Esc。
    ///
    /// <b>长度校验为什么在提交时做</b>
    /// ---------
    /// <see cref="MaxLength"/> 是数据约束，不是编辑器的输入法限制：粘贴、脚本灌值、旧 .vms
    /// 都可能绕过 TextBox 的 MaxLength。提交时按 trim 后的长度再判一次，才是"写进变量的那一份"
    /// 真正守住的长度。超长不写、不截断——静默截掉操作员敲的字比拒绝更危险。
    /// </summary>
    [TemplatePart(Name = PartEditor, Type = typeof(TextBox))]
    [TemplatePart(Name = PartPlaceholder, Type = typeof(TextBlock))]
    [TemplatePart(Name = PartFrame, Type = typeof(Border))]
    public partial class TextFieldElement : ScadaElementBase
    {
        /// <summary>模板部件名：就地编辑用的输入框</summary>
        public const string PartEditor = "PART_Editor";

        /// <summary>模板部件名：值为空时显示占位提示的文本块</summary>
        public const string PartPlaceholder = "PART_Placeholder";

        /// <summary>模板部件名：外框（提交失败时描红）</summary>
        public const string PartFrame = "PART_Frame";

        /// <summary>模式取值：只读（只显示变量里的文本，点不动）</summary>
        public const string ModeOutput = "Output";

        /// <summary>模式取值：读写（点一下就地编辑并写回）</summary>
        public const string ModeInputOutput = "InputOutput";

        /// <summary><see cref="Value"/> 在描述符里的属性键（写回时按它找绑定）</summary>
        private const string ValueKey = "Value";

        /// <summary>长度下限：0 长度的框连一个字都输不进去，没有意义</summary>
        public const int MinMaxLength = 1;

        /// <summary>长度上限：再长就该用多行备注控件，而不是一行输入域</summary>
        public const int MaxMaxLength = 512;

        /// <summary>出错时外框的描边色（报警语义色不跟主题走，与数值域同一支红）</summary>
        private static readonly Brush ErrorBrush = ScadaBrushes.Frozen("#FFD32F2F");

        // 声明次序 = 属性面板里的行序：数据 → 文字 → 常规。

        /// <summary>当前文本（绑到工程变量上）</summary>
        [ScadaProperty(Key = "Value", DisplayName = "文本", Kind = ElementPropertyKind.Text,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "", IsBindable = true,
                       Description = "运行态把字符串变量绑到这里；框里显示的就是它。" +
                                     "提交时把 trim 后的文本写回变量，空串表示清除文本（合法值）")]
        public partial string? Value { get; set; }

        /// <summary>值为空时显示的占位提示</summary>
        [ScadaProperty(Key = "Placeholder", DisplayName = "占位提示", Kind = ElementPropertyKind.Text,
                       Group = ScadaPropertyGroups.Text, DefaultValue = "请输入…",
                       Description = "值为空且不在编辑态时显示的一行灰字；进入编辑态或已有内容时收起")]
        public partial string? Placeholder { get; set; }

        /// <summary>允许的最大字符数（按 trim 后的长度判）</summary>
        [ScadaProperty(Key = "MaxLength", DisplayName = "最大长度", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.General, DefaultValue = "64", Min = 1, Max = 512,
                       Description = "提交时按 trim 后的字符数校验；超长不写入、不截断。" +
                                     "TextBox 自带的 MaxLength 只是输入辅助，粘贴与脚本灌值都可能绕过它，所以这里再判一次")]
        public partial int MaxLength { get; set; }

        /// <summary>模式：<see cref="ModeOutput"/> 只读 / <see cref="ModeInputOutput"/> 读写</summary>
        [ScadaProperty(Key = "Mode", DisplayName = "模式", Kind = ElementPropertyKind.Choice,
                       Group = ScadaPropertyGroups.General, DefaultValue = "InputOutput",
                       Choices = new[] { "Output", "InputOutput" }, AffectsRender = false,
                       Description = "Output = 只读显示，点击不进入编辑；InputOutput = 可编辑。" +
                                     "可编辑还需绑了变量、有写通道、权限够（见属性面板「交互」）")]
        public partial string? Mode { get; set; }

        /// <summary>是否正处于就地编辑态（运行态内部状态，不落描述符）</summary>
        public static readonly DependencyProperty IsEditingProperty = DependencyProperty.Register(
            nameof(IsEditing), typeof(bool), typeof(TextFieldElement),
            new FrameworkPropertyMetadata(false, OnIsEditingChanged));

        /// <summary>编辑框里的文本（运行态内部状态；双向绑到模板里的输入框）</summary>
        public static readonly DependencyProperty EditTextProperty = DependencyProperty.Register(
            nameof(EditText), typeof(string), typeof(TextFieldElement),
            new FrameworkPropertyMetadata(string.Empty));

        /// <summary>上一次提交失败的原因（运行态内部状态；非空时外框描红）</summary>
        public static readonly DependencyProperty EditErrorProperty = DependencyProperty.Register(
            nameof(EditError), typeof(string), typeof(TextFieldElement),
            new FrameworkPropertyMetadata(string.Empty, OnEditErrorChanged));

        private static readonly DependencyPropertyKey IsEditablePropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(IsEditable), typeof(bool), typeof(TextFieldElement),
            new PropertyMetadata(false));

        /// <summary>此刻能不能编辑（只读；四道门同数值域）</summary>
        public static readonly DependencyProperty IsEditableProperty = IsEditablePropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey IsReadOnlyPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(IsReadOnly), typeof(bool), typeof(TextFieldElement),
            new PropertyMetadata(true));

        /// <summary>是否只读（只读；= !<see cref="IsEditable"/>，给模板/绑定直接消费）</summary>
        public static readonly DependencyProperty IsReadOnlyProperty = IsReadOnlyPropertyKey.DependencyProperty;

        static TextFieldElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(TextFieldElement),
                new FrameworkPropertyMetadata(typeof(TextFieldElement)));
        }

        public TextFieldElement() => UpdateEditable();

        /// <summary>是否正处于就地编辑态（见 <see cref="IsEditingProperty"/>）</summary>
        public bool IsEditing
        {
            get => (bool)GetValue(IsEditingProperty);
            set => SetValue(IsEditingProperty, value);
        }

        /// <summary>编辑框里的文本（见 <see cref="EditTextProperty"/>）</summary>
        public string? EditText
        {
            get => (string?)GetValue(EditTextProperty);
            set => SetValue(EditTextProperty, value);
        }

        /// <summary>上一次提交失败的原因；为空串表示没有未处理的错误（见 <see cref="EditErrorProperty"/>）</summary>
        public string? EditError
        {
            get => (string?)GetValue(EditErrorProperty);
            set => SetValue(EditErrorProperty, value ?? string.Empty);
        }

        /// <summary>此刻能不能编辑（只读，见 <see cref="IsEditableProperty"/>）</summary>
        public bool IsEditable => (bool)GetValue(IsEditableProperty);

        /// <summary>是否只读（只读，见 <see cref="IsReadOnlyProperty"/>）</summary>
        public bool IsReadOnly => (bool)GetValue(IsReadOnlyProperty);

        /// <summary>实际生效的最大长度（= <see cref="MaxLength"/> 收敛到 1~512）</summary>
        public int EffectiveMaxLength => Math.Clamp(MaxLength, MinMaxLength, MaxMaxLength);

        private bool IsOutputMode => string.Equals(Mode, ModeOutput, StringComparison.OrdinalIgnoreCase);

        /// <summary>能不能改：模式允许写、有写通道、Value 真绑了变量且没停用、当前角色够格</summary>
        private bool CanOperate
            => !IsOutputMode
               && RuntimeContext?.Writer is not null
               && Element?.FindBinding(ValueKey) is { IsEnabled: true }
               && (Element?.RequiredRole is not { } required
                   || RuntimeContext?.AccessPolicy.CanOperate(required, out _) == true);

        /// <summary>
        /// 进入就地编辑态：把当前文本填进编辑框、把焦点交给它。
        /// 不可写时是空操作——"只读框点了不该弹出键盘"。
        /// </summary>
        public void BeginEdit()
        {
            if (!IsEditable || IsEditing)
                return;

            EditText = Value ?? string.Empty;
            EditError = string.Empty;
            IsEditing = true;

            // 编辑框刚从 Collapsed 翻成 Visible，还没排过版；先强制布局再要焦点，
            // 否则 Focus() 会因为"还不可见"返回 false，字敲不进去（数值域踩过同一个坑）。
            UpdateLayout();

            if (GetTemplateChild(PartEditor) is TextBox editor)
            {
                editor.Focus();
                editor.SelectAll();
            }
        }

        /// <summary>
        /// 提交编辑：trim → 长度校验 → 写回变量。
        ///
        /// 任何一步失败都<b>留在编辑态</b>并置 <see cref="EditError"/>：
        /// 操作员敲的字还在框里，改一下就能重提；若失败就退出，那串字会一起消失。
        /// </summary>
        public void CommitEdit()
        {
            if (!IsEditing)
                return;

            var text = EditText?.Trim() ?? string.Empty;
            int max = EffectiveMaxLength;

            if (text.Length > max)
            {
                EditError = $"文本长度 {text.Length} 超过上限 {max}，未写入";
                return;
            }

            if (RuntimeContext?.Writer is not { } writer)
            {
                EditError = "当前没有写通道，无法写入变量";
                return;
            }

            if (Element is not { } element)
            {
                EditError = "图元还没挂到画面上，无法写入变量";
                return;
            }

            // 写前再查一次权限：编辑框可能是在有权限时打开的，之后操作员登出/换了角色。
            if (!CanOperate)
            {
                EditError = "当前角色没有操作权限，未写入";
                return;
            }

            if (!writer.TryWriteText(element, ValueKey, text, out var error))
            {
                EditError = string.IsNullOrWhiteSpace(error) ? "写入失败" : error;
                return;
            }

            IsEditing = false;
            EditError = string.Empty;

            // 成功路径的最后一行才发声：失败分支都已 return，结构上不可能"失败也发一次"；
            // 发在写回之后，配在它上面的动作读到的变量已经是新值。
            RaiseScadaEvent(ScadaEventType.InputCompleted);
        }

        /// <summary>放弃编辑：退出编辑态、清掉错误，<see cref="Value"/> 一点没动过</summary>
        public void CancelEdit()
        {
            if (!IsEditing)
                return;

            IsEditing = false;
            EditError = string.Empty;
        }

        protected override void OnRuntimeContextChanged(ScadaRuntimeContext? oldContext, ScadaRuntimeContext? newContext)
            => UpdateEditable();

        protected override void OnElementRefreshed() => UpdateEditable();

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            if (_editor is not null)
            {
                _editor.KeyDown -= OnEditorKeyDown;
                _editor.LostFocus -= OnEditorLostFocus;
            }

            _editor = GetTemplateChild(PartEditor) as TextBox;

            if (_editor is not null)
            {
                _editor.KeyDown += OnEditorKeyDown;
                _editor.LostFocus += OnEditorLostFocus;
            }

            UpdateVisualState();
        }

        private TextBox? _editor;

        private void OnEditorKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Enter:
                    CommitEdit();
                    e.Handled = true;
                    break;
                case Key.Escape:
                    CancelEdit();
                    e.Handled = true;
                    break;
            }
        }

        // 点别处即提交：现场操作员不会记得按回车，失焦提交才是默认预期。
        private void OnEditorLostFocus(object sender, RoutedEventArgs e) => CommitEdit();

        protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseLeftButtonDown(e);

            // 隧道事件：编辑态下框里是 TextBox，它会吃掉冒泡的左键按下。
            if (!e.Handled && IsEditable && !IsEditing)
            {
                BeginEdit();
                e.Handled = true;
            }
        }

        partial void OnValuePropertyChanged() => UpdateVisualState();
        partial void OnPlaceholderPropertyChanged() => UpdateVisualState();
        partial void OnModePropertyChanged() => UpdateEditable();

        private static void OnIsEditingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((TextFieldElement)d).UpdateVisualState();

        private static void OnEditErrorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((TextFieldElement)d).UpdateVisualState();

        private void UpdateEditable()
        {
            bool editable = CanOperate;

            SetValue(IsEditablePropertyKey, editable);
            SetValue(IsReadOnlyPropertyKey, !editable);

            if (!editable)
                CancelEdit(); // 运行停了 / 绑定被摘了 / 登出了：正在编辑的框要收掉

            UpdateVisualState();
        }

        private void UpdateVisualState()
        {
            SetPartVisible(PartEditor, IsEditing);
            SetPartVisible(PartPlaceholder, !IsEditing && string.IsNullOrEmpty(Value));

            if (GetTemplateChild(PartFrame) is Border frame)
                frame.BorderBrush = string.IsNullOrEmpty(EditError) ? Stroke : ErrorBrush;
        }
    }
}
