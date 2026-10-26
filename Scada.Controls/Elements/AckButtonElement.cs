using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using VisionMaster.Scada;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 报警确认按钮图元（TypeKey = <c>Hmi.AckButton</c>）：一键确认当前报警。
    ///
    /// <b>它和「按钮」的分工</b>
    /// ---------
    /// 普通按钮是动作入口，点下去干什么由事件动作表配；本图元<b>只做一件事</b>：
    /// 调 <see cref="ScadaAlarmEngine.AcknowledgeAll"/> 或 <see cref="ScadaAlarmEngine.Acknowledge"/>。
    /// 把这件事做成专用图元，是因为它要读报警引擎（控件库之外的领域对象）、
    /// 要判权限、要把"确认了几条"反馈给操作员——这些都不是一条通用动作能表达的。
    ///
    /// <b>两个范围</b>
    /// ---------
    /// <see cref="Scope"/> = <c>All</c>：确认全部待确认报警；
    /// <c>Oldest</c>：按 <see cref="ScadaAlarmRecord.ActivatedAtUtc"/> 取真正最早的一条
    /// （<see cref="ScadaAlarmEngine.ActiveAlarms"/> 的排序是严重度优先，不能直接拿 First）。
    ///
    /// <b>为什么订阅报警事件</b>
    /// ---------
    /// 按钮上通常带一个"还有几条没确认"的徽标。只在点击后刷新的话，报警自己来了/走了，
    /// 徽标会一直停在旧数字上——操作员会以为报警系统坏了。所以与报警条同一做法：
    /// 挂引擎的三个事件、任何一条来了就整表重读一次计数；事件可能在轮询线程上抛，
    /// 所以统一切回本控件的 Dispatcher。
    ///
    /// <b>权限</b>
    /// ---------
    /// <see cref="ScadaElement.RequiredRole"/> 为 null 一律放行；否则必须过
    /// <see cref="IScadaAccessPolicy.CanOperate"/>。没权限时 <see cref="CanOperate"/> = false、
    /// <see cref="CommandMessage"/> 给出原因，点击不碰引擎。
    /// </summary>
    [TemplatePart(Name = PartAckButton, Type = typeof(Button))]
    public partial class AckButtonElement : ScadaElementBase
    {
        /// <summary>模板部件名：确认按钮本体</summary>
        public const string PartAckButton = "PART_AckButton";

        /// <summary>范围取值：全部待确认报警</summary>
        public const string ScopeAll = "All";

        /// <summary>范围取值：当前报警列表里的第一条</summary>
        public const string ScopeOldest = "Oldest";

        // 声明次序 = 属性面板里的行序。

        /// <summary>确认范围</summary>
        [ScadaProperty(Key = "Scope", DisplayName = "确认范围", Kind = ElementPropertyKind.Choice,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "All",
                       Choices = new[] { "All", "Oldest" }, AffectsRender = false,
                       Description = "All = 确认全部待确认报警；Oldest = 确认当前报警列表的第一条" +
                                     "（引擎按严重度降序、同级按激活时间降序排，第一条就是最该先处理的）")]
        public partial string? Scope { get; set; }

        private static readonly DependencyPropertyKey UnacknowledgedCountPropertyKey =
            DependencyProperty.RegisterReadOnly(
                nameof(UnacknowledgedCount), typeof(int), typeof(AckButtonElement),
                new PropertyMetadata(0));

        /// <summary>当前待确认报警条数（只读；跟着报警引擎刷新）</summary>
        public static readonly DependencyProperty UnacknowledgedCountProperty =
            UnacknowledgedCountPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey CanOperatePropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(CanOperate), typeof(bool), typeof(AckButtonElement),
            new PropertyMetadata(false));

        /// <summary>此刻能不能确认（只读；有引擎且权限够）</summary>
        public static readonly DependencyProperty CanOperateProperty =
            CanOperatePropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey CommandMessagePropertyKey =
            DependencyProperty.RegisterReadOnly(
                nameof(CommandMessage), typeof(string), typeof(AckButtonElement),
                new PropertyMetadata(string.Empty));

        /// <summary>上一次操作的结果 / 权限原因（只读；给按钮旁边的状态行用）</summary>
        public static readonly DependencyProperty CommandMessageProperty =
            CommandMessagePropertyKey.DependencyProperty;

        static AckButtonElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(AckButtonElement),
                new FrameworkPropertyMetadata(typeof(AckButtonElement)));
        }

        public AckButtonElement() => RefreshState();

        /// <summary>当前待确认报警条数（只读，见 <see cref="UnacknowledgedCountProperty"/>）</summary>
        public int UnacknowledgedCount => (int)GetValue(UnacknowledgedCountProperty);

        /// <summary>此刻能不能确认（只读，见 <see cref="CanOperateProperty"/>）</summary>
        public bool CanOperate => (bool)GetValue(CanOperateProperty);

        /// <summary>上一次操作的结果 / 权限原因（只读，见 <see cref="CommandMessageProperty"/>）</summary>
        public string? CommandMessage => (string?)GetValue(CommandMessageProperty);

        private bool IsOldestScope
            => string.Equals(Scope, ScopeOldest, StringComparison.OrdinalIgnoreCase);

        protected override void OnRuntimeContextChanged(ScadaRuntimeContext? oldContext, ScadaRuntimeContext? newContext)
        {
            if (oldContext != null)
            {
                oldContext.Alarms.AlarmRaised -= OnAlarmEvent;
                oldContext.Alarms.AlarmChanged -= OnAlarmEvent;
                oldContext.Alarms.AlarmCleared -= OnAlarmEvent;
            }

            if (newContext != null)
            {
                newContext.Alarms.AlarmRaised += OnAlarmEvent;
                newContext.Alarms.AlarmChanged += OnAlarmEvent;
                newContext.Alarms.AlarmCleared += OnAlarmEvent;
            }

            RefreshState();
        }

        protected override void OnElementRefreshed() => RefreshState();

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            if (_button is not null)
                _button.Click -= OnButtonClick;

            _button = GetTemplateChild(PartAckButton) as Button;

            if (_button is not null)
                _button.Click += OnButtonClick;

            RefreshState();
        }

        private Button? _button;

        private void OnButtonClick(object sender, RoutedEventArgs e) => Acknowledge();

        /// <summary>
        /// 执行一次确认。<b>鼠标点击与断言共用这一个入口</b>（同 <see cref="BitButtonElement.BeginPress"/>）：
        /// 门禁、引擎调用、消息与计数刷新都只写在这一处，断言不必去伪造 WPF 的按钮点击。
        /// </summary>
        public void Acknowledge()
        {
            RefreshState();

            if (!CanOperate)
            {
                if (RuntimeContext != null)
                    SetValue(CommandMessagePropertyKey, "当前角色没有操作权限");

                return;
            }

            var alarms = RuntimeContext!.Alarms;
            int changed;

            if (IsOldestScope)
            {
                // “最早”按激活时间取真正最早的一条；ActiveAlarms 的排序是
                // “严重度降序、同级按时间降序”（给报警条扫一眼用的），不能直接拿 First。
                var oldest = alarms.ActiveAlarms
                    .OrderBy(a => a.ActivatedAtUtc)
                    .FirstOrDefault();
                changed = oldest is null ? 0 : alarms.Acknowledge(oldest.AlarmId);
            }
            else
            {
                changed = alarms.AcknowledgeAll();
            }

            SetValue(CommandMessagePropertyKey,
                changed > 0 ? $"已确认 {changed} 条" : "没有待确认报警");

            RefreshState();
        }

        /// <summary>
        /// 重读引擎状态：待确认条数 + 权限。权限不足时把原因摆到 <see cref="CommandMessage"/>。
        /// 权限够时不动消息——那可能是上一次操作的结果（"已确认 2 条"），不该被刷新抹掉。
        /// </summary>
        private void RefreshState()
        {
            var context = RuntimeContext;

            SetValue(UnacknowledgedCountPropertyKey, context?.Alarms.UnacknowledgedCount ?? 0);

            bool allowed = context != null && context.AccessPolicy.CanElement(Element);

            SetValue(CanOperatePropertyKey, allowed);

            if (context != null && !allowed)
            {
                SetValue(CommandMessagePropertyKey, "当前角色没有操作权限");
            }
            else if (allowed && CommandMessage == "当前角色没有操作权限")
            {
                // 权限恢复后把上一次的拒绝原因收掉，别让"已经能按了"的按钮还挂着红字。
                SetValue(CommandMessagePropertyKey, string.Empty);
            }
        }

        /// <summary>引擎的任何动静 → 切回 UI 线程重读一次计数（三个事件共用）</summary>
        private void OnAlarmEvent(ScadaAlarmRecord record) => RunOnUi(RefreshState);

        /// <summary>
        /// 把一段动作送回本控件的 UI 线程执行（与报警条同一范式）：
        /// 已在 UI 线程就直跑；窗口正在关闭就丢掉（往已停摆的 Dispatcher 排队会抛）。
        /// </summary>
        private void RunOnUi(Action action)
        {
            var dispatcher = Dispatcher;

            if (dispatcher.CheckAccess())
            {
                action();
                return;
            }

            if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
                return;

            dispatcher.BeginInvoke(DispatcherPriority.Background, action);
        }
    }
}
