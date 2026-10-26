using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Core.Interfaces;
using Prism.Dialogs;
using Prism.Mvvm;
using VisionMaster.Scada;
using VisionMaster.Scada.Controls;
using VisionMaster.Models;
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
        /// <summary>B3 轴面板夹具：管理员组含全部 15 个 Key，避免默认策略把命令全拒。</summary>
        private static FakeAccessPolicy AxisAdminPolicy()
            => new() { CurrentRole = ScadaRole.Administrator };

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
            SystemDeviceCatalogChecks();
            AxisCommandChecks();
            AxisJogModeChecks();
            AxisPointTableChecks();
            AxisConsoleMergeChecks();
            AxisOptionsSummaryChecks();
            MotionDispatcherChainChecks();
            AxisConsoleRenderChecks();
            LegacyAxisMigrationChecks();
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

            // 操作按钮是面板"调试台"形态的核心：命名件缺一个，按钮就永远不响应
            // （代码里的 GetTemplateChild 拿不到 → 静默无操作，编译期全绿）。
            var commandParts = new[] { "BtnEnable", "BtnStop", "BtnHome", "BtnJogForward", "BtnJogReverse", "BtnClearAlarm" };
            var missing = commandParts.Where(n => panel.Template?.FindName(n, panel) is not Button).ToList();

            Check("[AX3] 六个操作按钮的命名件齐备（缺一个就是永远点不动的按钮）",
                templated && missing.Count == 0,
                missing.Count == 0 ? "6/6" : "缺：" + string.Join("、", missing));

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
            var context = new ScadaRuntimeContext(engine, beat, axisSource: source,
                accessPolicy: AxisAdminPolicy());

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

            // 真验证"拓扑变化才涨版本"：再拍一次（什么都没变）版本必须不动
            int afterFirst = publisher.Version;
            publisher.RefreshNow();
            Check("[AX5] 轴表没变化时不推版本号（否则面板每拍都重建下拉项）",
                publisher.Version == afterFirst, $"{afterFirst} → {publisher.Version}");

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

            // 报警态：停止 / 失能必须放行，使能必须拒绝并说清"先清报警"。
            // 这三条与 FlowCanvasChecks 的设备级断言同一口径（那里用虚拟卡验基类门禁，
            // 这里用假设备验执行器的闸门——两层都要对，缺一层就会出现"面板放行、卡拒绝"）。
            device.State = MotionCardState.Alarm;

            device.LastCommand = null;
            var stopAction = new ScadaAction { Type = ScadaActionType.MotionStop };
            stopAction.BindAxis(xId, "X");
            ok = executor.TryExecute(stopAction, out message);
            Check("[AX7] 报警态下停止命令仍被下发（停是安全动作，不能被门禁挡）",
                ok && device.LastCommand?.Kind == MotionCommandKind.Stop, message);

            device.LastCommand = null;
            var disableAction = new ScadaAction { Type = ScadaActionType.MotionDisable };
            disableAction.BindAxis(xId, "X");
            ok = executor.TryExecute(disableAction, out message);
            Check("[AX7] 报警态下失能命令仍被下发（清报警前往往要先失能）",
                ok && device.LastCommand?.Kind == MotionCommandKind.Disable, message);

            device.LastCommand = null;
            var enableInAlarm = new ScadaAction { Type = ScadaActionType.MotionEnable };
            enableInAlarm.BindAxis(xId, "X");
            ok = executor.TryExecute(enableInAlarm, out message);
            Check("[AX7] 报警态下使能被拒绝并提示先清报警（不能带着报警上电）",
                !ok && device.LastCommand == null && message.Contains("报警"), message);

            // 清报警必须按**单轴**下发：旧实现不带 PhysicalAxis，驱动会清整卡。
            device.State = MotionCardState.Alarm;
            device.LastCommand = null;
            var clearAction = new ScadaAction { Type = ScadaActionType.MotionClearAlarm };
            clearAction.BindAxis(xId, "X");
            ok = executor.TryExecute(clearAction, out message);
            Check("[AX7] 清报警按选中轴的物理轴号下发（不是清整卡）",
                ok && device.LastCommand?.Kind == MotionCommandKind.ClearAlarm
                && device.LastCommand.PhysicalAxis == 0,
                $"{message} / axis={device.LastCommand?.PhysicalAxis}");

            device.State = MotionCardState.Online;

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

            // ★ 反向：Id 失效时**不许**按名字兜底到另一根轴（"删了旧的、又建了同名新的"是产线常态）
            var stale = new ScadaAction { Type = ScadaActionType.MotionEnable };
            stale.BindAxis(Guid.NewGuid(), "X");
            device.LastCommand = null;
            ok = executor.TryExecute(stale, out message);
            Check("[AX7] 轴 Id 已失效时拒绝执行，绝不按名字兜底到另一根轴（防错靶）",
                !ok && device.LastCommand == null && message.Contains("已被删除"),
                message);

            // 点位：示教写位置+速度、改速度只写速度（运行期改配置）
            ok = executor.TryTeach(xId, 3, 42.5, 30, out var teachError);
            var point3 = card.GetAxisPoints("X").First(p => p.Index == 3);
            Check("[AX7] 示教把位置与速度写进指定点位",
                ok && Math.Abs(point3.PositionMm - 42.5) < 1e-9
                && Math.Abs(point3.SpeedMmPerS - 30) < 1e-9,
                teachError ?? "");

            ok = executor.TrySetPointSpeed(xId, 3, 15, out var speedError);
            Check("[AX7] 改速度只动速度，不动位置",
                ok && Math.Abs(point3.SpeedMmPerS - 15) < 1e-9
                && Math.Abs(point3.PositionMm - 42.5) < 1e-9,
                speedError ?? "");

            ok = executor.TrySetPointSpeed(xId, 3, -1, out var badSpeed);
            Check("[AX7] 非法速度（负数）被拒绝且不写模型",
                !ok && badSpeed != null && Math.Abs(point3.SpeedMmPerS - 15) < 1e-9,
                badSpeed ?? "");

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
            var catalog = new VisionMaster.Binding.SystemVariableCatalog(workspace, workspace.VariableRegistry);

            catalog.Rebuild(registry.Snapshot());

            Check("[AX8] 一根轴投影出 1 个父变量 + 12 个只读字段（共 13 条）",
                catalog.Variables.Count == 13, $"{catalog.Variables.Count} 个");

            Check("[AX8] 父变量是 MotionAxis 类型的整轴快照，名字是 System.Motion.X",
                catalog.TryGet("System.Motion.X", out var axisDevice)
                && axisDevice.DataType == typeof(VisionMaster.Models.MotionAxis)
                && axisDevice is VisionMaster.Models.ISystemVariableContainer { Fields.Count: 12 }
                && axisDevice.VariableType == VisionMaster.VariableType.System,
                $"{axisDevice?.DataType.Name} / 字段 {(axisDevice as VisionMaster.Models.ISystemVariableContainer)?.Fields.Count}");

            Check("[AX8] 清单顺序是「父 → 12 字段」（界面按这个顺序展开）",
                ReferenceEquals(catalog.Variables[0], axisDevice)
                && catalog.Variables.Skip(1).All(v => v.Name.StartsWith("System.Motion.X.", StringComparison.Ordinal)),
                string.Join("、", catalog.Variables.Take(3).Select(v => v.Name)));

            Check("[AX8] 名字带 Motion. 前缀且能按名查到",
                catalog.TryGet("System.Motion.X.Position", out var position)
                && position.DataType == typeof(double),
                string.Join("、", catalog.Variables.Take(3).Select(v => v.Name)));

            // 兼容红线：叶子的身份推导一个字节都不能变，否则老方案里按 Id 绑定的
            // 连线 / 画面 / 监视项在重构后全部失联。
            Check("[AX8] 叶子变量 Id 与重构前逐字节一致（老 .vms 零迁移）",
                position.VariableId == LegacyMotionLeafId(axisId, "Position"),
                $"{position.VariableId} vs {LegacyMotionLeafId(axisId, "Position")}");

            Check("[AX8] 身份是确定性的（同一份轴表重建得到同一个变量 Id）",
                position.VariableId != Guid.Empty
                && VisionMaster.Binding.SystemVariableCatalog.IsReservedName(position.Name),
                $"{position.VariableId}");

            // 真验证"确定性"：换一个全新的 catalog（模拟重启）从同一份轴表重建，Id 必须一致
            var second = new VisionMaster.Binding.SystemVariableCatalog(
                new WorkspaceContext(), null);
            second.Rebuild(registry.Snapshot());
            second.TryGet(position.Name, out var rebuilt);

            Check("[AX8] 另一个 catalog 实例（模拟重启）投影出同一个 Id（确定性身份）",
                rebuilt != null && rebuilt.VariableId == position.VariableId,
                $"{position.VariableId} vs {rebuilt?.VariableId}");

            second.TryGet("System.Motion.X", out var rebuiltDevice);
            Check("[AX8] 父变量同样是确定性身份（模拟重启后同一个 Id）",
                rebuiltDevice != null && axisDevice != null
                && rebuiltDevice.VariableId == axisDevice.VariableId
                && rebuiltDevice.VariableId != position.VariableId,
                $"{axisDevice?.VariableId} vs {rebuiltDevice?.VariableId}");

            // 实例复用：重建后必须是同一个对象（否则订阅方全部失联）
            var before = position;
            catalog.Rebuild(registry.Snapshot());
            catalog.TryGet("System.Motion.X.Position", out var after);
            Check("[AX8] 重建投影复用同一模型实例（订阅方不会失联）",
                ReferenceEquals(before, after), "");

            // 拓扑与名字都没变 → 必须短路（100ms 一拍，不能每拍重建表/触发宿主刷新）
            var snapshotBefore = catalog.Snapshot();
            catalog.Rebuild(registry.Snapshot());

            Check("[AX8] 拓扑与名字都没变时重建短路（不重建表）",
                ReferenceEquals(snapshotBefore, catalog.Snapshot()), string.Empty);

            // 集合里挂着别家（通讯/相机）的系统变量时，本目录仍必须判"已最新"。
            // 这条打的是"按类型数变量"的写法：那种写法在有第二个提供方之后永远对不上，
            // 表现是运动目录每 100ms 全量重建一次（不报错，只是白烧 CPU）。
            var foreign = new VisionMaster.Models.SystemVariableModel(
                Guid.NewGuid(), "System.Comm.PLC_X", typeof(string), "外来变量", owner: "Comm");
            workspace.SystemVariables.Add(foreign);
            var snapshotWithForeign = catalog.Snapshot();
            catalog.Rebuild(registry.Snapshot());

            Check("[AX8] 集合里有别家系统变量时本目录仍短路（不按类型全数）",
                ReferenceEquals(snapshotWithForeign, catalog.Snapshot()),
                $"集合 {workspace.SystemVariables.Count} 条");

            workspace.SystemVariables.Remove(foreign);

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

            Check("[AX8] 父变量的值是整根轴的 MotionAxis 快照（与字段同一拍）",
                axisDevice?.Value is VisionMaster.Models.MotionAxis axisSnapshot
                && axisSnapshot.IsValid
                && axisSnapshot.AxisName == "X"
                && Math.Abs(axisSnapshot.PositionMm - 12.5) < 1e-9
                && Math.Abs(axisSnapshot.VelocityMmPerS) < 1e-9,
                axisDevice?.Value?.GetType().Name ?? "(null)");

            // ★ 差分推送：MotionAxis 含 TimestampUtc，而发布器每拍都会写新时间戳。
            //   若时间戳参与 Equals，父变量就会每 100ms 发一次 ValueChanged（一屏几十个绑定白刷）。
            //   这里连发两拍同值：必须只通知一次（第一拍变值，第二拍同值）。
            int parentRaised = 0;
            axisDevice!.ValueChanged += (_, _) => parentRaised++;

            catalog.Publish(axisId, Sample(13.0, 0, enabled: true, alarm: false, moving: false));
            catalog.Publish(axisId, Sample(13.0, 0, enabled: true, alarm: false, moving: false));

            Check("[AX8] 父变量同值只通知一次（时间戳不参与相等判断）",
                parentRaised == 1, $"{parentRaised} 次");

            // 还原成后面离线断言期望的位置
            catalog.Publish(axisId, Sample(12.5, 0, enabled: true, alarm: false, moving: false));

            // 离线：数值保留上次已知值、状态文本换成原因（不把未知伪装成 0）
            catalog.Publish(axisId, ScadaAxisSample.Invalid("运动卡「卡A」未连接"));
            catalog.TryGet("System.Motion.X.StatusText", out var statusText);

            Check("[AX8] 离线时保留上次已知位置、状态文本写明原因（不编 0）",
                Convert.ToDouble(after.Value) == 12.5
                && statusText.Value is string s && s.Contains("未连接"),
                $"{after.Value} / {statusText.Value}");

            Check("[AX8] 离线时父变量保留上次位置、Error 写明原因（同样不编 0）",
                axisDevice?.Value is VisionMaster.Models.MotionAxis offlineAxis
                && !offlineAxis.IsValid
                && Math.Abs(offlineAxis.PositionMm - 12.5) < 1e-9
                && (offlineAxis.Error?.Contains("未连接") ?? false),
                axisDevice?.Value?.ToString() ?? "(null)");

            // ★★ 反向：轴改名之后，**新名必须能查到、旧名必须查不到**。
            // 这条打的是"实例复用只改 Name、集合不增不减 → 名字索引永久停在旧名"那一类静默断链：
            // 表面一切正常（按 Id 仍命中、值照常刷新），只有"按新名查找"永远失败。
            card.Axes[0].LogicalName = "X2";
            catalog.Rebuild(registry.Snapshot());

            bool newNameResolves = workspace.VariableRegistry.FindByName("System.Motion.X2.Position") != null;
            bool oldNameGone = workspace.VariableRegistry.FindByName("System.Motion.X.Position") == null;
            bool newDeviceResolves = workspace.VariableRegistry.FindByName("System.Motion.X2") != null;
            bool oldDeviceGone = workspace.VariableRegistry.FindByName("System.Motion.X") == null;

            Check("[AX8] 轴改名后按新名可查、按旧名查不到（名字索引随派生名刷新）",
                newNameResolves && oldNameGone && newDeviceResolves && oldDeviceGone,
                $"字段 新名={newNameResolves}/旧名仍命中={!oldNameGone}；父变量 新名={newDeviceResolves}/旧名仍命中={!oldDeviceGone}");

            Check("[AX8] 轴改名后变量 Id 不变（按 Id 的绑定不会断）",
                ReferenceEquals(workspace.VariableRegistry.FindById(after.VariableId), after),
                $"{after.VariableId}");

            // ★ 新建与改名共用同一份命名规则：System. 是系统变量保留前缀。
            //   交接里记的"只有 TryRename 拦、新建不拦"就是两处各写一份造成的；
            //   现在两处都走 VariableNameRules，这条断言把它钉住。
            bool newReserved = VisionMaster.Binding.VariableNameRules.TryValidateNewName("System.X", out var reservedError);
            bool newEmpty = VisionMaster.Binding.VariableNameRules.TryValidateNewName("   ", out var emptyError);
            bool newOk = VisionMaster.Binding.VariableNameRules.TryValidateNewName("Var", out _);

            Check("[AX8] 新建变量拒绝 System. 保留前缀与空名（与改名共用 VariableNameRules）",
                !newReserved && reservedError.Contains("保留前缀")
                && !newEmpty && emptyError.Contains("不能为空")
                && newOk,
                $"{reservedError} / {emptyError}");

            var local = new VisionMaster.Models.LocalVariableModel { Name = "Var" };
            workspace.GlobalVariables.Add(local);
            bool renamedReserved = workspace.VariableRegistry.TryRename(local, "System.X", out var renameError);
            workspace.GlobalVariables.Remove(local);

            Check("[AX8] 改名同样拒绝 System. 保留前缀（两条入口同一句话）",
                !renamedReserved && renameError.Contains("保留前缀"),
                renameError);
        }

        /// <summary>
        /// 重构前的叶子身份推导（原样抄一份，作为"零迁移"的外部锚点）。
        /// 不从产品代码里调同一个函数：那样改了推导也一起变，断言等于自证。
        /// </summary>
        private static Guid LegacyMotionLeafId(Guid axisId, string field)
        {
            var key = $"motion-var|{axisId:N}|{field}";
            var hash = SHA1.HashData(Encoding.UTF8.GetBytes(key));

            Span<byte> bytes = stackalloc byte[16];
            hash.AsSpan(0, 16).CopyTo(bytes);
            return new Guid(bytes);
        }

        /// <summary>
        /// [AX-8b] 通讯 / 相机的系统变量目录：设备级复合变量 + 字段投影。
        ///
        /// 这两块是新增能力（重构前根本没有），所以断言覆盖三件事：
        /// ① 拓扑（一台设备 = 1 父 + N 字段，设备移除后整组消失）；
        /// ② 值投影（状态/错误/计数来自设备，不是编的）；
        /// ③ 名字与身份（父变量按名字可解析、重名相机不撞名、实例复用）。
        /// </summary>
        private static void SystemDeviceCatalogChecks()
        {
            // ================= 通讯 =================
            var commSource = new FakeCommDeviceStateSource();
            commSource.Devices.Add(new VisionMaster.Binding.CommDeviceState(
                "PLC_Modbus", "Modbus TCP", true, "Connected", "已连接", string.Empty, true, 200));
            commSource.Devices.Add(new VisionMaster.Binding.CommDeviceState(
                "Meter_Modbus", "Modbus TCP", false, "Reconnecting", "重连中", "连接被拒绝", true, 500));

            var comm = new VisionMaster.Binding.CommSystemVariableCatalog(commSource);
            comm.Refresh();

            Check("[AX8b] 两台通讯设备 → 2 个父变量 + 14 个字段（共 16 条）",
                comm.Snapshot().Count == 16, $"{comm.Snapshot().Count} 条");

            Check("[AX8b] 通讯父变量是 CommDevice 类型、名字 System.Comm.{连接名}",
                comm.TryGet("System.Comm.PLC_Modbus", out var plc)
                && plc.DataType == typeof(VisionMaster.Models.CommDevice)
                && plc is VisionMaster.Models.ISystemVariableContainer { Fields.Count: 7 },
                $"{plc?.DataType.Name} / 字段 {(plc as VisionMaster.Models.ISystemVariableContainer)?.Fields.Count}");

            comm.TryGet("System.Comm.PLC_Modbus.Connected", out var plcConnected);
            comm.TryGet("System.Comm.Meter_Modbus.State", out var meterState);
            comm.TryGet("System.Comm.Meter_Modbus.LastError", out var meterError);

            Check("[AX8b] 通讯字段值来自链路状态（已连接 / 重连中 / 最后错误）",
                plcConnected?.Value is true
                && Equals(meterState?.Value, "Reconnecting")
                && (meterError?.Value as string)?.Contains("拒绝") == true,
                $"Connected={plcConnected?.Value} / State={meterState?.Value} / LastError={meterError?.Value}");

            Check("[AX8b] 通讯父变量快照是 CommDevice（整台设备一份）",
                plc.Value is VisionMaster.Models.CommDevice { Connected: true, Protocol: "Modbus TCP" },
                plc.Value?.GetType().Name ?? "(null)");

            // 设备移除：父变量与 7 个字段必须一起消失（不能留下"还在列表里但永远不刷新"的幽灵）
            commSource.Devices.RemoveAll(d => d.Name == "Meter_Modbus");
            comm.Refresh();

            Check("[AX8b] 设备移除后父变量与字段一起消失（不残留幽灵变量）",
                !comm.TryGet("System.Comm.Meter_Modbus", out _)
                && !comm.TryGet("System.Comm.Meter_Modbus.State", out _)
                && comm.Snapshot().Count == 8,
                $"{comm.Snapshot().Count} 条");

            // 实例复用：同一设备再次刷新必须沿用同一个模型（否则订阅方全部失联）
            comm.TryGet("System.Comm.PLC_Modbus", out var plcBefore);
            comm.Refresh();
            comm.TryGet("System.Comm.PLC_Modbus", out var plcAfter);

            Check("[AX8b] 通讯目录重建复用同一实例（订阅方不会失联）",
                ReferenceEquals(plcBefore, plcAfter), string.Empty);

            // ================= 相机 =================
            var provider = new FakeCameraProvider();
            var cameraDescriptor = new CameraDescriptor
            {
                Id = Guid.NewGuid(),
                SerialNo = "SN-001",
                DisplayName = "上相机"
            };
            var cameraDevice = new FakeCameraDevice(cameraDescriptor)
            {
                State = CameraConnectionState.Streaming,
                StateDetail = "正在出图",
                ReceivedFrameCount = 120,
                DroppedFrameCount = 2,
                OverflowCount = 1,
                PendingFrameCount = 3
            };
            provider.Add(cameraDescriptor, cameraDevice);

            var cam = new VisionMaster.Binding.CameraSystemVariableCatalog(provider);
            cam.Refresh();

            Check("[AX8b] 一台相机 → 1 个父变量 + 8 个字段（共 9 条）",
                cam.Snapshot().Count == 9, $"{cam.Snapshot().Count} 条");

            Check("[AX8b] 相机父变量是 CameraDevice 类型、名字 System.Camera.{显示名}",
                cam.TryGet("System.Camera.上相机", out var camDevice)
                && camDevice.DataType == typeof(VisionMaster.Models.CameraDevice)
                && camDevice is VisionMaster.Models.ISystemVariableContainer { Fields.Count: 8 },
                $"{camDevice?.DataType.Name}");

            cam.TryGet("System.Camera.上相机.Online", out var camOnline);
            cam.TryGet("System.Camera.上相机.StateText", out var camStateText);
            cam.TryGet("System.Camera.上相机.ReceivedFrameCount", out var camReceived);

            Check("[AX8b] 相机字段值来自设备（在线 / 采流中 / 收帧计数）",
                camOnline?.Value is true
                && Equals(camStateText?.Value, "采流中")
                && Convert.ToInt64(camReceived?.Value) == 120,
                $"Online={camOnline?.Value} / StateText={camStateText?.Value} / 收帧={camReceived?.Value}");

            Check("[AX8b] 相机父变量快照是 CameraDevice（整台相机一份）",
                camDevice.Value is VisionMaster.Models.CameraDevice { Online: true, ReceivedFrameCount: 120 },
                camDevice.Value?.GetType().Name ?? "(null)");

            // 设备拿不到（驱动未加载 / 尚未创建）：状态改成不可用，计数保留上一拍（不编 0）
            provider.RemoveDevice(cameraDescriptor.Id);
            cam.Refresh();

            cam.TryGet("System.Camera.上相机.Online", out var camOnline2);
            cam.TryGet("System.Camera.上相机.StateText", out var camStateText2);
            cam.TryGet("System.Camera.上相机.ReceivedFrameCount", out var camReceived2);

            Check("[AX8b] 相机设备不可用时状态改为不可用、计数保留上一拍（不编 0）",
                camOnline2?.Value is false
                && Equals(camStateText2?.Value, "设备不可用")
                && Convert.ToInt64(camReceived2?.Value) == 120,
                $"Online={camOnline2?.Value} / StateText={camStateText2?.Value} / 收帧={camReceived2?.Value}");

            // 重名相机：第二台自动加序列号后缀，名字索引里不打架
            var duplicateProvider = new FakeCameraProvider();
            var first = new CameraDescriptor { Id = Guid.NewGuid(), SerialNo = "SN-A", DisplayName = "Cam" };
            var secondCam = new CameraDescriptor { Id = Guid.NewGuid(), SerialNo = "SN-B", DisplayName = "Cam" };
            duplicateProvider.Add(first, new FakeCameraDevice(first));
            duplicateProvider.Add(secondCam, new FakeCameraDevice(secondCam));

            var duplicateCatalog = new VisionMaster.Binding.CameraSystemVariableCatalog(duplicateProvider);
            duplicateCatalog.Refresh();

            Check("[AX8b] 同名相机自动加序列号后缀（名字索引不打架）",
                duplicateCatalog.TryGet("System.Camera.Cam", out _)
                && duplicateCatalog.TryGet("System.Camera.Cam_SN-B", out _)
                && duplicateCatalog.Snapshot().Count == 18,
                $"{duplicateCatalog.Snapshot().Count} 条");

            // ================= 三源合并（生产路径） =================
            // 运动目录走宿主托管模式，通讯/相机直接 Register——这正是 MotionModule +
            // SystemVariableModule 的接线方式。这里验的是"三份快照真的合并进同一个集合"，
            // 而不是三个目录各自能跑（那三条上面已经分别验过）。
            var mergeWorkspace = new WorkspaceContext();
            var mergeHost = new VisionMaster.Binding.SystemVariableHost(mergeWorkspace.SystemVariables);

            var mergeMotion = new VisionMaster.Binding.SystemVariableCatalog(
                mergeWorkspace, mergeWorkspace.VariableRegistry);
            mergeMotion.AttachToHost(mergeHost);

            var mergeCommSource = new FakeCommDeviceStateSource();
            mergeCommSource.Devices.Add(new VisionMaster.Binding.CommDeviceState(
                "PLC_Modbus", "Modbus TCP", true, "Connected", "已连接", string.Empty, true, 200));
            var mergeComm = new VisionMaster.Binding.CommSystemVariableCatalog(mergeCommSource);
            mergeHost.Register(mergeComm);
            mergeComm.Refresh();

            var mergeProvider = new FakeCameraProvider();
            var mergeCameraDescriptor = new CameraDescriptor
            {
                Id = Guid.NewGuid(),
                SerialNo = "SN-M",
                DisplayName = "上相机"
            };
            mergeProvider.Add(mergeCameraDescriptor, new FakeCameraDevice(mergeCameraDescriptor));
            var mergeCamera = new VisionMaster.Binding.CameraSystemVariableCatalog(mergeProvider);
            mergeHost.Register(mergeCamera);
            mergeCamera.Refresh();

            var mergeCard = new MotionDescriptor { Id = Guid.NewGuid(), DisplayName = "卡A" };
            mergeCard.Axes.Add(new AxisMapping
            {
                AxisId = Guid.NewGuid(),
                LogicalName = "X",
                PhysicalIndex = 0
            });
            var mergeRegistry = new MotionAxisRegistry(() => new[] { mergeCard });
            mergeMotion.Rebuild(mergeRegistry.Snapshot());

            Check("[AX8b] 宿主把运动/通讯/相机三源合并进同一个集合（父与字段都在）",
                mergeWorkspace.SystemVariables.Count == 13 + 8 + 9
                && mergeWorkspace.SystemVariables.Any(v => v.Name == "System.Motion.X")
                && mergeWorkspace.SystemVariables.Any(v => v.Name == "System.Motion.X.Position")
                && mergeWorkspace.SystemVariables.Any(v => v.Name == "System.Comm.PLC_Modbus")
                && mergeWorkspace.SystemVariables.Any(v => v.Name == "System.Camera.上相机"),
                $"{mergeWorkspace.SystemVariables.Count} 条");

            // ================= 变量管理的根行筛选 =================
            // 这段错了不报错，只会让字段以"根行"再出现一次（清单翻倍、计数把一台设备算成 13）。
            var uiFields = new IVariable[]
            {
                new SystemVariableModel(Guid.NewGuid(), "System.Motion.X.Position", typeof(double), "位置"),
                new SystemVariableModel(Guid.NewGuid(), "System.Motion.X.Velocity", typeof(double), "速度"),
            };
            var uiParent = new SystemVariableModel(
                Guid.NewGuid(), "System.Motion.X", typeof(MotionAxis), "轴 X", uiFields);
            var uiLeaf = new SystemVariableModel(
                Guid.NewGuid(), "System.Camera.Cam0.Online", typeof(bool), "相机在线");

            var uiRoots = GlobalVariableManagerViewModel.SelectRootSystemVariables(
                new IVariable[] { uiParent, uiFields[0], uiFields[1], uiLeaf });

            Check("[AX8b] 变量管理的根行只含父变量与独立叶子（字段不重复成根行）",
                uiRoots.Count == 2
                && ReferenceEquals(uiRoots[0], uiParent)
                && ReferenceEquals(uiRoots[1], uiLeaf),
                string.Join("、", uiRoots.Select(r => r.Name)));

            // 字段行映射：显示短名、复制完整名、只读、OriginalModel 指向字段本身
            var parentNode = new VariableNode { Name = "System.Motion.X", Level = 0, SourceLabel = "系统" };
            var fieldNodes = GlobalVariableManagerViewModel.BuildSystemFieldNodes(uiParent, parentNode);

            Check("[AX8b] 字段行：短名显示 + 完整名可复制 + 只读（OriginalModel 指向字段）",
                fieldNodes.Count == 2
                && fieldNodes[0].Name == "Position"
                && fieldNodes[0].FullName == "System.Motion.X.Position"
                && fieldNodes[0].Level == 1
                && fieldNodes[0].IsValueReadOnly
                && ReferenceEquals(fieldNodes[0].OriginalModel, uiFields[0]),
                $"{fieldNodes.Count} 行 / {fieldNodes.FirstOrDefault()?.Name} / {fieldNodes.FirstOrDefault()?.FullName}");
        }

        // ==================================================================
        //  [AX-9] 面板自带命令按钮：出口、权限、无出口时的诚实提示
        // ==================================================================
        private static void AxisCommandChecks()
        {
            Guid x = Guid.NewGuid();
            var element = ElementRegistry.CreateElement("Hmi.AxisPanel");
            element.SetProperty(ScadaAxisOptionsCodec.Key,
                ScadaAxisOptionsCodec.Format(new[] { new ScadaAxisRef(x, "X") }));

            var panel = (AxisPanelElement)ElementRegistry.CreateControl(element);
            panel.Refresh();

            var source = new FakeAxisSource();
            source.Add(x, "X", "卡A", enabled: true, available: true,
                Sample(0, 0, enabled: true, alarm: false, moving: false));

            var commands = new FakeAxisCommandSink();
            var doc = new ScadaDocument();
            var engine = new ScadaAlarmEngine(doc, new FakeValueSource());
            var beat = new ScadaBeatSource(Dispatcher.CurrentDispatcher, TimeSpan.FromMilliseconds(20));

            // ① 没有命令出口：按钮不可用，但提示要诚实
            panel.RuntimeContext = new ScadaRuntimeContext(
                engine, beat, axisSource: source, axisCommands: null,
                accessPolicy: AxisAdminPolicy());

            Check("[AX9] 没有命令出口时按钮不可用（不给出一个按不动的按钮）",
                !panel.CanOperate, $"canOperate={panel.CanOperate}");

            panel.TryExecuteCommand(ScadaAxisCommandKind.Enable);
            Check("[AX9] 没有命令出口时按下去给明确原因，不静默空跑",
                panel.CommandMessage.Contains("未接入运动模块"), panel.CommandMessage);

            // ② 接上出口：默认角色=操作员、RequiredRole=null → 放行
            panel.RuntimeContext = new ScadaRuntimeContext(
                engine, beat, axisSource: source, axisCommands: commands,
                accessPolicy: AxisAdminPolicy());

            Check("[AX9] 有出口且有选中轴时按钮可用",
                panel.CanOperate, $"canOperate={panel.CanOperate}");

            bool ok = panel.TryExecuteCommand(ScadaAxisCommandKind.Enable);
            Check("[AX9] 使能命令带着当前轴的 Id 下发到出口",
                ok && commands.LastCommand == ScadaAxisCommandKind.Enable && commands.LastAxisId == x,
                $"{commands.LastCommand} / {commands.LastAxisId}");

            ok = panel.TryExecuteCommand(ScadaAxisCommandKind.JogStart, -5);
            Check("[AX9] 点动命令把带符号速度原样传给出口（负=反向）",
                ok && commands.LastCommand == ScadaAxisCommandKind.JogStart
                && Math.Abs((commands.LastValue ?? 0) + 5) < 1e-9,
                $"{commands.LastCommand} / {commands.LastValue}");

            ok = panel.TryExecuteCommand(ScadaAxisCommandKind.Stop);
            Check("[AX9] 停止命令下发（与动作表走同一个出口）",
                ok && commands.LastCommand == ScadaAxisCommandKind.Stop,
                $"{commands.LastCommand}");

            // ③ 权限：显式 RequiredPermission 收紧到 UserAdministration，当前组是工程师 → 拒绝且不调出口
            int before = commands.CallCount;
            panel.RuntimeContext = new ScadaRuntimeContext(
                engine, beat, axisSource: source, axisCommands: commands,
                accessPolicy: new FakeAccessPolicy { CurrentRole = ScadaRole.Engineer });
            element.RequiredPermission = ScadaPermissionKeys.UserAdministration;
            panel.Refresh();

            Check("[AX9] RequiredPermission 收紧后按钮变灰（工程师组没有用户管理权限）",
                !panel.CanOperate, $"canOperate={panel.CanOperate}");

            ok = panel.TryExecuteCommand(ScadaAxisCommandKind.Stop);
            Check("[AX9] 权限不足时命令不落到出口，并给出中文原因",
                !ok && commands.CallCount == before && panel.CommandMessage.Contains("权限"),
                $"calls={commands.CallCount - before} / msg={panel.CommandMessage}");

            // ③b B3：无 Stop 权限时按钮仍可点（CanAttemptStop），点击弹登录；登录后继续原操作
            var stopPolicy = new FakeAccessPolicy { CurrentRole = ScadaRole.Engineer }; // 工程师组没有 Stop
            var stopPrompts = new List<string>();
            panel.RuntimeContext = new ScadaRuntimeContext(
                engine, beat, axisSource: source, axisCommands: commands,
                accessPolicy: stopPolicy,
                accessLoginPrompt: key =>
                {
                    stopPrompts.Add(key);
                    stopPolicy.Grant(ScadaPermissionKeys.ControlRun, ScadaPermissionKeys.Stop);
                    return true;
                });
            element.RequiredPermission = null;
            panel.Refresh();

            Check("[AX9-b] 无 Stop 权限时按钮仍可点（置灰就弹不出登录）",
                !panel.CanStop && panel.CanAttemptStop,
                $"canStop={panel.CanStop} attempt={panel.CanAttemptStop}");

            int stopBefore = commands.CallCount;
            bool stopOk = panel.TryExecuteCommand(ScadaAxisCommandKind.Stop);
            Check("[AX9-b] 点停止先弹登录，登录后重判并继续下发停止",
                stopOk && commands.CallCount == stopBefore + 1
                && commands.LastCommand == ScadaAxisCommandKind.Stop
                && stopPrompts.Count == 1 && stopPrompts[0] == ScadaPermissionKeys.Stop,
                $"ok={stopOk} calls={commands.CallCount - stopBefore} prompts={stopPrompts.Count} key={stopPrompts.FirstOrDefault()}");

            // ③c 非交互命令（松手补发 JogStop）不弹登录：安全收手不该打扰人
            stopPolicy.CurrentRole = ScadaRole.Engineer; // 重置回无 Stop
            int nonInteractivePrompts = 0;
            panel.RuntimeContext = new ScadaRuntimeContext(
                engine, beat, axisSource: source, axisCommands: commands,
                accessPolicy: stopPolicy,
                accessLoginPrompt: _ => { nonInteractivePrompts++; return true; });
            panel.Refresh();
            int nonInteractiveBefore = commands.CallCount;
            bool nonInteractiveOk = panel.TryExecuteCommand(
                ScadaAxisCommandKind.Stop, null, interactive: false);

            Check("[AX9-c] 非交互命令不弹登录（自动收手不打扰人）",
                !nonInteractiveOk && nonInteractivePrompts == 0
                && commands.CallCount == nonInteractiveBefore,
                $"ok={nonInteractiveOk} prompts={nonInteractivePrompts} calls={commands.CallCount - nonInteractiveBefore}");

            // 恢复显式配置，后续 ShowCommands 用例按管理员组跑
            element.RequiredPermission = null;
            panel.RuntimeContext = new ScadaRuntimeContext(
                engine, beat, axisSource: source, axisCommands: commands,
                accessPolicy: AxisAdminPolicy());
            panel.Refresh();

            // ④ ShowCommands=false：操作区整体收起（纯监视面板）
            element.SetProperty("ShowCommands", "False");
            panel.Refresh();
            Check("[AX9] 关掉「显示操作按钮」后操作区收起（可退化成纯监视面板）",
                panel.CommandsVisibility == Visibility.Collapsed, $"{panel.CommandsVisibility}");

            element.SetProperty("ShowCommands", "True");
            panel.Refresh();
            Check("[AX9] 重新打开后操作区恢复",
                panel.CommandsVisibility == Visibility.Visible, $"{panel.CommandsVisibility}");

            panel.RuntimeContext = null;
        }

        // ==================================================================
        //  [AX-12] 点动模式：连续 / 单步（档位 0~50mm，运动中只锁点动两键）
        // ==================================================================
        private static void AxisJogModeChecks()
        {
            Guid x = Guid.NewGuid();
            var element = ElementRegistry.CreateElement("Hmi.AxisPanel");
            element.SetProperty(ScadaAxisOptionsCodec.Key,
                ScadaAxisOptionsCodec.Format(new[] { new ScadaAxisRef(x, "X") }));

            var panel = (AxisPanelElement)ElementRegistry.CreateControl(element);
            panel.Refresh();

            var source = new FakeAxisSource();
            source.Add(x, "X", "卡A", enabled: true, available: true,
                Sample(128.48, 0, enabled: true, alarm: false, moving: false));

            var commands = new FakeAxisCommandSink();
            var doc = new ScadaDocument();
            var engine = new ScadaAlarmEngine(doc, new FakeValueSource());
            var beat = new ScadaBeatSource(Dispatcher.CurrentDispatcher, TimeSpan.FromMilliseconds(20));
            panel.RuntimeContext = new ScadaRuntimeContext(
                engine, beat, axisSource: source, axisCommands: commands,
                accessPolicy: AxisAdminPolicy());

            int before = commands.CallCount;

            Check("[AX12] 默认是连续点动模式",
                panel.JogMode == ScadaJogMode.Continuous && panel.IsContinuousJog && !panel.IsStepJog,
                $"mode={panel.JogMode} cont={panel.IsContinuousJog} step={panel.IsStepJog}");

            Check("[AX12] 连续模式下档位框只读且不显示数值",
                !panel.IsStepFieldEditable && panel.JogStepText.Length > 0,
                $"editable={panel.IsStepFieldEditable} text={panel.JogStepText}");

            // ① 连续模式：按住发 JogStart(±JogVelocity)，档位不影响速度
            element.SetProperty("JogVelocity", "12.5");
            panel.Refresh();
            before = commands.CallCount;
            bool ok = panel.StartJog(1);
            Check("[AX12] 连续模式点动发 JogStart，速度取「点动速度」",
                ok && commands.LastCommand == ScadaAxisCommandKind.JogStart
                && Math.Abs((commands.LastValue ?? 0) - 12.5) < 1e-9,
                $"{commands.LastCommand} / {commands.LastValue}");

            panel.EndJog();
            Check("[AX12] 松手发 JogStop",
                commands.LastCommand == ScadaAxisCommandKind.JogStop, $"{commands.LastCommand}");
            before = commands.CallCount;

            // ② 单步模式：一次 TryMove(当前位置 ± 档位)，不发 JogStart
            element.SetProperty("JogMode", "Step");
            element.SetProperty("JogStepMm", "3");
            panel.Refresh();
            Check("[AX12] 切到单步模式后档位框可编辑并显示 3.00",
                panel.IsStepJog && panel.IsStepFieldEditable && panel.JogStepText == "3.00",
                $"step={panel.IsStepJog} editable={panel.IsStepFieldEditable} text={panel.JogStepText}");

            ok = panel.StartJog(1);
            Check("[AX12] 单步点动走定位出口，目标是「当前位置 + 档位」",
                ok && Math.Abs((commands.LastMoveTarget ?? 0) - (128.48 + 3)) < 1e-6,
                $"ok={ok} target={commands.LastMoveTarget}");

            Check("[AX12] 单步点动不再额外发 JogStart 或 JogStop（它本身就是一条完整定位）",
                commands.LastCommand != ScadaAxisCommandKind.JogStart
                && commands.LastCommand != ScadaAxisCommandKind.JogStop,
                $"{commands.LastCommand}");

            ok = panel.StartJog(-1);
            Check("[AX12] 反向单步的目标是「当前位置 - 档位」",
                ok && Math.Abs((commands.LastMoveTarget ?? 0) - (128.48 - 3)) < 1e-6,
                $"target={commands.LastMoveTarget}");

            // ③ 档位边界
            element.SetProperty("JogStepMm", "0");
            panel.Refresh();
            before = commands.CallCount;
            ok = panel.StartJog(1);
            Check("[AX12] 档位为 0 时单步点动拒绝并说清原因",
                !ok && commands.CallCount == before && panel.CommandMessage.Contains("步长"),
                $"ok={ok} calls={commands.CallCount - before} msg={panel.CommandMessage}");

            element.SetProperty("JogStepMm", "62.5");
            panel.Refresh();
            before = commands.CallCount;
            ok = panel.StartJog(1);
            Check("[AX12] 档位超出 0~50 时拒绝（不静默按 50 走）",
                !ok && commands.CallCount == before && panel.CommandMessage.Contains("50"),
                $"ok={ok} msg={panel.CommandMessage}");

            element.SetProperty("JogStepMm", "50");
            panel.Refresh();
            before = commands.CallCount;
            ok = panel.StartJog(1);
            Check("[AX12] 档位 50 是合法上界，可以下发",
                ok && Math.Abs((commands.LastMoveTarget ?? 0) - (128.48 + 50)) < 1e-6,
                $"ok={ok} target={commands.LastMoveTarget}");

            // ④ 运动中只锁点动两键
            element.SetProperty("JogStepMm", "1");
            source.Update(x, Sample(129.48, 20, enabled: true, alarm: false, moving: true));
            panel.Refresh();

            // 前提先立住：没进「运动中」状态，下面的锁断言就没有意义（R38：跳过型断言必须能被反证）
            Check("[AX12] 前提：快照标记运动中后控件进入 IsMoving",
                panel.IsMoving,
                $"isMoving={panel.IsMoving} canJog={panel.CanJog}");

            before = commands.CallCount;
            ok = panel.StartJog(1);
            Check("[AX12] 运动指令执行中点动被锁，且不发任何命令",
                !ok && commands.CallCount == before && panel.CommandMessage.Contains("运动"),
                $"refused={!ok} callsDelta={commands.CallCount - before} msg={panel.CommandMessage}");

            Check("[AX12] 运动中 CanJog 关闭但 CanOperate 仍为真（只锁点动，不锁整块操作区）",
                !panel.CanJog && panel.CanOperate,
                $"canJog={panel.CanJog} canOperate={panel.CanOperate}");

            Check("[AX12] 运动中显示橙色提示条",
                panel.MovingBannerVisibility == Visibility.Visible,
                $"{panel.MovingBannerVisibility}");

            Check("[AX12] 运动中 CanJog 关闭但 CanOperate 仍为真（只锁点动，不锁整块操作区）",
                !panel.CanJog && panel.CanOperate,
                $"canJog={panel.CanJog} canOperate={panel.CanOperate}");

            Check("[AX12] 运动中显示橙色提示条",
                panel.MovingBannerVisibility == Visibility.Visible,
                $"{panel.MovingBannerVisibility}");

            // ⑤ 反向：运动中「停止」与「清报警」仍然可发
            before = commands.CallCount;
            ok = panel.TryExecuteCommand(ScadaAxisCommandKind.Stop);
            Check("[AX12] 反向：运动中「停止」仍可发（安全动作不被运动锁挡住）",
                ok && commands.LastCommand == ScadaAxisCommandKind.Stop,
                $"ok={ok} {commands.LastCommand}");

            ok = panel.TryExecuteCommand(ScadaAxisCommandKind.ClearAlarm);
            Check("[AX12] 反向：运动中「清报警」仍可发",
                ok && commands.LastCommand == ScadaAxisCommandKind.ClearAlarm,
                $"ok={ok} {commands.LastCommand}");

            // 反向收尾：把 moving 清掉，同一个点动键必须能发出去。
            // 没有这一条，「被锁」可能只是别的门禁在挡——那就证明不了锁的是「运动」。
            source.Update(x, Sample(129.48, 0, enabled: true, alarm: false, moving: false));
            panel.Refresh();
            before = commands.CallCount;
            ok = panel.StartJog(1);
            Check("[AX12] 反向：不动之后同一个点动键必须能发出去（证明上面的锁是运动锁而不是别的门禁）",
                ok && commands.CallCount > before && panel.CanJog && panel.MovingBannerVisibility == Visibility.Collapsed,
                $"accepted={ok} callsDelta={commands.CallCount - before} canJog={panel.CanJog} banner={panel.MovingBannerVisibility}");

            // ⑥ 档位归一：非法值夹回并量化到 0.1
            Check("[AX12] 档位归一化：负数夹到 0、超限夹到 50、量化到 0.1",
                Math.Abs(AxisPanelElement.NormalizeJogStep(-3)) < 1e-9
                && Math.Abs(AxisPanelElement.NormalizeJogStep(62.5) - 50) < 1e-9
                && Math.Abs(AxisPanelElement.NormalizeJogStep(3.14159) - 3.1) < 1e-9,
                $"{AxisPanelElement.NormalizeJogStep(-3)} / {AxisPanelElement.NormalizeJogStep(62.5)} / {AxisPanelElement.NormalizeJogStep(3.14159)}");

            panel.RuntimeContext = null;
        }

        // ==================================================================
        //  [AX-10] 轴点位表：列表、示教、定位、改速度、权限
        // ==================================================================
        private static void AxisPointTableChecks()
        {
            Guid x = Guid.NewGuid();
            var element = ElementRegistry.CreateElement("Hmi.AxisPointTable");
            element.SetProperty(ScadaAxisOptionsCodec.Key,
                ScadaAxisOptionsCodec.Format(new[] { new ScadaAxisRef(x, "X") }));

            var table = (AxisPanelElement)ElementRegistry.CreateControl(element);

            var theme = new ResourceDictionary
            {
                Source = new Uri("/VM.Scada.Controls;component/Themes/Generic.xaml", UriKind.Relative)
            };
            table.Style = (Style)theme[typeof(AxisPanelElement)];

            bool templated = table.ApplyTemplate();
            table.Measure(new Size(340, 300));
            table.Arrange(new Rect(0, 0, 340, 300));
            table.UpdateLayout();

            bool hasCombo = table.Template?.FindName("AxisCombo", table) is ComboBox;
            Check("[AX10] 点位表模板能实例化并找到轴选择器",
                templated && hasCombo,
                !templated ? "ApplyTemplate=false" : hasCombo ? "AxisCombo 已就位" : "未找到 AxisCombo");

            var source = new FakeAxisSource();
            source.Add(x, "X", "卡A", enabled: true, available: true,
                Sample(12.5, 0, enabled: true, alarm: false, moving: false));

            var points = Enumerable.Range(0, 16)
                .Select(i => new ScadaAxisPointInfo(i, i == 1 ? "取料位" : $"点位{i}", i * 100, 10 + i))
                .ToArray();
            source.SetPoints(x, points);

            var commands = new FakeAxisCommandSink();
            var confirmations = new FakeConfirmationService();
            var doc = new ScadaDocument();
            var engine = new ScadaAlarmEngine(doc, new FakeValueSource());
            var beat = new ScadaBeatSource(Dispatcher.CurrentDispatcher, TimeSpan.FromMilliseconds(20));

            table.RuntimeContext = new ScadaRuntimeContext(
                engine, beat, axisSource: source, axisCommands: commands, confirmations: confirmations,
                accessPolicy: AxisAdminPolicy());

            Check("[AX10] 选中轴后列出全部 16 行点位",
                table.Rows.Count == 16, $"{table.Rows.Count} 行");

            Check("[AX10] 行文本按位置/速度格式化（位置三位、速度两位）",
                table.Rows[1].Caption == "P1 · 取料位"
                && table.Rows[1].PositionText == "100.000"
                && table.Rows[1].SpeedText == "11.00",
                $"{table.Rows[1].Caption} / {table.Rows[1].PositionText} / {table.Rows[1].SpeedText}");

            var header = table.Template?.FindName("PointsHeader", table) as Grid;
            string headerText = header == null
                ? "找不到 PointsHeader"
                : string.Join("|", header.Children.OfType<TextBlock>().Select(t => t.Text));

            Check("[AX10] 点位表有列头：位置(mm) / 速度(mm/s) / 操作",
                headerText.Contains("位置(mm)")
                && headerText.Contains("速度(mm/s)")
                && headerText.Contains("操作"),
                headerText);

            Check("[AX10] 示教/定位默认开启二次确认",
                table.ConfirmCommands, $"confirm={table.ConfirmCommands}");

            // 示教：把**当前轴位置**写进第 0 行（不是行上的位置）
            table.TeachCommand.Execute(table.Rows[0]);
            Check("[AX10] 示教把当前轴位置写进目标点位（不是点位原来的位置）",
                Math.Abs((commands.LastTeachPosition ?? 0) - 12.5) < 1e-9
                && commands.LastPointIndex == 0
                && Math.Abs((commands.LastTeachSpeed ?? 0) - 10) < 1e-9,
                $"pos={commands.LastTeachPosition} / speed={commands.LastTeachSpeed}");

            Check("[AX10] 示教先弹二次确认，确认后才落出口",
                confirmations.Calls == 1
                && confirmations.LastTitle == "示教确认"
                && (confirmations.LastMessage ?? "").Contains("12.500"),
                $"{confirmations.LastTitle} / {confirmations.LastMessage}");

            // 定位：走行上的位置与速度
            table.LocateCommand.Execute(table.Rows[1]);
            Check("[AX10] 定位把行上的位置与速度交给出口",
                Math.Abs((commands.LastMoveTarget ?? 0) - 100) < 1e-9
                && Math.Abs((commands.LastMoveVelocity ?? 0) - 11) < 1e-9,
                $"target={commands.LastMoveTarget} / speed={commands.LastMoveVelocity}");

            Check("[AX10] 定位同样先弹二次确认（位置与速度都写进确认文案）",
                confirmations.Calls == 2
                && confirmations.LastTitle == "定位确认"
                && (confirmations.LastMessage ?? "").Contains("100.000")
                && (confirmations.LastMessage ?? "").Contains("11.00"),
                $"{confirmations.LastTitle} / {confirmations.LastMessage}");

            // ★ 取消二次确认：命令不落出口（防误触）
            int callsBeforeCancel = commands.CallCount;
            int confirmBeforeCancel = confirmations.Calls;
            confirmations.Result = false;
            table.TeachCommand.Execute(table.Rows[0]);

            Check("[AX10] 取消二次确认后命令不落出口（防误触）",
                confirmations.Calls == confirmBeforeCancel + 1
                && commands.CallCount == callsBeforeCancel,
                $"confirm={confirmations.Calls - confirmBeforeCancel} / calls={commands.CallCount - callsBeforeCancel}");

            // ★ 关掉开关：不弹确认、直接执行（权限仍照常检查）
            element.SetProperty("ConfirmCommands", "False");
            table.Refresh();

            int confirmBeforeOff = confirmations.Calls;
            int callsBeforeOff = commands.CallCount;
            table.TeachCommand.Execute(table.Rows[0]);

            Check("[AX10] 关掉「操作二次确认」后直接执行（不弹窗、命令照常落出口）",
                !table.ConfirmCommands
                && confirmations.Calls == confirmBeforeOff
                && commands.CallCount == callsBeforeOff + 1,
                $"confirm={table.ConfirmCommands} / popups={confirmations.Calls - confirmBeforeOff} / calls={commands.CallCount - callsBeforeOff}");

            element.SetProperty("ConfirmCommands", "True");
            table.Refresh();
            confirmations.Result = true;

            // ★ 外部改点位要自动刷新（不需要重开画面/切轴）：
            //   点位是配置、List<MotionPoint> 不是可观察集合，所以靠节拍 + 指纹比对。
            beat.Start();

            var unchangedRow = table.Rows[1];
            PumpDispatcher(TimeSpan.FromMilliseconds(70));
            Check("[AX10] 点位没变化时不重建行（不打断正在输入的速度框）",
                ReferenceEquals(unchangedRow, table.Rows[1]), "");

            // 模拟「运动卡设置 → 点位列表」里改了 P1：位置 100→250、速度 11→33
            source.SetPoints(x, points
                .Select(p => p.Index == 1 ? new ScadaAxisPointInfo(1, "取料位", 250, 33) : p)
                .ToArray());

            PumpDispatcher(TimeSpan.FromMilliseconds(70));

            Check("[AX10] 外部改点位后自动刷新（新位置/速度出现在表里）",
                table.Rows[1].PositionText == "250.000" && table.Rows[1].SpeedText == "33.00",
                $"{table.Rows[1].PositionText} / {table.Rows[1].SpeedText}");

            beat.Stop();

            // 权限：显式收紧到 UserAdministration，当前组是工程师 → 示教/定位不可用且不落出口
            int before = commands.CallCount;
            int confirmBeforeDenied = confirmations.Calls;
            table.RuntimeContext = new ScadaRuntimeContext(
                engine, beat, axisSource: source, axisCommands: commands, confirmations: confirmations,
                accessPolicy: new FakeAccessPolicy { CurrentRole = ScadaRole.Engineer });
            element.RequiredPermission = ScadaPermissionKeys.UserAdministration;
            table.Refresh();

            Check("[AX10] 权限不足时 CanOperate 为假，但命令仍可尝试（B3：点击后弹登录，不把按钮灰死）",
                !table.CanOperate
                && table.CanAttemptOperate
                && table.TeachCommand.CanExecute(table.Rows[0])
                && table.LocateCommand.CanExecute(table.Rows[0]),
                $"canOperate={table.CanOperate} attempt={table.CanAttemptOperate}");

            table.TeachCommand.Execute(table.Rows[0]);
            Check("[AX10] 权限不足且没有登录弹窗时命令不落到出口，且不会先弹确认框（先判权限、再谈确认）",
                commands.CallCount == before
                && confirmations.Calls == confirmBeforeDenied
                && table.CommandMessage.Contains("权限"),
                $"calls={commands.CallCount - before} / popups={confirmations.Calls - confirmBeforeDenied} / msg={table.CommandMessage}");

            // B3：有登录弹窗时，缺权限 → 弹登录 → 登录后继续原操作
            var teachPolicy = new FakeAccessPolicy { CurrentRole = ScadaRole.Engineer };
            int teachPrompts = 0;
            table.RuntimeContext = new ScadaRuntimeContext(
                engine, beat, axisSource: source, axisCommands: commands, confirmations: confirmations,
                accessPolicy: teachPolicy,
                accessLoginPrompt: key =>
                {
                    teachPrompts++;
                    teachPolicy.Grant(ScadaPermissionKeys.UserAdministration);
                    return true;
                });
            element.RequiredPermission = ScadaPermissionKeys.UserAdministration;
            table.Refresh();

            int teachBefore = commands.CallCount;
            int teachConfirmBefore = confirmations.Calls;
            table.TeachCommand.Execute(table.Rows[0]);

            Check("[AX10] 示教缺权限时先弹登录，登录后继续执行并落出口",
                teachPrompts == 1
                && commands.CallCount == teachBefore + 1
                && confirmations.Calls == teachConfirmBefore + 1,
                $"prompts={teachPrompts} calls={commands.CallCount - teachBefore} popups={confirmations.Calls - teachConfirmBefore}");

            // 关掉操作入口：按钮收起、速度框只读
            element.SetProperty("ShowCommands", "False");
            table.Refresh();

            Check("[AX10] 关掉「显示操作入口」后按钮收起、速度框只读（退化成浏览表）",
                table.CommandsVisibility == Visibility.Collapsed && !table.CanEdit,
                $"visibility={table.CommandsVisibility} / canEdit={table.CanEdit}");

            table.RuntimeContext = null;
        }

        // ==================================================================
        //  [AX-11] 合并后的轴控台：一个轴选择器驱动监控 + 运动 + 点位
        // ==================================================================
        private static void AxisConsoleMergeChecks()
        {
            // 三个 TypeKey 是同一个控件类的三种预设
            var consoleElement = ElementRegistry.CreateElement("Hmi.AxisConsole");
            var console = (AxisPanelElement)ElementRegistry.CreateControl(consoleElement);

            var panelElement = ElementRegistry.CreateElement("Hmi.AxisPanel");
            var panel = (AxisPanelElement)ElementRegistry.CreateControl(panelElement);

            var legacyElement = ElementRegistry.CreateElement("Hmi.AxisPointTable");
            var legacy = (AxisPanelElement)ElementRegistry.CreateControl(legacyElement);

            Check("[AX11] 轴控台预设：操作与点位都打开（完整调试台）",
                console.ShowCommands && console.ShowPoints,
                $"commands={console.ShowCommands} / points={console.ShowPoints}");

            Check("[AX11] 轴面板预设：只开操作、不开点位（紧凑面板）",
                panel.ShowCommands && !panel.ShowPoints,
                $"commands={panel.ShowCommands} / points={panel.ShowPoints}");

            Check("[AX11] 旧点位表 TypeKey 仍可用且行为不变（操作+点位都开），但工具箱不再列出",
                legacy.ShowCommands && legacy.ShowPoints
                && ElementRegistry.Find("Hmi.AxisPointTable")?.ShowInToolbox == false,
                $"commands={legacy.ShowCommands} / points={legacy.ShowPoints} / toolbox={ElementRegistry.Find("Hmi.AxisPointTable")?.ShowInToolbox}");

            // 一个 SelectedAxisId 同时驱动监控与点位
            Guid x = Guid.NewGuid(), y = Guid.NewGuid();
            consoleElement.SetProperty(ScadaAxisOptionsCodec.Key, ScadaAxisOptionsCodec.Format(new[]
            {
                new ScadaAxisRef(x, "X"),
                new ScadaAxisRef(y, "Y"),
            }));
            console.Refresh();

            var source = new FakeAxisSource();
            source.Add(x, "X", "卡A", true, true, Sample(11, 0, true, false, false));
            source.Add(y, "Y", "卡A", true, true, Sample(22, 0, true, false, false));
            source.SetPoints(x, new[] { new ScadaAxisPointInfo(0, "X点", 100, 10) });
            source.SetPoints(y, new[] { new ScadaAxisPointInfo(0, "Y点", 200, 20) });

            var commands = new FakeAxisCommandSink();
            var doc = new ScadaDocument();
            var engine = new ScadaAlarmEngine(doc, new FakeValueSource());
            var beat = new ScadaBeatSource(Dispatcher.CurrentDispatcher, TimeSpan.FromMilliseconds(20));

            console.RuntimeContext = new ScadaRuntimeContext(
                engine, beat, axisSource: source, axisCommands: commands,
                accessPolicy: AxisAdminPolicy());

            Check("[AX11] 默认选中第一根轴，监控与点位同时就位",
                console.SelectedAxisId == x
                && console.PositionText == "11.000"
                && console.Rows.Count == 1 && console.Rows[0].Caption == "P0 · X点",
                $"{console.SelectedAxisName} / {console.PositionText} / {console.Rows.FirstOrDefault()?.Caption}");

            // 切轴：监控、点位、命令靶一起换
            console.SelectedAxisId = y;

            Check("[AX11] 切轴后监控与点位一起换成新轴（不存在「看的是 Y、点位还是 X」）",
                console.PositionText == "22.000"
                && console.Rows.Count == 1 && console.Rows[0].Caption == "P0 · Y点",
                $"{console.PositionText} / {console.Rows.FirstOrDefault()?.Caption}");

            // ★ 切轴先停旧轴的点动：按着 X 点动时切到 Y，X 必须收到 JogStop（不能等看门狗）
            console.SelectedAxisId = x;
            bool jogStarted = console.StartJog(+1);
            Check("[AX11] 点动开始落到当前轴（X）",
                jogStarted && commands.LastCommand == ScadaAxisCommandKind.JogStart && commands.LastAxisId == x,
                $"{commands.LastCommand} / {commands.LastAxisId}");

            console.SelectedAxisId = y;

            Check("[AX11] 切轴时先给**旧轴**发 JogStop（新轴不被误停，旧轴不会一直走）",
                commands.LastCommand == ScadaAxisCommandKind.JogStop && commands.LastAxisId == x,
                $"{commands.LastCommand} / {commands.LastAxisId}");

            console.RuntimeContext = null;
        }

        // ==================================================================
        //  [AX-12] 候选轴摘要：按 Id 取注册表最新名，轴改名/删除后立刻回填
        // ==================================================================
        private static void AxisOptionsSummaryChecks()
        {
            var workspace = new WorkspaceContext();
            workspace.GlobalVariables.Clear();

            var solution = new SolutionModel();
            solution.Flows.Clear();
            workspace.SwitchSolution(solution);

            var cardId = Guid.NewGuid();
            var axisId = Guid.NewGuid();

            var card = new MotionDescriptor { Id = cardId, DisplayName = "卡A" };
            card.Axes.Add(new AxisMapping { AxisId = axisId, LogicalName = "X", PhysicalIndex = 0 });
            solution.MotionCards.Add(card);

            var registry = new MotionAxisRegistry(() => solution.MotionCards);

            var editor = new VisionMaster.ViewModels.ScadaEditorViewModel(workspace, null!);
            editor.AddPageCommand.Execute();

            var element = ElementRegistry.CreateElement("Hmi.AxisConsole");
            // 故意写一个过期名字：模拟"选轴时叫旧名X，之后在运动卡设置里改成了 X"。
            element.SetProperty(ScadaAxisOptionsCodec.Key,
                ScadaAxisOptionsCodec.Format(new[] { new ScadaAxisRef(axisId, "旧名X") }));
            editor.SelectedElement = element;

            var panel = new VisionMaster.ViewModels.ScadaPropertyViewModel(
                editor,
                new RecordingNotifier(),
                new FakeVariablePicker(),
                new FakePagePicker(),
                new FakeFlowPicker(),
                new FakeAxisPicker(),
                registry);

            var row = panel.Groups
                .SelectMany(g => g.Rows)
                .OfType<VisionMaster.ViewModels.ScadaAxisListRow>()
                .FirstOrDefault();

            Check("[AX12] 候选轴摘要按 Id 取注册表最新名（不信任落盘时那份旧名）",
                row != null && row.Summary == "X",
                row?.Summary ?? "面板里没有 AxisList 行");

            if (row == null) return;

            int summaryRaised = 0;
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(VisionMaster.ViewModels.ScadaAxisListRow.Summary))
                    summaryRaised++;
            };

            var renamed = registry.Rename("X", "X2");
            Check("[AX12] 轴改名后摘要立刻回填新名（不必重开选轴弹窗）",
                renamed.Success && row.Summary == "X2" && summaryRaised > 0,
                $"{renamed.Message} / summary={row.Summary} / raised={summaryRaised}");

            var removed = registry.Remove("X2");
            Check("[AX12] 轴被删除后摘要标注「已移除」（不静默显示旧名）",
                removed.Success && row.Summary.Contains("已移除"),
                $"{removed.Message} / summary={row.Summary}");
        }

        // ==================================================================
        //  [AX-13] 分发器 → 运动执行器整链：成功 / 未接入 / 静默三档
        // ==================================================================
        private static void MotionDispatcherChainChecks()
        {
            var cardId = Guid.NewGuid();
            var axisId = Guid.NewGuid();

            var card = new MotionDescriptor { Id = cardId, DisplayName = "测试卡" };
            card.Axes.Add(new AxisMapping
            {
                AxisId = axisId,
                LogicalName = "X",
                PhysicalIndex = 0,
                SoftLimitMinMm = -1_000_000,
                SoftLimitMaxMm = 1_000_000,
            });

            var device = new FakeMotionDevice(card)
            {
                State = MotionCardState.Online,
                IsHomed = true,
                Status = new AxisStatus { PhysicalIndex = 0, Enabled = true, Moving = false },
            };

            var provider = new FakeMotionProvider(card, device);
            var registry = new MotionAxisRegistry(() => new[] { card });
            var executor = new MotionActionExecutor(registry, provider, new FakeLogService());

            var logger = new FaultyLogger("__never__");
            var dispatcher = new ScadaActionDispatcher(
                logger, new FakeValueSource(), new FakeNavigator(), motions: executor);

            var moveHook = new ScadaEventHook { Event = ScadaEventType.Pressed };
            var move = new ScadaAction
            {
                Type = ScadaActionType.MotionMoveAbsolute,
                TargetMm = 25,
                VelocityMmPerS = 10,
            };
            move.BindAxis(axisId, "X");
            moveHook.Actions.Add(move);

            dispatcher.Dispatch(moveHook, "测试按钮", "操作员");

            // 2026-10-25 纠偏：事件动作表不再下发轴控（按钮事件没有轴面板那套闸门）。
            // 老 .vms 里配着的运动动作必须"如实拒绝并说明去哪做"，既不执行也不静默消失。
            Check("[AX13] 分发器不再下发事件里的轴控动作（老配置如实拒绝并说明去哪做）",
                device.LastCommand == null
                && logger.Lines.Any(l => l.Contains("不允许从事件动作表下发")),
                string.Join(" | ", logger.Lines));

            // 运动族整体生效：点动与清报警同样被拒，不是只挡绝对定位
            var jogHook = new ScadaEventHook { Event = ScadaEventType.Pressed };
            var jog = new ScadaAction { Type = ScadaActionType.MotionJogStart, TargetMm = 1 };
            jog.BindAxis(axisId, "X");
            jogHook.Actions.Add(jog);

            int before = logger.Lines.Count;
            dispatcher.Dispatch(jogHook, "测试按钮", "操作员");

            Check("[AX13] 运动族整体被拒（点动/清报警与定位同一道闸），且每次都留一行说明",
                device.LastCommand == null && logger.Lines.Count > before,
                $"新增 {logger.Lines.Count - before} 行 / {string.Join(" | ", logger.Lines.Skip(before))}");

            executor.Dispose();
        }

        // ==================================================================
        //  [AX-14] 轴控台离屏渲染：默认尺寸 320×470 / 窄面板 280 宽 / 6 行点位
        // ==================================================================
        private static void AxisConsoleRenderChecks()
        {
            Guid x = Guid.NewGuid(), y = Guid.NewGuid();

            var element = ElementRegistry.CreateElement("Hmi.AxisConsole");
            element.SetProperty(ScadaAxisOptionsCodec.Key, ScadaAxisOptionsCodec.Format(new[]
            {
                new ScadaAxisRef(x, "X"),
                new ScadaAxisRef(y, "Y"),
            }));

            var panel = (AxisPanelElement)ElementRegistry.CreateControl(element);
            var theme = new ResourceDictionary
            {
                Source = new Uri("/VM.Scada.Controls;component/Themes/Generic.xaml", UriKind.Relative)
            };
            panel.Style = (Style)theme[typeof(AxisPanelElement)];
            panel.ApplyTemplate();

            var source = new FakeAxisSource();
            source.Add(x, "X", "卡A", enabled: true, available: true,
                Sample(123.456, 50, enabled: true, alarm: false, moving: false));
            source.Add(y, "Y", "卡A", enabled: true, available: true,
                Sample(-3.5, 0, enabled: false, alarm: true, moving: false));

            var points = Enumerable.Range(0, 16)
                .Select(i => new ScadaAxisPointInfo(i, i == 0 ? "取料位" : $"点位{i}", i * 10, 10 + i))
                .ToArray();
            source.SetPoints(x, points);
            source.SetPoints(y, points);

            var commands = new FakeAxisCommandSink();
            var doc = new ScadaDocument();
            var engine = new ScadaAlarmEngine(doc, new FakeValueSource());
            var beat = new ScadaBeatSource(Dispatcher.CurrentDispatcher, TimeSpan.FromMilliseconds(20));
            panel.RuntimeContext = new ScadaRuntimeContext(
                engine, beat, axisSource: source, axisCommands: commands,
                accessPolicy: AxisAdminPolicy());

            var combo = panel.Template?.FindName("AxisCombo", panel) as ComboBox;
            Check("[AX14] 轴选择器显示当前轴名（不是空框）",
                combo != null
                && combo.SelectedItem is AxisPanelItem selected
                && selected.DisplayName.StartsWith("X", StringComparison.Ordinal),
                combo == null ? "找不到 AxisCombo" : $"selected={combo.SelectedItem?.ToString() ?? "null"}");

            string dir = Path.Combine(AppContext.BaseDirectory, "artifacts");
            Directory.CreateDirectory(dir);

            // 320 = 轴控台默认宽；280 = 轴面板预设宽（点位区在窄面板下最挤的那一档）。
            foreach (double width in new[] { 320d, 280d })
            {
                // 控件从元素描述符带出显式 Width（轴控台默认 320）：不改成目标宽，
                // Arrange 到 280 只会把 320 宽的控件裁掉右边 40px，量到的仍是 320 的布局。
                panel.Width = width;
                panel.Height = 470;

                panel.Measure(new Size(width, double.PositiveInfinity));
                double desiredHeight = panel.DesiredSize.Height;

                panel.Arrange(new Rect(0, 0, width, 470));
                panel.UpdateLayout();

                Check($"[AX14] 轴控台在 {width:0} 宽下内容高度不超过默认 470（否则默认尺寸会裁掉底部）",
                    desiredHeight <= 470.5, $"desired={desiredHeight:F1} / 470");

                var scroll = panel.Template?.FindName("PointsScroll", panel) as ScrollViewer;
                var items = panel.Template?.FindName("PointsList", panel) as ItemsControl;
                var rootGrid = panel.Template?.FindName("RootGrid", panel) as Grid;
                var pointsPanel = panel.Template?.FindName("PointsPanel", panel) as StackPanel;
                Check($"[AX14] 点位表横向不溢出（{width:0} 宽：滚动视口装得下行内容）",
                    scroll != null && items != null && items.ActualWidth <= scroll.ViewportWidth + 0.5,
                    $"root={rootGrid?.ActualWidth:F1} panel={pointsPanel?.ActualWidth:F1} scroll={scroll?.ActualWidth:F1}/viewport={scroll?.ViewportWidth:F1} items={items?.ActualWidth:F1}/desired={items?.DesiredSize.Width:F1}");

                var bitmap = new RenderTargetBitmap((int)width, 470, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(panel);

                var file = Path.Combine(dir, $"scada-axis-console-{width:0}.png");
                using (var stream = File.Create(file))
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    encoder.Save(stream);
                }
            }

            Check("[AX14] 轴控台默认 6 行点位（PointsHeight = 6×22）",
                panel.PointsVisibility == Visibility.Visible
                && Math.Abs(panel.PointsHeight - 132) < 0.01,
                $"visibility={panel.PointsVisibility} / height={panel.PointsHeight}");

            Check("[AX14] 16 行点位全部给到控件（6 行视口由模板滚动，不是只读 6 行）",
                panel.Rows.Count == 16, $"{panel.Rows.Count} 行");

            panel.RuntimeContext = null;

            Console.WriteLine($"        （轴控台渲染证据：{Path.Combine(dir, "scada-axis-console-320.png")} / -280.png）");
        }

        // ==================================================================
        //  [AX-15] 旧轴控件迁移：只在"整页只有轴面板+旧点位表"时自动合并
        // ==================================================================
        private static void LegacyAxisMigrationChecks()
        {
            Guid x = Guid.NewGuid(), y = Guid.NewGuid();

            // ① 安全情形：整页只有一块轴面板 + 一张旧点位表 → 自动合并
            var doc = new ScadaDocument();
            var page = doc.AddPage("轴调试页");

            var panel = ElementRegistry.CreateElement("Hmi.AxisPanel");
            panel.Width = 280;
            panel.Height = 280;
            panel.SetProperty(ScadaAxisOptionsCodec.Key,
                ScadaAxisOptionsCodec.Format(new[] { new ScadaAxisRef(x, "X") }));

            var table = ElementRegistry.CreateElement("Hmi.AxisPointTable");
            table.SetProperty(ScadaAxisOptionsCodec.Key,
                ScadaAxisOptionsCodec.Format(new[]
                {
                    new ScadaAxisRef(x, "X"),
                    new ScadaAxisRef(y, "Y"),
                }));

            page.TryAddElement(panel, out var addPanelError);
            page.TryAddElement(table, out var addTableError);

            int merged = doc.MigrateLegacyAxisElements(out var messages);
            var options = ScadaAxisOptionsCodec.Parse(panel.GetProperty(ScadaAxisOptionsCodec.Key));

            Check("[AX15] 整页只有轴面板+旧点位表时自动合并：旧表删除、面板打开点位、候选轴取并集",
                merged == 1
                && addPanelError.Length == 0 && addTableError.Length == 0
                && page.Elements.Count == 1
                && ReferenceEquals(page.Elements[0], panel)
                && panel.GetProperty("ShowPoints") == "True"
                && panel.GetProperty("ShowCommands") == "True"
                && options.Count == 2 && options[0].AxisId == x && options[1].AxisId == y,
                $"merged={merged} / elements={page.Elements.Count} / options={options.Count} / {string.Join(" | ", messages)}");

            Check("[AX15] 合并后轴面板尺寸补到至少 320×470（点位区 6 行放得下）",
                panel.Width >= 320 && panel.Height >= 470,
                $"{panel.Width:0}×{panel.Height:0}");

            Check("[AX15] 迁移是幂等的：再跑一次没有旧表可合并",
                doc.MigrateLegacyAxisElements(out _) == 0,
                "");

            // ② 有其它图元：不自动改版面，只给提示
            var busyDoc = new ScadaDocument();
            var busyPage = busyDoc.AddPage("复杂页");
            var busyPanel = ElementRegistry.CreateElement("Hmi.AxisPanel");
            var busyTable = ElementRegistry.CreateElement("Hmi.AxisPointTable");
            var busyRect = ElementRegistry.CreateElement("Hmi.Rectangle");
            busyPage.TryAddElement(busyPanel, out _);
            busyPage.TryAddElement(busyTable, out _);
            busyPage.TryAddElement(busyRect, out _);

            int busyMerged = busyDoc.MigrateLegacyAxisElements(out var busyMessages);

            Check("[AX15] 同页还有其它图元时不自动合并（避免改版面），并给出提示",
                busyMerged == 0
                && busyPage.Elements.Count == 3
                && busyMessages.Any(m => m.Contains("其它图元")),
                string.Join(" | ", busyMessages));

            // ③ 多块面板：无法判断并到哪一块，不自动合并
            var multiDoc = new ScadaDocument();
            var multiPage = multiDoc.AddPage("多面板页");
            multiPage.TryAddElement(ElementRegistry.CreateElement("Hmi.AxisPanel"), out _);
            multiPage.TryAddElement(ElementRegistry.CreateElement("Hmi.AxisPanel"), out _);
            multiPage.TryAddElement(ElementRegistry.CreateElement("Hmi.AxisPointTable"), out _);

            int multiMerged = multiDoc.MigrateLegacyAxisElements(out var multiMessages);

            Check("[AX15] 同页有多块轴面板时不自动合并（不猜并到哪一块）",
                multiMerged == 0
                && multiPage.Elements.Count == 3
                && multiMessages.Any(m => m.Contains("未自动合并")),
                string.Join(" | ", multiMessages));
        }

        // ==================================================================
        //  测试替身
        // ==================================================================

        /// <summary>
        /// 手动泵一段 Dispatcher（让节拍源真的跳几拍）。
        /// 断言宿主没有消息循环，`DispatcherTimer` 不泵就永远不会触发——
        /// 与渲染探针里的 `PushFrame` 同一手法。
        /// </summary>
        private static void PumpDispatcher(TimeSpan duration)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(
                duration,
                DispatcherPriority.Background,
                (_, _) => frame.Continue = false,
                Dispatcher.CurrentDispatcher);
            timer.Start();
            Dispatcher.PushFrame(frame);
        }

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

        private sealed class FakeConfirmationService : IScadaConfirmationService
        {
            public int Calls { get; private set; }

            /// <summary>预置结果：true = 用户确认，false = 取消</summary>
            public bool Result { get; set; } = true;

            /// <summary>确认框打开期间要执行的动作（用来模拟模态期间数据泵回读改值）</summary>
            public Action? OnConfirm { get; set; }

            /// <summary>true = 确认时压一个 DispatcherFrame，让 Background 队列（数据泵）有机会跑</summary>
            public bool PumpDispatcher { get; set; }

            public string? LastTitle { get; private set; }

            public string? LastMessage { get; private set; }

            public bool Confirm(string title, string message)
            {
                Calls++;
                LastTitle = title;
                LastMessage = message;

                if (PumpDispatcher)
                {
                    var frame = new DispatcherFrame();
                    Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                    {
                        OnConfirm?.Invoke();
                        frame.Continue = false;
                    }));
                    Dispatcher.PushFrame(frame);
                }
                else
                {
                    OnConfirm?.Invoke();
                }

                return Result;
            }
        }

        private sealed class FakeAxisCommandSink : IScadaAxisCommandSink
        {
            public int CallCount { get; private set; }

            public Guid LastAxisId { get; private set; }

            public ScadaAxisCommandKind LastCommand { get; private set; }

            public double? LastValue { get; private set; }

            /// <summary>预置结果：默认成功；断言里可改成 false 验失败路径</summary>
            public bool Result { get; set; } = true;

            public string? Error { get; set; }

            public bool TryExecute(Guid axisId, ScadaAxisCommandKind command, double? value, out string? error)
            {
                CallCount++;
                LastAxisId = axisId;
                LastCommand = command;
                LastValue = value;
                error = Result ? null : (Error ?? "预置失败");
                return Result;
            }

            public bool TryMove(Guid axisId, double targetMm, double velocityMmPerS, out string? error)
            {
                CallCount++;
                LastAxisId = axisId;
                LastCommand = ScadaAxisCommandKind.Stop; // 复用记录位；下面两个字段单独记
                LastMoveTarget = targetMm;
                LastMoveVelocity = velocityMmPerS;
                error = Result ? null : (Error ?? "预置失败");
                return Result;
            }

            public bool TryTeach(Guid axisId, int pointIndex, double positionMm, double speedMmPerS, out string? error)
            {
                CallCount++;
                LastAxisId = axisId;
                LastPointIndex = pointIndex;
                LastTeachPosition = positionMm;
                LastTeachSpeed = speedMmPerS;
                error = Result ? null : (Error ?? "预置失败");
                return Result;
            }

            public bool TrySetPointSpeed(Guid axisId, int pointIndex, double speedMmPerS, out string? error)
            {
                CallCount++;
                LastAxisId = axisId;
                LastPointIndex = pointIndex;
                LastTeachSpeed = speedMmPerS;
                error = Result ? null : (Error ?? "预置失败");
                return Result;
            }

            public double? LastMoveTarget { get; private set; }
            public double? LastMoveVelocity { get; private set; }
            public int? LastPointIndex { get; private set; }
            public double? LastTeachPosition { get; private set; }
            public double? LastTeachSpeed { get; private set; }
        }

        private sealed class FakeAxisSource : IScadaAxisSource
        {
            private readonly Dictionary<Guid, ScadaAxisInfo> _infos = new();
            private readonly Dictionary<Guid, ScadaAxisSample> _samples = new();
            private readonly Dictionary<Guid, IReadOnlyList<ScadaAxisPointInfo>> _points = new();

            public int Version { get; private set; }

            public event EventHandler? Changed;

            public void Add(Guid id, string name, string card, bool enabled, bool available, ScadaAxisSample sample)
            {
                _infos[id] = new ScadaAxisInfo(id, name, card, enabled, available);
                _samples[id] = sample;
                Version++;
                Changed?.Invoke(this, EventArgs.Empty);
            }

            /// <summary>预置点位表（点位表的断言用）</summary>
            public void SetPoints(Guid id, IReadOnlyList<ScadaAxisPointInfo> points)
                => _points[id] = points;

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
                _points.Remove(id);
                Version++;
                Changed?.Invoke(this, EventArgs.Empty);
            }

            public bool TryGetInfo(Guid axisId, out ScadaAxisInfo info)
                => _infos.TryGetValue(axisId, out info!);

            public bool TryGetSample(Guid axisId, out ScadaAxisSample sample)
                => _samples.TryGetValue(axisId, out sample);

            public bool TryGetPoints(Guid axisId, out IReadOnlyList<ScadaAxisPointInfo> points)
            {
                if (_points.TryGetValue(axisId, out points!))
                    return true;

                // 轴在、但没预置点位：与真实实现一致，返回空表而不是 false。
                points = Array.Empty<ScadaAxisPointInfo>();
                return _infos.ContainsKey(axisId);
            }
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
