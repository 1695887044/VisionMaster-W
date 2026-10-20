using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using VisionMaster.Scada;
using VisionMaster.Scada.Controls;
using ShapePath = System.Windows.Shapes.Path;

namespace ScadaChecks
{
    /// <summary>
    /// [TK] 罐体 / KPI 看板：Phase 1 两个新增显示类图元的功能断言。
    ///
    /// 与 [AX] 段同一纪律：控件与 Dispatcher 必须在 STA 线程上跑，所以整段另起一个 STA 线程；
    /// 断言只读数据侧（DP 值 / 几何 / 模板部件 / 运行态绑定结果），不依赖真实窗口。
    ///
    /// 模板部件那两条刻意<b>不依赖 ui agent 的最终 XAML</b>：
    /// · 若 Themes/Generic.xaml 里已经有 TankElement 的样式，就用真样式套模板；
    /// · 还没有时用一份等价测试模板（部件名与契约逐字一致）验证 OnApplyTemplate / SetPartVisible
    ///   这条代码路径——真模板的视觉验收留给 ui agent 的渲染探针。
    /// 这样本批断言既不会因为并行开发而假红，也不会把"代码里根本没接部件"放过去。
    /// </summary>
    internal partial class Program
    {
        /// <summary>
        /// 独立入口：Main 里只加一行 <c>TankKpiChecks.Run();</c>。
        ///
        /// 做成 Program 的嵌套类型而不是顶层类，是为了复用 Program 的私有断言框架
        /// （Section / Check）——它们是本断言宿主唯一的计数口径，另开一套会让"通过/失败"
        /// 汇总分成两本账。嵌套类型可以访问外层私有成员，不需要把 Check 改成 public。
        /// </summary>
        private static class TankKpiChecks
        {
            public static void Run() => RunTankKpiChecks();
        }

        private static void RunTankKpiChecks()
        {
            Section("[TK] 罐体 / KPI 看板：描述符、几何、模板部件与运行态绑定");

            Exception? failure = null;

            var thread = new Thread(() =>
            {
                try { RunTankKpiChecksCore(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            thread.Join();

            if (failure != null)
                Check("罐体/KPI 断言全程未抛异常", false, failure.ToString());
        }

        private static void RunTankKpiChecksCore()
        {
            // ==================================================================
            //  ① 注册表与描述符契约
            // ==================================================================

            var tankDescriptor = ElementRegistry.Find("Hmi.Tank");
            var kpiDescriptor = ElementRegistry.Find("Hmi.KpiBoard");

            Check("[TK1] 注册表能按 Hmi.Tank 找到罐体，默认尺寸/分类/工具箱可见性正确",
                tankDescriptor is not null
                && tankDescriptor.DisplayName == "罐体/料仓"
                && tankDescriptor.ControlType == typeof(TankElement)
                && tankDescriptor.Category == "工艺"
                && Math.Abs(tankDescriptor.DefaultWidth - 160) < 0.001
                && Math.Abs(tankDescriptor.DefaultHeight - 200) < 0.001
                && tankDescriptor.ShowInToolbox,
                tankDescriptor is null
                    ? "未注册"
                    : $"{tankDescriptor.DisplayName} / {tankDescriptor.Category} / " +
                      $"{tankDescriptor.DefaultWidth}×{tankDescriptor.DefaultHeight} / toolbox={tankDescriptor.ShowInToolbox}");

            Check("[TK2] 注册表能按 Hmi.KpiBoard 找到 KPI 看板，默认尺寸/分类/工具箱可见性正确",
                kpiDescriptor is not null
                && kpiDescriptor.DisplayName == "KPI 看板"
                && kpiDescriptor.ControlType == typeof(KpiBoardElement)
                && kpiDescriptor.Category == "数值"
                && Math.Abs(kpiDescriptor.DefaultWidth - 360) < 0.001
                && Math.Abs(kpiDescriptor.DefaultHeight - 180) < 0.001
                && kpiDescriptor.ShowInToolbox,
                kpiDescriptor is null
                    ? "未注册"
                    : $"{kpiDescriptor.DisplayName} / {kpiDescriptor.Category} / " +
                      $"{kpiDescriptor.DefaultWidth}×{kpiDescriptor.DefaultHeight} / toolbox={kpiDescriptor.ShowInToolbox}");

            var levelSpec = ElementRegistry.FindProperty("Hmi.Tank", "Level");
            var capacitySpec = ElementRegistry.FindProperty("Hmi.Tank", "Capacity");
            var trackSpec = ElementRegistry.FindProperty("Hmi.Tank", "TrackColor");
            var tankProps = tankDescriptor?.Properties ?? Array.Empty<ElementPropertyDescriptor>();

            Check("[TK3] 罐体属性契约：Level 可绑且 0~100、Capacity 下限 0、TrackColor 在「外观」组、" +
                  "基类 Fill/Stroke/CornerRadius 由打包助手带出",
                levelSpec is { IsBindable: true, Kind: ElementPropertyKind.Number }
                && Math.Abs(levelSpec.Min - 0) < 0.001 && Math.Abs(levelSpec.Max - 100) < 0.001
                && capacitySpec is { Kind: ElementPropertyKind.Number } && Math.Abs(capacitySpec.Min) < 0.001
                && trackSpec is { Kind: ElementPropertyKind.Color, Group: ScadaPropertyGroups.Appearance }
                && tankProps.Any(p => p.Key == "Fill" && ReferenceEquals(p.TargetProperty, ScadaElementBase.FillProperty))
                && tankProps.Any(p => p.Key == "Stroke" && ReferenceEquals(p.TargetProperty, ScadaElementBase.StrokeProperty))
                && tankProps.Any(p => p.Key == "CornerRadius" && ReferenceEquals(p.TargetProperty, ScadaElementBase.CornerRadiusProperty)),
                $"Level=({levelSpec?.Min}~{levelSpec?.Max}, bind={levelSpec?.IsBindable}) / " +
                $"Capacity.Min={capacitySpec?.Min} / Track={trackSpec?.Group} / 属性 {tankProps.Count} 条");

            var kpiProps = kpiDescriptor?.Properties ?? Array.Empty<ElementPropertyDescriptor>();
            var valueSpecs = new[] { "Value1", "Value2", "Value3", "Value4" }
                .Select(k => kpiProps.FirstOrDefault(p => p.Key == k)).ToArray();
            var labelSpecs = new[] { "Label1", "Label2", "Label3", "Label4" }
                .Select(k => kpiProps.FirstOrDefault(p => p.Key == k)).ToArray();
            var unitSpecs = new[] { "Unit1", "Unit2", "Unit3", "Unit4" }
                .Select(k => kpiProps.FirstOrDefault(p => p.Key == k)).ToArray();
            var columnsSpec = ElementRegistry.FindProperty("Hmi.KpiBoard", "Columns");

            Check("[TK4] KPI 属性契约：四组数值可绑、标签/单位不可绑、Columns 限 2~4",
                valueSpecs.All(p => p is { IsBindable: true })
                && labelSpecs.All(p => p is { IsBindable: false })
                && unitSpecs.All(p => p is { IsBindable: false })
                && columnsSpec is { Kind: ElementPropertyKind.Number }
                && Math.Abs(columnsSpec.Min - 2) < 0.001 && Math.Abs(columnsSpec.Max - 4) < 0.001,
                $"values bind={string.Join(",", valueSpecs.Select(p => p?.IsBindable))} / " +
                $"labels bind={string.Join(",", labelSpecs.Select(p => p?.IsBindable))} / " +
                $"columns={columnsSpec?.Min}~{columnsSpec?.Max}");

            // ==================================================================
            //  ② 罐体：液位比例 / 几何 / 读数 / 坏值收敛
            // ==================================================================

            var tankElement = ElementRegistry.CreateElement("Hmi.Tank");
            var tank = (TankElement)ElementRegistry.CreateControl(tankElement);

            Check("[TK5] 罐体默认值落地：Level=50 → FillRatio=0.5（构造即算第一帧，不依赖布局）",
                Math.Abs(tank.Level - 50) < 0.001 && Math.Abs(tank.FillRatio - 0.5) < 0.001,
                $"Level={tank.Level} / ratio={tank.FillRatio}");

            bool ratiosOk = true;
            var ratioDetail = new List<string>();
            foreach (var (level, expected) in new[] { (0d, 0d), (50d, 0.5d), (100d, 1d) })
            {
                tankElement.SetProperty("Level", level.ToString(System.Globalization.CultureInfo.InvariantCulture));
                tank.Refresh();
                ratioDetail.Add($"{level}→{tank.FillRatio:0.###}");
                ratiosOk &= Math.Abs(tank.FillRatio - expected) < 1e-9;
            }

            Check("[TK6] 液位 0/50/100 → FillRatio 0/0.5/1（比例只由 Level 推导）",
                ratiosOk, string.Join(" / ", ratioDetail));

            // 布局：默认 160×200，扣掉半线宽 + 1px 后的罐体应铺满内缩矩形。
            // 前面的比例循环停在 100，这里必须先回到 50 再量，否则量到的是满罐。
            tankElement.SetProperty("Level", "50");
            tank.Refresh();
            tank.Measure(new Size(160, 200));
            tank.Arrange(new Rect(0, 0, 160, 200));
            tank.UpdateLayout();

            var bodyBounds = tank.TankBodyGeometry?.Bounds ?? Rect.Empty;
            var fillBoundsAt50 = tank.FillGeometry?.Bounds ?? Rect.Empty;

            Check("[TK7] 罐体几何铺满内缩矩形（半线宽 + 1px 呼吸空间），液位 50 的填充从底部起、高度为罐体一半",
                tank.TankBodyGeometry is not null
                && Math.Abs(bodyBounds.Left - 1.5) < 0.01
                && Math.Abs(bodyBounds.Top - 1.5) < 0.01
                && Math.Abs(bodyBounds.Width - 157) < 0.01
                && Math.Abs(bodyBounds.Height - 197) < 0.01
                && tank.FillGeometry is not null
                && Math.Abs(fillBoundsAt50.Bottom - bodyBounds.Bottom) < 0.01
                && Math.Abs(fillBoundsAt50.Height - bodyBounds.Height / 2d) < 0.5,
                $"body={bodyBounds} / fill50={fillBoundsAt50}");

            // 25% 与 75%：底边始终贴罐底，高度按比例走
            tankElement.SetProperty("Level", "25");
            tank.Refresh();
            var fillAt25 = tank.FillGeometry?.Bounds ?? Rect.Empty;

            tankElement.SetProperty("Level", "75");
            tank.Refresh();
            var fillAt75 = tank.FillGeometry?.Bounds ?? Rect.Empty;

            Check("[TK8] 液位 25/75 的填充底边始终贴罐底、高度按比例（液体从底部往上长）",
                Math.Abs(fillAt25.Bottom - bodyBounds.Bottom) < 0.01
                && Math.Abs(fillAt75.Bottom - bodyBounds.Bottom) < 0.01
                && Math.Abs(fillAt25.Height - bodyBounds.Height * 0.25d) < 0.5
                && Math.Abs(fillAt75.Height - bodyBounds.Height * 0.75d) < 0.5,
                $"25%={fillAt25.Height:0.##} / 75%={fillAt75.Height:0.##} / body={bodyBounds.Height:0.##}");

            // ★ 反向断言：NaN 不许抛、不许出现负高度，且收敛到空罐（0）
            bool nanThrew = false;
            string? nanError = null;
            try
            {
                tankElement.SetProperty("Level", "NaN");
                tank.Refresh();
            }
            catch (Exception ex)
            {
                nanThrew = true;
                nanError = ex.Message;
            }

            double nanFillHeight = TkGeometryHeight(tank.FillGeometry);

            Check("[TK9] ★反向：Level=NaN 不抛异常、FillRatio 收敛到 0、填充几何高度非负（坏值不打断渲染）",
                !nanThrew
                && Math.Abs(tank.FillRatio) < 1e-9
                && !double.IsNaN(nanFillHeight)
                && nanFillHeight >= 0d,
                nanThrew
                    ? "抛了：" + nanError
                    : $"ratio={tank.FillRatio} / fillHeight={nanFillHeight:0.##}");

            // 越界：150 → 1、-10 → 0
            tankElement.SetProperty("Level", "150");
            tank.Refresh();
            double overRatio = tank.FillRatio;
            double overHeight = TkGeometryHeight(tank.FillGeometry);

            tankElement.SetProperty("Level", "-10");
            tank.Refresh();
            double underRatio = tank.FillRatio;
            double underHeight = TkGeometryHeight(tank.FillGeometry);

            Check("[TK10] 越界液位收敛：150 → 1（不溢出罐体）、-10 → 0（空罐，不出现负高度）",
                Math.Abs(overRatio - 1d) < 1e-9
                && Math.Abs(overHeight - bodyBounds.Height) < 0.5
                && Math.Abs(underRatio) < 1e-9
                && underHeight >= 0d,
                $"150→ratio {overRatio}/height {overHeight:0.##}；-10→ratio {underRatio}/height {underHeight:0.##}");

            // 容量 / 单位 / 显示开关
            tankElement.SetProperty("Level", "50");
            tankElement.SetProperty("Capacity", "1000");
            tankElement.SetProperty("Unit", "L");
            tankElement.SetProperty("ShowValue", "True");
            tank.Refresh();

            bool valueLanded = tank.ValueText == "500 L"
                               && Math.Abs(tank.Capacity - 1000) < 0.001
                               && tank.Unit == "L"
                               && tank.ShowValue;
            string volumeText = tank.ValueText ?? string.Empty;

            tankElement.SetProperty("Capacity", "0");
            tank.Refresh();
            string percentFallback = tank.ValueText ?? string.Empty;

            tankElement.SetProperty("ShowValue", "False");
            tank.Refresh();

            Check("[TK11] 容量/单位/显示开关落地：50% × 1000 L = 「500 L」；容量为 0 退回百分比；ShowValue 关掉",
                valueLanded
                && percentFallback == "50%"
                && !tank.ShowValue,
                $"volume=\"{volumeText}\" / fallback=\"{percentFallback}\" / show={tank.ShowValue}");

            // ==================================================================
            //  ③ KPI 看板：四组数据落地 + 列数收敛
            // ==================================================================

            var kpiElement = ElementRegistry.CreateElement("Hmi.KpiBoard");
            var kpi = (KpiBoardElement)ElementRegistry.CreateControl(kpiElement);

            kpiElement.SetProperty("Value1", "42.5");
            kpiElement.SetProperty("Value2", "12,840");
            kpiElement.SetProperty("Value3", "98.6%");
            kpiElement.SetProperty("Value4", "0.62");
            kpiElement.SetProperty("Label1", "节拍");
            kpiElement.SetProperty("Label2", "累计");
            kpiElement.SetProperty("Label3", "OEE");
            kpiElement.SetProperty("Label4", "压力");
            kpiElement.SetProperty("Unit1", "瓶/分");
            kpiElement.SetProperty("Unit2", "瓶");
            kpiElement.SetProperty("Unit3", "本班");
            kpiElement.SetProperty("Unit4", "MPa");
            kpiElement.SetProperty("Columns", "3");
            kpi.Refresh();

            Check("[TK12] KPI 四组数值/标签/单位全部落地，Columns=3 原样保留",
                kpi.Value1 == "42.5" && kpi.Value2 == "12,840"
                && kpi.Value3 == "98.6%" && kpi.Value4 == "0.62"
                && kpi.Label1 == "节拍" && kpi.Label2 == "累计"
                && kpi.Label3 == "OEE" && kpi.Label4 == "压力"
                && kpi.Unit1 == "瓶/分" && kpi.Unit2 == "瓶"
                && kpi.Unit3 == "本班" && kpi.Unit4 == "MPa"
                && kpi.Columns == 3,
                $"{kpi.Value1}/{kpi.Value2}/{kpi.Value3}/{kpi.Value4} / columns={kpi.Columns}");

            var columnCases = new List<string>();
            bool columnsOk = true;
            foreach (var (raw, expected) in new[] { (1, 2), (2, 2), (3, 3), (4, 4), (9, 4) })
            {
                kpiElement.SetProperty("Columns", raw.ToString(System.Globalization.CultureInfo.InvariantCulture));
                kpi.Refresh();
                columnCases.Add($"{raw}→{kpi.EffectiveColumns}");
                columnsOk &= kpi.EffectiveColumns == expected;
            }

            Check("[TK13] Columns 越界收敛到 2~4（1→2、9→4），合法值原样透传；原始配置不被改写",
                columnsOk && kpi.Columns == 9,
                string.Join(" / ", columnCases) + $" / 原始={kpi.Columns}");

            // ==================================================================
            //  ④ 模板部件：OnApplyTemplate 后三个命名件齐备，ShowValue 由代码翻显隐
            // ==================================================================

            var themeStyle = TkFindTankStyle();
            if (themeStyle is not null)
                tank.Style = themeStyle;
            else
                tank.Template = TkBuildTankProbeTemplate();

            bool templated = tank.ApplyTemplate();
            tank.Measure(new Size(160, 200));
            tank.Arrange(new Rect(0, 0, 160, 200));
            tank.UpdateLayout();

            var bodyPart = tank.Template?.FindName(TankElement.PartTankBody, tank);
            var fillPart = tank.Template?.FindName(TankElement.PartTankFill, tank);
            var valuePart = tank.Template?.FindName(TankElement.PartValue, tank);

            Check("[TK14] 模板部件齐备：PART_TankBody / PART_TankFill 是 Path，PART_Value 是 TextBlock" +
                  "（" + (themeStyle is null ? "主题样式尚未落地，用等价测试模板验证部件契约" : "真主题样式") + "）",
                templated
                && bodyPart is ShapePath
                && fillPart is ShapePath
                && valuePart is TextBlock,
                $"templated={templated} / body={bodyPart?.GetType().Name ?? "null"} / " +
                $"fill={fillPart?.GetType().Name ?? "null"} / value={valuePart?.GetType().Name ?? "null"}");

            tankElement.SetProperty("ShowValue", "False");
            tank.Refresh();
            tank.UpdateLayout();
            var hidden = (valuePart as FrameworkElement)?.Visibility;

            tankElement.SetProperty("ShowValue", "True");
            tank.Refresh();
            tank.UpdateLayout();
            var shown = (valuePart as FrameworkElement)?.Visibility;

            Check("[TK15] ShowValue=false 时 PART_Value 隐藏、置真后恢复（代码翻显隐，不依赖模板 DataTrigger）",
                hidden == Visibility.Collapsed && shown == Visibility.Visible,
                $"false→{hidden} / true→{shown}");

            // ==================================================================
            //  ⑤ 运行态绑定：ScadaRuntimeBinder 把变量值写进 Level / Value1
            // ==================================================================

            var doc = new ScadaDocument();
            var page = doc.AddPage("罐体KPI运行画面");
            page.Width = 800;
            page.Height = 600;
            var layer = page.Layers.FirstOrDefault() ?? page.AddLayer();

            var levelVar = new FakeValueHandle
            {
                VariableId = Guid.NewGuid(),
                Name = "液位",
                DataType = typeof(double),
                Value = 25d,
            };
            var oeeVar = new FakeValueHandle
            {
                VariableId = Guid.NewGuid(),
                Name = "OEE",
                DataType = typeof(string),
                Value = "98.6%",
            };

            var source = new FakeValueSource();
            source.ById[levelVar.VariableId] = levelVar;
            source.ByName[levelVar.Name] = levelVar;
            source.ById[oeeVar.VariableId] = oeeVar;
            source.ByName[oeeVar.Name] = oeeVar;

            var runTank = ElementRegistry.CreateElement("Hmi.Tank", 40, 40);
            runTank.Name = "运行罐体";
            runTank.ZIndex = 1;
            page.Elements.Add(runTank);
            page.TryAssignLayer(runTank, layer, out _);
            runTank.Bindings.Add(new ScadaBinding
            {
                TargetProperty = "Level",
                VariableId = levelVar.VariableId,
                VariableName = levelVar.Name,
            });

            var runKpi = ElementRegistry.CreateElement("Hmi.KpiBoard", 240, 40);
            runKpi.Name = "运行看板";
            runKpi.ZIndex = 2;
            page.Elements.Add(runKpi);
            page.TryAssignLayer(runKpi, layer, out _);
            runKpi.Bindings.Add(new ScadaBinding
            {
                TargetProperty = "Value1",
                VariableId = oeeVar.VariableId,
                VariableName = oeeVar.Name,
            });

            var canvas = BuildBinderCanvas(page);
            var controls = canvas.EnumerateControls().ToList();

            var tankControl = controls.OfType<TankElement>().FirstOrDefault();
            var kpiControl = controls.OfType<KpiBoardElement>().FirstOrDefault();

            var binder = new ScadaRuntimeBinder(canvas, source, page, (_, _) => { });
            binder.Start();
            binder.FlushNow();

            Check("[TK16] 数据泵建表即把变量当前值写进 Level / Value1（打开画面就看到此刻的真实值）",
                tankControl is not null && kpiControl is not null
                && Math.Abs(tankControl.Level - 25d) < 1e-9
                && Math.Abs(tankControl.FillRatio - 0.25d) < 1e-9
                && kpiControl.Value1 == "98.6%",
                tankControl is null || kpiControl is null
                    ? $"控件未创建（tank={tankControl != null}, kpi={kpiControl != null}）"
                    : $"Level={tankControl.Level} / ratio={tankControl.FillRatio} / Value1={kpiControl.Value1}");

            levelVar.Raise(75d);
            oeeVar.Raise("97.2%");
            binder.FlushNow();

            Check("[TK17] 变量变化后 Level / Value1 跟着变，罐体比例由 Level 重新推导",
                tankControl is not null && kpiControl is not null
                && Math.Abs(tankControl.Level - 75d) < 1e-9
                && Math.Abs(tankControl.FillRatio - 0.75d) < 1e-9
                && kpiControl.Value1 == "97.2%",
                tankControl is null || kpiControl is null
                    ? "控件未创建"
                    : $"Level={tankControl.Level} / ratio={tankControl.FillRatio} / Value1={kpiControl.Value1}");

            binder.Stop();

            Check("[TK18] 停表后控件回到设计值（运行值只活在控件上，不回流模型）",
                tankControl is not null && kpiControl is not null
                && Math.Abs(tankControl.Level - 50d) < 1e-9
                && kpiControl.Value1 == string.Empty,
                tankControl is null || kpiControl is null
                    ? "控件未创建"
                    : $"Level={tankControl.Level} / Value1=\"{kpiControl.Value1}\"");
        }

        /// <summary>
        /// 取 Themes/Generic.xaml 里的 TankElement 样式；ui agent 的样式还没落地时返回 null。
        ///
        /// 不用 Application.Current：ScadaChecks 不是 WPF Application，主题字典必须显式加载
        /// （与 AxisPanelChecks / ViewportRenderChecks 同一做法）。
        /// </summary>
        private static Style? TkFindTankStyle()
        {
            try
            {
                var theme = new ResourceDictionary
                {
                    Source = new Uri("/VM.Scada.Controls;component/Themes/Generic.xaml", UriKind.Relative),
                };

                return theme[typeof(TankElement)] as Style;
            }
            catch (Exception)
            {
                // 主题字典本身坏了不是本段的断言对象（UIThemeSmokeTest 管）；这里退回测试模板，
                // 让"代码里的部件契约"照样能被验证。
                return null;
            }
        }

        /// <summary>
        /// 等价测试模板：部件名与契约逐字一致，用来在真主题样式落地之前验证
        /// OnApplyTemplate / SetPartVisible 这条代码路径。
        /// 只绑几何与文字，不复制任何视觉样式。
        /// </summary>
        private static ControlTemplate TkBuildTankProbeTemplate()
        {
            var template = new ControlTemplate(typeof(TankElement));
            var root = new FrameworkElementFactory(typeof(Grid));

            var body = new FrameworkElementFactory(typeof(ShapePath)) { Name = TankElement.PartTankBody };
            body.SetBinding(ShapePath.DataProperty, TkTemplatedBinding(nameof(TankElement.TankBodyGeometry)));
            body.SetBinding(System.Windows.Shapes.Shape.FillProperty, TkTemplatedBinding(nameof(TankElement.TrackColor)));
            body.SetBinding(System.Windows.Shapes.Shape.StrokeProperty, TkTemplatedBinding(nameof(TankElement.Stroke)));
            root.AppendChild(body);

            var fill = new FrameworkElementFactory(typeof(ShapePath)) { Name = TankElement.PartTankFill };
            fill.SetBinding(ShapePath.DataProperty, TkTemplatedBinding(nameof(TankElement.FillGeometry)));
            fill.SetBinding(System.Windows.Shapes.Shape.FillProperty, TkTemplatedBinding(nameof(TankElement.Fill)));
            root.AppendChild(fill);

            var value = new FrameworkElementFactory(typeof(TextBlock)) { Name = TankElement.PartValue };
            value.SetBinding(TextBlock.TextProperty, TkTemplatedBinding(nameof(TankElement.ValueText)));
            root.AppendChild(value);

            template.VisualTree = root;
            return template;
        }

        private static Binding TkTemplatedBinding(string path)
            => new(path) { RelativeSource = RelativeSource.TemplatedParent };

        /// <summary>
        /// 几何高度（空几何按 0 算）。
        ///
        /// 不能直接读 <c>Bounds.Height</c>：液位 0 时填充是零高度矩形，Bounds 可能是 Rect.Empty，
        /// 而 Rect.Empty 的 Height 语义容易读错；这里统一折成"没有面积 = 0"。
        /// </summary>
        private static double TkGeometryHeight(Geometry? geometry)
        {
            if (geometry is null)
                return double.NaN;

            var bounds = geometry.Bounds;
            return bounds.IsEmpty ? 0d : bounds.Height;
        }
    }
}
