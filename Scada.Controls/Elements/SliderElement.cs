using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace VisionMaster.Scada.Controls
{
    /// <summary>滑块的写入时机</summary>
    public enum SliderWriteMode
    {
        /// <summary>松手写一次（默认；拖动只改显示，不把变量写几百次）</summary>
        Release,

        /// <summary>拖动中每 100ms 写一次（只用于必须连续跟随的场合）</summary>
        Throttled,
    }

    /// <summary>
    /// 模拟量滑块图元（TypeKey = <c>Hmi.Slider</c>）：连续量"拖动即写"。
    ///
    /// <b>与棒图的边界</b>：棒图只显示比例、不接受输入；滑块把同一段量程变成可拖的轨道，
    /// 拖动时只更新<b>显示值</b>，松手才通过 <see cref="IScadaValueWriter"/> 写一次变量。
    /// 这样不会因为一次拖动把 PLC 写几百次（设计基线 07 的硬约定）。
    ///
    /// <b>写失败必须回弹</b>：写入被拒时把显示值退回模型 <see cref="Value"/>（数据泵读回的当前值），
    /// 轨道变红并把原因放进 <see cref="WriteError"/>——不许停在"看起来已经改了"的位置。
    ///
    /// <b>可编辑的四道门</b>与数值域/多档旋钮同一口径：模式不是只读、有写通道、
    /// <see cref="Value"/> 真绑了变量、当前角色过得了权限。
    /// </summary>
    [TemplatePart(Name = PartTrackHost, Type = typeof(Grid))]
    [TemplatePart(Name = PartTrack, Type = typeof(Border))]
    [TemplatePart(Name = PartFill, Type = typeof(Border))]
    [TemplatePart(Name = PartThumb, Type = typeof(Border))]
    [TemplatePart(Name = PartValueText, Type = typeof(TextBlock))]
    public partial class SliderElement : ScadaElementBase
    {
        public const string PartTrackHost = "PART_TrackHost";
        public const string PartTrack = "PART_Track";
        public const string PartFill = "PART_Fill";
        public const string PartThumb = "PART_Thumb";
        public const string PartValueText = "PART_ValueText";

        /// <summary>模式：只读</summary>
        public const string ModeOutput = "Output";

        /// <summary>模式：读写</summary>
        public const string ModeInputOutput = "InputOutput";

        private const string ValueKey = "Value";
        private const double ThumbSize = 20d;
        private const double TrackThickness = 8d;

        private static readonly Brush ErrorBrush = ScadaBrushes.Frozen("#FFEF4444");

        private double _displayValue;
        private bool _dragging;
        private readonly Stopwatch _throttle = new();

        /// <summary>当前值（绑到工程变量上；拖动期间不改它，松手才写）</summary>
        [ScadaProperty(Key = "Value", DisplayName = "当前值", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "0", IsBindable = true,
                       Description = "绑到工程变量上。拖动只改显示，松手（或键盘步进）才写一次变量；写失败回弹到这个值")]
        public partial double Value { get; set; }

        /// <summary>量程下限</summary>
        [ScadaProperty(Key = "Minimum", DisplayName = "量程下限", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "0",
                       Description = "下限不小于上限时按 0 比例显示，不会让非法量程打断渲染")]
        public partial double Minimum { get; set; }

        /// <summary>量程上限</summary>
        [ScadaProperty(Key = "Maximum", DisplayName = "量程上限", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "100")]
        public partial double Maximum { get; set; }

        /// <summary>键盘 / 点按步进（拖动不受它限制，显示连续值）</summary>
        [ScadaProperty(Key = "Step", DisplayName = "步长", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "1", Min = 0,
                       Description = "键盘左右/上下键每次改变多少；拖动仍是连续值，不受步长限制")]
        public partial double Step { get; set; }

        /// <summary>数值显示格式（标准 .NET 数字格式串，可带单位）</summary>
        [ScadaProperty(Key = "ValueFormat", DisplayName = "数值格式", Kind = ElementPropertyKind.Text,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "0.#",
                       Description = "标准 .NET 数字格式串，可带单位（如 0.0 ℃）；写错退回通用格式，不让画面出错")]
        public partial string? ValueFormat { get; set; }

        /// <summary>走向</summary>
        [ScadaProperty(Key = "Orientation", DisplayName = "走向", Kind = ElementPropertyKind.Choice,
                       Group = ScadaPropertyGroups.Appearance, DefaultValue = "Horizontal",
                       Choices = new[] { "Horizontal", "Vertical" },
                       Description = "横向从左往右；纵向从下往上（方向固定，不提供翻转，避免与量程语义打架）")]
        public partial ProgressBarOrientation Orientation { get; set; }

        /// <summary>轨道底色</summary>
        [ScadaProperty(Key = "TrackColor", DisplayName = "轨道色", Kind = ElementPropertyKind.Color,
                       Group = ScadaPropertyGroups.Appearance, DefaultValue = "#FF26313D",
                       Description = "未填充那一段的颜色；与「填充色」要有明显明度差")]
        public partial Brush? TrackColor { get; set; }

        /// <summary>是否显示数值文字</summary>
        [ScadaProperty(Key = "ShowValue", DisplayName = "显示数值", Kind = ElementPropertyKind.Bool,
                       Group = ScadaPropertyGroups.Text, DefaultValue = "True")]
        public partial bool ShowValue { get; set; }

        /// <summary>模式：读写 / 只读</summary>
        [ScadaProperty(Key = "Mode", DisplayName = "模式", Kind = ElementPropertyKind.Choice,
                       Group = ScadaPropertyGroups.General, DefaultValue = "InputOutput",
                       Choices = new[] { "InputOutput", "Output" }, AffectsRender = false,
                       Description = "InputOutput = 可拖可写（还需绑变量、有写通道、权限够）；Output = 只看不写")]
        public partial string? Mode { get; set; }

        /// <summary>写入时机：松手一次 / 拖动中 100ms 节流</summary>
        [ScadaProperty(Key = "WriteMode", DisplayName = "写入时机", Kind = ElementPropertyKind.Choice,
                       Group = ScadaPropertyGroups.General, DefaultValue = "Release",
                       Choices = new[] { "Release", "Throttled" }, AffectsRender = false,
                       Description = "Release = 松手写一次（默认）；Throttled = 拖动中每 100ms 写一次（仅用于必须连续跟随的场合）")]
        public partial SliderWriteMode WriteMode { get; set; }

        /// <summary>写入前是否二次确认（速度/压力/温度类建议打开）</summary>
        [ScadaProperty(Key = "ConfirmDanger", DisplayName = "写入前确认", Kind = ElementPropertyKind.Bool,
                       Group = ScadaPropertyGroups.General, DefaultValue = "False", AffectsRender = false,
                       Description = "打开后松手写入前弹一次二次确认；没有确认出口时拒绝写入，不静默放行")]
        public partial bool ConfirmDanger { get; set; }

        private static readonly DependencyPropertyKey IsEditablePropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(IsEditable), typeof(bool), typeof(SliderElement),
            new PropertyMetadata(false));

        /// <summary>此刻能不能拖/写（只读；四道门见类注释）</summary>
        public static readonly DependencyProperty IsEditableProperty = IsEditablePropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey WriteErrorPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(WriteError), typeof(string), typeof(SliderElement),
            new PropertyMetadata(string.Empty));

        /// <summary>上一次写入失败的原因（只读；成功时为空串）</summary>
        public static readonly DependencyProperty WriteErrorProperty = WriteErrorPropertyKey.DependencyProperty;

        static SliderElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(SliderElement),
                new FrameworkPropertyMetadata(typeof(SliderElement)));
        }

        public SliderElement()
        {
            Focusable = true;
            IsTabStop = true;
            _displayValue = Value;
            SizeChanged += (_, _) => UpdateVisuals();
        }

        public bool IsEditable => (bool)GetValue(IsEditableProperty);

        public string? WriteError
        {
            get => (string?)GetValue(WriteErrorProperty);
            private set
            {
                SetValue(WriteErrorPropertyKey, value ?? string.Empty);
                UpdateVisuals();
            }
        }

        /// <summary>当前显示值（拖动中会与模型 Value 不同；松手成功后才由数据泵追平）</summary>
        public double DisplayValue => _displayValue;

        /// <summary>显示值在量程里的比例（0~1；纵向 0 在底部）</summary>
        public double Ratio => ComputeRatio(_displayValue);

        public bool IsDragging => _dragging;

        private bool CanOperate
            => !string.Equals(Mode, ModeOutput, StringComparison.OrdinalIgnoreCase)
               && RuntimeContext?.Writer is not null
               && Element?.FindBinding(ValueKey) is { IsEnabled: true }
               && IsEnabled
               && (Element?.RequiredRole is not { } required
                   || RuntimeContext?.AccessPolicy.CanOperate(required, out _) == true);

        protected override void OnRuntimeContextChanged(ScadaRuntimeContext? oldContext, ScadaRuntimeContext? newContext)
            => UpdateEditable();

        protected override void OnElementRefreshed()
        {
            // 描述符按源码次序落地（Value 在 Minimum/Maximum 之前），逐个钩子用当时的默认量程
            // 夹会把 150/200 提前夹成 100、把 -50/-100 提前夹成 0。等所有属性都落地后再按最终量程夹一次。
            if (!_dragging)
                _displayValue = Clamp(Value);

            UpdateEditable();
            UpdateVisuals();
        }

        public override void OnApplyTemplate()
        {
            if (_trackHost != null)
                _trackHost.SizeChanged -= OnTrackHostSizeChanged;

            base.OnApplyTemplate();

            _trackHost = GetTemplateChild(PartTrackHost) as Grid;

            if (_trackHost != null)
                _trackHost.SizeChanged += OnTrackHostSizeChanged;

            UpdateEditable();
            UpdateVisuals();
        }

        private Grid? _trackHost;

        private void OnTrackHostSizeChanged(object sender, SizeChangedEventArgs e) => UpdateVisuals();

        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);

            if (e.Property == FillProperty
                || e.Property == ForegroundProperty
                || e.Property == IsEnabledProperty
                || e.Property == CornerRadiusProperty)
            {
                UpdateEditable();
                UpdateVisuals();
            }
        }

        partial void OnValuePropertyChanged()
        {
            // 这里不 Clamp：描述符落地次序是 Value → Minimum → Maximum，此刻的量程可能还是默认值。
            // 最终量程在 OnElementRefreshed 里统一夹一次。
            if (!_dragging)
                _displayValue = double.IsFinite(Value) ? Value : 0d;

            UpdateVisuals();
        }

        partial void OnMinimumPropertyChanged()
        {
            _displayValue = Clamp(_displayValue);
            UpdateVisuals();
        }

        partial void OnMaximumPropertyChanged()
        {
            _displayValue = Clamp(_displayValue);
            UpdateVisuals();
        }

        partial void OnValueFormatPropertyChanged() => UpdateVisuals();
        partial void OnOrientationPropertyChanged() => UpdateVisuals();
        partial void OnShowValuePropertyChanged() => UpdateVisuals();
        partial void OnModePropertyChanged() => UpdateEditable();
        partial void OnWriteModePropertyChanged() => UpdateVisuals();
        partial void OnConfirmDangerPropertyChanged() => UpdateVisuals();

        /// <summary>
        /// 从比例开始拖动（鼠标与断言共用入口）。<paramref name="ratio"/> 0 = 下限，1 = 上限。
        /// 不可写时不开始，原因写进 <see cref="WriteError"/>。
        /// </summary>
        public bool BeginDragAtRatio(double ratio, bool captureMouse = true)
        {
            WriteError = string.Empty;

            if (!IsEditable)
            {
                WriteError = DescribeRejection();
                return false;
            }

            if (double.IsNaN(ratio) || double.IsInfinity(ratio))
                return false;

            // 真实鼠标路径必须拿到捕获；断言路径可以传 false（没有 PresentationSource 时
            // CaptureMouse 必然失败，不能因此把逻辑测试挡在门外）。
            if (captureMouse && !CaptureMouse())
            {
                WriteError = "无法捕获鼠标，未开始拖动";
                return false;
            }

            _dragging = true;
            _displayValue = ValueFromRatio(ratio);
            _throttle.Restart();
            UpdateVisuals();

            return true;
        }

        /// <summary>拖动到某个比例；只改显示。Throttled 模式下每 100ms 写一次。</summary>
        public void DragToRatio(double ratio)
        {
            if (!_dragging || double.IsNaN(ratio) || double.IsInfinity(ratio))
                return;

            _displayValue = ValueFromRatio(ratio);
            UpdateVisuals();

            if (WriteMode == SliderWriteMode.Throttled
                && !ConfirmDanger
                && _throttle.ElapsedMilliseconds >= 100)
            {
                _throttle.Restart();

                if (!WriteCurrent(allowConfirm: false))
                {
                    _dragging = false;
                    ReleaseMouseCapture();
                }
            }
        }

        /// <summary>松手：写一次最终值；失败回弹到模型值并保留错误原因。</summary>
        public bool EndDrag()
        {
            if (!_dragging)
                return false;

            _dragging = false;
            ReleaseMouseCapture();

            if (Math.Abs(_displayValue - Value) < 1e-9)
                return true;

            return WriteCurrent(allowConfirm: true);
        }

        /// <summary>键盘/点按步进：立即写一次（与拖动松手同一出口）。</summary>
        public bool Nudge(double delta)
        {
            WriteError = string.Empty;

            if (!IsEditable)
            {
                WriteError = DescribeRejection();
                return false;
            }

            var next = Clamp(_displayValue + delta);

            if (Math.Abs(next - _displayValue) < 1e-9)
                return true;

            _displayValue = next;
            UpdateVisuals();
            return WriteCurrent(allowConfirm: true);
        }

        protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseLeftButtonDown(e);

            if (e.Handled || !IsEditable)
                return;

            Focus();
            BeginDragAtRatio(RatioFromPoint(e.GetPosition(TrackHostOrSelf())));
            e.Handled = true;
        }

        protected override void OnPreviewMouseMove(MouseEventArgs e)
        {
            base.OnPreviewMouseMove(e);

            if (!_dragging)
                return;

            // 左键已经松开（MouseUp 因切窗/系统抢占丢失）：按松手处理，写最终值；
            // 不能只把 _dragging 置 false 就退出，否则拇指停在拖动位置、变量还是旧值。
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                EndDrag();
                return;
            }

            DragToRatio(RatioFromPoint(e.GetPosition(TrackHostOrSelf())));
            e.Handled = true;
        }

        protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseLeftButtonUp(e);

            if (!_dragging)
                return;

            DragToRatio(RatioFromPoint(e.GetPosition(TrackHostOrSelf())));
            EndDrag();
            e.Handled = true;
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);

            // 捕获被系统抢走（切窗口/弹窗）也要把最终值写掉，不能停在拖动中途。
            if (_dragging)
            {
                _dragging = false;
                WriteCurrent(allowConfirm: true);
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (!IsEditable)
                return;

            var step = Step > 0 ? Step : 1d;
            var handled = true;

            switch (e.Key)
            {
                case Key.Left:
                case Key.Down:
                    Nudge(-step);
                    break;

                case Key.Right:
                case Key.Up:
                    Nudge(step);
                    break;

                case Key.Home:
                    Nudge(Minimum - _displayValue);
                    break;

                case Key.End:
                    Nudge(Maximum - _displayValue);
                    break;

                default:
                    handled = false;
                    break;
            }

            e.Handled = handled;
        }

        private FrameworkElement TrackHostOrSelf()
            => GetTemplateChild(PartTrackHost) as FrameworkElement ?? this;

        private double RatioFromPoint(Point point)
        {
            var host = TrackHostOrSelf();

            if (Orientation == ProgressBarOrientation.Vertical)
            {
                var height = host.ActualHeight;
                return height > 0 ? 1d - point.Y / height : 0d;
            }

            var width = host.ActualWidth;
            return width > 0 ? point.X / width : 0d;
        }

        private double ValueFromRatio(double ratio)
        {
            var min = double.IsFinite(Minimum) ? Minimum : 0d;
            var max = double.IsFinite(Maximum) ? Maximum : min;

            if (!double.IsFinite(ratio) || max <= min)
                return min;

            return Clamp(min + Math.Clamp(ratio, 0d, 1d) * (max - min));
        }

        private double ComputeRatio(double value)
        {
            var min = double.IsFinite(Minimum) ? Minimum : 0d;
            var max = double.IsFinite(Maximum) ? Maximum : min;

            if (!double.IsFinite(value) || max <= min)
                return 0d;

            return Math.Clamp((value - min) / (max - min), 0d, 1d);
        }

        private double Clamp(double value)
        {
            var min = double.IsFinite(Minimum) ? Minimum : 0d;
            var max = double.IsFinite(Maximum) ? Maximum : min;

            if (!double.IsFinite(value) || max <= min)
                return min;

            return Math.Clamp(value, min, max);
        }

        private bool WriteCurrent(bool allowConfirm)
        {
            if (!CanOperate)
            {
                WriteError = DescribeRejection();
                ResetDisplay();
                return false;
            }

            // 确认框是同步模态（内部泵 DispatcherFrame）：确认期间数据泵会把变量回读写进 Value，
            // OnValuePropertyChanged 随即覆盖 _displayValue。这里先把"用户要写的值"固定下来，
            // 确认文案与真正写入都用它——否则用户确认的是 70，写进去的可能是回读到的 50.2。
            var valueToWrite = _displayValue;

            if (ConfirmDanger && allowConfirm)
            {
                if (RuntimeContext?.Confirmations is not { } confirmations)
                {
                    WriteError = "没有二次确认出口，未写入";
                    ResetDisplay();
                    return false;
                }

                var request = new ScadaConfirmationRequest(
                    "确认写入",
                    $"{Element?.Name ?? "滑块"} → {FormatValue(valueToWrite)}",
                    Hint: "写入后变量会立即改变",
                    Danger: true,
                    PrimaryText: "确认写入");

                bool confirmed;

                try
                {
                    confirmed = confirmations.Confirm(request);
                }
                catch (Exception ex)
                {
                    WriteError = $"确认出口异常：{ex.Message}";
                    ResetDisplay();
                    return false;
                }

                if (!confirmed)
                {
                    WriteError = string.Empty;
                    ResetDisplay();
                    return false;
                }

                // 模态期间权限/绑定/启用状态可能变了，写前按当前状态复核一次。
                if (!CanOperate)
                {
                    WriteError = DescribeRejection();
                    ResetDisplay();
                    return false;
                }

                // 量程也可能在模态期间被改（数据泵/属性面板）。用户确认的是 valueToWrite，
                // 若它已落到新量程之外，静默夹取就会写进一个"没被确认过"的值——直接拒绝重来。
                var clamped = Clamp(valueToWrite);

                if (Math.Abs(clamped - valueToWrite) > 1e-9)
                {
                    WriteError = "量程已变化，请重新确认";
                    ResetDisplay();
                    return false;
                }

                valueToWrite = clamped;
            }

            if (RuntimeContext?.Writer is not { } writer || Element is not { } element)
            {
                WriteError = "当前没有写通道，无法写入变量";
                ResetDisplay();
                return false;
            }

            var text = valueToWrite.ToString("R", CultureInfo.InvariantCulture);

            if (!writer.TryWriteText(element, ValueKey, text, out var error))
            {
                WriteError = string.IsNullOrWhiteSpace(error) ? "写入失败，已回弹" : error;
                ResetDisplay();
                return false;
            }

            // 写成功：把显示值恢复到"刚写出去的值"。确认期间被回读覆盖过的话，
            // 不恢复就会在成功后瞬间跳回旧值，看起来像没写进去。
            _displayValue = Clamp(valueToWrite);
            WriteError = string.Empty;
            UpdateVisuals();
            RaiseScadaEvent(ScadaEventType.InputCompleted);
            return true;
        }

        private void ResetDisplay()
        {
            _displayValue = Clamp(Value);
            UpdateVisuals();
        }

        private string DescribeRejection()
        {
            if (string.Equals(Mode, ModeOutput, StringComparison.OrdinalIgnoreCase))
                return "当前为只读模式，未写入";

            if (RuntimeContext?.Writer is null)
                return "当前没有写通道，无法写入变量";

            if (Element?.FindBinding(ValueKey) is not { IsEnabled: true })
                return "「当前值」没有绑定可用的变量，未写入";

            if (!IsEnabled)
                return "图元已禁用，未写入";

            return "当前角色没有操作权限，未写入";
        }

        private void UpdateEditable()
        {
            var editable = CanOperate;
            SetValue(IsEditablePropertyKey, editable);

            if (!editable && _dragging)
            {
                _dragging = false;
                ReleaseMouseCapture();
                ResetDisplay();
            }

            UpdateVisuals();
        }

        private void UpdateVisuals()
        {
            if (GetTemplateChild(PartTrackHost) is not Grid host)
                return;

            var horizontal = Orientation != ProgressBarOrientation.Vertical;
            var length = horizontal ? host.ActualWidth : host.ActualHeight;

            if (!double.IsFinite(length) || length < 0)
                length = 0;

            var ratio = ComputeRatio(_displayValue);
            var fillLength = Math.Max(0, length * ratio);
            var thumbOffset = Math.Clamp(fillLength - ThumbSize / 2, 0, Math.Max(0, length - ThumbSize));
            var corner = double.IsFinite(CornerRadius) ? Math.Max(0, CornerRadius) : 4d;

            // 纵向时数值文字是覆盖层：TrackHost 跨两行、去掉上下边距，
            // 否则默认 48px 高里轨道只剩十几像素，20px 的拇指根本走不动。
            Grid.SetRow(host, horizontal ? 1 : 0);
            Grid.SetRowSpan(host, horizontal ? 1 : 2);
            host.Margin = horizontal ? new Thickness(10, 0, 10, 0) : new Thickness(0);
            host.ToolTip = WriteError;

            if (GetTemplateChild(PartTrack) is Border track)
            {
                track.Width = horizontal ? double.NaN : TrackThickness;
                track.Height = horizontal ? TrackThickness : double.NaN;
                track.HorizontalAlignment = horizontal ? HorizontalAlignment.Stretch : HorizontalAlignment.Center;
                track.VerticalAlignment = horizontal ? VerticalAlignment.Center : VerticalAlignment.Stretch;
                track.BorderBrush = string.IsNullOrEmpty(WriteError) ? Stroke : ErrorBrush;
                track.CornerRadius = new CornerRadius(corner);
            }

            if (GetTemplateChild(PartFill) is Border fill)
            {
                fill.Width = horizontal ? fillLength : TrackThickness;
                fill.Height = horizontal ? TrackThickness : fillLength;
                fill.HorizontalAlignment = horizontal ? HorizontalAlignment.Left : HorizontalAlignment.Center;
                fill.VerticalAlignment = horizontal ? VerticalAlignment.Center : VerticalAlignment.Bottom;
                fill.Background = string.IsNullOrEmpty(WriteError) ? Fill : ErrorBrush;
                fill.CornerRadius = new CornerRadius(corner);
            }

            if (GetTemplateChild(PartThumb) is Border thumb)
            {
                thumb.Width = ThumbSize;
                thumb.Height = ThumbSize;
                thumb.HorizontalAlignment = horizontal ? HorizontalAlignment.Left : HorizontalAlignment.Center;
                thumb.VerticalAlignment = horizontal ? VerticalAlignment.Center : VerticalAlignment.Bottom;
                thumb.Margin = horizontal
                    ? new Thickness(thumbOffset, 0, 0, 0)
                    : new Thickness(0, 0, 0, thumbOffset);
                thumb.Opacity = IsEditable ? 1d : 0.45d;
            }

            if (GetTemplateChild(PartValueText) is TextBlock valueText)
            {
                var error = WriteError;

                Panel.SetZIndex(valueText, 2);
                valueText.Text = string.IsNullOrEmpty(error) ? FormatValue(_displayValue) : error;
                valueText.Foreground = string.IsNullOrEmpty(error) ? Foreground : ErrorBrush;
                valueText.Visibility = ShowValue || !string.IsNullOrEmpty(error)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                valueText.ToolTip = error;
            }
        }

        private string FormatValue(double value)
        {
            if (!double.IsFinite(value))
                return "—";

            var format = string.IsNullOrWhiteSpace(ValueFormat) ? "0.#" : ValueFormat;

            try
            {
                // 注意：这里不是复合格式串（"{0:0.#}"），不能走 string.Format；
                // 直接把 "0.#" 当复合串传给 string.Format 会因为没有占位符而原样返回 "0.#"。
                return value.ToString(format, CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
                return value.ToString("0.#", CultureInfo.InvariantCulture);
            }
        }
    }
}
