using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Newtonsoft.Json;
using VisionMaster.Models;
using VisionMaster.Scada;
using VisionMaster.Scada.Controls;
using VisionMaster.Scada.Snapshots;
using VisionMaster.Services;
using VisionMaster.ViewModels;
using VisionMaster.Views;

namespace ScadaChecks
{
    internal partial class Program
    {
        /// <summary>
        /// [MB/PU] 多基座 + 弹窗（特殊画面）专项断言。
        ///
        /// 两条主线：
        /// · 多基座：一页可以按顺序引用多只基座；渲染/运行态投影/快照/编辑器多选都按列表走。
        /// · 弹窗：IsPopup 页面 + OpenPopup/ClosePopup，不改 CurrentPage、不压页栈；
        ///   模态弹窗挡住背景页事件，非模态放行；v1 不允许嵌套。
        ///
        /// 数据侧在普通线程跑；画布/运行窗口另开 STA 线程（与 Phase2UiChecks 同一纪律）。
        /// </summary>
        private static class ScadaMultiBasePopupChecks
        {
            public static void Run()
            {
                Section("多基座 + 弹窗（特殊画面）");

                RunModelChecks();
                RunRuntimeChecks();
                RunEditorChecks();

                RunOnSta(() =>
                {
                    RunCanvasChecks();
                    RunWindowChecks();
                });
            }

            // ---------------------------------------------------------------- 模型

            private static (ScadaDocument Doc, ScadaPage A, ScadaPage B, ScadaPage Page) BuildDocument()
            {
                var doc = new ScadaDocument();
                var a = doc.AddPage("基座A");
                var b = doc.AddPage("基座B");
                var page = doc.AddPage("引用页");

                var okA = doc.TryAddBasePage(a, out var errorA);
                var okB = doc.TryAddBasePage(b, out var errorB);

                Check("多基座准备：两只基座都能注册（基座自身不引用基座）",
                    okA && okB,
                    $"{errorA}|{errorB}");

                return (doc, a, b, page);
            }

            private static void RunModelChecks()
            {
                var (doc, a, b, page) = BuildDocument();

                Check("多基座①追加：按调用顺序进 BasePageIds，ResolveBasePages 原样返回两只",
                    doc.TryAddBasePageReference(page, a, out _)
                    && doc.TryAddBasePageReference(page, b, out _)
                    && page.BasePageIds.SequenceEqual(new[] { a.PageId, b.PageId })
                    && doc.ResolveBasePages(page).SequenceEqual(new[] { a, b }),
                    string.Join("/", doc.ResolveBasePages(page).Select(p => p.Name)));

                Check("多基座②顺序：把 B 移到最底 → BasePageIds 与渲染顺序一起变成 B/A",
                    doc.TryMoveBasePageReference(page, b, 0, out _)
                    && page.BasePageIds.SequenceEqual(new[] { b.PageId, a.PageId })
                    && doc.ResolveBasePages(page).SequenceEqual(new[] { b, a }),
                    string.Join("/", page.BasePageIds.Select(id => doc.FindBasePage(id)?.Name)));

                Check("多基座③幂等：重复添加同一只不产生重复项，重复移除也不报错",
                    doc.TryAddBasePageReference(page, a, out _)
                    && page.BasePageIds.Count(id => id == a.PageId) == 1
                    && doc.TryRemoveBasePageReference(page, a, out _)
                    && doc.TryRemoveBasePageReference(page, a, out _)
                    && page.BasePageIds.SequenceEqual(new[] { b.PageId }),
                    string.Join("/", page.BasePageIds));

                Check("多基座④悬空不清理：摘掉基座注册后 Id 留着、Resolve 跳过它，恢复注册后又能解析",
                    doc.TryRemoveBasePage(b, out _)
                    && page.BasePageIds.Contains(b.PageId)
                    && doc.ResolveBasePages(page).Count == 0
                    && doc.TryAddBasePage(b, out _)
                    && doc.ResolveBasePages(page).SequenceEqual(new[] { b }),
                    $"ids={page.BasePageIds.Count} / resolved={doc.ResolveBasePages(page).Count}");

                var addRejected = !doc.TryAddBasePageReference(a, b, out var addError);
                var setRejected = !doc.TrySetBasePage(a, b, out var setError);

                Check("多基座⑤一层限制：基座页不能再引用别的基座（两条入口都拒绝）",
                    addRejected
                    && setRejected
                    && !string.IsNullOrWhiteSpace(addError)
                    && !string.IsNullOrWhiteSpace(setError),
                    $"{addError} / {setError}");

                // 手改过的 .vms：基座页带引用、普通页带自引用与重复 Id → EnsureIdentity 清理结构性脏数据
                a.SetBasePageIds(new[] { b.PageId });
                page.SetBasePageIds(new[] { page.PageId, b.PageId, b.PageId });

                doc.EnsureIdentity();

                Check("多基座⑥加载期归一：基座页的引用被清空、普通页去掉自引用与重复 Id（悬空 Id 保留）",
                    a.BasePageIds.Count == 0
                    && page.BasePageIds.SequenceEqual(new[] { b.PageId }),
                    $"baseA={a.BasePageIds.Count} / page={string.Join("/", page.BasePageIds)}");

                var oldDoc = new ScadaDocument();
                var oldPopup = oldDoc.AddPage("旧弹窗");
                oldPopup.IsPopup = true;
                oldDoc.BasePages.Add(oldPopup); // 模拟手改过的 .vms：弹窗页混进基座集合
                oldDoc.EnsureIdentity();

                Check("多基座⑥b 旧数据里弹窗页误注册为基座：加载期从 BasePages 摘掉",
                    !oldDoc.BasePages.Contains(oldPopup),
                    $"bases={oldDoc.BasePages.Count}");

                // 旧 .vms 的单值字段迁移；新文件只写列表
                var legacy = JsonConvert.DeserializeObject<ScadaPage>(
                    $"{{\"BasePageId\":\"{a.PageId:D}\"}}");

                Check("多基座⑦旧 .vms 兼容：单值 BasePageId 读进 BasePageIds（不用迁移脚本）",
                    legacy != null
                    && legacy.BasePageIds.Count == 1
                    && legacy.BasePageIds[0] == a.PageId
                    && legacy.BasePageId == a.PageId,
                    legacy == null ? "null" : string.Join("/", legacy.BasePageIds));

                var json = JsonConvert.SerializeObject(page);
                var legacyIndex = json.IndexOf("\"BasePageId\"", StringComparison.Ordinal);

                Check("多基座⑧新文件只写 BasePageIds，不再写旧的单值 BasePageId",
                    json.Contains("\"BasePageIds\"", StringComparison.Ordinal)
                    && legacyIndex < 0,
                    legacyIndex < 0
                        ? "ok"
                        : json.Substring(Math.Max(0, legacyIndex - 60), Math.Min(140, json.Length - Math.Max(0, legacyIndex - 60))));

                var normalPage = new ScadaPage();
                var popupPage = new ScadaPage { IsPopup = true };
                var isPopupSpec = ScadaPageProperties.All.Single(p => p.Key == "IsPopup");
                var popupModalSpec = ScadaPageProperties.All.Single(p => p.Key == "PopupModal");
                var popupAnchorSpec = ScadaPageProperties.All.Single(p => p.Key == "PopupAnchor");

                Check("弹窗页面属性①默认值：普通页 IsPopup=false、模态=true、锚点=居中",
                    !normalPage.IsPopup && normalPage.PopupModal && normalPage.PopupAnchor == ScadaPopupAnchor.Center,
                    $"{normalPage.IsPopup}/{normalPage.PopupModal}/{normalPage.PopupAnchor}");

                Check("弹窗页面属性②条件可见：普通页只露「弹窗页」开关，勾上后才露模态/位置/偏移",
                    isPopupSpec.IsVisibleFor(normalPage)
                    && !popupModalSpec.IsVisibleFor(normalPage)
                    && !popupAnchorSpec.IsVisibleFor(normalPage)
                    && popupModalSpec.IsVisibleFor(popupPage)
                    && popupAnchorSpec.IsVisibleFor(popupPage),
                    $"normal={popupModalSpec.IsVisibleFor(normalPage)} / popup={popupModalSpec.IsVisibleFor(popupPage)}");

                var popupFirstDoc = new ScadaDocument();
                var popupFirst = popupFirstDoc.AddPage("弹窗首页");
                popupFirst.IsPopup = true;
                var normalFirst = popupFirstDoc.AddPage("普通首页");

                Check("弹窗页面属性③启动页回落跳过弹窗页：第一页是弹窗时，运行先显示第一张普通页",
                    ReferenceEquals(popupFirstDoc.ResolveStartupPage(), normalFirst),
                    popupFirstDoc.ResolveStartupPage()?.Name ?? "null");
            }

            // ---------------------------------------------------------------- 运行态 + 弹窗

            private static void RunRuntimeChecks()
            {
                var (doc, a, b, home) = BuildDocument();
                doc.TryAddBasePageReference(home, a, out _);
                doc.TryAddBasePageReference(home, b, out _);

                var other = doc.AddPage("另一页");
                doc.TryAddBasePageReference(other, b, out _);
                doc.SetStartupPage(home);

                var runtime = new ScadaRuntime(doc);

                Check("多基座运行态①Start 后 CurrentBasePages 按页面顺序投影两只基座（CurrentBasePage = 第一只）",
                    runtime.Start()
                    && runtime.CurrentBasePages.SequenceEqual(new[] { a, b })
                    && ReferenceEquals(runtime.CurrentBasePage, a),
                    string.Join("/", runtime.CurrentBasePages.Select(p => p.Name)));

                var baseNotices = 0;
                runtime.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(ScadaRuntime.CurrentBasePages))
                        baseNotices++;
                };

                runtime.Navigate(other);

                Check("多基座运行态②切页后 CurrentBasePages 换成新页的列表并逐次通知（画布要跟着重装）",
                    runtime.CurrentBasePages.SequenceEqual(new[] { b })
                    && baseNotices > 0,
                    $"notices={baseNotices} / {string.Join("/", runtime.CurrentBasePages.Select(p => p.Name))}");

                // 回主页把页栈清空：下面弹窗的 CanGoBack 断言要排除"本来就有上一页"。
                runtime.GoBack();
                var beforePopup = runtime.CurrentPage;

                // 弹窗页：普通页切不过去，只能 OpenPopup
                var popup = doc.AddPage("轴参数");
                popup.IsPopup = true;
                popup.PopupModal = true;
                popup.PopupAnchor = ScadaPopupAnchor.TopRight;
                doc.TryAddBasePageReference(popup, a, out _);

                var homeButton = new ScadaElement { TypeKey = "Hmi.Button", Name = "主页按钮" };
                home.TryAddElement(homeButton, out _);
                homeButton.GetOrAddEventHook(ScadaEventType.Pressed).Actions.Add(
                    new ScadaAction { Type = ScadaActionType.Log, Text = "主页点击" });

                var popupButton = new ScadaElement { TypeKey = "Hmi.Button", Name = "弹窗按钮" };
                popup.TryAddElement(popupButton, out _);
                popupButton.GetOrAddEventHook(ScadaEventType.Pressed).Actions.Add(
                    new ScadaAction { Type = ScadaActionType.Log, Text = "弹窗点击" });

                Check("弹窗①弹窗页不是导航目标：Navigate(popup) 拒绝，IsNavigable=false",
                    !doc.IsNavigable(popup) && !runtime.Navigate(popup),
                    $"navigable={doc.IsNavigable(popup)}");

                var navigatePopupOk = runtime.Navigate(popup.PageId, popup.Name, out var navigatePopupReason);

                Check("弹窗①b 按 Id 切到弹窗页也拒绝，原因说清「只能用打开弹窗」",
                    !navigatePopupOk
                    && navigatePopupReason != null
                    && navigatePopupReason.Contains("弹窗页", StringComparison.Ordinal),
                    navigatePopupReason);

                var beforeIdFallback = runtime.CurrentPage;
                other.Name = "轴参数"; // 与弹窗同名，模拟"Id 指向弹窗、名字能匹配普通页"的歧义
                var idFallbackOk = runtime.Navigate(popup.PageId, "轴参数", out var idFallbackReason);
                other.Name = "另一页";

                Check("弹窗①c Id 指向弹窗页时不再按名字兜底到同名普通页（Id 优先且不可导航即拒绝）",
                    !idFallbackOk
                    && ReferenceEquals(runtime.CurrentPage, beforeIdFallback)
                    && idFallbackReason != null
                    && idFallbackReason.Contains("弹窗页", StringComparison.Ordinal),
                    idFallbackReason);

                Check("弹窗②OpenPopup 成功：CurrentPage 不变、页栈不压、CanGoBack 变 true",
                    runtime.OpenPopup(popup, out var openError)
                    && ReferenceEquals(runtime.CurrentPage, beforePopup)
                    && ReferenceEquals(runtime.CurrentPopup, popup)
                    && runtime.IsPopupOpen
                    && runtime.IsPopupModal
                    && runtime.PageStackDepth == 0
                    && runtime.CanGoBack,
                    $"{openError ?? "ok"} / popup={runtime.CurrentPopup?.Name} / depth={runtime.PageStackDepth}");

                Check("弹窗③弹窗画布拿到自己的基座列表（与主页列表互不串）",
                    runtime.CurrentPopupBasePages.SequenceEqual(new[] { a }),
                    string.Join("/", runtime.CurrentPopupBasePages.Select(p => p.Name)));

                Check("弹窗④v1 不嵌套：已有弹窗时再开一只被拒绝，原弹窗保持不变",
                    !runtime.OpenPopup(popup, out var nestedReason)
                    && ReferenceEquals(runtime.CurrentPopup, popup)
                    && !string.IsNullOrWhiteSpace(nestedReason),
                    nestedReason);

                Check("弹窗⑤模态事件闸门：弹窗内按钮命中，背景页按钮被挡；非模态时背景页放行",
                    runtime.RaiseElementEvent(popupButton, ScadaEventType.Pressed) == 1
                    && runtime.RaiseElementEvent(homeButton, ScadaEventType.Pressed) == 0,
                    $"popup={runtime.RaiseElementEvent(popupButton, ScadaEventType.Pressed)} / home={runtime.RaiseElementEvent(homeButton, ScadaEventType.Pressed)}");

                popup.PopupModal = false;

                Check("弹窗⑥非模态：背景页事件重新放行（同一时刻仍只有一个弹窗）",
                    runtime.RaiseElementEvent(homeButton, ScadaEventType.Pressed) == 1
                    && !runtime.IsPopupModal,
                    $"home={runtime.RaiseElementEvent(homeButton, ScadaEventType.Pressed)} / modal={runtime.IsPopupModal}");

                popup.PopupModal = true;

                var snapshot = new ScadaPageSnapshotBuilder().Build(runtime);

                Check("弹窗⑦快照：主页快照带弹窗层与弹窗基座，弹窗页自己的 IsPopup/锚点也进了契约",
                    snapshot != null
                    && snapshot.Popup != null
                    && snapshot.Popup.PageName == popup.Name
                    && snapshot.Popup.IsPopup
                    && snapshot.Popup.PopupAnchor == nameof(ScadaPopupAnchor.TopRight)
                    && snapshot.Popup.BasePages.Count == 1
                    && snapshot.BasePages.Count == 2,
                    $"popup={snapshot?.Popup?.PageName} / bases={snapshot?.BasePages.Count}");

                // 重入闸：Unloaded 钩子里再调 ClosePopup 不许递归（否则栈溢出直接杀进程）
                popup.GetOrAddEventHook(ScadaEventType.Unloaded).Actions.Add(
                    new ScadaAction { Type = ScadaActionType.ClosePopup });
                var unloadedCalls = 0;

                void OnPageEvent(ScadaPage page, ScadaEventHook hook)
                {
                    if (ReferenceEquals(page, popup) && hook.Event == ScadaEventType.Unloaded)
                    {
                        unloadedCalls++;
                        runtime.ClosePopup();
                    }
                }

                runtime.PageEventRaised += OnPageEvent;
                var closed = runtime.ClosePopup();
                runtime.PageEventRaised -= OnPageEvent;

                Check("弹窗⑦b 重入闸：Unloaded 钩子里再关弹窗只广播一次、不递归",
                    closed
                    && unloadedCalls == 1
                    && runtime.CurrentPopup == null,
                    $"closed={closed} / unloaded={unloadedCalls} / popup={runtime.CurrentPopup?.Name}");

                // 后面的 GoBack 断言需要弹窗重新打开
                runtime.OpenPopup(popup, out _);

                Check("弹窗⑧GoBack 先关弹窗：栈本来为空也返回 true，CurrentPage 不动",
                    runtime.GoBack()
                    && runtime.CurrentPopup == null
                    && ReferenceEquals(runtime.CurrentPage, beforePopup)
                    && !runtime.CanGoBack,
                    $"page={runtime.CurrentPage?.Name} / popup={runtime.CurrentPopup?.Name}");

                Check("弹窗⑨没有弹窗时 ClosePopup 返回 false（不抛、不误伤当前页）",
                    !runtime.ClosePopup() && ReferenceEquals(runtime.CurrentPage, beforePopup),
                    runtime.CurrentPage?.Name);

                // 角色闸门：默认策略是操作员，管理员页开不了
                popup.RequiredRole = ScadaRole.Administrator;

                Check("弹窗⑩页面级角色：操作员打开管理员弹窗被拒，原因带所需角色",
                    !runtime.OpenPopup(popup, out var roleReason)
                    && runtime.CurrentPopup == null
                    && roleReason != null
                    && roleReason.Contains("管理员", StringComparison.Ordinal),
                    roleReason);

                popup.RequiredRole = null;

                // 动作分发：OpenPopup / ClosePopup 两条都真的走到导航出口
                var nav = new FakeNavigator();
                var log = new LoggerStub();
                var dispatcher = new ScadaActionDispatcher(log, new FakeValueSource(), nav);
                var hook = new ScadaEventHook { Event = ScadaEventType.Pressed };
                hook.Actions.Add(new ScadaAction
                {
                    Type = ScadaActionType.OpenPopup,
                    TargetPageId = popup.PageId,
                    TargetPageName = popup.Name,
                });

                dispatcher.Dispatch(hook, "打开参数");

                Check("弹窗11动作分发：OpenPopup 走到导航出口（目标 Id/名字原样带过去，日志有记录）",
                    nav.OpenPopupCalls == 1
                    && nav.LastPopupPageId == popup.PageId
                    && nav.LastPopupPageName == popup.Name
                    && log.Lines.Any(line => line.Contains("打开弹窗", StringComparison.Ordinal)),
                    $"calls={nav.OpenPopupCalls} / {string.Join(" | ", log.Lines)}");

                hook.Actions.Clear();
                hook.Actions.Add(new ScadaAction { Type = ScadaActionType.ClosePopup });
                dispatcher.Dispatch(hook, "关闭参数");

                Check("弹窗12动作分发：ClosePopup 走到导航出口并记一行",
                    nav.ClosePopupCalls == 1
                    && log.Lines.Any(line => line.Contains("关闭弹窗", StringComparison.Ordinal)),
                    $"calls={nav.ClosePopupCalls} / {string.Join(" | ", log.Lines)}");

                runtime.Stop();
            }

            // ---------------------------------------------------------------- 编辑器

            private static void RunEditorChecks()
            {
                var (doc, a, b, page) = BuildDocument();
                var workspace = new WorkspaceContext();
                workspace.SwitchSolution(new SolutionModel { Scada = doc });

                var vm = new ScadaEditorViewModel(workspace, new FakeRuntimeHost());
                vm.Activate();
                vm.SelectedPage = page;

                Check("多基座编辑器①候选项列出全部已注册基座，勾选状态取自当前页",
                    vm.BasePageSelections.Count == 2
                    && vm.BasePageSelections.All(s => !s.IsSelected),
                    string.Join("/", vm.BasePageSelections.Select(s => s.Name)));

                vm.BasePageSelections[0].IsSelected = true;
                vm.BasePageSelections[1].IsSelected = true;

                Check("多基座编辑器②勾两只 → 文档列表两只、摘要显示「等 2 个」",
                    page.BasePageIds.SequenceEqual(new[] { a.PageId, b.PageId })
                    && vm.SelectedBasePages.Count == 2
                    && vm.BasePageSummary.Contains("等 2 个", StringComparison.Ordinal),
                    vm.BasePageSummary);

                a.Name = "基座A-改名";

                Check("多基座编辑器②b 基座页改名后顶栏摘要立刻跟着变（不用切页重建）",
                    vm.BasePageSummary.Contains("基座A-改名", StringComparison.Ordinal),
                    vm.BasePageSummary);

                vm.ClearBasePagesCommand.Execute();

                Check("多基座编辑器③「（无基座）」清空列表，画布绑定随之变空",
                    page.BasePageIds.Count == 0
                    && vm.SelectedBasePages.Count == 0
                    && vm.BasePageSummary == "（无基座）",
                    vm.BasePageSummary);

                vm.SelectedPage = a;

                Check("多基座编辑器④编辑基座页时多选菜单置灰（一层限制）",
                    !vm.CanSelectBasePage,
                    $"CanSelectBasePage={vm.CanSelectBasePage}");
            }

            // ---------------------------------------------------------------- WPF（STA）

            private static void RunCanvasChecks()
            {
                var (doc, a, b, page) = BuildDocument();
                var pa = new ScadaElement { TypeKey = "Hmi.Rectangle", Name = "A图元", ZIndex = 5 };
                var pb = new ScadaElement { TypeKey = "Hmi.Rectangle", Name = "B图元", ZIndex = 1 };
                var pc = new ScadaElement { TypeKey = "Hmi.Rectangle", Name = "本页图元", ZIndex = 0 };
                a.TryAddElement(pa, out _);
                b.TryAddElement(pb, out _);
                page.TryAddElement(pc, out _);

                var canvas = new ScadaCanvas
                {
                    Page = page,
                    ItemsSource = page.Elements,
                    BasePages = new[] { a, b },
                    IsReadOnly = true,
                    PageWidth = 800,
                    PageHeight = 600,
                };

                Layout(canvas);

                Check("多基座画布①两只基座 + 本页三只图元全部装配（按列表顺序叠在下面）",
                    canvas.RenderedElementCount == 3
                    && ReferenceEquals(canvas.BasePage, a),
                    $"rendered={canvas.RenderedElementCount} / basePage={canvas.BasePage?.Name} / template={canvas.Template != null} / app={Application.Current != null}");

                var controls = canvas.EnumerateControls().ToList();
                var za = Panel.GetZIndex(controls.First(c => ReferenceEquals(c.Element, pa)));
                var zb = Panel.GetZIndex(controls.First(c => ReferenceEquals(c.Element, pb)));
                var zc = Panel.GetZIndex(controls.First(c => ReferenceEquals(c.Element, pc)));

                Check("多基座画布①b 层序不被模型 ZIndex 打乱：基座A(Z=5) < 基座B(Z=1) < 本页(Z=0)",
                    za < zb && zb < zc,
                    $"zA={za} / zB={zb} / zC={zc}");

                Check("多基座画布②基座图元在引用页上不可选中（要改公共层就切到基座页去改）",
                    Select(canvas, pa) == null
                    && Select(canvas, pb) == null
                    && ReferenceEquals(Select(canvas, pc), pc),
                    $"A={canvas.SelectedElement?.Name ?? "null"}");

                canvas.BasePages = new[] { b };
                Layout(canvas);

                Check("多基座画布③换列表：只剩 B 基座 + 本页，单基座兼容视图同步成 B",
                    canvas.RenderedElementCount == 2
                    && ReferenceEquals(canvas.BasePage, b),
                    $"rendered={canvas.RenderedElementCount} / basePage={canvas.BasePage?.Name}");

                canvas.BasePages = Array.Empty<ScadaPage>();
                Layout(canvas);

                Check("多基座画布④清空基座：只剩本页图元，BasePage 兼容视图为 null",
                    canvas.RenderedElementCount == 1 && canvas.BasePage == null,
                    $"rendered={canvas.RenderedElementCount} / basePage={canvas.BasePage?.Name ?? "null"}");
            }

            private static void RunWindowChecks()
            {
                var (doc, a, b, home) = BuildDocument();
                doc.TryAddBasePageReference(home, a, out _);
                doc.SetStartupPage(home);

                var popup = doc.AddPage("轴参数");
                popup.IsPopup = true;
                popup.PopupModal = true;
                popup.PopupAnchor = ScadaPopupAnchor.TopRight;
                popup.PopupOffsetX = 12;
                popup.PopupOffsetY = 8;
                doc.TryAddBasePageReference(popup, b, out _);

                var runtime = new ScadaRuntime(doc);
                runtime.Start();

                var window = new ScadaRuntimeWindow(runtime);
                var layer = window.FindName("PopupLayer") as Grid;
                var backdrop = window.FindName("PopupBackdrop") as Border;
                var frame = window.FindName("PopupFrame") as Border;
                var popupCanvas = window.FindName("PopupCanvas") as ScadaCanvas;
                var close = window.FindName("PopupCloseButton") as Button;

                runtime.OpenPopup(popup, out var openError);
                PumpDispatcher(30);

                Check("弹窗真窗口①打开后浮层/遮罩可见，弹窗画布拿到弹窗页与它自己的基座",
                    openError == null
                    && layer?.Visibility == Visibility.Visible
                    && backdrop?.Visibility == Visibility.Visible
                    && popupCanvas?.Page == popup
                    && popupCanvas?.BasePages.Count == 1,
                    $"layer={layer?.Visibility} / backdrop={backdrop?.Visibility} / page={popupCanvas?.Page?.Name} / bases={popupCanvas?.BasePages.Count}");

                Check("弹窗真窗口②九宫格锚点落到对齐方式（右上），偏移走平移而不是改尺寸",
                    frame?.HorizontalAlignment == HorizontalAlignment.Right
                    && frame?.VerticalAlignment == VerticalAlignment.Top
                    && frame.RenderTransform is System.Windows.Media.TranslateTransform,
                    $"{frame?.HorizontalAlignment}/{frame?.VerticalAlignment} / transform={frame?.RenderTransform}");

                close?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                PumpDispatcher(30);

                Check("弹窗真窗口③关闭按钮走 ClosePopup：CurrentPopup 清空、浮层收起、CurrentPage 不变",
                    runtime.CurrentPopup == null
                    && layer?.Visibility == Visibility.Collapsed
                    && ReferenceEquals(runtime.CurrentPage, home),
                    $"popup={runtime.CurrentPopup?.Name} / layer={layer?.Visibility} / page={runtime.CurrentPage?.Name}");

                // 软键盘接线：输入控件 IsEditing 翻转 → 运行窗口弹/收键盘。
                // 这里直接翻 DP（不依赖写通道），只验窗口与键盘的接线，不验 IOField 的写变量路径。
                var textDoc = new ScadaDocument();
                var textPage = textDoc.AddPage("文本页");
                var textElement = new ScadaElement
                {
                    TypeKey = "Hmi.TextField",
                    Name = "批号",
                    X = 400,
                    Y = 300,
                    Width = 240,
                    Height = 48,
                    Rotation = 20,
                };
                textPage.TryAddElement(textElement, out _);
                textDoc.SetStartupPage(textPage);

                var textRuntime = new ScadaRuntime(textDoc);
                textRuntime.Start();

                var textWindow = new ScadaRuntimeWindow(textRuntime)
                {
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -4000,
                    Top = -4000,
                    Width = 1280,
                    Height = 800,
                    ShowInTaskbar = false,
                };

                textWindow.Show();
                PumpDispatcher(80);
                textWindow.UpdateLayout();

                var textCanvas = textWindow.CanvasHost;
                Layout(textCanvas);

                var field = textCanvas.EnumerateControls().OfType<TextFieldElement>().FirstOrDefault();
                var keyboardLayer = textWindow.FindName("KeyboardLayer") as Grid;
                var keyboardBackdrop = textWindow.FindName("KeyboardBackdrop") as Border;
                var softKeyboard = textWindow.FindName("SoftKeyboard") as ScadaSoftKeyboard;

                if (field != null)
                    field.IsEditing = true;

                PumpDispatcher(30);

                Check("软键盘真窗口⓪键盘层覆盖整个窗口：是根 Grid 的子级、跨三行、ZIndex 高于报警抽屉与弹窗",
                    keyboardLayer != null
                    && ReferenceEquals(keyboardLayer.Parent, textWindow.Content)
                    && Grid.GetRowSpan(keyboardLayer) == 3
                    && Panel.GetZIndex(keyboardLayer) >= 40,
                    $"parent={(keyboardLayer?.Parent as FrameworkElement)?.GetType().Name} / rowSpan={Grid.GetRowSpan(keyboardLayer)} / z={Panel.GetZIndex(keyboardLayer)}");

                Check("软键盘真窗口①字段进入编辑态 → 键盘层可见、键盘绑定到该字段",
                    field != null
                    && keyboardLayer?.Visibility == Visibility.Visible
                    && softKeyboard?.IsOpen == true
                    && ReferenceEquals(softKeyboard?.Target, field),
                    $"field={field?.Name} / layer={keyboardLayer?.Visibility} / open={softKeyboard?.IsOpen}");

                // 点键盘外（遮罩）= 取消，不是提交半截输入
                keyboardBackdrop?.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = UIElement.MouseLeftButtonDownEvent,
                });
                PumpDispatcher(30);

                Check("软键盘真窗口②点遮罩取消：字段退出编辑、键盘收起",
                    field?.IsEditing == false
                    && keyboardLayer?.Visibility == Visibility.Collapsed
                    && softKeyboard?.IsOpen == false,
                    $"editing={field?.IsEditing} / layer={keyboardLayer?.Visibility}");

                if (field != null)
                    field.IsEditing = true;

                PumpDispatcher(30);

                if (field != null)
                    field.IsEditing = false;

                PumpDispatcher(30);

                Check("软键盘真窗口③字段退出编辑态 → 键盘层收起",
                    keyboardLayer?.Visibility == Visibility.Collapsed
                    && softKeyboard?.IsOpen == false,
                    $"layer={keyboardLayer?.Visibility} / open={softKeyboard?.IsOpen}");

                if (field != null && keyboardLayer != null && softKeyboard != null)
                {
                    (bool Ok, string Detail) MeasurePlacement(double zoom)
                    {
                        textCanvas.Zoom = zoom;
                        textWindow.UpdateLayout();

                        field.IsEditing = true;
                        PumpDispatcher(30);

                        Point P(double x, double y)
                            => field.TranslatePoint(new Point(x, y), keyboardLayer);

                        var p0 = P(0, 0);
                        var p1 = P(field.ActualWidth, 0);
                        var p2 = P(0, field.ActualHeight);
                        var p3 = P(field.ActualWidth, field.ActualHeight);

                        var fieldRect = new Rect(
                            Math.Min(Math.Min(p0.X, p1.X), Math.Min(p2.X, p3.X)),
                            Math.Min(Math.Min(p0.Y, p1.Y), Math.Min(p2.Y, p3.Y)),
                            Math.Max(Math.Max(p0.X, p1.X), Math.Max(p2.X, p3.X))
                                - Math.Min(Math.Min(p0.X, p1.X), Math.Min(p2.X, p3.X)),
                            Math.Max(Math.Max(p0.Y, p1.Y), Math.Max(p2.Y, p3.Y))
                                - Math.Min(Math.Min(p0.Y, p1.Y), Math.Min(p2.Y, p3.Y)));

                        var expected = ScadaSoftKeyboard.ComputePlacement(
                            fieldRect,
                            softKeyboard.DesiredSize,
                            new Size(keyboardLayer.ActualWidth, keyboardLayer.ActualHeight),
                            8);

                        var actual = new Point(
                            System.Windows.Controls.Canvas.GetLeft(softKeyboard),
                            System.Windows.Controls.Canvas.GetTop(softKeyboard));

                        var ok = Math.Abs(actual.X - expected.X) < 1.0
                                 && Math.Abs(actual.Y - expected.Y) < 1.0;

                        field.IsEditing = false;
                        PumpDispatcher(20);

                        return (ok,
                            $"zoom={zoom} / field={fieldRect} / expected={expected} / actual={actual}");
                    }

                    var halfZoom = MeasurePlacement(0.5);
                    var doubleZoom = MeasurePlacement(2.0);

                    Check("软键盘真窗口④Zoom=0.5 + 旋转 20°：四角换算后的摆放与纯函数一致",
                        halfZoom.Ok, halfZoom.Detail);

                    Check("软键盘真窗口⑤Zoom=2.0 + 旋转 20°：放大画布上仍按字段真实包围盒摆放",
                        doubleZoom.Ok, doubleZoom.Detail);
                }

                textWindow.Close();
                textRuntime.Stop();
            }

            // ---------------------------------------------------------------- 工具

            private static void RunOnSta(Action action)
            {
                Exception? error = null;

                var thread = new Thread(() =>
                {
                    try
                    {
                        EnsureApplicationTheme();
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
                    Check("多基座/弹窗 UI 段执行完成（没有未捕获异常）", false, error.ToString());
            }

            private static void Layout(ScadaCanvas canvas)
            {
                // 断言宿主不是 WPF Application 的正常启动路径：默认主题查找在跨线程时不可靠，
                // 显式把 Generic.xaml 里的 ScadaCanvas 样式套上（与 R53 的"窗口级主题字典"同一手法）。
                if (canvas.Style == null)
                {
                    var theme = new ResourceDictionary
                    {
                        Source = new Uri("/VM.Scada.Controls;component/Themes/Generic.xaml", UriKind.Relative),
                    };

                    if (theme[typeof(ScadaCanvas)] is Style style)
                        canvas.Style = style;
                }

                canvas.ApplyTemplate();
                canvas.Measure(new Size(1920, 1080));
                canvas.Arrange(new Rect(0, 0, 1920, 1080));
                canvas.UpdateLayout();
            }

            /// <summary>
            /// 画布/窗口断言需要 <c>Themes/Generic.xaml</c> 里的默认样式；Phase2UiChecks 通常已经建过
            /// Application，这里只补齐资源字典（重复合并会让模板取到两份，先判重）。
            /// </summary>
            private static void EnsureApplicationTheme()
            {
                var app = Application.Current
                    ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

                var uri = new Uri("/VM.Scada.Controls;component/Themes/Generic.xaml", UriKind.Relative);

                if (!app.Resources.MergedDictionaries.Any(d => d.Source == uri))
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = uri });
            }

            private static ScadaElement? Select(ScadaCanvas canvas, ScadaElement element)
            {
                canvas.SelectedElement = element;
                return canvas.SelectedElement;
            }
        }
    }
}
