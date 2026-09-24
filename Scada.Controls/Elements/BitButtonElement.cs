using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 位按钮图元（TypeKey = <c>Hmi.BitButton</c>）：把"一个开关量"接到操作员手指上的控件
    /// （启停、手/自动切换、复位、点动）。
    ///
    /// <b>它和「按钮」（<c>Hmi.Button</c>）的边界在哪</b>
    /// ---------
    /// 按钮是<b>动作入口</b>：点下去干什么由属性面板的「事件」里配的动作表决定（记日志 / 写变量 / 切画面），
    /// 它自己不认任何变量。位按钮是<b>变量的化身</b>：它绑一根开关量，点击直接对这根量做位操作
    /// （置位 / 复位 / 取反 / 按下ON / 按下OFF），同时按这根量的值显示两种状态。
    /// 手册把两者分成两个控件（7.3.3.5 按钮 / 7.3.3.7 位按钮），这里保持同样的切分：
    /// 硬把位逻辑塞进按钮，会让"只想记一条日志"的按钮也背上一个必须绑的变量。
    ///
    /// <b>为什么状态外观要算成只读依赖属性</b>
    /// ---------
    /// 与指示灯（<see cref="IndicatorElement"/>）同一套路子：外观不由基类的 Fill / Foreground 决定，
    /// 而是由一组输入推导——状态 <see cref="IsOn"/>（绑变量）+ 输出反向 <see cref="OutputInvert"/>
    /// + 六条状态外观（<see cref="OnText"/> / <see cref="OffText"/> / <see cref="OnFill"/> /
    /// <see cref="OffFill"/> / <see cref="OnForeground"/> / <see cref="OffForeground"/>）。
    /// 推导结果放在只读的 <see cref="DisplayText"/> / <see cref="DisplayFill"/> / <see cref="DisplayForeground"/>
    /// 上供模板绑定，模板里一条 DataTrigger 都不用写。
    ///
    /// 为什么不让模板自己用 Trigger 挑颜色：那等于把"状态怎么判"这件事复制进 XAML，
    /// 而"变量值与状态不匹配时按状态1显示"（手册 7.3.3.7）这类规则在 XAML 里表达不清楚，
    /// 在 C# 里却是一行。颜色是算出来的，就应该算在代码里，断言也能直接读这三个串。
    ///
    /// <b>点击为什么写在这里，而不是交给运行态的动作表</b>
    /// ---------
    /// 位操作要<b>先读回当前值</b>（取反）、要区分按下与释放（按下ON/按下OFF），
    /// 而"写变量"动作是无状态的单向写（给什么写什么），表达不了这两种语义。
    /// 数值域的输入框（<see cref="IOFieldElement.CommitEdit"/>）已经开了同一个先例：
    /// 图元在自己的输入回调里调 <see cref="IScadaValueWriter"/> 回写工程变量，
    /// 再由数据泵把值读回来显示——"画面上显示的"与"变量里存的"永远是同一个值。
    /// 写失败的原因（没绑变量 / 绑定停用 / 变量不存在 / 写不进去）由写通道自己记进运行日志，
    /// 图元不另造一套提示。
    ///
    /// <b>按下与释放的口径</b>
    /// ---------
    /// 与画布对「释放」的定义保持一致（见 <c>ScadaCanvas.OnPreviewMouseLeftButtonUp</c>）：
    /// 在按钮上按下、滑开再松手 = 这一下没成。区别只有一处——
    /// 按下ON / 按下OFF 这类<b>瞬动</b>模式在滑开时要把值复位，否则会留下一个"手已经移开、
    /// 量还按着"的假象（现场表现为电机一直转），那是不可接受的。
    /// </summary>
    public class BitButtonElement : ScadaElementBase
    {
        /// <summary>模式取值：置位（释放时写 1）</summary>
        public const string ModeSet = "置位";

        /// <summary>模式取值：复位（释放时写 0）</summary>
        public const string ModeReset = "复位";

        /// <summary>模式取值：取反（释放时把当前值取反写回）</summary>
        public const string ModeInvert = "取反";

        /// <summary>模式取值：按下ON（按下写 1、释放写 0）</summary>
        public const string ModePressOn = "按下ON";

        /// <summary>模式取值：按下OFF（按下写 0、释放写 1）</summary>
        public const string ModePressOff = "按下OFF";

        /// <summary>「读变量」在描述符里的属性键；回写时按它找绑定（读写同一根变量）</summary>
        private const string StateKey = "IsOn";

        // 默认色必须冻结（Freeze）：依赖属性默认值会被所有实例共享，
        // 未冻结的 Freezable 一旦被某个实例改到，其余实例会跟着变（见 ScadaBrushes 的类注释）。
        private static readonly Brush DefaultOnFill = ScadaBrushes.Frozen("#FF2D7DD2");
        private static readonly Brush DefaultOffFill = ScadaBrushes.Frozen("#FF4A5568");
        private static readonly Brush DefaultOnForeground = ScadaBrushes.Frozen("#FFFFFFFF");
        private static readonly Brush DefaultOffForeground = ScadaBrushes.Frozen("#FFFFFFFF");

        /// <summary>模式（见 <see cref="ModeSet"/> 等五个常量）</summary>
        public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(
            nameof(Mode), typeof(string), typeof(BitButtonElement),
            new FrameworkPropertyMetadata(ModePressOn));

        /// <summary>输出反向：勾选则对读取的值取反后再判状态（只影响显示，不影响写出去的值）</summary>
        public static readonly DependencyProperty OutputInvertProperty = DependencyProperty.Register(
            nameof(OutputInvert), typeof(bool), typeof(BitButtonElement),
            new FrameworkPropertyMetadata(
                false, FrameworkPropertyMetadataOptions.AffectsRender, OnDisplayInputChanged));

        /// <summary>状态（true = 状态1）；运行态把工程变量的布尔值绑到这里</summary>
        public static readonly DependencyProperty IsOnProperty = DependencyProperty.Register(
            nameof(IsOn), typeof(bool), typeof(BitButtonElement),
            new FrameworkPropertyMetadata(
                false, FrameworkPropertyMetadataOptions.AffectsRender, OnDisplayInputChanged));

        /// <summary>状态1（ON）时显示的文字；留空表示沿用「文字」</summary>
        public static readonly DependencyProperty OnTextProperty = DependencyProperty.Register(
            nameof(OnText), typeof(string), typeof(BitButtonElement),
            new FrameworkPropertyMetadata(
                string.Empty, FrameworkPropertyMetadataOptions.AffectsRender, OnDisplayInputChanged));

        /// <summary>状态0（OFF）时显示的文字；留空表示沿用「文字」</summary>
        public static readonly DependencyProperty OffTextProperty = DependencyProperty.Register(
            nameof(OffText), typeof(string), typeof(BitButtonElement),
            new FrameworkPropertyMetadata(
                string.Empty, FrameworkPropertyMetadataOptions.AffectsRender, OnDisplayInputChanged));

        /// <summary>状态1（ON）时的背景色</summary>
        public static readonly DependencyProperty OnFillProperty = DependencyProperty.Register(
            nameof(OnFill), typeof(Brush), typeof(BitButtonElement),
            new FrameworkPropertyMetadata(
                DefaultOnFill, FrameworkPropertyMetadataOptions.AffectsRender, OnDisplayInputChanged));

        /// <summary>状态0（OFF）时的背景色</summary>
        public static readonly DependencyProperty OffFillProperty = DependencyProperty.Register(
            nameof(OffFill), typeof(Brush), typeof(BitButtonElement),
            new FrameworkPropertyMetadata(
                DefaultOffFill, FrameworkPropertyMetadataOptions.AffectsRender, OnDisplayInputChanged));

        /// <summary>状态1（ON）时的文字颜色</summary>
        public static readonly DependencyProperty OnForegroundProperty = DependencyProperty.Register(
            nameof(OnForeground), typeof(Brush), typeof(BitButtonElement),
            new FrameworkPropertyMetadata(
                DefaultOnForeground, FrameworkPropertyMetadataOptions.AffectsRender, OnDisplayInputChanged));

        /// <summary>状态0（OFF）时的文字颜色</summary>
        public static readonly DependencyProperty OffForegroundProperty = DependencyProperty.Register(
            nameof(OffForeground), typeof(Brush), typeof(BitButtonElement),
            new FrameworkPropertyMetadata(
                DefaultOffForeground, FrameworkPropertyMetadataOptions.AffectsRender, OnDisplayInputChanged));

        private static readonly DependencyPropertyKey DisplayTextPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(DisplayText), typeof(string), typeof(BitButtonElement),
            new PropertyMetadata(string.Empty));

        /// <summary>当前该显示的文字（状态文本留空则回落到「文字」；只读）</summary>
        public static readonly DependencyProperty DisplayTextProperty = DisplayTextPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey DisplayFillPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(DisplayFill), typeof(Brush), typeof(BitButtonElement),
            new PropertyMetadata(Brushes.Transparent));

        /// <summary>当前该显示的背景色（= 状态 ? OnFill : OffFill；只读）</summary>
        public static readonly DependencyProperty DisplayFillProperty = DisplayFillPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey DisplayForegroundPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(DisplayForeground), typeof(Brush), typeof(BitButtonElement),
            new PropertyMetadata(Brushes.Transparent));

        /// <summary>当前该显示的文字颜色（= 状态 ? OnForeground : OffForeground；只读）</summary>
        public static readonly DependencyProperty DisplayForegroundProperty = DisplayForegroundPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey IsPressedPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(IsPressed), typeof(bool), typeof(BitButtonElement),
            new PropertyMetadata(false));

        /// <summary>本控件是否正被按住（只读，给模板做按下反馈；与 <see cref="IsOn"/> 无关）</summary>
        public static readonly DependencyProperty IsPressedProperty = IsPressedPropertyKey.DependencyProperty;

        /// <summary>这一下按下是否还"算数"（滑开即作废，见类注释"按下与释放的口径"）</summary>
        private bool _pressed;

        static BitButtonElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(BitButtonElement),
                new FrameworkPropertyMetadata(typeof(BitButtonElement)));

            // 「文字」是状态文本的回退值，它一变显示串就得重算。基类注册它时没挂变更回调，
            // 这里为本类型补一个。OverrideMetadata 只影响本类型（元数据是按类型查找的），
            // 基类与其它图元一行不动。
            TextProperty.OverrideMetadata(
                typeof(BitButtonElement),
                new FrameworkPropertyMetadata(
                    string.Empty, FrameworkPropertyMetadataOptions.AffectsRender, OnDisplayInputChanged));
        }

        /// <summary>
        /// 构造函数里必须把显示算一遍：一堆输入的依赖属性默认值与描述符声明的默认值完全一致，
        /// 于是 ApplyTargetProperty 发现"值没变"会跳过 SetValue，那些变更回调一个都不会响。
        /// </summary>
        public BitButtonElement() => UpdateDisplay();

        /// <summary>模式（见 <see cref="ModeProperty"/>）</summary>
        public string? Mode
        {
            get => (string?)GetValue(ModeProperty);
            set => SetValue(ModeProperty, value);
        }

        /// <summary>输出反向（见 <see cref="OutputInvertProperty"/>）</summary>
        public bool OutputInvert
        {
            get => (bool)GetValue(OutputInvertProperty);
            set => SetValue(OutputInvertProperty, value);
        }

        /// <summary>状态（见 <see cref="IsOnProperty"/>）</summary>
        public bool IsOn
        {
            get => (bool)GetValue(IsOnProperty);
            set => SetValue(IsOnProperty, value);
        }

        /// <summary>状态1文字（见 <see cref="OnTextProperty"/>）</summary>
        public string? OnText
        {
            get => (string?)GetValue(OnTextProperty);
            set => SetValue(OnTextProperty, value);
        }

        /// <summary>状态0文字（见 <see cref="OffTextProperty"/>）</summary>
        public string? OffText
        {
            get => (string?)GetValue(OffTextProperty);
            set => SetValue(OffTextProperty, value);
        }

        /// <summary>状态1背景色（见 <see cref="OnFillProperty"/>）</summary>
        public Brush? OnFill
        {
            get => (Brush?)GetValue(OnFillProperty);
            set => SetValue(OnFillProperty, value);
        }

        /// <summary>状态0背景色（见 <see cref="OffFillProperty"/>）</summary>
        public Brush? OffFill
        {
            get => (Brush?)GetValue(OffFillProperty);
            set => SetValue(OffFillProperty, value);
        }

        /// <summary>状态1文字颜色（见 <see cref="OnForegroundProperty"/>）</summary>
        public Brush? OnForeground
        {
            get => (Brush?)GetValue(OnForegroundProperty);
            set => SetValue(OnForegroundProperty, value);
        }

        /// <summary>状态0文字颜色（见 <see cref="OffForegroundProperty"/>）</summary>
        public Brush? OffForeground
        {
            get => (Brush?)GetValue(OffForegroundProperty);
            set => SetValue(OffForegroundProperty, value);
        }

        /// <summary>当前显示文字（只读，见 <see cref="DisplayTextProperty"/>）</summary>
        public string? DisplayText => (string?)GetValue(DisplayTextProperty);

        /// <summary>当前显示背景色（只读，见 <see cref="DisplayFillProperty"/>）</summary>
        public Brush? DisplayFill => (Brush?)GetValue(DisplayFillProperty);

        /// <summary>当前显示文字颜色（只读，见 <see cref="DisplayForegroundProperty"/>）</summary>
        public Brush? DisplayForeground => (Brush?)GetValue(DisplayForegroundProperty);

        /// <summary>本控件是否正被按住（只读，见 <see cref="IsPressedProperty"/>）</summary>
        public bool IsPressed => (bool)GetValue(IsPressedProperty);

        /// <summary>当前状态（true = 状态1）：读取的值按 <see cref="OutputInvert"/> 取反后的结果</summary>
        public bool State => IsOn ^ OutputInvert;

        // extra = 4：与按钮同一口径，文字离边稍远一点，密集排在一起才不局促
        protected override void OnElementRefreshed() => ApplyStrokeInset(4);

        protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseLeftButtonDown(e);

            // 隧道事件里画布（祖先）先跑、我们后跑，且运行态下画布刻意不吞事件（图元的交互要完整），
            // 所以这里收得到。e.Handled 只可能是别的隧道处理器先抢了（如数值域进编辑态），那就别动。
            if (e.Handled)
                return;

            BeginPress();
        }

        protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseLeftButtonUp(e);

            ReleasePress();
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);

            CancelPress();
        }

        /// <summary>
        /// 按下。<b>鼠标事件与断言共用这一个入口</b>（同 <see cref="IOFieldElement.BeginEdit"/> 的先例）：
        /// 交互的"唯一真相"留在控件里，断言才能像真按一下那样驱动它，
        /// 而不必去伪造 WPF 的鼠标设备（<see cref="MouseEventArgs"/> 连公开构造函数都没有）。
        /// 门禁（<see cref="CanOperate"/>）也放在这里而不是事件处理器里——少一处判断就少一处漏判。
        /// </summary>
        public void BeginPress()
        {
            if (_pressed || !CanOperate)
                return;

            _pressed = true;
            SetValue(IsPressedPropertyKey, true);

            // 瞬动模式在"按下"这一侧就生效；置位/复位/取反要等到释放
            if (IsMode(ModePressOn))
                Write(true);
            else if (IsMode(ModePressOff))
                Write(false);
        }

        /// <summary>释放：这一下算数，落"释放"侧的动作</summary>
        public void ReleasePress() => EndPress(applyRelease: true);

        /// <summary>
        /// 按下后滑开（鼠标移出控件）：与画布对「释放」的同对象校验同一口径——这一下取消。
        /// 但瞬动模式（按下ON / 按下OFF）必须把值复位，否则会留下"手已经移开、量还按着"的假象。
        /// </summary>
        public void CancelPress() => EndPress(applyRelease: IsMode(ModePressOn) || IsMode(ModePressOff));

        /// <summary>收这一下按下：清按住态，按需落"释放"侧的动作</summary>
        private void EndPress(bool applyRelease)
        {
            if (!_pressed)
                return;

            _pressed = false;
            SetValue(IsPressedPropertyKey, false);

            if (!applyRelease || !CanOperate)
                return;

            if (IsMode(ModeSet))
                Write(true);
            else if (IsMode(ModeReset))
                Write(false);
            else if (IsMode(ModeInvert))
                Write(!State); // 取反的是"当前显示的状态"，写回去之后显示随之翻转
            else if (IsMode(ModePressOn))
                Write(false);
            else if (IsMode(ModePressOff))
                Write(true);
        }

        /// <summary>
        /// 能不能操作：有写通道（运行态才有，设计期 RuntimeContext 是 null，点一下只是选中图元）、
        /// 「读变量」真绑了变量且没被停用、没被禁用、且当前角色够格。与数值域的可写判定（
        /// <see cref="IOFieldElement.IsEditable"/>）同一口径——少任何一条，点击都不该动现场量：
        /// "点了没反应"总好过"点完才在日志里说写不进去"。
        ///
        /// 权限这一条只能卡在这里：写通道不认识角色，领域层那条闸门（<c>ScadaRuntime.RaiseElementEvent</c>）
        /// 卡的是事件钩子而不是写入本身——"谁都不许按的按钮，按下去却把变量改了"就是这么漏出来的。
        /// <see cref="ScadaElement.RequiredRole"/> 为 null（没配过权限）一律放行。
        /// </summary>
        private bool CanOperate
            => RuntimeContext?.Writer is not null
               && Element?.FindBinding(StateKey) is { IsEnabled: true }
               && IsEnabled
               && (Element?.RequiredRole is not { } required
                   || RuntimeContext?.AccessPolicy.CanOperate(required, out _) == true);

        /// <summary>
        /// 把状态写回工程变量。<b>刻意不自己改 <see cref="IsOn"/></b>：值由变量持有，
        /// 数据泵马上会把新值读回来，画面显示与变量里存的是同一个值。
        /// 失败原因由写通道记进运行日志（没绑变量 / 绑定停用 / 变量不存在 / 写不进去），
        /// 这里不吞也不另造提示——图元够不着变量注册表，编不出更准的话。
        /// </summary>
        private void Write(bool value)
        {
            if (RuntimeContext?.Writer is not { } writer || Element is not { } element)
                return;

            writer.TryWriteText(element, StateKey, value ? "1" : "0", out _);
        }

        private bool IsMode(string mode)
            => string.Equals(Mode, mode, StringComparison.OrdinalIgnoreCase);

        private static void OnDisplayInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((BitButtonElement)d).UpdateDisplay();

        private void UpdateDisplay()
        {
            var on = State;

            SetValue(DisplayTextPropertyKey, PickStateText(on ? OnText : OffText));
            SetValue(DisplayFillPropertyKey, (on ? OnFill : OffFill) ?? Brushes.Transparent);
            SetValue(DisplayForegroundPropertyKey, (on ? OnForeground : OffForeground) ?? Brushes.Transparent);
        }

        /// <summary>状态文本留空时回落到「文字」：这样"两种状态同一段字"的按钮只填一处</summary>
        private string PickStateText(string? stateText)
            => string.IsNullOrEmpty(stateText) ? Text ?? string.Empty : stateText;
    }
}
