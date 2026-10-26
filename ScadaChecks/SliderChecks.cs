using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using VisionMaster.Scada;
using VisionMaster.Scada.Controls;

namespace ScadaChecks
{
    internal partial class Program
    {
        /// <summary>
        /// [SL] 模拟量滑块：拖动只改显示、松手写一次、失败回弹、键盘步进、只读/确认门禁。
        ///
        /// 控件与模板在 STA 线程上跑；写通道/确认出口用 Program 里既有的假实现，
        /// 断言只读数据侧（DisplayValue / WriteError / writer.Writes / 模板部件），不依赖真实窗口。
        /// </summary>
        private static class SliderChecks
        {
            public static void Run() => RunOnSta(RunChecks);

            private static void RunChecks()
            {
                Section("模拟量滑块（Hmi.Slider）");

                var descriptor = ElementRegistry.Find("Hmi.Slider");
                Check("[SL1] 描述符：Hmi.Slider 已注册，默认 280×48，Value 可绑、InputCompleted 事件在",
                    descriptor != null
                    && descriptor.DefaultWidth == 280
                    && descriptor.DefaultHeight == 48
                    && descriptor.Properties.Any(p => p.Key == "Value" && p.IsBindable)
                    && descriptor.Events.Contains(ScadaEventType.InputCompleted),
                    descriptor == null ? "null" : $"{descriptor.DefaultWidth}×{descriptor.DefaultHeight}");

                var element = ElementRegistry.CreateElement("Hmi.Slider")!;
                element.SetProperty("Value", "50");
                element.SetProperty("Minimum", "0");
                element.SetProperty("Maximum", "100");
                element.GetOrAddBinding("Value").Bind(Guid.NewGuid(), "Speed");

                var writer = new FakeValueWriter();
                var confirmations = new FakeConfirmationService();
                var slider = (SliderElement)ElementRegistry.CreateControl(element)!;

                slider.RuntimeContext = SliderContext(writer, confirmations);
                slider.Style = SliderStyle();
                slider.Refresh();
                Layout(slider);

                var inputCompleted = 0;
                slider.ScadaEvent += (_, e) =>
                {
                    if (e.ScadaEvent == ScadaEventType.InputCompleted)
                        inputCompleted++;
                };

                Check("[SL2] 初始：Value=50 / 量程 0~100 → 显示值 50、比例 0.5，可编辑",
                    Math.Abs(slider.DisplayValue - 50) < 1e-9
                    && Math.Abs(slider.Ratio - 0.5) < 1e-9
                    && slider.IsEditable,
                    $"value={slider.DisplayValue} / ratio={slider.Ratio} / editable={slider.IsEditable}");

                var valueText = slider.Template?.FindName(SliderElement.PartValueText, slider) as TextBlock;

                Check("[SL3] 模板部件齐全：轨道/填充/拇指/数值文字都在，数值文字显示格式化值",
                    slider.Template?.FindName(SliderElement.PartTrack, slider) is Border
                    && slider.Template.FindName(SliderElement.PartFill, slider) is Border
                    && slider.Template.FindName(SliderElement.PartThumb, slider) is Border
                    && valueText != null
                    && valueText.Text == "50",
                    $"text={valueText?.Text} / baseText={slider.Text} / format={slider.ValueFormat} / display={slider.DisplayValue} / binding={valueText?.GetBindingExpression(TextBlock.TextProperty)?.ParentBinding.Path.Path}");

                slider.BeginDragAtRatio(0.2, captureMouse: false);
                slider.DragToRatio(0.7);

                Check("[SL4] 拖动只改显示，不写变量（松手前 writer 0 次）",
                    slider.IsDragging
                    && Math.Abs(slider.DisplayValue - 70) < 1e-9
                    && writer.Writes.Count == 0,
                    $"display={slider.DisplayValue} / writes={writer.Writes.Count}");

                slider.EndDrag();

                Check("[SL5] 松手写一次最终值：Value 属性键 + 不变区域性文本，成功清错误",
                    writer.Writes.Count == 1
                    && writer.Writes[0].Target == "Value"
                    && writer.Writes[0].Text == "70"
                    && string.IsNullOrEmpty(slider.WriteError)
                    && !slider.IsDragging
                    && inputCompleted == 1,
                    string.Join(" | ", writer.Writes.Select(w => $"{w.Target}={w.Text}")));

                writer.FailReason = "设备离线，写入被拒绝";
                slider.BeginDragAtRatio(0.1, captureMouse: false);
                slider.DragToRatio(0.9);
                var failed = slider.EndDrag();

                Check("[SL6] 写入失败：回弹到模型值 50、错误原文保留、不停在假位置",
                    !failed
                    && Math.Abs(slider.DisplayValue - 50) < 1e-9
                    && slider.WriteError == "设备离线，写入被拒绝"
                    && writer.Writes.Count == 2
                    && inputCompleted == 1,
                    $"display={slider.DisplayValue} / error={slider.WriteError}");

                var errorText = slider.Template?.FindName(SliderElement.PartValueText, slider) as TextBlock;

                Check("[SL6b] 写入失败原因上屏：数值文字区显示错误原文，ToolTip 同步",
                    errorText?.Text == "设备离线，写入被拒绝"
                    && Equals(errorText.ToolTip, "设备离线，写入被拒绝"),
                    $"text={errorText?.Text} / tip={errorText?.ToolTip}");

                var errorTrack = slider.Template?.FindName(SliderElement.PartTrack, slider) as Border;
                var errorFill = slider.Template?.FindName(SliderElement.PartFill, slider) as Border;

                Check("[SL6c] 写入失败红轨：轨道边框与填充都变成报警红",
                    errorTrack?.BorderBrush is SolidColorBrush trackBrush
                    && trackBrush.Color == Color.FromRgb(0xEF, 0x44, 0x44)
                    && errorFill?.Background is SolidColorBrush fillBrush
                    && fillBrush.Color == Color.FromRgb(0xEF, 0x44, 0x44),
                    $"track={errorTrack?.BorderBrush} / fill={errorFill?.Background}");

                writer.FailReason = null;
                slider.Nudge(5);

                Check("[SL7] 键盘步进：立即写一次，显示值 55",
                    writer.Writes.Count == 3
                    && writer.Writes[^1].Text == "55"
                    && Math.Abs(slider.DisplayValue - 55) < 1e-9
                    && inputCompleted == 2,
                    $"writes={writer.Writes.Count} / display={slider.DisplayValue}");

                slider.ConfirmDanger = true;
                confirmations.Result = false;
                var beforeCancel = writer.Writes.Count;
                slider.Nudge(5);

                Check("[SL8] 写入前确认：取消不写、回弹到模型值，确认出口被调用一次",
                    confirmations.Calls == 1
                    && writer.Writes.Count == beforeCancel
                    && Math.Abs(slider.DisplayValue - 50) < 1e-9
                    && string.IsNullOrEmpty(slider.WriteError),
                    $"calls={confirmations.Calls} / writes={writer.Writes.Count} / display={slider.DisplayValue}");

                confirmations.Result = true;
                slider.Nudge(5);

                Check("[SL9] 确认后写入：确认两次、写入一次，显示值 55",
                    confirmations.Calls == 2
                    && writer.Writes.Count == beforeCancel + 1
                    && writer.Writes[^1].Text == "55"
                    && Math.Abs(slider.DisplayValue - 55) < 1e-9
                    && inputCompleted == 3,
                    $"calls={confirmations.Calls} / writes={writer.Writes.Count} / text={writer.Writes[^1].Text} / display={slider.DisplayValue}");

                // 模态确认期间数据泵会回读变量并写 Value，OnValuePropertyChanged 会覆盖 _displayValue。
                // 让假确认出口真的压一个 DispatcherFrame，在 Background 队列里改 Value，
                // 复现"确认的是 85、回读成 20"的时序（不是同步直接改）。
                confirmations.PumpDispatcher = true;
                confirmations.OnConfirm = () => slider.Value = 20;
                var beforeReadback = writer.Writes.Count;
                var callsBeforeReadback = confirmations.Calls;
                slider.Nudge(30);

                Check("[SL9b] 确认期间回读改 Value：确认文案与写入仍是用户确认的 85，不被回读覆盖",
                    confirmations.Calls == callsBeforeReadback + 1
                    && writer.Writes.Count == beforeReadback + 1
                    && writer.Writes[^1].Text == "85"
                    && Math.Abs(slider.DisplayValue - 85) < 1e-9
                    && (confirmations.LastMessage?.Contains("85", StringComparison.Ordinal) ?? false),
                    $"calls={confirmations.Calls} / write={writer.Writes[^1].Text} / display={slider.DisplayValue} / message={confirmations.LastMessage}");

                // 确认期间量程被改到 50，用户确认的 80 已越界：必须拒绝重来，
                // 不能静默夹成 50 写进去（那是一个没被确认过的值）。
                slider.Value = 50;
                confirmations.OnConfirm = () => slider.Maximum = 50;
                var beforeRangeChange = writer.Writes.Count;
                var callsBeforeRangeChange = confirmations.Calls;
                slider.Nudge(30);

                Check("[SL9c] 确认期间量程缩小：确认值 80 越界 → 拒绝写入并提示重新确认，不静默夹成 50",
                    confirmations.Calls == callsBeforeRangeChange + 1
                    && writer.Writes.Count == beforeRangeChange
                    && slider.WriteError.Contains("量程", StringComparison.Ordinal)
                    && Math.Abs(slider.DisplayValue - 50) < 1e-9,
                    $"calls={confirmations.Calls} / writes={writer.Writes.Count} / display={slider.DisplayValue} / error={slider.WriteError}");

                confirmations.PumpDispatcher = false;
                confirmations.OnConfirm = null;
                slider.Maximum = 100;
                slider.Value = 50;

                slider.ConfirmDanger = false;
                slider.Mode = SliderElement.ModeOutput;
                var beforeReadOnly = writer.Writes.Count;
                var readOnlyNudge = slider.Nudge(5);

                Check("[SL10] 只读模式：不能拖/步进，原因写明「只读」，不写变量",
                    !readOnlyNudge
                    && !slider.IsEditable
                    && writer.Writes.Count == beforeReadOnly
                    && slider.WriteError.Contains("只读", StringComparison.Ordinal),
                    $"editable={slider.IsEditable} / error={slider.WriteError}");

                slider.Mode = SliderElement.ModeInputOutput;
                slider.Orientation = ProgressBarOrientation.Vertical;
                Layout(slider);
                slider.BeginDragAtRatio(0.25, captureMouse: false);

                var track11 = slider.Template?.FindName("PART_Track", slider) as Border;
                Check("[SL11] 纵向：比例 0.25 → 显示 25，填充块高度按轨道高度比例",
                    Math.Abs(slider.DisplayValue - 25) < 1e-9
                    && slider.Template?.FindName(SliderElement.PartFill, slider) is Border fill
                    // 期望值按**实际轨道高度**算，不写死像素：模板加了量程标注行后轨道变矮，
                    // 写死的 12 会把"比例正确但模板改了"误判成失败（2026-10-25）。
                    && track11 != null
                    && Math.Abs(fill.Height - track11.ActualHeight * 0.25) < 0.6,
                    $"display={slider.DisplayValue} / fillHeight={(slider.Template?.FindName(SliderElement.PartFill, slider) as Border)?.Height} / track={track11?.ActualHeight}");

                slider.EndDrag();

                slider.Value = 40;
                var externalText = slider.Template?.FindName(SliderElement.PartValueText, slider) as TextBlock;

                Check("[SL12] 外部改 Value（数据泵回读）→ 显示值同步、数值文字跟到 40",
                    Math.Abs(slider.DisplayValue - 40) < 1e-9
                    && externalText?.Text == "40",
                    $"display={slider.DisplayValue} / text={externalText?.Text}");

                // 跟手气泡（2026-10-25 评审 Q3）：数值外框的水平位置随拇指比例移动，且夹在控件内。
                // 判据取"两个不同比例下的左偏移"，不写死像素——模板/字号改了也不会误报。
                // 注意：上面 SL11 把走向切成了纵向；跟手只对横向生效，这里先切回来。
                slider.Orientation = ProgressBarOrientation.Horizontal;
                Layout(slider);

                var plate = slider.Template?.FindName(SliderElement.PartValuePlate, slider) as FrameworkElement;
                var plateLeftAt40 = plate?.Margin.Left ?? double.NaN;

                slider.Value = 10;
                Layout(slider);
                var plateLeftAt10 = plate?.Margin.Left ?? double.NaN;

                Check("[SL12b] 数值气泡跟手：值越小越靠左，且始终夹在控件宽度内",
                    plate != null
                    && plateLeftAt10 < plateLeftAt40
                    && plateLeftAt10 >= -0.5
                    && plateLeftAt40 <= slider.ActualWidth + 0.5,
                    $"plate@10={plateLeftAt10:0.#} / plate@40={plateLeftAt40:0.#} / width={slider.ActualWidth:0.#}");

                slider.Value = 40;
                Layout(slider);

                slider.ShowValue = false;
                Layout(slider);

                Check("[SL13] ShowValue=false → 数值文字收起（轨道仍可操作）",
                    (slider.Template?.FindName(SliderElement.PartValueText, slider) as TextBlock)?.Visibility
                        == Visibility.Collapsed,
                    (slider.Template?.FindName(SliderElement.PartValueText, slider) as TextBlock)?.Visibility.ToString());

                writer.FailReason = "离线（隐藏数值时）";
                slider.Nudge(5);
                var hiddenErrorText = slider.Template?.FindName(SliderElement.PartValueText, slider) as TextBlock;
                var hiddenErrorShown = hiddenErrorText?.Visibility == Visibility.Visible
                    && hiddenErrorText.Text == "离线（隐藏数值时）";

                writer.FailReason = null;
                slider.Nudge(5);
                var hiddenAfterRecover = hiddenErrorText?.Visibility == Visibility.Collapsed;

                Check("[SL13b] ShowValue=false 时写失败仍显示错误原文；错误清掉后重新收起",
                    hiddenErrorShown && hiddenAfterRecover,
                    $"shown={hiddenErrorShown} / visibility={hiddenErrorText?.Visibility} / text={hiddenErrorText?.Text}");

                slider.ShowValue = true;
                slider.Minimum = double.NaN;
                slider.Maximum = double.NaN;
                slider.Value = double.NaN;
                Layout(slider);

                Check("[SL14] NaN/非有限量程不崩：显示值回落到有限值",
                    double.IsFinite(slider.DisplayValue) && double.IsFinite(slider.Ratio),
                    $"display={slider.DisplayValue} / ratio={slider.Ratio}");

                slider.Minimum = 0;
                slider.Maximum = 100;
                slider.Value = 50;
                Layout(slider);

                var captured = slider.BeginDragAtRatio(0.5);
                if (captured)
                    slider.EndDrag();

                Check("[SL15] 无 PresentationSource 时 CaptureMouse 失败 → 不进入拖动",
                    !captured
                    && !slider.IsDragging
                    && slider.WriteError.Contains("捕获", StringComparison.Ordinal),
                    $"captured={captured} / dragging={slider.IsDragging} / error={slider.WriteError}");

                slider.WriteMode = SliderWriteMode.Throttled;
                var beforeThrottle = writer.Writes.Count;
                slider.BeginDragAtRatio(0.1, captureMouse: false);
                Thread.Sleep(130);
                slider.DragToRatio(0.6);
                var duringThrottle = writer.Writes.Count;
                slider.EndDrag();

                Check("[SL16] Throttled：拖动中按 100ms 写一次，松手再写最终值",
                    duringThrottle > beforeThrottle
                    && writer.Writes.Count == duringThrottle + 1,
                    $"before={beforeThrottle} / during={duringThrottle} / after={writer.Writes.Count}");

                var throttleBefore = writer.Writes.Count;
                slider.BeginDragAtRatio(0.2, captureMouse: false);
                slider.DragToRatio(0.3);
                slider.DragToRatio(0.4);
                var noEarlyWrite = writer.Writes.Count == throttleBefore;

                Thread.Sleep(130);
                slider.DragToRatio(0.6);
                var afterThrottleTick = writer.Writes.Count;
                slider.EndDrag();

                Check("[SL16b] Throttled 的 100ms 窗口内不写，过窗后才写一次，松手再写最终值",
                    noEarlyWrite
                    && afterThrottleTick == throttleBefore + 1
                    && writer.Writes.Count == afterThrottleTick + 1,
                    $"before={throttleBefore} / tick={afterThrottleTick} / after={writer.Writes.Count}");

                writer.FailReason = "离线，节流写失败";
                slider.BeginDragAtRatio(0.1, captureMouse: false);
                Thread.Sleep(130);
                slider.DragToRatio(0.7);
                var throttleFailWrites = writer.Writes.Count;
                slider.DragToRatio(0.8);

                Check("[SL16c] Throttled 写入失败：停止拖动、回弹、后续移动不再写",
                    !slider.IsDragging
                    && Math.Abs(slider.DisplayValue - 50) < 1e-9
                    && slider.WriteError == "离线，节流写失败"
                    && writer.Writes.Count == throttleFailWrites,
                    $"dragging={slider.IsDragging} / display={slider.DisplayValue} / writes={writer.Writes.Count}");

                writer.FailReason = null;
                slider.ConfirmDanger = true;
                confirmations.Result = true;
                var beforeConfirmThrottle = writer.Writes.Count;
                var callsBeforeThrottle = confirmations.Calls;
                slider.BeginDragAtRatio(0.1, captureMouse: false);
                Thread.Sleep(130);
                slider.DragToRatio(0.8);
                var duringConfirmThrottle = writer.Writes.Count;
                slider.EndDrag();

                Check("[SL16d] Throttled + ConfirmDanger：拖动中不写，松手只确认一次并写一次",
                    duringConfirmThrottle == beforeConfirmThrottle
                    && confirmations.Calls == callsBeforeThrottle + 1
                    && writer.Writes.Count == beforeConfirmThrottle + 1
                    && writer.Writes[^1].Text == "80",
                    $"during={duringConfirmThrottle} / calls={confirmations.Calls - callsBeforeThrottle} / " +
                    $"writes={writer.Writes.Count - beforeConfirmThrottle} / text={writer.Writes[^1].Text}");

                slider.CornerRadius = 8;
                Layout(slider);

                Check("[SL17] CornerRadius=8 → 轨道/填充的圆角真的变成 8（不是写死 4）",
                    slider.Template?.FindName(SliderElement.PartTrack, slider) is Border track
                    && track.CornerRadius.TopLeft == 8
                    && slider.Template.FindName(SliderElement.PartFill, slider) is Border fillBorder
                    && fillBorder.CornerRadius.TopLeft == 8,
                    $"track={(slider.Template?.FindName(SliderElement.PartTrack, slider) as Border)?.CornerRadius.TopLeft} / " +
                    $"fill={(slider.Template?.FindName(SliderElement.PartFill, slider) as Border)?.CornerRadius.TopLeft}");

                element.SetProperty("ValueFormat", "0.0");
                element.SetProperty("Value", "50");
                slider.Refresh();
                Layout(slider);
                var plainText = (slider.Template?.FindName(SliderElement.PartValueText, slider) as TextBlock)?.Text;

                element.SetProperty("ValueFormat", "0.0 ℃");
                slider.Refresh();
                Layout(slider);
                var unitText = (slider.Template?.FindName(SliderElement.PartValueText, slider) as TextBlock)?.Text;

                Check("[SL18] ValueFormat：固定小数与单位后缀都按 .NET 数字格式显示",
                    plainText == "50.0"
                    && unitText == "50.0 ℃",
                    $"plain={plainText} / unit={unitText}");

                element.SetProperty("Minimum", "0");
                element.SetProperty("Maximum", "200");
                element.SetProperty("Value", "150");
                slider.Refresh();
                Layout(slider);

                Check("[SL18b] 量程 0~200 的 Value=150：加载后显示 150，不被默认 0~100 夹成 100",
                    Math.Abs(slider.DisplayValue - 150) < 1e-9
                    && Math.Abs(slider.Ratio - 0.75) < 1e-9,
                    $"display={slider.DisplayValue} / ratio={slider.Ratio}");

                element.SetProperty("Minimum", "-100");
                element.SetProperty("Maximum", "100");
                element.SetProperty("Value", "-50");
                slider.Refresh();
                Layout(slider);

                Check("[SL18c] 量程 -100~100 的 Value=-50：加载后显示 -50，不被默认 0 夹住",
                    Math.Abs(slider.DisplayValue - -50) < 1e-9
                    && Math.Abs(slider.Ratio - 0.25) < 1e-9,
                    $"display={slider.DisplayValue} / ratio={slider.Ratio}");

                element.SetProperty("Minimum", "0");
                element.SetProperty("Maximum", "100");
                element.SetProperty("Value", "50");
                slider.Refresh();
                slider.Orientation = ProgressBarOrientation.Horizontal;
                Layout(slider);
                slider.Orientation = ProgressBarOrientation.Vertical;
                Layout(slider);

                var track18 = slider.Template?.FindName("PART_Track", slider) as Border;
                Check("[SL18d] 只切 Orientation 再布局（不触发拖动）：纵向填充按新轨道高度重算",
                    slider.Template?.FindName(SliderElement.PartFill, slider) is Border orientationFill
                    // 同 SL11：按实际轨道高度算（模板加量程标注后轨道变矮）
                    && track18 != null
                    && Math.Abs(orientationFill.Height - track18.ActualHeight * 0.5) < 0.6,
                    $"fillHeight={(slider.Template?.FindName(SliderElement.PartFill, slider) as Border)?.Height} / track={track18?.ActualHeight}");

                var writesBeforeGates = writer.Writes.Count;
                slider.IsEnabled = false;
                var disabledNudge = slider.Nudge(1);

                Check("[SL19] IsEnabled=false：不可拖/步进、不写变量",
                    !disabledNudge
                    && !slider.IsEditable
                    && writer.Writes.Count == writesBeforeGates,
                    $"editable={slider.IsEditable} / error={slider.WriteError}");

                slider.IsEnabled = true;
                slider.RuntimeContext = SliderContext(null, confirmations);
                slider.Refresh();
                var noWriter = slider.Nudge(1);

                Check("[SL20] 无写通道：不可编辑、拒绝写入并说明原因",
                    !noWriter
                    && !slider.IsEditable
                    && writer.Writes.Count == writesBeforeGates
                    && slider.WriteError.Contains("写通道", StringComparison.Ordinal),
                    $"editable={slider.IsEditable} / error={slider.WriteError}");

                element.Bindings.Clear();
                slider.RuntimeContext = SliderContext(writer, confirmations);
                slider.Refresh();
                var noBinding = slider.Nudge(1);

                Check("[SL21] 无绑定：不可编辑、拒绝写入并说明「没有绑定」",
                    !noBinding
                    && !slider.IsEditable
                    && writer.Writes.Count == writesBeforeGates
                    && slider.WriteError.Contains("绑定", StringComparison.Ordinal),
                    $"editable={slider.IsEditable} / error={slider.WriteError}");

                element.GetOrAddBinding("Value").Bind(Guid.NewGuid(), "Speed");
                element.RequiredRole = ScadaRole.Administrator;
                var policy = new FakeAccessPolicy { CurrentRole = ScadaRole.Operator };
                slider.RuntimeContext = SliderContext(writer, confirmations, policy);
                slider.Refresh();
                var denied = slider.Nudge(1);

                Check("[SL22] 权限不足：不可编辑、拒绝写入并说明角色",
                    !denied
                    && !slider.IsEditable
                    && writer.Writes.Count == writesBeforeGates
                    && slider.WriteError.Contains("权限", StringComparison.Ordinal),
                    $"editable={slider.IsEditable} / error={slider.WriteError}");

                element.RequiredRole = null;
                slider.RuntimeContext = SliderContext(writer, null);
                slider.Refresh();
                slider.ConfirmDanger = true;
                var noConfirmExit = slider.Nudge(1);

                Check("[SL23] 要求确认但没有确认出口：拒绝写入，不静默放行",
                    !noConfirmExit
                    && writer.Writes.Count == writesBeforeGates
                    && slider.WriteError.Contains("确认出口", StringComparison.Ordinal),
                    $"writes={writer.Writes.Count} / error={slider.WriteError}");
            }

            private static ScadaRuntimeContext SliderContext(
                IScadaValueWriter? writer,
                IScadaConfirmationService? confirmations,
                IScadaAccessPolicy? policy = null)
            {
                var alarms = new ScadaAlarmEngine(new ScadaDocument(), new FakeValueSource(), () => DateTime.UtcNow);
                var beat = new ScadaBeatSource(Dispatcher.CurrentDispatcher, TimeSpan.FromMilliseconds(20));
                return new ScadaRuntimeContext(
                    alarms,
                    beat,
                    writer,
                    policy,
                    confirmations: confirmations);
            }

            private static Style SliderStyle()
            {
                var theme = new ResourceDictionary
                {
                    Source = new Uri("/VM.Scada.Controls;component/Themes/Generic.xaml", UriKind.Relative),
                };

                return (Style)theme[typeof(SliderElement)];
            }

            private static void Layout(Control control)
            {
                control.ApplyTemplate();
                control.Measure(new Size(280, 48));
                control.Arrange(new Rect(0, 0, 280, 48));
                control.UpdateLayout();
            }

            private static void RunOnSta(Action action)
            {
                Exception? error = null;

                var thread = new Thread(() =>
                {
                    try
                    {
                        if (Application.Current == null)
                            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

                        action();
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                    }
                });

                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join();

                if (error != null)
                    Check("滑块 UI 段执行完成（没有未捕获异常）", false, error.ToString());
            }
        }
    }
}
