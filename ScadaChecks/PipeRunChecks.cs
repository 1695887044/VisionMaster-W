using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using VisionMaster.Scada;
using VisionMaster.Scada.Controls;

namespace ScadaChecks
{
    internal partial class Program
    {
        /// <summary>
        /// [PR] 管道连线：走线几何 / 外框写回 / 标签 / 端点跟随 / 连线约束。
        ///
        /// 走线断言直接测 <see cref="ScadaPipeRouter"/>（纯几何），控件层只测
        /// "端点解析 + 外框 + 标签 + 跟随"，避免把同一套算术测两遍。
        /// </summary>
        private static class PipeRunChecks
        {
            public static void Run() => RunOnSta(RunChecks);

            private static void RunChecks()
            {
                Section("[PR] 管道连线：走线 / 外框 / 标签 / 跟随 / 连线约束");

                // ---------------- 走线 ----------------

                var left = new ScadaPipeRect(100, 100, 100, 80);   // right 口 = (200,140)
                var right = new ScadaPipeRect(400, 200, 100, 80);  // left 口 = (400,240)

                var z = ScadaPipeRouter.Route(left, ScadaPipePort.Right, right, ScadaPipePort.Left,
                    ScadaPipeRouteMode.Orthogonal);

                Check("[PR1] 左右相对：Z 形走线（出线直段 + 中线 + 进线直段），中线取两直段中点",
                    z.Count == 6
                    && Near(z[0], 200, 140) && Near(z[1], 222, 140)
                    && Near(z[2], 300, 140) && Near(z[3], 300, 240)
                    && Near(z[4], 378, 240) && Near(z[5], 400, 240),
                    Describe(z));

                var lShape = ScadaPipeRouter.Route(
                    new ScadaPipeRect(100, 100, 100, 80), ScadaPipePort.Top,
                    new ScadaPipeRect(400, 200, 100, 80), ScadaPipePort.Left,
                    ScadaPipeRouteMode.Orthogonal);

                Check("[PR2] 上出线对左进线：L 形（只拐一次，拐点在对端的 X / 本端的 Y）",
                    lShape.Count == 5 && Near(lShape[2], 378, 78),
                    Describe(lShape));

                var straightish = ScadaPipeRouter.Route(
                    new ScadaPipeRect(100, 100, 100, 80), ScadaPipePort.Right,
                    new ScadaPipeRect(400, 100, 100, 80), ScadaPipePort.Left,
                    ScadaPipeRouteMode.Orthogonal);

                Check("[PR3] 同一水平线上：直连（不硬造两个拐点）",
                    straightish.Count == 4
                    && Near(straightish[0], 200, 140) && Near(straightish[3], 400, 140),
                    Describe(straightish));

                // 下出线 vs 上进线、且两者几乎同高：直段会打架 → 绕到目标设备另一侧
                var loop = ScadaPipeRouter.Route(
                    new ScadaPipeRect(500, 100, 100, 80), ScadaPipePort.Bottom,
                    new ScadaPipeRect(300, 196, 100, 80), ScadaPipePort.Top,
                    ScadaPipeRouteMode.Orthogonal);

                Check("[PR4] 相对而望、间距不够出线：绕到目标设备上侧（不插回设备身体）",
                    loop.Count >= 4
                    && loop.Any(p => p.Y < 196 - ScadaPipeRouter.Stub + 0.5),
                    Describe(loop));

                var straight = ScadaPipeRouter.Route(left, ScadaPipePort.Right, right, ScadaPipePort.Left,
                    ScadaPipeRouteMode.Straight);

                Check("[PR5] 直线模式：只有两端点",
                    straight.Count == 2 && Near(straight[0], 200, 140) && Near(straight[1], 400, 240),
                    Describe(straight));

                // 障碍：一块正好压在 Z 形中线上的图元
                var obstacle = new[] { new ScadaPipeRect(280, 120, 60, 140) };
                var avoided = ScadaPipeRouter.Route(left, ScadaPipePort.Right, right, ScadaPipePort.Left,
                    ScadaPipeRouteMode.Orthogonal, obstacle);

                Check("[PR6] 避让：基础走线穿模时改绕上方/下方，且不再穿过障碍",
                    !Crosses(avoided, obstacle[0])
                    && avoided.Any(p => p.Y < 120 - 0.5 || p.Y > 260 + 0.5),
                    Describe(avoided));

                var clear = ScadaPipeRouter.Route(left, ScadaPipePort.Right, right, ScadaPipePort.Left,
                    ScadaPipeRouteMode.Orthogonal, new[] { new ScadaPipeRect(600, 600, 40, 40) });

                Check("[PR7] 避让不误伤：障碍不在路上时保持基础走线",
                    clear.Count == z.Count, Describe(clear));

                var bounds = ScadaPipeRouter.Bounds(z);
                Check("[PR8] 外框 = 折线包围盒；长度 = 各段之和；按比例取点在折线上",
                    Near(bounds.X, 200) && Near(bounds.Y, 140)
                    && Near(bounds.Width, 200) && Near(bounds.Height, 100)
                    && Near(ScadaPipeRouter.Length(z), 300)
                    && Near(ScadaPipeRouter.PointAt(z, 0.5).Y, 190),
                    $"bounds=({bounds.X},{bounds.Y},{bounds.Width},{bounds.Height}) len={ScadaPipeRouter.Length(z)}");

                // ---------------- 控件：端点解析 / 外框 / 标签 ----------------

                var page = new ScadaPage { Name = "管道页" };
                var tank = ElementRegistry.CreateElement("Hmi.Symbol", 100, 100);
                tank.Width = 100;
                tank.Height = 80;
                tank.Name = "T-101";

                var pump = ElementRegistry.CreateElement("Hmi.Symbol", 400, 200);
                pump.Width = 100;
                pump.Height = 80;
                pump.Name = "P-101";

                page.Elements.Add(tank);
                page.Elements.Add(pump);

                var pipeModel = ElementRegistry.CreateElement("Hmi.PipeRun");
                pipeModel.SetProperty("FromId", tank.ElementId.ToString());
                pipeModel.SetProperty("FromPort", "Right");
                pipeModel.SetProperty("ToId", pump.ElementId.ToString());
                pipeModel.SetProperty("ToPort", "Left");
                pipeModel.SetProperty("Text", "PL-1001");
                page.Elements.Add(pipeModel);

                var pipe = (PipeRunElement)ElementRegistry.CreateControl(pipeModel)!;
                pipe.Style = ThemeStyle(typeof(PipeRunElement));
                pipe.PageElementsResolver = () => page.Elements;
                pipe.Refresh();
                Layout(pipe, pipeModel.Width, pipeModel.Height);

                Check("[PR9] 控件：两端解析成功、几何画出、外框写回模型（选中框贴着线）",
                    pipe.IsLinked
                    && pipe.PipeGeometry != null
                    && Near(pipeModel.X, 200) && Near(pipeModel.Y, 140)
                    && Near(pipeModel.Width, 200) && Near(pipeModel.Height, 100)
                    && Near(pipe.PipeLength, 300),
                    $"linked={pipe.IsLinked} box=({pipeModel.X},{pipeModel.Y},{pipeModel.Width},{pipeModel.Height}) len={pipe.PipeLength}");

                Check("[PR10] 标签：默认落在折线中点（本地坐标 = 绝对 - 自身 X/Y），文本就是 Text",
                    pipe.LabelVisibility == Visibility.Visible
                    && Near(pipe.LabelLeft + PipeRunElement.LabelWidth / 2, 100, 1)
                    && Near(pipe.LabelTop + 14, 50, 1)
                    && pipe.Text == "PL-1001",
                    $"left={pipe.LabelLeft} top={pipe.LabelTop} text={pipe.Text}");

                var textDescriptor = ElementRegistry.FindProperty("Hmi.PipeRun", "Text")!;
                bool applied = pipe.TryApplyRuntimeValue(textDescriptor, "PL-2002", null, out var textError);

                Check("[PR11] 标签可绑变量：Text 声明为可绑，运行态写入后文本跟着变",
                    textDescriptor.IsBindable && applied && pipe.Text == "PL-2002",
                    $"bindable={textDescriptor.IsBindable} applied={applied} text={pipe.Text} err={textError}");

                pipeModel.SetProperty("ShowLabel", "False");
                pipe.Refresh();
                Check("[PR12] 关掉「显示标签」→ 标签收起（只画线）",
                    pipe.LabelVisibility == Visibility.Collapsed,
                    pipe.LabelVisibility.ToString());

                // ---------------- 端点跟随 ----------------

                var before = ScadaPipeRouter.Length(pipe.Route);
                var beforeHeight = pipeModel.Height;
                pump.Y = 320;   // 拖动终点设备
                PumpDispatcher(60);

                Check("[PR13] 拖设备 → 管线自动重排（长度/外框跟着变，不用手动刷新）",
                    !Near(ScadaPipeRouter.Length(pipe.Route), before, 1)
                    && !Near(pipeModel.Height, beforeHeight, 1),
                    $"before={before}/{beforeHeight} after={ScadaPipeRouter.Length(pipe.Route)}/{pipeModel.Height}");

                Check("[PR14] 走线端点始终落在两个连接口上（第一条/最后一条的端点就是端口坐标）",
                    Near(pipe.Route[0], 200, 140) && Near(pipe.Route[^1], 400, 360),
                    Describe(pipe.Route));

                // ---------------- 端点丢失 ----------------

                var orphan = ElementRegistry.CreateElement("Hmi.PipeRun");
                orphan.SetProperty("FromId", Guid.NewGuid().ToString());
                orphan.SetProperty("ToId", pump.ElementId.ToString());
                var orphanControl = (PipeRunElement)ElementRegistry.CreateControl(orphan)!;
                orphanControl.Style = ThemeStyle(typeof(PipeRunElement));
                orphanControl.PageElementsResolver = () => page.Elements;
                orphanControl.Refresh();

                Check("[PR15] 端点找不到 → 不画线、标「未连接」（不抛、不画乱指一根）",
                    !orphanControl.IsLinked && orphanControl.PipeGeometry == null,
                    $"linked={orphanControl.IsLinked}");

                // ---------------- 画布连线 ----------------

                var canvas = new ScadaCanvas
                {
                    Page = page,
                    ItemsSource = page.Elements,
                    PageWidth = 800,
                    PageHeight = 600,
                    SnapToGrid = false,
                };
                // 画布自己也要套模板：PART_ElementLayer 在模板里，没有它一个容器都不会造
                canvas.Style = (Style)ThemeStyle(typeof(ScadaCanvas));
                canvas.ApplyTemplate();
                canvas.Measure(new Size(800, 600));
                canvas.Arrange(new Rect(0, 0, 800, 600));
                canvas.UpdateLayout();
                PumpDispatcher(60);

                var connected = canvas.TryConnect(tank.ElementId, "Bottom", pump.ElementId, "Top", out var connectError);
                canvas.UpdateLayout();
                PumpDispatcher(120);

                Check("[PR16] 画布连线：两个空闲口之间生成一根管道，默认属性写进属性袋",
                    connected != null
                    && connectError == null
                    && connected.TypeKey == "Hmi.PipeRun"
                    && connected.GetProperty("FromPort") == "Bottom"
                    && connected.GetProperty("ToPort") == "Top"
                    && page.Elements.Count == 4,
                    connectError ?? connected?.TypeKey);

                var duplicate = canvas.TryConnect(tank.ElementId, "Bottom", pump.ElementId, "Right", out var duplicateError);
                Check("[PR17] 同一个口只接一根：已占用的口再连 → 拒绝并给原因",
                    duplicate == null && duplicateError != null && duplicateError.Contains("已经接了"),
                    duplicateError ?? "(null)");

                var selfLoop = canvas.TryConnect(pump.ElementId, "Right", pump.ElementId, "Left", out var selfError);
                Check("[PR18] 连回自己 → 拒绝",
                    selfLoop == null && selfError != null && selfError.Contains("同一个图元"),
                    selfError ?? "(null)");

                var created = page.Elements.First(e => ReferenceEquals(e, connected));
                var createdControl = canvas.EnumerateControls()
                    .OfType<PipeRunElement>()
                    .FirstOrDefault(c => ReferenceEquals(c.Element, created));

                Check("[PR19] 新连线在画布上渲染出几何，外框由路由器写回（不是默认 200×60）",
                    createdControl != null
                    && createdControl.IsLinked
                    && createdControl.PipeGeometry != null
                    && !Near(created.Width, 200, 0.5),
                    $"control={(createdControl == null ? "null" : "ok")} controls={canvas.EnumerateControls().Count()} box=({created.X},{created.Y},{created.Width},{created.Height})");

                // 连线手势的第一道闸：按在连接口附近才算"开始拉线"（否则会去拖设备）
                var portViewport = canvas.ToViewportPoint(new Point(200, 140));
                bool hitPort = canvas.TryHitPortAt(portViewport, 12, out var hitElement, out var hitPortValue);
                bool missedFar = !canvas.TryHitPortAt(canvas.ToViewportPoint(new Point(320, 500)), 12, out _, out _);

                Check("[PR20] 连接口命中：口上 12px 内命中右侧口、远处不命中（按口才拉线，按设备仍是拖动）",
                    hitPort && ReferenceEquals(hitElement, tank) && hitPortValue == ScadaPipePort.Right && missedFar,
                    $"hit={hitPort} element={hitElement?.Name} port={hitPortValue} missedFar={missedFar}");
            }

            private static bool Near(ScadaPipePoint point, double x, double y)
                => Near(point.X, x) && Near(point.Y, y);

            private static bool Near(double actual, double expected, double tolerance = 0.5)
                => Math.Abs(actual - expected) <= tolerance;

            private static string Describe(IReadOnlyList<ScadaPipePoint> points)
                => string.Join(" → ", points.Select(p => $"{p.X:0.#},{p.Y:0.#}"));

            private static bool Crosses(IReadOnlyList<ScadaPipePoint> points, ScadaPipeRect rect)
            {
                for (var i = 1; i < points.Count; i++)
                {
                    var p1 = points[i - 1];
                    var p2 = points[i];
                    if (Math.Max(p1.X, p2.X) > rect.X && Math.Min(p1.X, p2.X) < rect.Right
                        && Math.Max(p1.Y, p2.Y) > rect.Y && Math.Min(p1.Y, p2.Y) < rect.Bottom)
                    {
                        return true;
                    }
                }

                return false;
            }

            private static Style ThemeStyle(Type controlType)
            {
                var theme = new ResourceDictionary
                {
                    Source = new Uri("/VM.Scada.Controls;component/Themes/Generic.xaml", UriKind.Relative),
                };
                return (Style)theme[controlType];
            }

            private static void Layout(Control control, double width, double height)
            {
                control.ApplyTemplate();
                control.Measure(new Size(Math.Max(1, width), Math.Max(1, height)));
                control.Arrange(new Rect(0, 0, Math.Max(1, width), Math.Max(1, height)));
                control.UpdateLayout();
            }

            private static void RunOnSta(Action action)
            {
                Exception? error = null;
                var thread = new Thread(() =>
                {
                    try { action(); }
                    catch (Exception ex) { error = ex; }
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join();

                if (error != null)
                    Check("管道连线断言全程未抛异常", false, error.ToString());
            }
        }
    }
}
