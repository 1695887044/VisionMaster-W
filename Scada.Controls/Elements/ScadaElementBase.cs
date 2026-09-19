using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 全部 SCADA 图元控件的基类：把 <see cref="ScadaElement"/> 上的一袋字符串翻译成画面。
    ///
    /// 数据流是<b>单向</b>的，这一点必须记牢：
    ///
    /// <code>
    ///   ScadaElement（模型，落盘）  ──PropertyChanged──▶  ScadaElementBase（控件，渲染）
    ///          ▲                                                    │
    ///          └──────── S3 编辑器 / S4 运行态的写入 ◀──────────────┘
    ///                        （控件自己不回写模型）
    /// </code>
    ///
    /// 控件不回写模型，换来的是：编辑器拖动时"模型改了 → 控件刷新"永远成立，
    /// 而"控件被布局系统改宽高 → 模型跟着变"这种回流不会发生（那正是 WPF 尺寸协商
    /// 把文档数据搅烂的经典路径）。所以控件的 Width/Height 由模型单向决定，
    /// 模板里也不该写死尺寸。
    ///
    /// 落地规则分两段：
    /// ① <b>几何键</b>（$Name/$X/$Y/$Width/$Height/$Rotation）由 <see cref="ApplyGeometry"/> 直接落到
    ///    FrameworkElement 自身的 Width/Height/Canvas.Left/Canvas.Top/RenderTransform。
    ///    它们不进描述符的 TargetProperty——那是"图元特有属性"的落点，而几何是每个可视对象
    ///    都有的公共量，写进描述符只会变成六条一模一样的重复声明。
    /// ② <b>属性袋键</b>按描述符逐条落地：有 <see cref="ElementPropertyDescriptor.TargetProperty"/>
    ///    的走 WPF 现成的类型转换器写依赖属性；没有的交给子类覆写
    ///    <see cref="ApplyCustomProperty"/>（用于"一个字符串要摊成多个视觉元素"之类的表达）。
    /// </summary>
    public abstract class ScadaElementBase : ContentControl
    {
        /// <summary>本控件呈现的图元模型（null 表示空控件，渲染成模板的默认样子）</summary>
        public static readonly DependencyProperty ElementProperty = DependencyProperty.Register(
            nameof(Element), typeof(ScadaElement), typeof(ScadaElementBase),
            new FrameworkPropertyMetadata(null, OnElementChanged));

        /// <summary>
        /// 图元上挂的一段文字（设备框里的名字、按钮上的"启动"、指示灯下的"1#电机"）。
        ///
        /// 放在基类，是因为"每个图元都可以有个标签"是 SCADA 的通用语义，
        /// 而依赖属性本身不花钱——控件用不到的属性就是一组没人读的默认值。
        /// 至于某个图元要不要显示它、显示在哪儿，由那个图元的模板决定
        /// （指示灯把标签放在灯下方，按钮放在正中，矩形居中）。
        /// </summary>
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
            nameof(Text), typeof(string), typeof(ScadaElementBase),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>
        /// 文字在框内的水平对齐。
        ///
        /// 为什么不是直接用 WPF 的 <c>Control.HorizontalContentAlignment</c>：那个是
        /// <see cref="HorizontalAlignment"/> 类型，而 TextBlock 认的是 <see cref="System.Windows.TextAlignment"/>，
        /// 两套枚举值不通用，TemplateBinding 会因类型不匹配而静默失效。
        ///
        /// 放在基类（而不是像早期那样只长在文本图元上），理由与 <see cref="TextProperty"/> 相同：
        /// "这段文字靠哪边"是通用文字语义，而依赖属性本身不花钱——用不到的图元就是一组没人读的默认值。
        /// 于是文本、时钟、将来的报警条共用同一个属性，不必各声明一份同名依赖属性。
        /// </summary>
        public static readonly DependencyProperty TextAlignmentProperty = DependencyProperty.Register(
            nameof(TextAlignment), typeof(TextAlignment), typeof(ScadaElementBase),
            new FrameworkPropertyMetadata(TextAlignment.Center, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>填充色（矩形的内部、按钮的底色、指示灯点亮时的灯色）</summary>
        public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
            nameof(Fill), typeof(Brush), typeof(ScadaElementBase),
            new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>边框色</summary>
        public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
            nameof(Stroke), typeof(Brush), typeof(ScadaElementBase),
            new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>边框粗细（0 = 不画边框）</summary>
        public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
            nameof(StrokeThickness), typeof(double), typeof(ScadaElementBase),
            new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>圆角半径（矩形/按钮；0 = 直角）</summary>
        public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
            nameof(CornerRadius), typeof(double), typeof(ScadaElementBase),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

        static ScadaElementBase()
        {
            // 让本类型去 Themes/Generic.xaml 里找默认样式。
            // 少了这一句，控件会以"裸 Control"出现且不报任何错——最难查的一类外观问题。
            // 派生类若自带模板，只需在自己的静态构造里把 typeof(…) 换成自己。
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(ScadaElementBase),
                new FrameworkPropertyMetadata(typeof(ScadaElementBase)));
        }

        #region 组态事件（冒泡出口）

        /// <summary>
        /// 图元上的组态事件（按下 / 释放 / 值改变…）沿可视树向上冒泡的载体。
        ///
        /// 注册在 <see cref="ScadaElementBase"/> 而不是画布上，理由是"谁出事谁发声"：
        /// 这个事件描述的是<b>某个图元</b>身上发生的事，控件自己就该有能力发出来。
        /// 第三方新加的图元只要在自己的交互里调用 <see cref="RaiseScadaEvent"/>，
        /// 不必知道宿主有没有画布、画布叫什么，接得上就接得上。
        ///
        /// 用 <b>Bubble</b> 而不是 Tunnel/Direct：运行窗口在根上挂一个 handler 就能收到所有图元的事件，
        /// 中间不需要任何一层转发（详见 <see cref="ScadaElementEventArgs"/> 的说明）。
        /// </summary>
        public static readonly RoutedEvent ScadaEventEvent = EventManager.RegisterRoutedEvent(
            nameof(ScadaEventEvent), RoutingStrategy.Bubble,
            typeof(EventHandler<ScadaElementEventArgs>), typeof(ScadaElementBase));

        /// <summary>CLR 事件包装，写法是 WPF 路由事件的标准模板（挂摘都转给 <see cref="RoutedEvent"/>）</summary>
        public event EventHandler<ScadaElementEventArgs> ScadaEvent
        {
            add => AddHandler(ScadaEventEvent, value);
            remove => RemoveHandler(ScadaEventEvent, value);
        }

        /// <summary>
        /// 发出一个组态事件。
        ///
        /// <c>protected internal</c> 开的是两条路：派生类在自己的交互里直接发（跨装配也行），
        /// 以及同装配的画布在运行态命中测试后代发（内置图元不用各自抄一遍鼠标处理）。
        ///
        /// <paramref name="eventType"/> 只是"发生了什么"，<b>不代表它会被执行</b>——
        /// 有没有配钩子、该不该执行、按什么顺序执行，全在运行态会话那三道闸门里（D1：规则住在领域层）。
        /// 所以这里不判 IsReadOnly、不查 EventHooks：一个只负责发声的控件不该认识产品规则。
        /// </summary>
        protected internal void RaiseScadaEvent(ScadaEventType eventType)
        {
            var args = new ScadaElementEventArgs(ScadaEventEvent, eventType) { Source = this };

            RaiseEvent(args);
        }

        #endregion

        protected ScadaElementBase()
        {
            // 画面里大量 1px 边框与细线，不取整就会出现半像素灰边（缩放后尤其明显）
            UseLayoutRounding = true;
        }

        /// <summary>本控件呈现的图元模型</summary>
        public ScadaElement? Element
        {
            get => (ScadaElement?)GetValue(ElementProperty);
            set => SetValue(ElementProperty, value);
        }

        /// <summary>图元上挂的一段文字（见 <see cref="TextProperty"/>）</summary>
        public string? Text
        {
            get => (string?)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        /// <summary>文字在框内的水平对齐（见 <see cref="TextAlignmentProperty"/>）</summary>
        public TextAlignment TextAlignment
        {
            get => (TextAlignment)GetValue(TextAlignmentProperty);
            set => SetValue(TextAlignmentProperty, value);
        }

        /// <summary>填充色（见 <see cref="FillProperty"/>）</summary>
        public Brush? Fill
        {
            get => (Brush?)GetValue(FillProperty);
            set => SetValue(FillProperty, value);
        }

        /// <summary>边框色（见 <see cref="StrokeProperty"/>）</summary>
        public Brush? Stroke
        {
            get => (Brush?)GetValue(StrokeProperty);
            set => SetValue(StrokeProperty, value);
        }

        /// <summary>边框粗细（见 <see cref="StrokeThicknessProperty"/>）</summary>
        public double StrokeThickness
        {
            get => (double)GetValue(StrokeThicknessProperty);
            set => SetValue(StrokeThicknessProperty, value);
        }

        /// <summary>圆角半径（见 <see cref="CornerRadiusProperty"/>）</summary>
        public double CornerRadius
        {
            get => (double)GetValue(CornerRadiusProperty);
            set => SetValue(CornerRadiusProperty, value);
        }

        /// <summary>
        /// 本图元的描述符（按 <see cref="ScadaElement.TypeKey"/> 现查注册表）。
        /// 类型未注册时为 null，此时只落几何、不落属性——S3 会把它显示成"未知图元"占位框，
        /// 这样低版本软件打开高版本 .vms 仍能看见画面结构，而不是整页打不开。
        /// </summary>
        protected ElementDescriptor? Descriptor
        {
            get
            {
                var typeKey = Element?.TypeKey;

                if (_descriptor is null || !string.Equals(_descriptorTypeKey, typeKey, StringComparison.OrdinalIgnoreCase))
                {
                    _descriptor = ElementRegistry.Find(typeKey);
                    _descriptorTypeKey = typeKey;
                }

                return _descriptor;
            }
        }

        /// <summary>已挂上变更订阅的模型（与 <see cref="Element"/> 的当前值可能不同，见 SyncSubscription）</summary>
        private ScadaElement? _subscribedElement;

        private ElementDescriptor? _descriptor;
        private string? _descriptorTypeKey;
        private bool _refreshing;

        /// <summary>
        /// 把字符串转成目标依赖属性要的值，复用 WPF 现成的类型转换器
        /// （Brush/double/FontWeight/枚举各有各的转换器，不必我们重造一份颜色解析）。
        ///
        /// 用 <c>ConvertFromInvariantString</c>：.vms 是跨机器交换的，
        /// 小数点不能跟着系统区域设置走（德语系统上 "1.5" 会被读成 15）。
        /// </summary>
        public static object? ConvertFromString(Type targetType, string text)
        {
            ArgumentNullException.ThrowIfNull(targetType);
            return TypeDescriptor.GetConverter(targetType).ConvertFromInvariantString(text);
        }

        /// <summary>
        /// 模型改了 → 刷新控件。公开出来是给 S3/S4 在"批量改完一堆属性"后强制对齐用的
        /// （订阅还在，所以正常情况下不需要调）。
        /// </summary>
        public void Refresh()
        {
            if (_refreshing)
                return; // 子类在刷新过程中回写模型会再次触发本方法，这里挡住递归

            _refreshing = true;
            try
            {
                RefreshCore();
            }
            finally
            {
                _refreshing = false;
            }
        }

        /// <summary>
        /// 宿主（S3 画布）注入的"这个图元此刻该不该显示"判定。
        ///
        /// 为什么传一个委托进来，而不是让控件自己去读图层：可见性的<b>唯一口径</b>在
        /// <see cref="ScadaPage.IsElementVisible"/>（图层隐藏会盖掉图元自身），可控件基类
        /// 压根不该认识"画面"这个概念——它连模型集合都不碰，只认自己手上这一个 Element。
        /// 注入了就照注入的判定办；没注入（控件单独使用、无画面的预览、单元测试）就永远可见，
        /// 于是"加图层"这件事对不接线的用法是零影响。
        ///
        /// 依赖方向因此始终是"画布 → 控件"这一条向下的线，控件不会反过来抓住画布。
        /// </summary>
        public Func<ScadaElement, bool>? LayerVisibilityResolver { get; set; }

        /// <summary>
        /// 图层开关翻转后由宿主逐个调用，重算本控件的可见性。
        ///
        /// 为什么另开一个入口而不是让人直接 <see cref="Refresh"/>：隐藏图层是<b>图层</b>的属性变了，
        /// 图上几十个图元自己的属性一个都没动。走全量刷新等于把几十个 brush 重新解析一遍，
        /// 白给；而"只改可见性"是一次赋值。
        /// </summary>
        public void RefreshLayerVisibility() => ApplyLayerVisibility();

        private void ApplyLayerVisibility()
        {
            // 用 Collapsed 而不是 Hidden：Hidden 仍占位仍吃命中，
            // 会留下一个"看不见但挡住鼠标、底下的图层点不着"的图层——比不隐藏更糟。
            // Collapsed 天然不参与命中，正好等于"隐藏层既不渲染也不接受操作"。
            bool shown = Element == null || LayerVisibilityResolver?.Invoke(Element) != false;
            Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        }

        #region 运行态写值（S6 数据泵的落点）

        /// <summary>
        /// 运行态写值入口：把一个工程变量的值直接落到本控件的目标属性上。
        ///
        /// <b>为什么写控件、不写模型</b>
        /// ---------
        /// 运行态刷新每秒可能几百次，而 <see cref="ScadaElement"/> 是落盘的组态数据：
        /// 让运行值回流进去，就会把"操作员此刻看到的瞬时值"写进方案文件，还会顺带触发
        /// 脏标记、撤销栈、编辑器重绘。所以运行值与设计值<b>各走各的路</b>——
        /// 模型 → 控件走 <see cref="Refresh"/>，变量 → 控件走本方法。
        ///
        /// 这样还白送一条语义：<b>停止运行 = 回到设计值</b>。运行值只活在控件上，
        /// 停的时候再 <see cref="Refresh"/> 一次，画面就自己回到组态时的样子，
        /// 不需要任何"恢复现场"的备份表（那才是运行态最容易漏、最难测的一块）。
        ///
        /// <b>为什么收一个描述符、而不是裸的依赖属性</b>
        /// ---------
        /// 几何键（$X/$Y/$Width/$Height/$Rotation）压根没有
        /// <see cref="ElementPropertyDescriptor.TargetProperty"/>，它们的落点是控件自身的
        /// Width/Height、Canvas 附加属性、RenderTransform。把"这个键落在哪"的判断留在本类里，
        /// 与设计期 <see cref="ApplyGeometry"/> 同一处口径；数据泵就只管"变量变了 → 叫控件写"。
        /// </summary>
        /// <param name="property">属性声明（由 <see cref="ElementRegistry.FindProperty"/> 取到）</param>
        /// <param name="value">变量当前值（任意 CLR 对象，由 <see cref="ScadaValueConverter"/> 换算）</param>
        /// <param name="format">展示格式串（<see cref="ScadaBinding.DisplayFormat"/>，仅文本类目标使用）</param>
        /// <param name="error">失败原因（可直接展示给操作员的中文）；成功为 null</param>
        /// <returns>写成功返回 true；转换失败返回 false 且控件保持原样</returns>
        public bool TryApplyRuntimeValue(ElementPropertyDescriptor property, object? value, string? format, out string? error)
        {
            ArgumentNullException.ThrowIfNull(property);

            if (property.IsGeometry)
                return TryApplyRuntimeGeometry(property.Key, value, out error);

            if (property.TargetProperty is not { } target)
            {
                error = $"属性「{property.DisplayName}」没有对应的控件属性，运行态不支持写入";
                return false;
            }

            if (!ScadaValueConverter.TryConvert(value, target.PropertyType, format, out var converted, out error))
                return false; // 值用不了就保持现状：一个坏值不该把整页画面打断

            if (!Equals(GetValue(target), converted))
                SetValue(target, converted);

            error = null;
            return true;
        }

        /// <summary>
        /// 几何键的运行态落点：位置贴 Canvas、尺寸写自身、旋转走 RenderTransform。
        /// 与设计期同一处落点，只是值的来源从模型换成了变量。
        /// </summary>
        private bool TryApplyRuntimeGeometry(string key, object? value, out string? error)
        {
            if (!TryToDouble(value, out double number))
            {
                error = value is null ? "变量当前值为空" : $"值「{value}」不是有效数字";
                return false;
            }

            switch (key)
            {
                case ElementValueAccess.XKey:
                    SetValue(Canvas.LeftProperty, number);
                    break;
                case ElementValueAccess.YKey:
                    SetValue(Canvas.TopProperty, number);
                    break;
                case ElementValueAccess.WidthKey:
                    Width = Math.Abs(number); // 负宽高在 WPF 里等于 0，取绝对值与 ElementValueAccess.Write 同一口径
                    break;
                case ElementValueAccess.HeightKey:
                    Height = Math.Abs(number);
                    break;
                case ElementValueAccess.RotationKey:
                    ApplyRotation(number);
                    break;
                default:
                    error = $"几何属性「{key}」不支持运行态写入";
                    return false;
            }

            error = null;
            return true;
        }

        /// <summary>把变量值折成 double（数值直接收、布尔按 0/1、文本按不变文化解析）</summary>
        private static bool TryToDouble(object? value, out double number)
        {
            switch (value)
            {
                case null:
                    number = 0;
                    return false;
                case double d when double.IsFinite(d):
                    number = d;
                    return true;
                case float f when float.IsFinite(f):
                    number = f;
                    return true;
                case bool b:
                    number = b ? 1 : 0;
                    return true;
                case string text:
                    return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
                default:
                    try
                    {
                        number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                        return double.IsFinite(number);
                    }
                    catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
                    {
                        number = 0;
                        return false;
                    }
            }
        }

        #endregion

        /// <summary>
        /// 落地一个"描述符表达不了"的属性（<see cref="ElementPropertyDescriptor.TargetProperty"/> 为 null 的那些）。
        ///
        /// 传空串表示该属性没配过（或恢复默认）——子类应还原成自己的默认外观，
        /// 而不是把值当成空字符串去解析。
        /// </summary>
        protected virtual void ApplyCustomProperty(string key, string value)
        {
        }

        /// <summary>标准属性全部落地后的收尾钩子（如按角度调整指针、按量程重画刻度）</summary>
        protected virtual void OnElementRefreshed()
        {
        }

        /// <summary>
        /// 把边框线宽的一半（外加 <paramref name="extra"/> 的呼吸空间）转成控件的
        /// <see cref="Control.Padding"/>，供模板里的形状元素用
        /// <c>Margin="{TemplateBinding Padding}"</c> 向内缩进。
        ///
        /// 为什么必须要有这一步：形状元素画在控件边界上，描边以边界线为中心向两侧各铺半个线宽，
        /// 于是"宽 100、边框 3"的矩形视觉上会变成 103——一排图元摆在一起就会错开。
        /// 内缩半个线宽后，视觉外沿正好落在标注尺寸上。
        ///
        /// 只有画边框的图元（矩形/椭圆/按钮）需要调用；纯文本图元不调，Padding 保持 0。
        /// </summary>
        protected void ApplyStrokeInset(double extra = 0)
        {
            var inset = StrokeThickness / 2 + extra;
            Padding = inset > 0 ? new Thickness(inset) : default;
        }

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);

            // 只在"挂上可视树"期间订阅模型：控件被移出画面后模型仍活着（文档对象），
            // 若不摘订阅，模型会反过来钉住控件，编辑期反复切换画面就是一路泄漏。
            Loaded += (_, _) => SyncSubscription();
            Unloaded += (_, _) => SyncSubscription();
        }

        private static void OnElementChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ScadaElementBase control)
                return;

            control.SyncSubscription();
            control.Refresh();
        }

        /// <summary>
        /// 让订阅跟随"<see cref="Element"/> 非空 <b>且</b> 控件已挂载"这一个条件。
        ///
        /// 为什么用一个条件而不是两处各自挂/摘：两处各写一遍，就必然有一个分支漏摘
        /// （ScadaPage.Clear() 走 Reset 分支漏摘订阅就是同一个坑）。
        /// 订阅丢了不可怕——<see cref="Loaded"/> 时会补一次 <see cref="Refresh"/> 追上错过的变更；
        /// 订阅多留了才可怕，那是泄漏。
        /// </summary>
        private void SyncSubscription()
        {
            var target = IsLoaded ? Element : null;

            if (ReferenceEquals(_subscribedElement, target))
                return;

            if (_subscribedElement != null)
                _subscribedElement.PropertyChanged -= OnElementPropertyChanged;

            _subscribedElement = target;

            if (target != null)
                target.PropertyChanged += OnElementPropertyChanged;
        }

        private void OnElementPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                // 几何单独走一条：拖动图元时每帧只改这几项，不值当顺带重刷全部属性
                case nameof(ScadaElement.X):
                case nameof(ScadaElement.Y):
                case nameof(ScadaElement.Width):
                case nameof(ScadaElement.Height):
                case nameof(ScadaElement.Rotation):
                case nameof(ScadaElement.Name):
                case nameof(ScadaElement.ZIndex):
                    ApplyGeometry();
                    return;

                default:
                    // 属性袋（Properties）、绑定集合（Bindings）以及 PropertyName 为 null 的"全都变了"
                    Refresh();
                    return;
            }
        }

        private void RefreshCore()
        {
            ApplyGeometry();
            ApplyLayerVisibility(); // 图元换图层归属（LayerId 变了）会从这条路上自然走到

            if (Element is null || Descriptor is not { } descriptor)
                return;

            foreach (var property in descriptor.Properties)
            {
                if (property.IsGeometry)
                    continue; // 已由 ApplyGeometry 落地

                var text = ElementValueAccess.Read(Element, property);

                if (property.TargetProperty is { } target)
                    ApplyTargetProperty(target, text);
                else
                    ApplyCustomProperty(property.Key, text);
            }

            OnElementRefreshed();
        }

        /// <summary>
        /// 几何落地：位置贴到 Canvas 附加属性，尺寸写自己的 Width/Height，旋转走 RenderTransform。
        ///
        /// 旋转用 <c>RenderTransformOrigin = (0.5, 0.5)</c> 而不是给 RotateTransform 设
        /// CenterX/CenterY：后者在图元尺寸变化时会把旧中心点留下，转轴就偏了；
        /// 按比例指定的原点自动跟着尺寸走。
        /// </summary>
        private void ApplyGeometry()
        {
            if (Element is null)
                return;

            Width = Element.Width;
            Height = Element.Height;

            SetValue(Canvas.LeftProperty, Element.X);
            SetValue(Canvas.TopProperty, Element.Y);

            // 叠放次序交给承载面板（S3 画布的元素层就是 Canvas）。
            // 放在这里而不是画布侧，是因为"模型改了→控件对齐"这件事只有控件自己清楚时机
            // （它已经订阅了模型的 PropertyChanged）；画布再订一遍就是第二份订阅、第二处漏摘风险。
            SetValue(Panel.ZIndexProperty, Element.ZIndex);

            ApplyRotation(Element.Rotation);
        }

        /// <summary>
        /// 旋转落地。设计期（<see cref="ApplyGeometry"/>）与运行态（<see cref="TryApplyRuntimeGeometry"/>）
        /// 共用这一段，免得"零度时摘掉变换"这类细节只在一处做到。
        /// </summary>
        private void ApplyRotation(double angle)
        {
            if (angle == 0)
            {
                RenderTransform = null; // 归零就摘掉，别留一个角度为 0 的变换在树上
                return;
            }

            RenderTransformOrigin = new Point(0.5, 0.5);

            if (RenderTransform is RotateTransform rotate)
                rotate.Angle = angle;
            else
                RenderTransform = new RotateTransform(angle);
        }

        /// <summary>
        /// 把描述符里的一条属性写进它的目标依赖属性。
        ///
        /// 空值（模型没配、描述符默认值也是空）时<b>回落到该依赖属性的默认值</b>，
        /// 而不是写 null：写 null 会把 Brush 之类抹成"透明"，表现为"这个图元没配颜色就隐形了"，
        /// 而正确语义是"没配就用控件默认外观"。
        /// </summary>
        private void ApplyTargetProperty(DependencyProperty target, string text)
        {
            object? value;

            if (string.IsNullOrEmpty(text))
            {
                value = target.DefaultMetadata.DefaultValue;
            }
            else
            {
                try
                {
                    value = ConvertFromString(target.PropertyType, text);
                }
                catch (Exception)
                {
                    // 转换失败保持现状：描述符默认值已在注册期校验过，这里能挡到的只有
                    // 手工改坏的 .vms 或运行态写入的脏值——不该让一个坏值把整页渲染打断。
                    return;
                }
            }

            if (!Equals(GetValue(target), value))
                SetValue(target, value);
        }
    }
}
