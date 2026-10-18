using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Core.Interfaces;
using Prism.Dialogs;
using Prism.Mvvm;
using VisionMaster.Scada;
using VisionMaster.Scada.Controls;
using VisionMaster.Services;
using VisionMaster.Services.Motion;
using VisionMaster.ViewModels.DialogViewModels;
using VisionMaster.Views.DialogViews;

namespace ScadaChecks
{
    /// <summary>
    /// [AX] 轴身份 / 轴候选清单 / 轴面板 / 轴状态发布器。
    ///
    /// 与 [S] 段同一纪律：控件与 Dispatcher 必须在 STA 线程上跑，
    /// 所以整段另起一个 STA 线程；断言只读数据侧（DP 值 / 快照 / 订阅数），不依赖真实窗口。
    /// </summary>
    internal partial class Program
    {
        private static void AxisPanelChecks()
        {
            Section("[AX] 轴身份 / 轴面板 / 轴状态发布器");

            Exception? failure = null;

            var thread = new Thread(() =>
            {
                try { RunAxisPanelChecks(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            thread.Join();

            if (failure != null)
                Check("轴面板断言未抛异常", false, failure.ToString());
        }

        private static void RunAxisPanelChecks()
        {
            AxisIdentityChecks();
            AxisOptionsCodecChecks();
            AxisPanelDesignChecks();
            AxisPanelRuntimeChecks();
            AxisPickerRenderChecks();
            AxisPublisherChecks();
            MotionActionChecks();
            SystemVariableChecks();
        }

        // ==================================================================
        //  [AX-1] 轴稳定身份：历史轴确定性补齐、新建轴有身份、改名/换轴号不变
        // ==================================================================
        private static void AxisIdentityChecks()
        {
            var cardId = Guid.NewGuid();

            Guid a = MotionAxisId.ForLegacy(cardId, 0);
            Guid b = MotionAxisId.ForLegacy(cardId, 0);
            Guid c = MotionAxisId.ForLegacy(cardId, 1);
            Guid other = MotionAxisId.ForLegacy(Guid.NewGuid(), 0);

            Check("[AX1] 历史轴身份是确定性的（同一卡同一轴号 → 同一 Guid）", a == b, $"{a} vs {b}");
            Check("[AX1] 不同物理轴号/不同卡得到不同身份", a != c && a != other, $"{a} / {c} / {other}");

            var card = new MotionDescriptor { Id = cardId, DisplayName = "测试卡" };
            card.Axes.Add(new AxisMapping { LogicalName = "X", PhysicalIndex = 0 });
            card.Axes.Add(new AxisMapping { LogicalName = "Y", PhysicalIndex = 1 });

            int assigned = card.EnsureAxisIds();
            Check("[AX1] 旧方案的两根轴各补到一个身份", assigned == 2 && card.Axes.All(x => x.AxisId != Guid.Empty),
                $"assigned={assigned}");

            var first = card.Axes[0].AxisId;
            Check("[AX1] 重复补齐是幂等的（已有身份一律不动）",
                card.EnsureAxisIds() == 0 && card.Axes[0].AxisId == first, "");

            // 注册表：新注册的轴有身份；改名/换卡/换轴号都不动它
            var cards = new List<MotionDescriptor> { card };
            var registry = new MotionAxisRegistry(() => cards);

            var registered = registry.Register("Z", cardId, 2);
            Guid zId = registered.Binding?.AxisId ?? Guid.Empty;
            Check("[AX1] 注册表新建的轴自带非空身份", registered.Success && zId != Guid.Empty,
                registered.Message);

            var renamed = registry.Rename("Z", "Z2");
            Check("[AX1] 轴改名后身份不变（SCADA 绑定按 Id 寻址）",
                renamed.Success && registry.Resolve("Z2").Binding?.AxisId == zId, renamed.Message);

            var updated = registry.Update("Z2", cardId, 3);
            Check("[AX1] 轴改指向另一个物理轴号后身份仍不变",
                updated.Success && registry.Resolve("Z2").Binding?.AxisId == zId, updated.Message);
        }

        // ==================================================================
        //  [AX-2] 候选轴清单编解码：去重、保序、坏 JSON 不抛
        // ==================================================================
        private static void AxisOptionsCodecChecks()
        {
            Guid x = Guid.NewGuid(), y = Guid.NewGuid();

            var refs = new[]
            {
                new ScadaAxisRef(x, "X"),
                new ScadaAxisRef(y, "Y"),
                new ScadaAxisRef(x, "X-dup"),
                new ScadaAxisRef(Guid.Empty, "空身份"),
            };

            string json = ScadaAxisOptionsCodec.Format(refs);
            var parsed = ScadaAxisOptionsCodec.Parse(json);

            Check("[AX2] 编解码往返保序、按 Id 去重、丢掉空身份",
                parsed.Count == 2 && parsed[0].AxisId == x && parsed[1].AxisId == y,
                json);

            Check("[AX2] 空串/坏 JSON 返回空表而不抛（一条坏配置不该让画面打不开）",
                ScadaAxisOptionsCodec.Parse("").Count == 0
                && ScadaAxisOptionsCodec.Parse("{not json").Count == 0,
                "");

            Check("[AX2] 空清单编码成空串（属性袋里不留一个空数组的 JSON）",
                ScadaAxisOptionsCodec.Format(Array.Empty<ScadaAxisRef>()) == "", "");
        }

        // ==================================================================
        //  [AX-3] 轴面板（设计期）：候选清单 → 下拉项 / 单轴收起 / 空态
        // ==================================================================
        private static void AxisPanelDesignChecks()
        {
            var element = ElementRegistry.CreateElement("Hmi.AxisPanel");
            var panel = (AxisPanelElement)ElementRegistry.CreateControl(element);

            Check("[AX3] 按 TypeKey 能造出轴面板控件", panel is AxisPanelElement, panel.GetType().Name);

            // 断言环境里没有窗口，WPF "DefaultStyleKey → 主题字典"的自动查找链走不通
            // （与画布/报警条那几段同一处理：显式把 Generic.xaml 里对应类型的那份样式套上）。
            var theme = new ResourceDictionary
            {
                Source = new Uri("/VM.Scada.Controls;component/Themes/Generic.xaml", UriKind.Relative)
            };
            panel.Style = (Style)theme[typeof(AxisPanelElement)];

            bool templated = panel.ApplyTemplate();
            panel.Measure(new Size(280, 200));
            panel.Arrange(new Rect(0, 0, 280, 200));
            panel.UpdateLayout();

            bool hasCombo = panel.Template?.FindName("AxisCombo", panel) is ComboBox;
            Check("[AX3] 默认模板能实例化并找到轴选择器（ComboBox 模板/绑定写坏会在这里红）",
                templated && hasCombo,
                !templated ? "ApplyTemplate=false" : hasCombo ? "AxisCombo 已就位" : "未找到 AxisCombo");

            Guid x = Guid.NewGuid(), y = Guid.NewGuid(), z = Guid.NewGuid();

            element.SetProperty(ScadaAxisOptionsCodec.Key, ScadaAxisOptionsCodec.Format(new[]
            {
                new ScadaAxisRef(x, "X"),
                new ScadaAxisRef(y, "Y"),
                new ScadaAxisRef(z, "Z"),
            }));
            panel.Refresh();

            Check("[AX3] 设计期多选 → 下拉项按选择顺序出现",
                panel.AxisItems.Count == 3
                && panel.AxisItems[0].AxisId == x
                && panel.AxisItems[1].AxisId == y
                && panel.AxisItems[2].AxisId == z,
                string.Join("、", panel.AxisItems.Select(i => i.DisplayName)));

            Check("[AX3] 多轴时下拉框可见、单轴标签收起",
                panel.HasMultipleAxes
                && panel.SelectorVisibility == Visibility.Visible
                && panel.SingleAxisVisibility == Visibility.Collapsed,
                $"multi={panel.HasMultipleAxes}");

            Check("[AX3] 设计期不编造量值：位置显示—、状态说明是设计预览",
                panel.PositionText == "—" && panel.StatusText.Contains("设计预览"),
                $"position={panel.PositionText} / status={panel.StatusText}");

            element.SetProperty(ScadaAxisOptionsCodec.Key,
                ScadaAxisOptionsCodec.Format(new[] { new ScadaAxisRef(x, "X") }));
            panel.Refresh();

            Check("[AX3] 只配一根轴时下拉框收起、静态轴名出现",
                !panel.HasMultipleAxes
                && panel.SelectorVisibility == Visibility.Collapsed
                && panel.SingleAxisVisibility == Visibility.Visible,
                $"multi={panel.HasMultipleAxes}");

            element.SetProperty(ScadaAxisOptionsCodec.Key, string.Empty);
            panel.Refresh();

            Check("[AX3] 一根轴都没配时给空态提示（而不是一个空白卡片）",
                !panel.HasAxes && panel.EmptyVisibility == Visibility.Visible,
                $"hasAxes={panel.HasAxes}");
        }

        // ==================================================================
        //  [AX-4] 轴面板（运行期）：下拉切换、快照落地、离线显示未知、退订干净
        // ==================================================================
        private static void AxisPanelRuntimeChecks()
        {
            Guid x = Guid.NewGuid(), y = Guid.NewGuid();

            var element = ElementRegistry.CreateElement("Hmi.AxisPanel");
            element.SetProperty(ScadaAxisOptionsCodec.Key, ScadaAxisOptionsCodec.Format(new[]
            {
                new ScadaAxisRef(x, "X"),
                new ScadaAxisRef(y, "Y"),
            }));

            var panel = (AxisPanelElement)ElementRegistry.CreateControl(element);
            panel.Refresh();

            var source = new FakeAxisSource();
            source.Add(x, "X", "卡A", enabled: true, available: true,
                Sample(12.345, 50, enabled: true, alarm: false, moving: false));
            source.Add(y, "Y", "卡A", enabled: true, available: true,
                Sample(-3.5, 0, enabled: false, alarm: true, moving: false));

            var doc = new ScadaDocument();
            var engine = new ScadaAlarmEngine(doc, new FakeValueSource());
            var beat = new ScadaBeatSource(Dispatcher.CurrentDispatcher, TimeSpan.FromMilliseconds(20));
            var context = new ScadaRuntimeContext(engine, beat, axisSource: source);

            int beatBefore = SubscriberCount(beat, nameof(ScadaBeatSource.Beat));
            panel.RuntimeContext = context;

            Check("[AX4] 装上运行态上下文后订阅统一节拍（不自己起定时器）",
                SubscriberCount(beat, nameof(ScadaBeatSource.Beat)) == beatBefore + 1,
                $"{SubscriberCount(beat, nameof(ScadaBeatSource.Beat))} 个订阅者");

            Check("[AX4] 默认选中第一根候选轴", panel.SelectedAxisId == x, panel.SelectedAxisName);

            Check("[AX4] 位置/速度按快照落地（三位小数、一位小数）",
                panel.PositionText == "12.345" && panel.VelocityText == "50.0",
                $"{panel.PositionText} / {panel.VelocityText}");

            Check("[AX4] 使能灯亮强调色、报警灯灭熄灭色",
                SameColor(panel.EnableLampBrush, panel.AccentColor)
                && SameColor(panel.AlarmLampBrush, panel.OffColor),
                $"{BrushText(panel.EnableLampBrush)} / {BrushText(panel.AlarmLampBrush)}");

            // 下拉切换：换到 Y，下一拍/下一次刷新读到 Y 的快照
            panel.SelectedAxisId = y;
            panel.Refresh();

            Check("[AX4] 切换轴后只显示当前轴：名字、位置、报警灯都跟着换",
                panel.SelectedAxisName == "Y"
                && panel.PositionText == "-3.500"
                && SameColor(panel.AlarmLampBrush, panel.AlarmColor),
                $"{panel.SelectedAxisName} / {panel.PositionText}");

            // 离线：不编 0，显示—并给出原因
            source.Update(y, ScadaAxisSample.Invalid("运动卡「卡A」未连接"));
            panel.Refresh();

            Check("[AX4] 离线时不把未知伪装成 0：位置—、状态带原因",
                !panel.IsValid && panel.PositionText == "—" && panel.StatusText.Contains("未连接"),
                $"valid={panel.IsValid} / status={panel.StatusText}");

            // 轴被移除：不自动跳到别的轴，项上标"已移除"
            source.Remove(y);
            panel.Refresh();

            Check("[AX4] 当前轴被移除后不自动跳到别的轴（防错靶）",
                panel.SelectedAxisId == y
                && panel.AxisItems.Any(i => i.AxisId == y && i.DisplayName.Contains("已移除")),
                string.Join("、", panel.AxisItems.Select(i => i.DisplayName)));

            // 摘上下文：节拍订阅必须成对退掉
            panel.RuntimeContext = null;
            Check("[AX4] 摘掉运行态上下文后节拍订阅归零（不把控件树钉在长命节拍上）",
                SubscriberCount(beat, nameof(ScadaBeatSource.Beat)) == beatBefore,
                $"{SubscriberCount(beat, nameof(ScadaBeatSource.Beat))} 个订阅者");
        }

        // ==================================================================
        //  [AX-6] 选轴弹窗：候选来自注册表（只列已启用）、XAML 能渲染
        // ==================================================================
        private static void AxisPickerRenderChecks()
        {
            var cardId = Guid.NewGuid();
            var card = new MotionDescriptor { Id = cardId, DisplayName = "卡A" };
            card.Axes.Add(new AxisMapping { LogicalName = "X", PhysicalIndex = 0 });
            card.Axes.Add(new AxisMapping { LogicalName = "Y", PhysicalIndex = 1 });
            card.Axes.Add(new AxisMapping { LogicalName = "停用轴", PhysicalIndex = 2, Enabled = false });

            var registry = new MotionAxisRegistry(() => new[] { card });
            var vm = new ScadaAxisPickerViewModel(registry);

            vm.OnDialogOpened(new DialogParameters());

            Check("[AX6] 弹窗只列已启用的轴（停用轴不出现）",
                vm.Axes.Count == 2 && vm.Axes.All(a => a.Name != "停用轴"),
                string.Join("、", vm.Axes.Select(a => a.Name)));

            // XAML 解析/绑定的运行期验证：编译期全绿也可能在套模板时炸。
            ViewModelLocationProvider.Register<ScadaAxisPickerView>(() => vm);
            var view = new ScadaAxisPickerView { DataContext = vm };
            view.Measure(new Size(560, 520));
            view.Arrange(new Rect(0, 0, 560, 520));
            view.UpdateLayout();

            int checkBoxes = CollectVisual<CheckBox>(view, _ => true).Count;
            Check("[AX6] 选轴弹窗渲染出与候选数量一致的勾选行",
                checkBoxes == vm.Axes.Count && checkBoxes > 0,
                $"勾选框 {checkBoxes} 个 / 候选 {vm.Axes.Count} 条");

            // 过滤与选择的关系：搜索框把已选行藏起来时，确认不能把它悄悄丢掉。
            IDialogResult? closed = null;
            vm.RequestClose = MakeDialogCloseListener(r => closed = r);

            vm.Axes.First(a => a.Name == "X").IsSelected = true;
            vm.Keyword = "Y";

            Check("[AX6] 已选行被搜索过滤掉时「确定」仍可用（选择不随可见性丢失）",
                vm.ConfirmCommand.CanExecute(), $"可见 {vm.Axes.Count} 条");

            vm.ConfirmCommand.Execute();
            var picked = closed?.Parameters.GetValue<List<ScadaAxisRef>>(ScadaAxisPicker.PickedAxesKey);

            Check("[AX6] 确认结果包含被过滤掉的已选轴（从全量而不是可见清单取）",
                picked != null && picked.Count == 1 && picked[0].Name == "X",
                picked == null ? "未回传" : string.Join("、", picked.Select(p => p.Name)));
        }

        // ==================================================================
        //  [AX-5] 轴状态发布器：AxisStatus → ScadaAxisSample，离线不编 0
        // ==================================================================
        private static void AxisPublisherChecks()
        {
            var cardId = Guid.NewGuid();
            var card = new MotionDescriptor
            {
                Id = cardId,
                DisplayName = "测试卡",
                Address = "127.0.0.1",
            };
            card.Axes.Add(new AxisMapping { LogicalName = "X", PhysicalIndex = 0 });

            var device = new FakeMotionDevice(card)
            {
                State = MotionCardState.Online,
                Status = new AxisStatus
                {
                    PhysicalIndex = 0,
                    PositionMm = 7.5,
                    VelocityMmPerS = 12,
                    Moving = true,
                    InPosition = false,
                    Enabled = true,
                    Alarm = false,
                    PositiveLimit = false,
                    NegativeLimit = false,
                    Origin = true,
                    EmergencyStop = false,
                },
            };

            var provider = new FakeMotionProvider(card, device);
            var cards = new List<MotionDescriptor> { card };
            var registry = new MotionAxisRegistry(() => cards);

            var publisher = new MotionStatePublisher(new FakeLogService(), provider, registry);

            // 第一拍会顺带让注册表给历史轴补身份（Snapshot → EnsureAxisIds），
            // 所以身份要在第一拍之后读，不能在这之前。
            publisher.RefreshNow();

            Guid axisId = card.Axes[0].AxisId;
            Check("[AX5] 发布器会先给历史轴补身份（否则按 Id 取不到）", axisId != Guid.Empty, $"{axisId}");

            Check("[AX5] 在线时能按轴身份读到量值快照",
                publisher.TryGetSample(axisId, out var sample)
                && sample.IsValid
                && Math.Abs(sample.PositionMm - 7.5) < 1e-9
                && sample.Moving
                && sample.Origin,
                $"{sample.PositionMm} / moving={sample.Moving}");

            Check("[AX5] 身份与可用性也按轴身份给出",
                publisher.TryGetInfo(axisId, out var info)
                && info.Name == "X"
                && info.IsAvailable,
                info?.Name ?? "null");

            // 离线：状态清空 → IsValid=false，但仍然按轴身份返回一条"未知"
            device.Status = null;
            device.State = MotionCardState.Closed;
            publisher.RefreshNow();

            Check("[AX5] 离线时返回 IsValid=false 的未知快照（不返回 0 位置）",
                publisher.TryGetSample(axisId, out var offline)
                && !offline.IsValid
                && !string.IsNullOrWhiteSpace(offline.Error),
                offline.Error ?? "");

            Check("[AX5] 轴表拓扑变化会推高版本号（轴面板据此重建下拉项）",
                publisher.Version > 0, $"version={publisher.Version}");

            publisher.Dispose();
        }

        // ==================================================================
        //  [AX-7] 运动命令族：门禁 + 下发（用假设备记账）
        // ==================================================================
        private static void MotionActionChecks()
        {
            var cardId = Guid.NewGuid();
            var card = new MotionDescriptor { Id = cardId, DisplayName = "测试卡", Address = "1.2.3.4" };
            card.Axes.Add(new AxisMapping { LogicalName = "X", PhysicalIndex = 0, AxisId = Guid.NewGuid() });
            card.Axes.Add(new AxisMapping { LogicalName = "停用轴", PhysicalIndex = 1, AxisId = Guid.NewGuid(), Enabled = false });

            var device = new FakeMotionDevice(card)
            {
                State = MotionCardState.Online,
                IsHomed = true,
                Status = new AxisStatus { PhysicalIndex = 0, Enabled = true, Moving = false },
            };

            var provider = new FakeMotionProvider(card, device);
            var registry = new MotionAxisRegistry(() => new[] { card });
            var executor = new MotionActionExecutor(registry, provider, new FakeLogService());

            var xId = card.Axes[0].AxisId;

            var move = new ScadaAction { Type = ScadaActionType.MotionMoveAbsolute, TargetMm = 100, VelocityMmPerS = 50 };
            move.BindAxis(xId, "X");

            bool ok = executor.TryExecute(move, out var message);
            Check("[AX7] 定位动作按轴身份下发成功（门禁全过 → Enqueue）",
                ok && device.LastCommand?.Kind == MotionCommandKind.MoveAbsolute
                && Math.Abs(device.LastCommand.TargetMm - 100) < 1e-9,
                message);

            // 未使能 → 门禁拒绝，且不产生命令
            device.Status = new AxisStatus { PhysicalIndex = 0, Enabled = false };
            device.LastCommand = null;
            ok = executor.TryExecute(move, out message);
            Check("[AX7] 未使能时被门禁拦下（不产生任何下发），原因可直接上屏",
                !ok && device.LastCommand == null && message.Contains("未使能"),
                message);

            // 软限位：目标越界必须拦
            device.Status = new AxisStatus { PhysicalIndex = 0, Enabled = true };
            card.Axes[0].SoftLimitMaxMm = 50;
            device.LastCommand = null;
            ok = executor.TryExecute(move, out message);
            Check("[AX7] 超软限位的目标被拦下（软限位是软件层最后一道防线）",
                !ok && device.LastCommand == null && message.Contains("软限位"),
                message);
            card.Axes[0].SoftLimitMaxMm = 1_000_000;

            // 相对定位：软限位判据是"走完之后在哪"
            device.Status = new AxisStatus { PhysicalIndex = 0, Enabled = true, PositionMm = 45 };
            card.Axes[0].SoftLimitMaxMm = 50;
            var rel = new ScadaAction { Type = ScadaActionType.MotionMoveRelative, TargetMm = 10 };
            rel.BindAxis(xId, "X");
            device.LastCommand = null;
            ok = executor.TryExecute(rel, out message);
            Check("[AX7] 相对定位按「当前位置 + 增量」判软限位（增量本身在界内也要拦）",
                !ok && device.LastCommand == null && message.Contains("软限位"),
                message);
            card.Axes[0].SoftLimitMaxMm = 1_000_000;

            // 停用轴 / 未选轴：明确拒绝
            var disabled = new ScadaAction { Type = ScadaActionType.MotionEnable };
            disabled.BindAxis(card.Axes[1].AxisId, "停用轴");
            ok = executor.TryExecute(disabled, out message);
            Check("[AX7] 停用的轴被拒绝并说明去哪儿启用",
                !ok && message.Contains("停用"), message);

            ok = executor.TryExecute(new ScadaAction { Type = ScadaActionType.MotionEnable }, out message);
            Check("[AX7] 没选轴时给一句可操作的原因（不是静默空跑）",
                !ok && message.Contains("还没选轴"), message);

            // 点动：忙时静默（message 为空串）。先给卡上报点动能力，否则门禁会先拦在"不支持点动"那一档。
            device.Capabilities = new MotionCapabilities { AxisCount = 2, SupportsJog = true };
            device.Status = new AxisStatus { PhysicalIndex = 0, Enabled = true, Moving = true };
            var jog = new ScadaAction { Type = ScadaActionType.MotionJogStart, TargetMm = 1 };
            jog.BindAxis(xId, "X");
            ok = executor.TryExecute(jog, out message);
            Check("[AX7] 点动遇轴忙时静默忽略（message 为空串，调用方不记日志）",
                !ok && message.Length == 0, $"message=\"{message}\"");

            executor.Dispose();
        }

        // ==================================================================
        //  [AX-8] 系统变量：只读投影、确定性身份、不落盘、不可写
        // ==================================================================
        private static void SystemVariableChecks()
        {
            var cardId = Guid.NewGuid();
            var axisId = Guid.NewGuid();
            var card = new MotionDescriptor { Id = cardId, DisplayName = "卡A" };
            card.Axes.Add(new AxisMapping { AxisId = axisId, LogicalName = "X", PhysicalIndex = 0 });

            var registry = new MotionAxisRegistry(() => new[] { card });

            var workspace = new WorkspaceContext();
            var catalog = new VisionMaster.Binding.SystemVariableCatalog(workspace);

            catalog.Rebuild(registry.Snapshot());

            Check("[AX8] 一根轴投影出 12 个只读系统变量",
                catalog.Variables.Count == 12, $"{catalog.Variables.Count} 个");

            Check("[AX8] 名字带 Motion. 前缀且能按名查到",
                catalog.TryGet("Motion.X.Position", out var position)
                && position.DataType == typeof(double),
                string.Join("、", catalog.Variables.Take(3).Select(v => v.Name)));

            Check("[AX8] 身份是确定性的（重建后同一个变量 Id 不变）",
                position.VariableId != Guid.Empty
                && VisionMaster.Binding.SystemVariableCatalog.IsReservedName("Motion.X.Position"),
                $"{position.VariableId}");

            // 实例复用：重建后必须是同一个对象（否则订阅方全部失联）
            var before = position;
            catalog.Rebuild(registry.Snapshot());
            catalog.TryGet("Motion.X.Position", out var after);
            Check("[AX8] 重建投影复用同一模型实例（订阅方不会失联）",
                ReferenceEquals(before, after), "");

            // 不落盘：工作区的用户变量集合里一条系统变量都不许出现
            // （GlobalVariables 由 InitializeCommonVariables 铺了演示变量，所以判据是"没有 Motion. 前缀的"，
            //  而不是"集合为空"）
            Check("[AX8] 系统变量不进用户变量集合（绝不落盘）",
                !workspace.GlobalVariables.Any(v => v.Name != null
                    && v.Name.StartsWith(VisionMaster.Binding.SystemVariableCatalog.NamePrefix, StringComparison.OrdinalIgnoreCase))
                && workspace.SystemVariables.Count == catalog.Variables.Count,
                $"用户 {workspace.GlobalVariables.Count} 个 / 系统 {workspace.SystemVariables.Count} 个");

            // 不可写：没有实现 IWritableVariable
            Check("[AX8] 系统变量不可写（不实现 IWritableVariable，写入路径会如实拒绝）",
                after is not VisionMaster.Models.IWritableVariable, after.GetType().Name);

            // 值投影 + 同值不通知
            int raised = 0;
            after.ValueChanged += (_, _) => raised++;

            catalog.Publish(axisId, Sample(12.5, 0, enabled: true, alarm: false, moving: false));
            catalog.Publish(axisId, Sample(12.5, 0, enabled: true, alarm: false, moving: false));

            Check("[AX8] 值投影落地且同值只通知一次（差分推送）",
                Convert.ToDouble(after.Value) == 12.5 && raised == 1,
                $"value={after.Value} / raised={raised}");

            // 离线：数值保留上次已知值、状态文本换成原因（不把未知伪装成 0）
            catalog.Publish(axisId, ScadaAxisSample.Invalid("运动卡「卡A」未连接"));
            catalog.TryGet("Motion.X.StatusText", out var statusText);

            Check("[AX8] 离线时保留上次已知位置、状态文本写明原因（不编 0）",
                Convert.ToDouble(after.Value) == 12.5
                && statusText.Value is string s && s.Contains("未连接"),
                $"{after.Value} / {statusText.Value}");
        }

        // ==================================================================
        //  测试替身
        // ==================================================================

        private static ScadaAxisSample Sample(double position, double velocity,
            bool enabled, bool alarm, bool moving)
            => new(
                IsValid: true,
                Error: null,
                PositionMm: position,
                VelocityMmPerS: velocity,
                Moving: moving,
                InPosition: !moving,
                Enabled: enabled,
                Alarm: alarm,
                PositiveLimit: false,
                NegativeLimit: false,
                Origin: false,
                EmergencyStop: false,
                StatusText: alarm ? "报警" : "就绪",
                TimestampUtc: DateTime.UtcNow);

        private static bool SameColor(Brush? actual, Brush? expected)
            => actual is SolidColorBrush a && expected is SolidColorBrush e && a.Color == e.Color;

        private static string BrushText(Brush? brush)
            => brush is SolidColorBrush s ? s.Color.ToString() : brush?.ToString() ?? "null";

        private sealed class FakeAxisSource : IScadaAxisSource
        {
            private readonly Dictionary<Guid, ScadaAxisInfo> _infos = new();
            private readonly Dictionary<Guid, ScadaAxisSample> _samples = new();

            public int Version { get; private set; }

            public event EventHandler? Changed;

            public void Add(Guid id, string name, string card, bool enabled, bool available, ScadaAxisSample sample)
            {
                _infos[id] = new ScadaAxisInfo(id, name, card, enabled, available);
                _samples[id] = sample;
                Version++;
                Changed?.Invoke(this, EventArgs.Empty);
            }

            public void Update(Guid id, ScadaAxisSample sample)
            {
                _samples[id] = sample;
                Version++;
                Changed?.Invoke(this, EventArgs.Empty);
            }

            public void Remove(Guid id)
            {
                _infos.Remove(id);
                _samples.Remove(id);
                Version++;
                Changed?.Invoke(this, EventArgs.Empty);
            }

            public bool TryGetInfo(Guid axisId, out ScadaAxisInfo info)
                => _infos.TryGetValue(axisId, out info!);

            public bool TryGetSample(Guid axisId, out ScadaAxisSample sample)
                => _samples.TryGetValue(axisId, out sample);
        }

        private sealed class FakeMotionProvider : IMotionProvider
        {
            private readonly IMotionDevice _device;

            public FakeMotionProvider(MotionDescriptor card, IMotionDevice device)
            {
                Cards = new[] { card };
                _device = device;
            }

            public IReadOnlyList<MotionDescriptor> Cards { get; }

            public bool TryGetDevice(Guid cardId, out IMotionDevice device)
            {
                device = _device;
                return true;
            }

            public bool TryGetByKey(string address, out IMotionDevice device)
            {
                device = _device;
                return true;
            }

            public bool TryGetByName(string displayName, out IMotionDevice device)
            {
                device = _device;
                return true;
            }

            public void ReleaseAll() { }
        }

        private sealed class FakeMotionDevice : IMotionDevice
        {
            public FakeMotionDevice(MotionDescriptor descriptor) => Descriptor = descriptor;

            public MotionDescriptor Descriptor { get; }

            public MotionCardState State { get; set; } = MotionCardState.Closed;

            public AxisStatus? Status { get; set; }

            /// <summary>最后一次被下发的命令（断言据此看"到底发了什么"）</summary>
            public MotionCommand? LastCommand { get; set; }

            public string StateDetail => State.ToString();

            public MotionCapabilities Capabilities { get; set; } = MotionCapabilities.Unknown;

            public string ModelName => "Fake";

            public MotionFault? LastFault => null;

            public bool IsHomed { get; set; }

            public long ExecutedCommandCount => 0;

            public long RejectedCommandCount => 0;

            public long FaultCount => 0;

            public int PendingCommandCount => 0;

            public event EventHandler? StateChanged;

            public event EventHandler<MotionFault>? FaultOccurred;

            public bool Connect() => true;

            public void Disconnect() { }

            public bool ClearAlarm(out string error)
            {
                error = string.Empty;
                return true;
            }

            public void NotifyAlive() { }

            public AxisStatus? GetAxisStatus(int physicalIndex) => Status;

            public bool ReadInput(int port) => false;

            public MotionCommandResult Enqueue(MotionCommand command)
            {
                LastCommand = command;
                return MotionCommandResult.Accepted;
            }

            public MotionCommandResult EmergencyStop() => MotionCommandResult.Accepted;

            public void CancelAll(string reason) { }

            public void ResetCounters() { }

            public void Dispose() { }
        }

        private sealed class FakeLogService : ILogService
        {
            public void Success(params string[] messages) { }
            public void Error(params Exception[] messages) { }
            public void Error(params string[] messages) { }
            public void Info(params string[] messages) { }
            public void Warn(params string[] messages) { }
        }
    }
}
