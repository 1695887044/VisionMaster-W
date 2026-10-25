using System;
using System.Collections.Generic;
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
        /// [SP] SubPage P1a：固定页 + 只读 + 活数据。
        /// 覆盖页面解析、弹窗拒绝、缺页/无权/未选、嵌套一层、绑定刷新与卸载退订阅。
        /// </summary>
        private static class SubPageChecks
        {
            public static void Run() => RunOnSta(RunChecks);

            private static void RunChecks()
            {
                Section("嵌入画面（Hmi.SubPage P1a）");

                var descriptor = ElementRegistry.Find("Hmi.SubPage");
                Check("[SP1] 描述符：Hmi.SubPage 已注册，默认 480×300，TargetPageId 为 Page 型",
                    descriptor != null
                    && descriptor.DefaultWidth == 480
                    && descriptor.DefaultHeight == 300
                    && descriptor.Properties.Any(p => p.Key == "TargetPageId" && p.Kind == ElementPropertyKind.Page),
                    descriptor == null ? "null" : $"{descriptor.DefaultWidth}×{descriptor.DefaultHeight}");

                var document = new ScadaDocument();
                var target = new ScadaPage { Name = "贴装单元 A", Width = 320, Height = 200 };
                var popup = new ScadaPage { Name = "弹窗", IsPopup = true };
                document.Pages.Add(target);
                document.Pages.Add(popup);

                var valueSource = new FakeValueSource();
                var handle = new FakeValueHandle
                {
                    VariableId = Guid.NewGuid(),
                    Name = "Line.Speed",
                    DataType = typeof(string),
                    Value = "12.4",
                };
                valueSource.ById[handle.VariableId] = handle;
                valueSource.ByName[handle.Name] = handle;

                var text = new ScadaElement
                {
                    TypeKey = "Hmi.Text",
                    Name = "速度",
                    Width = 120,
                    Height = 32,
                };
                text.SetProperty("Text", "old");
                text.GetOrAddBinding("Text").Bind(handle.VariableId, handle.Name);
                target.Elements.Add(text);

                var bitHandle = new FakeValueHandle
                {
                    VariableId = Guid.NewGuid(),
                    Name = "Line.Start",
                    DataType = typeof(bool),
                    Value = false,
                };
                valueSource.ById[bitHandle.VariableId] = bitHandle;
                valueSource.ByName[bitHandle.Name] = bitHandle;

                var bitButton = new ScadaElement
                {
                    TypeKey = "Hmi.BitButton",
                    Name = "启动",
                    Width = 88,
                    Height = 32,
                };
                bitButton.SetProperty("Mode", "置位");
                bitButton.GetOrAddBinding("IsOn").Bind(bitHandle.VariableId, bitHandle.Name);
                target.Elements.Add(bitButton);

                var element = ElementRegistry.CreateElement("Hmi.SubPage")!;
                element.SetProperty("TargetPageId", target.PageId.ToString("D"));
                element.SetProperty("TargetPageName", target.Name);
                element.SetProperty("Fit", "Uniform");

                var writer = new FakeValueWriter();
                var policy = new FakeAccessPolicy();
                var view = (SubPageElement)ElementRegistry.CreateControl(element)!;
                view.PageResolver = id => document.FindPage(id);
                view.ImageAssetResolver = _ => null;
                view.RuntimeContext = SubPageContext(writer, policy, valueSource);
                view.Style = SubPageStyle();
                view.Refresh();
                Layout(view, 480, 300);
                view.StartEmbeddedBindingNow();
                PumpDispatcher(60);

                Check("[SP2] 固定页解析成功：显示内嵌画布，状态条带目标页名",
                    view.ShowCanvas
                    && !view.ShowPlaceholder
                    && view.StatusText != null
                    && view.StatusText.Contains("贴装单元 A"),
                    $"canvas={view.ShowCanvas} / placeholder={view.ShowPlaceholder} / status={view.StatusText}");

                Check("[SP3] 内嵌画布只读，且禁止再嵌一层",
                    view.EmbeddedCanvas != null
                    && view.EmbeddedCanvas.IsReadOnly
                    && !view.EmbeddedCanvas.AllowSubPages,
                    $"readonly={view.EmbeddedCanvas?.IsReadOnly} / allowSub={view.EmbeddedCanvas?.AllowSubPages}");

                var nestedText = view.EmbeddedCanvas?.EnumerateControls().OfType<TextElement>().FirstOrDefault();
                Check("[SP4] 活数据：目标页的 Text 绑定被自有 binder 建表并刷到当前值",
                    view.EmbeddedBoundCount > 0
                    && nestedText != null
                    && nestedText.Text == "12.4",
                    $"bound={view.EmbeddedBoundCount} / text={nestedText?.Text}");

                var nestedButton = view.EmbeddedCanvas?.EnumerateControls().OfType<BitButtonElement>().FirstOrDefault();
                var writesBefore = writer.Writes.Count;
                nestedButton?.BeginPress();
                nestedButton?.ReleasePress();
                Check("[SP4b] P1a 只读不是只挂 IsReadOnly：子页 BitButton 点击不写变量（嵌套上下文 Writer=null）",
                    nestedButton != null && writer.Writes.Count == writesBefore,
                    $"button={(nestedButton == null ? "null" : "found")} / writes={writer.Writes.Count - writesBefore}");

                handle.Raise("13.6");
                PumpDispatcher(60);
                Check("[SP5] 变量变化后内嵌页跟着刷新（不是静态快照）",
                    nestedText != null && nestedText.Text == "13.6",
                    $"text={nestedText?.Text}");

                var subscribersWhileOpen = handle.Subscribers;
                view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                Check("[SP5b] 卸载：内嵌 binder 退订阅，嵌套画布松开目标页的 Layers/Elements",
                    handle.Subscribers == 0
                    && view.EmbeddedCanvas != null
                    && view.EmbeddedCanvas.Page == null
                    && view.EmbeddedCanvas.ItemsSource == null,
                    $"subscribers={handle.Subscribers} (before={subscribersWhileOpen}) / page={view.EmbeddedCanvas?.Page?.Name ?? "null"}");

                view.TargetPageId = string.Empty;
                Check("[SP6] 未选画面：占位“未选择嵌入画面”，并退掉内嵌订阅",
                    view.ShowPlaceholder
                    && view.PlaceholderTitle == "未选择嵌入画面"
                    && handle.Subscribers == 0,
                    $"title={view.PlaceholderTitle} / subscribers={handle.Subscribers} (before={subscribersWhileOpen})");

                view.TargetPageId = Guid.NewGuid().ToString("D");
                Check("[SP7] 目标页删除/坏 Id：占位“未找到画面”，不显示空白页",
                    view.ShowPlaceholder && view.PlaceholderTitle == "未找到画面",
                    $"title={view.PlaceholderTitle} / text={view.PlaceholderText}");

                view.TargetPageId = popup.PageId.ToString("D");
                Check("[SP8] 弹窗页不能嵌入：占位“弹窗页不能嵌入”",
                    view.ShowPlaceholder && view.PlaceholderTitle == "弹窗页不能嵌入",
                    $"title={view.PlaceholderTitle}");

                target.RequiredRole = ScadaRole.Engineer;
                policy.CurrentRole = ScadaRole.Operator;
                view.TargetPageId = target.PageId.ToString("D");
                view.Refresh();
                Check("[SP9] 目标页权限不足：占位“无权查看此画面”，不渲染半页",
                    view.ShowPlaceholder && view.PlaceholderTitle == "无权查看此画面",
                    $"title={view.PlaceholderTitle} / text={view.PlaceholderText}");

                policy.CurrentRole = ScadaRole.Engineer;
                target.RequiredRole = null;
                view.Refresh();
                Check("[SP10] 权限恢复后重新显示目标页",
                    view.ShowCanvas && !view.ShowPlaceholder,
                    $"canvas={view.ShowCanvas} / placeholder={view.ShowPlaceholder}");

                view.NestedTooDeep = true;
                Check("[SP11] 嵌套过深：只显示“嵌套子页仅支持一层”，不递归展开",
                    view.ShowPlaceholder && view.PlaceholderTitle == "嵌套子页仅支持一层",
                    $"title={view.PlaceholderTitle}");

                view.NestedTooDeep = false;
                view.Refresh();
                Check("[SP12] 复位嵌套标记后恢复显示", view.ShowCanvas,
                    $"canvas={view.ShowCanvas} / title={view.PlaceholderTitle}");

                view.MissingBehavior = "KeepLast";
                view.TargetPageId = Guid.NewGuid().ToString("D");
                Check("[SP13] KeepLast：目标页消失时保留上一次画面并停 binder（冻结，不继续刷已删页的变量）",
                    view.ShowCanvas
                    && view.StatusText != null
                    && view.StatusText.Contains("保留上一次画面")
                    && handle.Subscribers == 0,
                    $"canvas={view.ShowCanvas} / status={view.StatusText} / subscribers={handle.Subscribers}");

                // P1b：FollowVariable + 可交互 + 活跃子页登记
                var host = new RecordingSubPageHost();

                var followElement = ElementRegistry.CreateElement("Hmi.SubPage")!;
                followElement.SetProperty("PageSource", "FollowVariable");
                followElement.SetProperty("PageName", target.Name);
                var follow = (SubPageElement)ElementRegistry.CreateControl(followElement)!;
                follow.PageNameResolver = name => document.FindPageByName(name);
                follow.PageResolver = id => document.FindPage(id);
                follow.RuntimeContext = SubPageContext(writer, policy, valueSource, host);
                follow.Style = SubPageStyle();
                follow.Refresh();
                Layout(follow, 480, 300);
                follow.StartEmbeddedBindingNow();

                Check("[SP14] FollowVariable：按画面名切页，状态条带目标页名",
                    follow.ShowCanvas
                    && follow.StatusText != null
                    && follow.StatusText.Contains(target.Name),
                    $"canvas={follow.ShowCanvas} / status={follow.StatusText}");

                var interactiveElement = ElementRegistry.CreateElement("Hmi.SubPage")!;
                interactiveElement.SetProperty("TargetPageId", target.PageId.ToString("D"));
                interactiveElement.SetProperty("Interactive", "True");
                var interactive = (SubPageElement)ElementRegistry.CreateControl(interactiveElement)!;
                interactive.PageResolver = id => document.FindPage(id);
                interactive.RuntimeContext = SubPageContext(writer, policy, valueSource, host);
                interactive.Style = SubPageStyle();
                interactive.Refresh();
                Layout(interactive, 480, 300);
                interactive.StartEmbeddedBindingNow();
                PumpDispatcher(60);

                var interactiveButton = interactive.EmbeddedCanvas?.EnumerateControls()
                    .OfType<BitButtonElement>().FirstOrDefault();
                var writesBeforeInteractive = writer.Writes.Count;
                interactiveButton?.BeginPress();
                interactiveButton?.ReleasePress();

                Check("[SP15] Interactive=true：子页 BitButton 可写变量，且目标页登记为活跃子页",
                    interactiveButton != null
                    && writer.Writes.Count > writesBeforeInteractive
                    && host.Entered.Contains(target),
                    $"button={(interactiveButton == null ? "null" : "found")} / writes={writer.Writes.Count - writesBeforeInteractive} / entered={host.Entered.Count}");

                interactive.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                Check("[SP16] 可交互子页卸载：从活跃子页登记中退出",
                    host.Exited.Contains(target),
                    $"exited={host.Exited.Count}");
            }

            private sealed class RecordingSubPageHost : IScadaSubPageHost
            {
                public List<ScadaPage> Entered { get; } = new();
                public List<ScadaPage> Exited { get; } = new();

                public void Enter(ScadaPage page)
                {
                    if (!Entered.Contains(page))
                        Entered.Add(page);
                }

                public void Exit(ScadaPage page) => Exited.Add(page);
            }

            private static ScadaRuntimeContext SubPageContext(
                FakeValueWriter writer, FakeAccessPolicy policy, FakeValueSource values,
                IScadaSubPageHost? host = null)
            {
                var alarms = new ScadaAlarmEngine(new ScadaDocument(), values, () => DateTime.UtcNow);
                var beat = new ScadaBeatSource(Dispatcher.CurrentDispatcher, TimeSpan.FromMilliseconds(20));
                return new ScadaRuntimeContext(alarms, beat, writer, policy, values: values, subPages: host);
            }

            private static Style SubPageStyle()
            {
                var theme = new ResourceDictionary
                {
                    Source = new Uri("/VM.Scada.Controls;component/Themes/Generic.xaml", UriKind.Relative),
                };

                return (Style)theme[typeof(SubPageElement)];
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
                    try
                    {
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
                    Check("SubPage UI 段执行完成（没有未捕获异常）", false, error.ToString());
            }
        }
    }
}
