using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VisionMaster.Scada;
using VisionMaster.Scada.Controls;

namespace ScadaChecks
{
    internal partial class Program
    {
        /// <summary>
        /// [XY] XY 曲线图元（<c>Hmi.XYChart</c>）。
        ///
        /// 钉住四件最容易坏、而且坏了不报错的事：
        /// ① 描述符真的注册进了注册表，并且声明了 X / Y 变量与容量（键名是落盘的长期标识，
        ///    写错一个字母 = 属性面板改了不生效）；
        /// ② 设计期（RuntimeContext=null）渲染不抛、也<b>不</b>凭空画点；
        /// ③ 采样去重：变量没变就不该长点（"同一对值连续两拍"是最常见的误判）；
        /// ④ 容量上限：缓冲被截到 MaxPoints，不无限长。
        ///
        /// 绘制本身（坐标轴、网格、折线像素）不做视觉断言——那要有窗口与截图基线；
        /// 这里只保证"画得出来、不抛、点数口径对"，视觉验收归人工评审（R175 的分层）。
        /// </summary>
        internal static class XYChartChecks
        {
            public static void Run() => RunOnSta(RunChecks);

            private static void RunChecks()
            {
                Section("[XY] XY 曲线图元：描述符 / 设计期 / 采样去重 / 容量上限");

                CheckDescriptor();
                CheckDesignPeriod();
                CheckSampling();
                CheckCapacity();
                CheckUnresolvedVariable();
            }

            /// <summary>① 描述符：注册表里能按 TypeKey 找到，且关键属性键都在</summary>
            private static void CheckDescriptor()
            {
                var descriptor = ElementRegistry.Find("Hmi.XYChart");
                var keys = descriptor?.Properties.Select(p => p.Key).ToList() ?? new List<string>();

                Check("[XY1] 描述符存在（Hmi.XYChart / 「XY 曲线」/ 分类「图表」），并声明了 X 变量、Y 变量、最多点数",
                    descriptor != null
                    && descriptor.DisplayName == "XY 曲线"
                    && descriptor.Category == "图表"
                    && descriptor.ControlType == typeof(XYChartElement)
                    && descriptor.DefaultWidth == 420
                    && descriptor.DefaultHeight == 300
                    && keys.Contains("XVariable")
                    && keys.Contains("YVariable")
                    && keys.Contains("MaxPoints")
                    && keys.Contains("AutoScale")
                    && keys.Contains("LineColor"),
                    descriptor == null
                        ? "注册表里没有 Hmi.XYChart（BuiltInElements 尚未接线）"
                        : $"{descriptor.DisplayName}/{descriptor.Category}/{descriptor.DefaultWidth}×{descriptor.DefaultHeight}/{keys.Count} 条属性");
            }

            /// <summary>② 设计期：模板套得上、渲染不抛、缓冲是空的</summary>
            private static void CheckDesignPeriod()
            {
                var element = new XYChartElement();
                ApplyTheme(element);
                Layout(element, 420, 300);

                var error = string.Empty;
                try
                {
                    Render(element, 420, 300);
                }
                catch (Exception ex)
                {
                    error = ex.ToString();
                }

                Check("[XY2] 设计期（RuntimeContext=null）：主题分册能套上、渲染不抛、一个采样点都没有",
                    error.Length == 0
                    && element.Template != null
                    && VisualTreeHelper.GetChildrenCount(element) > 0
                    && element.PointCount == 0,
                    error.Length == 0
                        ? $"template={element.Template != null} children={VisualTreeHelper.GetChildrenCount(element)} points={element.PointCount}"
                        : error);
            }

            /// <summary>③ 采样去重：同一对值不重复追加</summary>
            private static void CheckSampling()
            {
                var source = new FakeScadaValueSource();
                source.Set("XPos", 10d);
                source.Set("YPos", 20d);

                var beat = new ScadaBeatSource(Dispatcher.CurrentDispatcher);
                var context = new ScadaRuntimeContext(
                    new ScadaAlarmEngine(new ScadaDocument(), source), beat, values: source);

                var live = new XYChartElement
                {
                    XVariable = "XPos",
                    YVariable = "YPos",
                    RuntimeContext = context,
                };

                live.Sample();
                var afterFirst = live.PointCount;

                live.Sample();
                var afterRepeat = live.PointCount;

                source.Set("YPos", 25d);
                live.Sample();
                var afterChange = live.PointCount;

                Check("[XY3] 采样：第一拍追加 1 点；同一对值再推不追加；值变了才追加（变量不变就不长点）",
                    afterFirst == 1 && afterRepeat == 1 && afterChange == 2,
                    $"first={afterFirst} repeat={afterRepeat} changed={afterChange}");
            }

            /// <summary>④ 容量上限：点数被截到 MaxPoints</summary>
            private static void CheckCapacity()
            {
                var source = new FakeScadaValueSource();
                source.Set("XPos", 0d);
                source.Set("YPos", 0d);

                var beat = new ScadaBeatSource(Dispatcher.CurrentDispatcher);
                var context = new ScadaRuntimeContext(
                    new ScadaAlarmEngine(new ScadaDocument(), source), beat, values: source);

                var capped = new XYChartElement
                {
                    XVariable = "XPos",
                    YVariable = "YPos",
                    MaxPoints = 5,
                    RuntimeContext = context,
                };

                for (var i = 0; i < 20; i++)
                {
                    source.Set("XPos", (double)i);
                    source.Set("YPos", i * 2d);
                    capped.Sample();
                }

                Check("[XY4] 容量：MaxPoints=5 时连推 20 对不同值，缓冲被截到 5 点（挤掉最旧的）",
                    capped.PointCount == 5, $"points={capped.PointCount}");
            }

            /// <summary>⑤ 变量解析不到：一个点都不追加（缺失不画成 0）</summary>
            private static void CheckUnresolvedVariable()
            {
                var source = new FakeScadaValueSource();
                source.Set("YPos", 20d);

                var beat = new ScadaBeatSource(Dispatcher.CurrentDispatcher);
                var context = new ScadaRuntimeContext(
                    new ScadaAlarmEngine(new ScadaDocument(), source), beat, values: source);

                var broken = new XYChartElement
                {
                    XVariable = "不存在的变量",
                    YVariable = "YPos",
                    RuntimeContext = context,
                };

                broken.Sample();
                broken.Sample();

                Check("[XY5] 变量解析不到：不追加任何点（缺失是缺失，不冒充 0）",
                    broken.PointCount == 0, $"points={broken.PointCount}");
            }

            /// <summary>
            /// 套上本图元自己的主题分册。
            ///
            /// 直接加载 <c>Themes/XYChartElement.xaml</c> 而不是 Generic.xaml：Generic 里那一行由
            /// BuiltInElements 的接线一并加上，本断言在"还没接线"时也要能验证模板本身
            /// （顺带证明那份 XAML 能被解析、里面确实有 XYChartElement 的 Style）。
            /// </summary>
            private static void ApplyTheme(FrameworkElement element)
            {
                var theme = new ResourceDictionary
                {
                    Source = new Uri("/VM.Scada.Controls;component/Themes/XYChartElement.xaml", UriKind.Relative),
                };

                if (theme[element.GetType()] is Style style)
                    element.Style = style;
            }

            private static void Layout(FrameworkElement element, double width, double height)
            {
                element.ApplyTemplate();
                element.Measure(new Size(width, height));
                element.Arrange(new Rect(0, 0, width, height));
                element.UpdateLayout();
            }

            private static void Render(FrameworkElement element, int width, int height)
            {
                var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(element);
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
                    Check("[XY] XY 曲线断言全程未抛异常", false, error.ToString());
            }
        }
    }
}
