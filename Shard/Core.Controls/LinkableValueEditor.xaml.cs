using Core.Events;
using Core.Interfaces;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Core.Controls
{
    /// <summary>
    /// 可链接参数编辑器（固定值 / 变量链接 二合一控件，VisionMaster 风格）
    /// 布局：标签 + 下划线输入框 + 链接(🔗) / 清除(✕) 图标按钮
    /// - 直接绑定 InputPort：端口名/类型/值/链接状态均从端口读取
    /// - 端口类型驱动控件形态：
    ///   可文本输入类型（string/int/double/enum...）→ 输入框 + 链接按钮 + 清除按钮
    ///   不可文本输入类型（HImage/byte[]...）     → 占位提示 + 链接按钮（未链接时无清除按钮）
    /// - 链接动作通过 GlobalEventBus 发布 LinkPathEvent，由主程序呼出变量绑定弹窗完成
    /// </summary>
    public partial class LinkableValueEditor : UserControl
    {
        #region 依赖属性

        /// <summary>
        /// 绑定的输入端口（端口即数据成员：名称/类型/值/链接状态均从端口读取）
        /// </summary>
        public static readonly DependencyProperty PortProperty =
            DependencyProperty.Register(
                nameof(Port), typeof(IInputPort), typeof(LinkableValueEditor),
                new FrameworkPropertyMetadata(null, OnPortChanged));

        public IInputPort Port
        {
            get => (IInputPort)GetValue(PortProperty);
            set => SetValue(PortProperty, value);
        }

        /// <summary>
        /// 左侧标签文本（如 "延时时间(ms)"），为空时不显示
        /// </summary>
        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register(
                nameof(Label), typeof(string), typeof(LinkableValueEditor),
                new PropertyMetadata(null, OnLabelChanged));

        public string Label
        {
            get => (string)GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        /// <summary>
        /// 当前链接显示地址（如 "Global.CT" 或 "上游步骤.端口名"），为空表示未链接
        /// </summary>
        public static readonly DependencyProperty LinkAddressProperty =
            DependencyProperty.Register(
                nameof(LinkAddress), typeof(string), typeof(LinkableValueEditor),
                new FrameworkPropertyMetadata(null, OnLinkAddressChanged));

        public string LinkAddress
        {
            get => (string)GetValue(LinkAddressProperty);
            set => SetValue(LinkAddressProperty, value);
        }

        /// <summary>
        /// 是否已链接（由 LinkAddress 派生，只读）
        /// </summary>
        private static readonly DependencyPropertyKey IsLinkedPropertyKey =
            DependencyProperty.RegisterReadOnly(
                nameof(IsLinked), typeof(bool), typeof(LinkableValueEditor),
                new PropertyMetadata(false));

        public static readonly DependencyProperty IsLinkedProperty = IsLinkedPropertyKey.DependencyProperty;

        public bool IsLinked
        {
            get => (bool)GetValue(IsLinkedProperty);
            private set => SetValue(IsLinkedPropertyKey, value);
        }

        /// <summary>
        /// 端口当前是否支持文本输入（由 Port.DataType 驱动，只读）
        /// string/基元类型/enum/decimal → true；HImage/byte[]/复杂类型 → false
        /// </summary>
        private static readonly DependencyPropertyKey IsTextEditablePropertyKey =
            DependencyProperty.RegisterReadOnly(
                nameof(IsTextEditable), typeof(bool), typeof(LinkableValueEditor),
                new PropertyMetadata(true));

        public static readonly DependencyProperty IsTextEditableProperty = IsTextEditablePropertyKey.DependencyProperty;

        public bool IsTextEditable
        {
            get => (bool)GetValue(IsTextEditableProperty);
            private set => SetValue(IsTextEditablePropertyKey, value);
        }

        /// <summary>
        /// 端口是否渲染成「只能选」的下拉（只读）：枚举、或声明了 IsFunctionalEnum（候选来自方案），
        /// **且当前真的取到了候选**。
        ///
        /// 两类端口的共同点是**合法值是一个封闭集合**：
        ///   · 枚举的候选是成员本身，随时能从类型算出来；
        ///   · 卡地址 / 轴名的候选来自当前方案（有哪些卡、哪些轴），绑上游等于允许
        ///     流程去填一个方案里不存在的卡 —— 那是把编译期能发现的问题推到运行期。
        ///
        /// ★ 候选为空时**不许**保持下拉：那是一个打不开的空下拉 —— 输入框已被折叠、
        ///   ComboBox 里一个项都没有，用户既选不了也输不了，只能先去别处配好方案再回来
        ///   （"还没配运动卡"这个最常见的第一步状态直接把人堵死）。
        ///   所以判据必须连"候选非空"一起算，空候选由 <see cref="IsOptionEmpty"/> 接管渲染。
        /// </summary>
        private static readonly DependencyPropertyKey IsOptionPortPropertyKey =
            DependencyProperty.RegisterReadOnly(
                nameof(IsOptionPort), typeof(bool), typeof(LinkableValueEditor),
                new PropertyMetadata(false));

        public static readonly DependencyProperty IsOptionPortProperty = IsOptionPortPropertyKey.DependencyProperty;

        public bool IsOptionPort
        {
            get => (bool)GetValue(IsOptionPortProperty);
            private set => SetValue(IsOptionPortPropertyKey, value);
        }

        /// <summary>
        /// 封闭集合端口**取不到候选**时的退化态（只读，2026-10-24 口径）。
        ///
        /// 【为什么要单列一个状态】"封闭集合"与"有下拉"是两件事：
        ///   · 前者决定**不许链接/清除**（值只能来自枚举成员或方案里的名字，见 UpdateButtonVisibility）；
        ///   · 后者决定**渲染成下拉**。
        /// 候选来自方案，天然可能为空（还没配卡、宿主没注册来源、方案里没有已启用的轴）。
        ///
        /// 【2026-10-24 改判（用户裁决）：这类端口恒不可手填】旧实现让它退化成可编辑文本框，
        /// 承诺"还没配卡的时候本来就该允许手填"。实测那是**静默缺陷**：轴名/卡名是框架的
        /// 权威清单，手填一个不存在的轴名界面毫无提示，要到运行期才炸；而且"没登记来源"
        /// 这种程序缺陷和"还没配卡"这种正常状态长得一模一样。
        /// 现在这个状态恒为 false（保留属性是为了不破坏既有绑定与断言），空候选改由
        /// <see cref="IsOptionEmpty"/> 渲染成"不许编辑 + 红字/提示说明原因"。
        ///
        /// 【例外】<see cref="DataPort.InputPort{T}.OptionKind"/> 为空的封闭集合端口（只有枚举）
        /// 本来就有候选，不会走到空态。
        /// </summary>
        private static readonly DependencyPropertyKey IsHandFillFallbackPropertyKey =
            DependencyProperty.RegisterReadOnly(
                nameof(IsHandFillFallback), typeof(bool), typeof(LinkableValueEditor),
                new PropertyMetadata(false));

        public static readonly DependencyProperty IsHandFillFallbackProperty = IsHandFillFallbackPropertyKey.DependencyProperty;

        /// <summary>⚠ 恒为 false（2026-10-24 起名字类字段不再允许手填）。语义见上。</summary>
        public bool IsHandFillFallback
        {
            get => (bool)GetValue(IsHandFillFallbackProperty);
            private set => SetValue(IsHandFillFallbackPropertyKey, value);
        }

        /// <summary>
        /// 封闭集合端口**取不到候选**的空态：显示不可编辑的占位 + 一行说明原因
        /// （<see cref="OptionEmptyReason"/>），并区分"正常没配"与"程序缺陷"。
        ///
        /// 【为什么要有独立状态】空候选有三种成因，界面上必须长得不一样：
        ///   · 没登记来源 / 来源抛异常 → 程序缺陷，红字（<see cref="IsOptionEmptyDefect"/> = true）；
        ///   · 登记了但清单为空 → 正常业务状态，灰字 + "去哪里配"的指引。
        /// 判据统一取 <see cref="StepConfigOptionSource.GetOptionsResult"/>，别各处自己判 Count。
        /// </summary>
        private static readonly DependencyPropertyKey IsOptionEmptyPropertyKey =
            DependencyProperty.RegisterReadOnly(
                nameof(IsOptionEmpty), typeof(bool), typeof(LinkableValueEditor),
                new PropertyMetadata(false));

        public static readonly DependencyProperty IsOptionEmptyProperty = IsOptionEmptyPropertyKey.DependencyProperty;

        public bool IsOptionEmpty
        {
            get => (bool)GetValue(IsOptionEmptyProperty);
            private set => SetValue(IsOptionEmptyPropertyKey, value);
        }

        /// <summary>空态是否属于程序缺陷（没登记来源 / 来源抛异常）。仅当 <see cref="IsOptionEmpty"/> 为真时有意义。</summary>
        private static readonly DependencyPropertyKey IsOptionEmptyDefectPropertyKey =
            DependencyProperty.RegisterReadOnly(
                nameof(IsOptionEmptyDefect), typeof(bool), typeof(LinkableValueEditor),
                new PropertyMetadata(false));

        public static readonly DependencyProperty IsOptionEmptyDefectProperty = IsOptionEmptyDefectPropertyKey.DependencyProperty;

        public bool IsOptionEmptyDefect
        {
            get => (bool)GetValue(IsOptionEmptyDefectProperty);
            private set => SetValue(IsOptionEmptyDefectPropertyKey, value);
        }

        /// <summary>空态的原因文案（界面直接显示；缺陷态会被复制进诊断文本）。</summary>
        private static readonly DependencyPropertyKey OptionEmptyReasonPropertyKey =
            DependencyProperty.RegisterReadOnly(
                nameof(OptionEmptyReason), typeof(string), typeof(LinkableValueEditor),
                // 加了变更回调：原因文字必须随属性变化**立刻**落到界面上。
                // 之前只在 UpdateButtonVisibility 里刷，而 OnPortChanged 是「先写 Reason、
                // 后重算渲染形态」，两者顺序一旦调整就会留下"有原因但界面显示旧文案"的哑巴状态。
                new PropertyMetadata(string.Empty, OnOptionEmptyReasonChanged));

        public static readonly DependencyProperty OptionEmptyReasonProperty = OptionEmptyReasonPropertyKey.DependencyProperty;

        public string OptionEmptyReason
        {
            get => (string)GetValue(OptionEmptyReasonProperty);
            private set => SetValue(OptionEmptyReasonPropertyKey, value);
        }

        /// <summary>
        /// 是否为「封闭集合端口」：枚举、或声明了 IsFunctionalEnum。
        /// 与 <see cref="IsOptionPort"/> 的区别是**不看候选是否为空** ——
        /// 它表达的是"这个值只能从枚举/方案的名字空间里来，不能由上游运行时决定"，
        /// 所以链接（🔗）与清除（✕）对它们恒隐藏（见 UpdateButtonVisibility），
        /// 空候选退化手填时同样不给链接（R33：名字类端口挂绑定弹窗 = 运行期拿值当名字）。
        ///
        /// 必须是依赖属性：XAML 要按它决定"已链接时还显不显示下拉"。
        /// 老工程里这类端口可能还留着上游连线（当年还是普通 string 端口时绑的），
        /// 那种情况下下拉要照常显示 —— 否则用户既改不了也拆不掉（🔗/✕ 已隐藏）。
        /// </summary>
        private static readonly DependencyPropertyKey IsClosedValuePortPropertyKey =
            DependencyProperty.RegisterReadOnly(
                nameof(IsClosedValuePort), typeof(bool), typeof(LinkableValueEditor),
                new PropertyMetadata(false));

        public static readonly DependencyProperty IsClosedValuePortProperty = IsClosedValuePortPropertyKey.DependencyProperty;

        public bool IsClosedValuePort
        {
            get => (bool)GetValue(IsClosedValuePortProperty);
            private set => SetValue(IsClosedValuePortPropertyKey, value);
        }

        /// <summary>
        /// 下拉候选（封闭集合端口按需填充；<see cref="IsOptionPort"/> = 候选非空）。
        ///
        /// 用 ObservableCollection 而不是 List：控件是先绑好 ItemsSource、
        /// 之后再设置 Port 的，普通 List 清空重填不会通知界面，下拉会是空的。
        /// 另：候选集合的每次变化都要重算"下拉还是手填"（见构造函数里的 CollectionChanged）。
        ///
        /// 元素用 StepConfigOption（值/显示分离）：枚举的显示是成员上的 <c>[Display(Name)]</c>
        /// （中文"圆"，值仍是成员名 Circle）；卡地址的显示是「设备名（IP）」——
        /// 只显示 IP 时几张卡根本认不出谁是谁，
        /// 而写进流程的必须是稳定的地址键（见 MotionDescriptor 关于显示名的注释）。
        /// </summary>
        public System.Collections.ObjectModel.ObservableCollection<StepConfigOption> EnumOptions { get; }
            = new();

        /// <summary>
        /// 端口当前值的字符串形式（供 XAML 双向绑定；内部桥接到 Port.Value）
        /// </summary>
        public static readonly DependencyProperty PortValueProperty =
            DependencyProperty.Register(
                nameof(PortValue), typeof(string), typeof(LinkableValueEditor),
                new FrameworkPropertyMetadata(string.Empty, OnPortValueChanged));

        public string PortValue
        {
            get => (string)GetValue(PortValueProperty);
            set => SetValue(PortValueProperty, value);
        }

        /// <summary>
        /// 步骤配置数据（IStepConfigData，绑定宿主 ViewModel 的 StepData 属性），必填
        /// </summary>
        public static readonly DependencyProperty StepDataProperty =
            DependencyProperty.Register(
                nameof(StepData), typeof(IStepConfigData), typeof(LinkableValueEditor),
                new FrameworkPropertyMetadata(null, OnRestoreStateChanged));

        public IStepConfigData StepData
        {
            get => (IStepConfigData)GetValue(StepDataProperty);
            set => SetValue(StepDataProperty, value);
        }

        /// <summary>
        /// 是否允许把全局/系统变量绑到这个端口（默认 true，兼容既有行为）。
        ///
        /// 置 false 时绑定弹窗隐藏「全局变量」节点：C#脚本的输入只接上游数据流，
        /// 全局/系统变量走它自己的「共享变量」声明表（按 VariableId 寻址，见 ScriptSharedVarDef）。
        /// </summary>
        public static readonly DependencyProperty AllowGlobalsProperty =
            DependencyProperty.Register(
                nameof(AllowGlobals), typeof(bool), typeof(LinkableValueEditor),
                new PropertyMetadata(true));

        public bool AllowGlobals
        {
            get => (bool)GetValue(AllowGlobalsProperty);
            set => SetValue(AllowGlobalsProperty, value);
        }

        /// <summary>
        /// 可选的浏览命令（如打开文件/文件夹对话框），设置后双击下划线区域触发
        /// </summary>
        public static readonly DependencyProperty BrowseCommandProperty =
            DependencyProperty.Register(
                nameof(BrowseCommand), typeof(ICommand), typeof(LinkableValueEditor),
                new PropertyMetadata(null));

        public ICommand BrowseCommand
        {
            get => (ICommand)GetValue(BrowseCommandProperty);
            set => SetValue(BrowseCommandProperty, value);
        }

        #endregion

        /// <summary>
        /// 重填候选期间为 true：此时 PortValue 的变更是 ComboBox 丢选中项推回来的**副作用**
        /// （见 <see cref="OnPortValueChanged(DependencyObject, DependencyPropertyChangedEventArgs)"/> ①）。
        /// </summary>
        private bool _suppressPortWrite;

        /// <summary>正在重填 <see cref="EnumOptions"/>：期间不逐项重算渲染形态</summary>
        private bool _refillingOptions;

        public LinkableValueEditor()
        {
            InitializeComponent();
            UpdateButtonVisibility();

            // 候选集合被重填/清空时也要重算"下拉还是手填"——
            // 只在 Port 变化时算一次的话，候选后来有了（或没了）界面不会跟着变。
            EnumOptions.CollectionChanged += (s, e) =>
            {
                if (!_refillingOptions) RecalculateOptionMode();
            };

            // 未链接时双击输入框触发浏览命令（可选）
            PART_ValueBox.MouseDoubleClick += (s, e) =>
            {
                if (!IsLinked && IsTextEditable)
                    BrowseCommand?.Execute(null);
            };
        }

        #region 内部逻辑

        private static void OnLabelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var editor = (LinkableValueEditor)d;
            var text = (string)e.NewValue;
            editor.PART_Label.Text = text;
            editor.PART_Label.Visibility = string.IsNullOrEmpty(text)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private static void OnLinkAddressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var editor = (LinkableValueEditor)d;
            var linked = !string.IsNullOrEmpty((string)e.NewValue);
            editor.SetValue(IsLinkedPropertyKey, linked);
            editor.UpdateButtonVisibility();
        }

        /// <summary>
        /// 原因文案变化 → 直接刷空态那一块。只刷文案与颜色，不动渲染形态
        /// （形态由 <see cref="RecalculateOptionMode"/> 管，两者分开，避免互相盖）。
        /// </summary>
        private static void OnOptionEmptyReasonChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((LinkableValueEditor)d).UpdateOptionEmptyState();
        }

        private static void OnPortChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var editor = (LinkableValueEditor)d;
            var port = e.NewValue as IInputPort;
            if (port == null)
            {
                editor.IsTextEditable = true;
                editor.IsClosedValuePort = false;
                editor._refillingOptions = true;
                try { editor.EnumOptions.Clear(); }
                finally { editor._refillingOptions = false; }
                editor.RecalculateOptionMode();
                editor.UpdateButtonVisibility();
                return;
            }

            // 端口类型驱动可输入性
            editor.IsTextEditable = IsTextEditableType(port.DataType);
            editor.IsClosedValuePort = port.DataType?.IsEnum == true || port.IsFunctionalEnum;

            // ★ R35：重填候选之前记住端口值，重填之后顶回去。
            //   候选与下拉是双向绑定的（SelectedValue ↔ PortValue）：ItemsSource 一清，
            //   ComboBox 会把 SelectedValue 置 null 并沿 TwoWay 把空串推回 PortValue ——
            //   不挡住的话，"换端口 / 重取候选"这一步就把端口值抹成默认值了
            //   （用户看到的是"重新打开参数面板，之前填的轴名没了"）。
            var keepValue = port.Value;
            editor._suppressPortWrite = true;
            editor._refillingOptions = true;
            try
            {
                editor.EnumOptions.Clear();

                if (port.DataType?.IsEnum == true)
                {
                    foreach (var name in Enum.GetNames(port.DataType))
                        editor.EnumOptions.Add(StepConfigOption.ForEnum(port.DataType, name));
                }
                else if (port.IsFunctionalEnum && port.OptionKind is { } kind)
                {
                    // 候选来自方案（有哪些卡、哪些轴）—— 绑定窗口同样每次打开都现取，
                    // 这里保持同一份语义：不取快照。
                    // 用 GetOptionsResult 而不是 GetOptionItems：后者把"没登记来源 / 来源抛异常 /
                    // 登记了但为空"三种情况都压成同一个空列表，界面就没法把程序缺陷说出来
                    //（2026-10-24 口径：名字类字段不再手填，必须说清为什么不给选）。
                    var result = StepConfigOptionSource.GetOptionsResult(kind);
                    editor.OptionEmptyReason = DescribeEmptyReason(port.Name, result);
                    editor.IsOptionEmptyDefect = result.IsDefect;

                    foreach (var option in result.Items)
                        editor.EnumOptions.Add(option);
                }
            }
            finally
            {
                editor._refillingOptions = false;
                editor._suppressPortWrite = false;
            }

            // 候选变了 → 渲染形态跟着重算（空候选 = 不可编辑的空态，不再退化手填）
            editor.RecalculateOptionMode();

            // 万一端口值仍被绑定的副作用改掉，顶回原值（R35 的后半条）
            if (!Equals(port.Value, keepValue))
                port.Value = keepValue;

            // 订阅端口值变化，同步刷新输入框显示
            port.ValueChanged -= editor.OnPortValueChanged;
            port.ValueChanged += editor.OnPortValueChanged;

            editor.SyncPortValue();
            editor.RestoreLinkState();
            editor.UpdateButtonVisibility();
        }

        /// <summary>
        /// 把 <see cref="StepConfigOptionResult"/> 翻译成用户能看懂的一句话（空态显示用）。
        /// 文案要能回答两个问题：**为什么这里没有可选项**、**我该做什么**。
        /// 缺陷态刻意写得像缺陷（"程序缺陷…请反馈"），因为用户在这里做不了任何事。
        /// </summary>
        private static string DescribeEmptyReason(string portName, StepConfigOptionResult result)
        {
            switch (result.Failure)
            {
                case StepConfigOptionFailureKind.NoProvider:
                    return $"程序缺陷：宿主没有为「{result.Kind}」登记候选来源，"
                         + $"「{portName}」本应有可选项。请把这个提示反馈给开发。";

                case StepConfigOptionFailureKind.ProviderThrew:
                    return $"程序缺陷：取「{result.Kind}」候选时出错 —— {result.ProviderErrors[0]}。"
                         + $"请把这个提示反馈给开发。";

                case StepConfigOptionFailureKind.EmptyList:
                    return result.Kind == StepConfigOptionKind.MotionAxisName
                        ? "当前方案里没有已启用的轴：先到「运动卡设置」配好卡与轴，再回到这里选择。"
                        : $"当前方案里没有可用的「{result.Kind}」候选：先到对应的设备设置里配好，再回到这里选择。";

                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// 按「封闭集合 + 当前候选数」重算渲染形态。
        /// 候选集合每次变化（换端口、重取候选、外部清空）都必须走这里 ——
        /// 否则会出现"候选没了但还停在下拉"或反之。
        /// </summary>
        private void RecalculateOptionMode()
        {
            bool closedSet = IsClosedValuePort;
            bool hasOptions = EnumOptions.Count > 0;

            // 有候选才给下拉；空候选不能停在"打不开的空下拉"上
            IsOptionPort = closedSet && hasOptions;

            // 空候选：渲染成"不可编辑 + 说明原因"（2026-10-24 口径）。
            // 手填退化恒关 —— 轴名/卡名是框架权威清单，手填不存在的名字要到运行期才炸。
            IsOptionEmpty = closedSet && !hasOptions;
            IsHandFillFallback = false;

            UpdateButtonVisibility();
        }

        private static void OnRestoreStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((LinkableValueEditor)d).RestoreLinkState();
        }

        private static void OnPortValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            // 输入框编辑 → 写回端口
            var editor = (LinkableValueEditor)d;
            editor.UpdateOptionEmptyState();
            if (editor.Port == null || !editor.IsTextEditable) return;

            var text = (string)e.NewValue;

            // ① 重填候选期间：空串是 ComboBox 丢选中项推回来的副作用，不是用户编辑（R35）。
            if (editor._suppressPortWrite) return;

            // ② 封闭集合端口没有"清空"这个合法动作（✕ 对它们恒隐藏，理由同 UpdateButtonVisibility）：
            //    空值只可能来自下拉因候选变化丢了选中项 —— 保留端口原值。
            //    否则"重取一次候选 / 换一次端口"就把已配好的轴名、卡名抹成默认值了。
            if (editor.IsClosedValuePort && string.IsNullOrEmpty(text)) return;

            // ②b 空候选态（2026-10-24 口径）：名字类端口取不到候选时**不接受任何手填值**。
            //     主输入框在 XAML 里已经折叠，但 PortValue 仍可从代码/绑定/老连线回填路径被写，
            //     所以判据必须落在写回点：值只能来自候选项，凭空打进去的名字运行期一定解析不到。
            if (editor.IsOptionEmpty) return;

            // ③ 封闭端口上还挂着旧上游连线（老 .vms：当年它还是普通 string 端口时绑的）：
            //    用户这次选了一项 = 明确要改成固定值，先拆线再写值。
            //    不拆的话编译期仍会应用旧连线，把用户刚选的值盖掉（面板看起来改了、跑起来没改）。
            //    只有"非空文本"会走到这里（上面 ② 已挡掉候选重填推回来的空串），
            //    所以程序灌值不会误拆。
            if (editor.IsClosedValuePort && editor.IsLinked)
            {
                editor.StepData?.RemoveLink(editor.Port.Name);
                editor.SetCurrentValue(LinkAddressProperty, null);
            }

            try
            {
                // 必须走共享的 ValueConverter（不是裸 Convert.ChangeType）：
                // ChangeType 不认"字符串→枚举"，抛异常被下面的 catch 吞掉，
                // 表现为"枚举端口在下拉里选了、点确定后又还原成默认值"（所有无自定义视图的
                // 枚举端口都中招，如「几何查找」的查找类型、数学计算的运算类型）。
                // ValueConverter 对枚举按名字 Enum.Parse（与 JSON 加载路径同一口径）。
                editor.Port.Value = string.IsNullOrEmpty(text)
                    ? GetDefaultValue(editor.Port.DataType)
                    : ValueConverter.Convert(text, editor.Port.DataType);
            }
            catch { /* 类型转换失败忽略，保留原值 */ }
        }

        private void OnPortValueChanged(object sender, EventArgs e)
        {
            // 端口灌值发生在后台线程（试运行桥接 / 流程运行给端口赋值），而本控件是 UI 对象——
            // 下面读 IsLinked / 写 PortValue 都是依赖属性操作，异线程直接碰会抛
            // "调用线程无法访问此对象"（VerifyAccess），导致整个试运行失败。一律投递回 UI 线程。
            var dispatcher = Dispatcher;
            if (!dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(new Action(() => OnPortValueChanged(sender, e)));
                return;
            }

            // 端口外部变更（如链接灌值）→ 同步输入框
            if (IsLinked) return;
            SyncPortValue();
        }

        /// <summary>
        /// 判断端口类型是否支持文本输入
        /// 白名单：string、object、基元类型（int/double/bool/...）、enum、decimal
        ///
        /// object 必须在名单里：「变量赋值」的「值」端口就是 InputPort&lt;object&gt;
        /// （因为它要能接任意类型的上游结果）。若不认 object，那一行就只剩链接按钮、没有输入框 ——
        /// 而它最常见的用法恰恰是手填一个常量。
        /// </summary>
        private static bool IsTextEditableType(Type type)
        {
            if (type == null) return false;
            return type == typeof(string)
                || type == typeof(object)
                || type.IsPrimitive
                || type.IsEnum
                || type == typeof(decimal);
        }

        /// <summary>
        /// 从端口读取当前值，刷新 PortValue 显示
        /// </summary>
        private void SyncPortValue()
        {
            if (Port == null) return;
            var val = Port.Value;
            SetCurrentValue(PortValueProperty, val?.ToString() ?? string.Empty);
        }

        private void RestoreLinkState()
        {
            if (StepData == null || Port == null) return;
            SetCurrentValue(LinkAddressProperty, StepData.GetLinkedAddress(Port.Name));
        }

        /// <summary>
        /// 更新占位提示和按钮可见性（由 IsLinked / IsTextEditable 驱动）
        /// </summary>
        private void UpdateButtonVisibility()
        {
            // ★ 枚举端口：候选是**封闭集合**，只能选 —— 因此**不给链接、也不给清除**。
            //
            // 两条理由都是硬的：
            //   ① 绑定的语义是"这个值由上游运行时决定"，而上游的值未必是合法枚举 ——
            //      绑上去等于把非法值塞进一个封闭集合，落盘/取用时都无处安放；
            //   ② "清除"对枚举没有意义：清掉之后没有一个"空枚举"可以表示。
            // 这也正是 InputPort.IsFunctionalEnum 的原始语义（"不需要链接上游变量"）。
            //
            // 反过来说：需要"由上游决定走哪个分支"的场景，该用普通端口（string/int）而不是枚举 ——
            // 那是两个不同的表达能力，不该混在一个控件上。
            // 候选来自**方案**的端口（枚举、运动卡地址、逻辑轴名……）只能选、不能绑。
            // 它们的合法值由方案决定，绑上游等于允许流程去填一个方案里不存在的卡/轴 ——
            // 那不是"灵活"，而是把一个编译期就能发现的问题推到运行期（"找不到运动卡"）。
            // IsFunctionalEnum 的原始语义正是"**不需要**链接上游变量"（见 InputPort 的注释），
            // 所以这里用它作判据，而不是只认枚举。
            //
            // ★ 判据是「封闭集合」（不看候选是否为空）：候选取不到时这一行是空态，
            //   但**依然不许链接** —— 轴名/卡名这类名字类端口挂绑定弹窗的后果是运行期
            //   "拿值当名字"（R33），与候选有没有配好无关。清除同理：清空后没有合法值可表示。
            var enumLocked = IsClosedValuePort;

            // 占位提示：不可文本输入 且 未链接 时显示（空态由 PART_OptionEmpty 单独接管）
            PART_Placeholder.Visibility =
                (!IsTextEditable && !IsLinked && !IsOptionEmpty) ? Visibility.Visible : Visibility.Collapsed;

            // ✕ 按钮：枚举恒隐藏；其余情况在"不可文本输入 且 未链接"时隐藏（没有值可清空，只能链接）
            PART_UnlinkBtn.Visibility =
                enumLocked || (!IsTextEditable && !IsLinked) ? Visibility.Collapsed : Visibility.Visible;

            // 🔗 按钮：枚举恒隐藏（理由见上）
            PART_LinkBtn.Visibility = enumLocked ? Visibility.Collapsed : Visibility.Visible;

            // 封闭端口 + 旧上游连线：下拉照常显示（XAML 里 IsOptionPort 不再看 IsLinked），
            // 这里给一句解释，否则用户会疑惑"明明连了线，怎么还能选"。
            if (PART_EnumBox != null)
            {
                PART_EnumBox.ToolTip = enumLocked && IsLinked
                    ? "该端口不接受上游供值；选择一项会改为固定值并清除旧连线"
                    : null;
            }

            UpdateOptionEmptyState();
        }

        /// <summary>
        /// 空候选态：显示不可编辑的占位框 + 一行原因文案（缺陷态染红）。
        ///
        /// 【为什么不复用输入框】2026-10-24 起名字类端口不再手填：能输入的框就等于允许写入
        /// 一个不存在的轴名/卡名，而校验要到运行期才发生。空态必须是**明确不可编辑**的，
        /// 并且把"为什么没得选、下一步去哪儿"直接写在行内 —— 用户在这一行里做不了别的事。
        /// </summary>
        private void UpdateOptionEmptyState()
        {
            if (PART_OptionEmpty == null || PART_OptionEmptyText == null) return;

            PART_OptionEmpty.Visibility = IsOptionEmpty ? Visibility.Visible : Visibility.Collapsed;
            if (!IsOptionEmpty) return;

            // 占位行（"当前没有可选值"）写死在 XAML 里，这里只填原因行。
            // 没原因时空串 → 原因行自动塌成 0 高，整块退回单行 36 高。
            PART_OptionEmptyText.Text = OptionEmptyReason ?? string.Empty;
            // 缺陷态染红：颜色令牌在 UI 库，Core.Controls 解析不到时保持内联灰（R140 存量口径）。
            if (IsOptionEmptyDefect)
            {
                var danger = TryFindResource("DialogDangerTextBrush") as Brush;
                PART_OptionEmptyText.Foreground = danger ?? new SolidColorBrush(Color.FromRgb(0xC0, 0x2B, 0x2B));
                PART_OptionEmpty.BorderBrush = danger ?? new SolidColorBrush(Color.FromRgb(0xE5, 0x7A, 0x7A));
                PART_OptionEmpty.Background = new SolidColorBrush(Color.FromRgb(0xFE, 0xF2, 0xF2));
            }
            else
            {
                PART_OptionEmptyText.Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
                PART_OptionEmpty.BorderBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1));
                PART_OptionEmpty.Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC));
            }
            PART_OptionEmpty.ToolTip = PART_OptionEmptyText.Text;
        }

        private static object GetDefaultValue(Type type)
        {
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }

        #endregion

        #region 事件处理

        private void LinkBtn_Click(object sender, RoutedEventArgs e)
        {
            var stepData = StepData;
            var port = Port;
            if (stepData == null || port == null) return;

            // 发布链接请求，主程序呼出 DataBindView 单绑定模式，选中后回调
            GlobalEventBus.Publish(new LinkPathEvent
            {
                InputPortName = port.Name,
                TargetType = port.DataType ?? typeof(object),
                AllowGlobals = AllowGlobals,
                OnBound = link =>
                {
                    stepData.SetLink(port.Name, link);
                    SetCurrentValue(LinkAddressProperty, link.DisplayAddress);
                }
            });
        }

        private void UnlinkBtn_Click(object sender, RoutedEventArgs e)
        {
            if (IsLinked)
            {
                // 已链接：解除链接，恢复固定值编辑
                StepData?.RemoveLink(Port.Name);
                SetCurrentValue(LinkAddressProperty, null);
                SyncPortValue(); // 恢复显示端口当前固定值
            }
            else
            {
                // 未链接：清空固定值
                SetCurrentValue(PortValueProperty, string.Empty);
            }
        }

        #endregion
    }
}
