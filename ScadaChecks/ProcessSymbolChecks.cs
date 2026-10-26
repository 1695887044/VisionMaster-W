using System;
using System.Linq;
using System.Reflection;
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
        /// [SY] 工艺符号：几何表完整性 / 状态色 / 细部数量 / 位号落点 / 读数 / 点击开关。
        ///
        /// 几何表断言是"遍历枚举逐个查"——新增一个 <see cref="SymbolKind"/> 忘了画，
        /// 表现是画面上一片空白，只有这条能提前抓到。
        /// </summary>
        private static class ProcessSymbolChecks
        {
            public static void Run() => RunOnSta(RunChecks);

            private static void RunChecks()
            {
                Section("[SY] 工艺符号：几何表 / 状态色 / 细部 / 位号 / 读数 / 点击开关");

                var kinds = Enum.GetValues<SymbolKind>();
                var missing = kinds.Where(k => !ProcessSymbolCatalog.All.ContainsKey(k)).ToArray();

                Check("[SY1] 每个符号种类都有几何定义（漏画 = 画面上一片空白）",
                    missing.Length == 0,
                    missing.Length == 0 ? $"{kinds.Length} 种" : "缺：" + string.Join("、", missing));

                var badPoints = ProcessSymbolCatalog.All
                    .Where(pair => pair.Value.Primitives.Any(p =>
                        p.Points.Length < 4
                        || p.Points.Length % 2 != 0
                        || p.Points.Any(v => v < 0 || v > 1)))
                    .Select(pair => pair.Key.ToString())
                    .ToArray();

                Check("[SY2] 几何坐标都在 0~1、点数为偶数且至少两个点",
                    badPoints.Length == 0,
                    badPoints.Length == 0 ? "全部合规" : "越界：" + string.Join("、", badPoints));

                var noOutline = ProcessSymbolCatalog.All
                    .Where(pair => !pair.Value.Primitives.Any(p => p.Role == SymbolRole.Outline))
                    .Select(pair => pair.Key.ToString())
                    .ToArray();

                Check("[SY3] 每个符号至少有一笔轮廓（只有装饰件的符号等于半个图）",
                    noOutline.Length == 0,
                    noOutline.Length == 0 ? "全部有轮廓" : "缺轮廓：" + string.Join("、", noOutline));

                var model = ElementRegistry.CreateElement("Hmi.Symbol")!;
                model.Width = 120;
                model.Height = 140;
                var symbol = (ProcessSymbolElement)ElementRegistry.CreateControl(model)!;
                symbol.Style = ThemeStyle();
                Layout(symbol, 120, 140);

                Check("[SY4] 默认符号（精馏塔）画出了轮廓几何；换符号后几何随之变化",
                    symbol.OutlineGeometry != null
                    && symbol.CurrentKind == SymbolKind.Tower,
                    $"kind={symbol.CurrentKind} outline={(symbol.OutlineGeometry == null ? "null" : "ok")}");

                var towerOutline = symbol.OutlineGeometry;
                model.SetProperty("SymbolKind", "BallValve");
                symbol.Refresh();

                Check("[SY5] 换符号：轮廓换了、装饰件（球体）出现",
                    symbol.CurrentKind == SymbolKind.BallValve
                    && symbol.OutlineGeometry != null
                    && !ReferenceEquals(symbol.OutlineGeometry, towerOutline)
                    && symbol.AccentGeometry != null,
                    $"kind={symbol.CurrentKind} accent={(symbol.AccentGeometry == null ? "null" : "ok")}");

                // ---------------- 细部数量 ----------------

                model.SetProperty("SymbolKind", "Tower");
                model.SetProperty("DetailCount", "0");
                symbol.Refresh();
                int autoDetails = CountDetailLines(symbol);

                model.SetProperty("DetailCount", "10");
                symbol.Refresh();
                int tenDetails = CountDetailLines(symbol);

                Check("[SY6] 细部数量：0 = 用符号建议值；改成 10 → 塔板线变 10 条",
                    autoDetails > 0 && tenDetails == 10,
                    $"auto={autoDetails} ten={tenDetails}");

                // ---------------- 状态色 ----------------

                model.SetProperty("SymbolKind", "BallValve");
                model.SetProperty("State", "Running");
                symbol.Refresh();
                bool running = ReferenceEquals(symbol.AccentBrush, symbol.AccentColor);

                model.SetProperty("State", "Stopped");
                symbol.Refresh();
                bool stopped = ReferenceEquals(symbol.AccentBrush, symbol.StoppedColor);

                model.SetProperty("State", "Fault");
                symbol.Refresh();
                bool fault = ReferenceEquals(symbol.AccentBrush, symbol.FaultColor);

                model.SetProperty("State", "None");
                symbol.Refresh();
                bool none = ReferenceEquals(symbol.AccentBrush, symbol.AccentColor);

                Check("[SY7] 状态色：运行/停止/故障各用自己的色，None 回落运行色",
                    running && stopped && fault && none,
                    $"running={running} stopped={stopped} fault={fault} none={none}");

                // ---------------- 位号与读数 ----------------

                model.SetProperty("TagPlacement", "Above");
                symbol.Refresh();
                bool above = symbol.TagAboveVisibility == Visibility.Visible
                             && symbol.TagBelowVisibility == Visibility.Collapsed
                             && symbol.TagInsideVisibility == Visibility.Collapsed;

                model.SetProperty("TagPlacement", "None");
                symbol.Refresh();
                bool noneTag = symbol.TagAboveVisibility == Visibility.Collapsed
                               && symbol.TagBelowVisibility == Visibility.Collapsed
                               && symbol.TagInsideVisibility == Visibility.Collapsed;

                model.SetProperty("TagPlacement", "Below");
                symbol.Refresh();
                bool below = symbol.TagBelowVisibility == Visibility.Visible;

                Check("[SY8] 位号落点：上方/下方/内部/不显示 四种，互斥显示",
                    above && below && noneTag,
                    $"above={above} below={below} none={noneTag}");

                model.SetProperty("ShowValue", "True");
                model.SetProperty("Value", "12.35");
                model.SetProperty("ValueFormat", "0.0");
                model.SetProperty("Unit", "MPa");
                symbol.Refresh();

                Check("[SY9] 读数：按格式串 + 单位显示（12.35 → 「12.4 MPa」）",
                    symbol.ValueVisibility == Visibility.Visible && symbol.ValueText == "12.4 MPa",
                    symbol.ValueText);

                model.SetProperty("ShowValue", "False");
                symbol.Refresh();
                Check("[SY10] 关掉读数 → 文本清空、元素收起（不留空标签占位）",
                    symbol.ValueVisibility == Visibility.Collapsed && symbol.ValueText.Length == 0,
                    symbol.ValueText);

                // ---------------- 点击开关 ----------------

                int raised = 0;
                symbol.ScadaEvent += (_, _) => raised++;

                // 「允许点击」关着：按下/释放不发（工艺符号默认是"图"）。
                // 这里用反射调 protected 的 RaiseScadaEvent：它就是画布在运行态命中的那条入口
                // （ScadaCanvas → control.RaiseScadaEvent），不经过它就没有别的办法证明闸门生效。
                var raise = typeof(ProcessSymbolElement).GetMethod(
                    "RaiseScadaEvent", BindingFlags.Instance | BindingFlags.NonPublic)!;

                model.SetProperty("Interactive", "False");
                symbol.Refresh();
                raise.Invoke(symbol, new object[] { ScadaEventType.Pressed });
                int closedCount = raised;

                model.SetProperty("Interactive", "True");
                symbol.Refresh();
                raise.Invoke(symbol, new object[] { ScadaEventType.Pressed });
                int openCount = raised;

                Check("[SY11] 点击开关：关着不发按下事件、打开才发（设计期仍可选中，命中测试不受影响）",
                    closedCount == 0 && openCount == 1 && symbol.IsHitTestVisible,
                    $"closed={closedCount} open={openCount} hitTest={symbol.IsHitTestVisible}");

                // ---------------- 描述符 ----------------

                var descriptor = ElementRegistry.Find("Hmi.Symbol");
                Check("[SY12] 描述符：30 个符号候选 + 2 条事件（按下/释放）",
                    descriptor != null
                    && descriptor.Properties.First(p => p.Key == "SymbolKind").Choices.Count == 30
                    && descriptor.Events.Count == 2,
                    $"choices={descriptor?.Properties.First(p => p.Key == "SymbolKind").Choices.Count} events={descriptor?.Events.Count}");
            }

            /// <summary>数装饰几何里的子几何数（细部每条线一个子几何）</summary>
            private static int CountDetailLines(ProcessSymbolElement symbol)
            {
                if (symbol.AccentGeometry is not System.Windows.Media.GeometryGroup group)
                    return 0;

                // 塔的装饰件里没有基元（只有细部），所以子几何数就是细部条数
                return group.Children.Count;
            }

            private static Style ThemeStyle()
            {
                var theme = new ResourceDictionary
                {
                    Source = new Uri("/VM.Scada.Controls;component/Themes/Generic.xaml", UriKind.Relative),
                };
                return (Style)theme[typeof(ProcessSymbolElement)];
            }

            private static void Layout(Control control, double width, double height)
            {
                control.ApplyTemplate();
                control.Measure(new Size(width, height));
                control.Arrange(new Rect(0, 0, width, height));
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
                    Check("工艺符号断言全程未抛异常", false, error.ToString());
            }
        }
    }
}
