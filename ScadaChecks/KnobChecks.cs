using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using VisionMaster.Scada;
using VisionMaster.Scada.Controls;

namespace ScadaChecks
{
    internal partial class Program
    {
        /// <summary>
        /// [KN] 旋钮（Hmi.Knob）：写值语义必须与滑块同一份契约——
        /// 拖动只改显示、松手写一次、写前判权限与写通道、写失败回弹并给原因。
        ///
        /// 断言只读数据侧（DisplayValue / ValueRatio / WriteError / ToolTip / writer.Writes / 模板部件），
        /// 不依赖真实窗口；控件与模板在 STA 线程上跑。
        ///
        /// 模板直接加载 <c>Themes/KnobElement.xaml</c>（而不是 <c>Generic.xaml</c>）：
        /// 本图元的描述符与 Generic 合并行由人工粘进 BuiltInElements / Generic.xaml，
        /// 在它们落地之前本组断言也必须能跑（判据不能等接线）。
        /// </summary>
        private static class KnobChecks
        {
            /// <summary>本图元应有的属性键（与 KnobElement 上的 [ScadaProperty] 一一对应）</summary>
            private static readonly string[] ExpectedKeys =
            {
                "Value", "Min", "Max", "Step", "StartAngle", "SweepAngle",
                "TickCount", "SnapToStep", "ShowValue", "ValueFormat", "KnobColor", "TrackColor",
            };

            public static void Run() => RunOnSta(RunChecks);

            private static void RunChecks()
            {
                Section("旋钮（Hmi.Knob）");

                // ---- [KN1] 描述符 ----
                // 描述符可能还没粘进 BuiltInElements（本批只允许新增三个文件），
                // 那就按"候选描述符"校验同一份属性表；粘进去之后这条自动改为校验真描述符。
                var registered = ElementRegistry.Find("Hmi.Knob");
                var descriptor = registered ?? CandidateDescriptor();

                var missing = ExpectedKeys
                    .Where(key => !descriptor.Properties.Any(p => string.Equals(p.Key, key, StringComparison.Ordinal)))
                    .ToArray();

                var valueProperty = descriptor.Properties
                    .FirstOrDefault(p => string.Equals(p.Key, "Value", StringComparison.Ordinal));

                Check("[KN1] 描述符：Hmi.Knob / 旋钮 / 操作 / 140×140，12 个属性键齐全、Value 可绑且落在 KnobElement.ValueProperty 上",
                    string.Equals(descriptor.TypeKey, "Hmi.Knob", StringComparison.Ordinal)
                    && string.Equals(descriptor.DisplayName, "旋钮", StringComparison.Ordinal)
                    && string.Equals(descriptor.Category, "操作", StringComparison.Ordinal)
                    && descriptor.ControlType == typeof(KnobElement)
                    && Math.Abs(descriptor.DefaultWidth - 140) < 1e-9
                    && Math.Abs(descriptor.DefaultHeight - 140) < 1e-9
                    && missing.Length == 0
                    && valueProperty is { IsBindable: true }
                    && ReferenceEquals(valueProperty.TargetProperty, KnobElement.ValueProperty),
                    (registered is null ? "未注册：按候选描述符校验（粘进 BuiltInElements 后自动校验真描述符）" : "已注册")
                    + $" / missing=[{string.Join(",", missing)}] / props={descriptor.Properties.Count}"
                    + $" / valueTarget={valueProperty?.TargetProperty?.Name ?? "null"}");

                // ---- 夹具：模型 + 控件 + 假写通道 ----
                var element = new ScadaElement
                {
                    TypeKey = "Hmi.Knob",
                    Name = "旋钮",
                    Width = 140,
                    Height = 140,
                };

                element.GetOrAddBinding("Value").Bind(Guid.NewGuid(), "Speed");

                var writer = new FakeValueWriter();
                var knob = new KnobElement { Element = element };

                knob.Min = 0;
                knob.Max = 100;
                knob.Step = 1;
                knob.SnapToStep = true;

                knob.RuntimeContext = KnobContext(writer);
                knob.Style = KnobStyle();
                knob.Refresh();

                // Value 必须在 Refresh **之后**设：Refresh 会从模型属性袋重读一遍配置，
                // 模型上没写 "Value" 键时落回描述符默认 0，先设的 50 会被悄悄覆盖成 0
                //（这正是本轮 5 条红 [KN2/5/6/10/12] 的根因：不是控件没做显示值，是测试设早了）。
                knob.Value = 50;
                Layout(knob);

                var inputCompleted = 0;
                knob.ScadaEvent += (_, e) =>
                {
                    if (e.ScadaEvent == ScadaEventType.InputCompleted)
                        inputCompleted++;
                };

                Check("[KN2] 初始：Value=50 / 量程 0~100 → 显示 50、比例 0.5，运行态可写",
                    Math.Abs(knob.DisplayValue - 50) < 1e-9
                    && Math.Abs(knob.ValueRatio - 0.5) < 1e-9
                    && knob.IsEditable
                    && !knob.IsDesignTime,
                    $"display={knob.DisplayValue} / ratio={knob.ValueRatio} / editable={knob.IsEditable}");

                // ---- [KN3] 夹紧 ----
                knob.SetValueFromUi(200);
                var clampedHigh = writer.Writes.Count == 1
                                  && writer.Writes[^1].Target == "Value"
                                  && writer.Writes[^1].Text == "100"
                                  && Math.Abs(knob.DisplayValue - 100) < 1e-9;

                knob.SetValueFromUi(-5);
                var clampedLow = writer.Writes.Count == 2
                                 && writer.Writes[^1].Text == "0"
                                 && Math.Abs(knob.DisplayValue) < 1e-9;

                Check("[KN3] 夹紧：SetValueFromUi(200) → 写 100；(-5) → 写 0（越界值不许原样写到变量）",
                    clampedHigh && clampedLow,
                    string.Join(" | ", writer.Writes.Select(w => $"{w.Target}={w.Text}")));

                // ---- [KN4] 步长吸附 ----
                knob.Step = 5;
                knob.SnapToStep = true;
                knob.SetValueFromUi(7.4);
                var snapped = writer.Writes[^1].Text == "5" && Math.Abs(knob.DisplayValue - 5) < 1e-9;

                knob.SnapToStep = false;
                knob.SetValueFromUi(7.4);
                var unsnapped = writer.Writes[^1].Text == "7.4" && Math.Abs(knob.DisplayValue - 7.4) < 1e-9;

                Check("[KN4] 吸附：Step=5 时 7.4 → 5；关掉「按步长吸附」后 7.4 原样写出",
                    snapped && unsnapped,
                    $"snapped={snapped} / unsnapped={unsnapped} / last={writer.Writes[^1].Text}");

                knob.Step = 1;
                knob.SnapToStep = true;

                // ---- [KN5] 权限拒绝 ----
                element.RequiredRole = ScadaRole.Administrator;
                knob.RuntimeContext = KnobContext(writer, new FakeAccessPolicy { CurrentRole = ScadaRole.Operator });
                knob.Refresh();
                knob.Value = 50; // Refresh 会把 DP 重读回模型默认 0，先设值会被覆盖（同 KN2 注释）

                var beforeDenied = writer.Writes.Count;
                var denied = knob.Commit(60);

                Check("[KN5] 权限不足：不写变量、显示回弹到模型值、原因写明「权限」",
                    !denied
                    && writer.Writes.Count == beforeDenied
                    && Math.Abs(knob.DisplayValue - 50) < 1e-9
                    && (knob.WriteError ?? string.Empty).Contains("权限", StringComparison.Ordinal)
                    && Equals(knob.ToolTip, knob.WriteError),
                    $"writes={writer.Writes.Count - beforeDenied} / display={knob.DisplayValue} / error={knob.WriteError}");

                // ---- [KN6] 写失败回弹 ----
                element.RequiredRole = null;
                knob.RuntimeContext = KnobContext(writer);
                knob.Refresh();
                knob.Value = 50; // 同上：Refresh 之后设值

                writer.FailReason = "设备离线，写入被拒绝";
                var beforeFail = writer.Writes.Count;
                var completedBeforeFail = inputCompleted;
                var failed = knob.Commit(80);

                Check("[KN6] 写入失败：回弹到写前值 50、错误原文保留并上屏（WriteError + ToolTip），不停在假位置",
                    !failed
                    && writer.Writes.Count == beforeFail + 1
                    && writer.Writes[^1].Text == "80"
                    && Math.Abs(knob.DisplayValue - 50) < 1e-9
                    && knob.WriteError == "设备离线，写入被拒绝"
                    && Equals(knob.ToolTip, "设备离线，写入被拒绝")
                    && inputCompleted == completedBeforeFail, // 失败这次不许发 InputCompleted
                    $"display={knob.DisplayValue} / error={knob.WriteError} / completed={inputCompleted - completedBeforeFail}");

                writer.FailReason = null;
                var completedBeforeRecover = inputCompleted;
                knob.Commit(60);

                Check("[KN7] 失败后恢复：下一次写入成功即清空错误、写一次并发 InputCompleted",
                    writer.Writes[^1].Text == "60"
                    && Math.Abs(knob.DisplayValue - 60) < 1e-9
                    && string.IsNullOrEmpty(knob.WriteError)
                    && inputCompleted == completedBeforeRecover + 1,
                    $"display={knob.DisplayValue} / error={knob.WriteError} / completed={inputCompleted - completedBeforeRecover}");

                // ---- [KN8] 拖动：只改显示，松手写一次 ----
                var beforeDrag = writer.Writes.Count;
                var dragging = knob.BeginDrag(100, captureMouse: false);
                knob.DragTo(80); // 向上 20 像素 = +10%（量程 100）
                var duringDrag = writer.Writes.Count;
                var previewValue = knob.DisplayValue;
                knob.EndDrag();

                Check("[KN8] 拖动只改显示（向上 = 增大，20 像素 = +10），松手写一次",
                    dragging
                    && duringDrag == beforeDrag
                    && Math.Abs(previewValue - 70) < 1e-9
                    && writer.Writes.Count == beforeDrag + 1
                    && writer.Writes[^1].Text == "70"
                    && !knob.IsDragging,
                    $"dragging={dragging} / preview={previewValue} / writes={writer.Writes.Count - beforeDrag}");

                // ---- [KN9] 非法量程：不除零、不抛、比例收敛 ----
                knob.Min = 0;
                knob.Max = 0;

                var noThrow = true;
                var ratioInRange = true;
                var finiteDisplay = true;

                try
                {
                    ratioInRange &= knob.ValueRatio is >= 0d and <= 1d;
                    knob.SetValueFromUi(50);
                    ratioInRange &= knob.ValueRatio is >= 0d and <= 1d;
                    finiteDisplay &= double.IsFinite(knob.DisplayValue);

                    knob.Value = double.NaN;
                    ratioInRange &= knob.ValueRatio is >= 0d and <= 1d;
                    finiteDisplay &= double.IsFinite(knob.DisplayValue);

                    knob.Value = double.PositiveInfinity;
                    ratioInRange &= knob.ValueRatio is >= 0d and <= 1d;
                    finiteDisplay &= double.IsFinite(knob.DisplayValue);
                }
                catch (Exception ex)
                {
                    noThrow = false;
                    Console.WriteLine("    " + ex);
                }

                Check("[KN9] Max==Min 与 NaN/∞ 值：不除零、不抛，ValueRatio 恒在 [0,1]、显示值恒有限",
                    noThrow && ratioInRange && finiteDisplay,
                    $"noThrow={noThrow} / ratioOk={ratioInRange} / finite={finiteDisplay} / ratio={knob.ValueRatio}");

                knob.Min = 0;
                knob.Max = 100;
                knob.Value = 50;
                knob.Refresh();

                // ---- [KN10] 设计期：可拖但绝不写值 ----
                knob.RuntimeContext = null;
                knob.Refresh();
                knob.Value = 50; // 设计期 Refresh 同样会把值重读回 0，先补回初值再拖

                var beforeDesign = writer.Writes.Count;
                var designDrag = knob.BeginDrag(100, captureMouse: false);
                knob.DragTo(60); // 向上 40 像素 = +20
                var designPreview = knob.DisplayValue;
                knob.EndDrag();

                Check("[KN10] 设计期：可拖动预览（只改显示），一次都不写变量，ToolTip 说明「设计期不写值」",
                    designDrag
                    && Math.Abs(designPreview - 70) < 1e-9
                    && writer.Writes.Count == beforeDesign
                    && !knob.IsEditable
                    && (knob.ToolTip as string ?? string.Empty).Contains("设计期", StringComparison.Ordinal),
                    $"preview={designPreview} / writes={writer.Writes.Count - beforeDesign} / editable={knob.IsEditable} / tip={knob.ToolTip}");

                // ---- [KN11] 模板：自绘面在模板里且认得到宿主 ----
                knob.RuntimeContext = KnobContext(writer);
                knob.Refresh();
                Layout(knob);

                var surface = knob.Template?.FindName(KnobElement.PartSurface, knob) as KnobSurface;

                Check("[KN11] 模板：PART_Surface 是自绘面 KnobSurface，Owner 指回本控件（几何画在子元素上，不被模板背景盖住）",
                    surface is not null
                    && ReferenceEquals(surface.Owner, knob)
                    && surface.ActualWidth > 0
                    && surface.ActualHeight > 0,
                    $"surface={(surface is null ? "null" : "ok")} / owner={ReferenceEquals(surface?.Owner, knob)} / " +
                    $"{surface?.ActualWidth:0.#}×{surface?.ActualHeight:0.#}");

                // ---- [KN12] 读数格式：坏格式回落 F0 ----
                knob.ShowValue = true;
                knob.ValueFormat = "F0";
                knob.Refresh();
                knob.Value = 60; // Refresh 之后设值：先设会被模型默认 0 覆盖
                var plainText = knob.DisplayText;

                knob.ValueFormat = "这不是格式";
                var badText = knob.DisplayText;

                knob.ValueFormat = "F1";
                var oneDecimalText = knob.DisplayText;

                Check("[KN12] 数值格式：F0 取整、F1 一位小数；写坏格式回落 F0（不抛、不显示空串）",
                    plainText == "60" && oneDecimalText == "60.0" && badText == "60",
                    $"F0={plainText} / F1={oneDecimalText} / bad={badText}");
            }

            /// <summary>
            /// 候选描述符：与要粘进 <c>BuiltInElements.cs</c> 的那一条同构（只是没注册）。
            /// 描述符已注册时本方法不会被调用。
            /// </summary>
            private static ElementDescriptor CandidateDescriptor() => new()
            {
                TypeKey = "Hmi.Knob",
                DisplayName = "旋钮",
                ControlType = typeof(KnobElement),
                Category = "操作",
                DefaultWidth = 140,
                DefaultHeight = 140,
                Properties = GeometryProperties.WithGroups(
                    ScadaPropertyCatalog.Collect(typeof(KnobElement))),
            };

            private static ScadaRuntimeContext KnobContext(
                IScadaValueWriter? writer,
                IScadaAccessPolicy? policy = null)
            {
                var alarms = new ScadaAlarmEngine(new ScadaDocument(), new FakeValueSource(), () => DateTime.UtcNow);
                var beat = new ScadaBeatSource(Dispatcher.CurrentDispatcher, TimeSpan.FromMilliseconds(20));
                return new ScadaRuntimeContext(alarms, beat, writer, policy);
            }

            private static Style KnobStyle()
            {
                // 直接加载本图元的分册：Generic.xaml 的合并行由人工补，断言不能等它。
                var theme = new ResourceDictionary
                {
                    Source = new Uri("/VM.Scada.Controls;component/Themes/KnobElement.xaml", UriKind.Relative),
                };

                return (Style)theme[typeof(KnobElement)];
            }

            private static void Layout(Control control)
            {
                control.ApplyTemplate();
                control.Measure(new Size(140, 140));
                control.Arrange(new Rect(0, 0, 140, 140));
                control.UpdateLayout();
            }

            private static void RunOnSta(Action action)
            {
                Exception? error = null;

                var thread = new Thread(() =>
                {
                    try
                    {
                        // 本宿主刻意不 new Application（Program.cs 头部契约）：
                        // Application 是单例且有线程亲和，在子 STA 线程上创建会把
                        // Application.Current 的线程亲和留在死线程上，毒化后续
                        // 所有触碰它的检查（ScadaMonitoringChecks 实测整进程崩溃）。
                        // WPF 控件只需要 Dispatcher（STA 线程自带），模板资源走
                        // LoadComponent 直读字典，不需要 Application 级查找。
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
                    Check("旋钮 UI 段执行完成（没有未捕获异常）", false, error.ToString());
            }
        }
    }
}
