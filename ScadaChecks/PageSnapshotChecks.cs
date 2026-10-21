using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using VisionMaster.Scada;
using VisionMaster.Scada.Controls;
using VisionMaster.Scada.Snapshots;

// 与剪贴板快照同名（不同命名空间、不同形状），这里一律显式取别名，避免歧义用法混进断言。
using BindingSnapshot = VisionMaster.Scada.Snapshots.ScadaBindingSnapshot;
using ElementSnapshot = VisionMaster.Scada.Snapshots.ScadaElementSnapshot;
using AnimationSnapshot = VisionMaster.Scada.Snapshots.ScadaAnimationSnapshot;

namespace ScadaChecks
{
    /// <summary>
    /// [PS] M1a 页面快照契约：不可变 DTO、基座一层、图层可见性、Z 序、属性袋与绑定/动画搬运。
    ///
    /// 与 [TK] / [AX] 同一纪律：整段另起 STA 线程跑（VM.Scada 不引用 WPF，
    /// <c>ScadaPageSnapshotBuilder</c> 的线程前提是"调用方在 UI 线程"，断言就在 STA 线程上验这条前提）。
    ///
    /// 断言只读数据侧（快照字段 / 模型前后对照），不建窗口、不建 Application、不碰控件。
    /// </summary>
    internal partial class Program
    {
        /// <summary>
        /// 独立入口：Main 里只加一行 <c>PageSnapshotChecks.Run();</c>。
        /// 做成 Program 的嵌套类型以复用私有断言框架（Section / Check），口径与 TankKpiChecks 一致。
        /// </summary>
        private static class PageSnapshotChecks
        {
            public static void Run() => RunPageSnapshotChecks();
        }

        private static void RunPageSnapshotChecks()
        {
            Section("[PS] 页面快照（M1a）：DTO 契约、基座一层、图层可见性、Z 序与绑定/动画搬运");

            Exception? failure = null;

            var thread = new Thread(() =>
            {
                try { RunPageSnapshotChecksCore(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            thread.Join();

            if (failure != null)
                Check("页面快照断言全程未抛异常", false, failure.ToString());
        }

        private static void RunPageSnapshotChecksCore()
        {
            var builder = new ScadaPageSnapshotBuilder();

            // ==================================================================
            //  ① 三种"没有画面可拍"的状态一律返回 null（不是抛异常）
            // ==================================================================

            Check("[PS1] 未启动的会话 Build 返回 null（窗口还没起来时渲染器照常工作）",
                builder.Build(new ScadaRuntime(new ScadaDocument())) is null,
                "未启动会话");

            var emptyDoc = new ScadaDocument();
            var emptySession = new ScadaRuntime(emptyDoc);
            bool emptyStarted = emptySession.Start();

            Check("[PS2] 方案里一页都没有：Start() 返回 false，Build 返回 null",
                !emptyStarted && builder.Build(emptySession) is null,
                $"started={emptyStarted} / page={emptySession.CurrentPage?.Name ?? "null"}");

            // ==================================================================
            //  ② 造一份"主画面 + 基座 + 10 个图元 + 隐藏图层 + 面板子图元"的文档
            // ==================================================================

            var doc = new ScadaDocument();

            // 基座画面（跨页公共层）：只有一个标题文字
            var basePage = doc.AddPage("基座");
            basePage.Width = 1920;
            basePage.Height = 1080;

            var baseTitle = ElementRegistry.CreateElement("Hmi.Text", 10, 10);
            baseTitle.Name = "基座标题";
            baseTitle.ZIndex = 1;
            baseTitle.SetProperty("Text", "公共层");
            basePage.TryAddElement(baseTitle, out _);

            bool baseRegistered = doc.TryAddBasePage(basePage, out var baseError);

            // 主画面：引用上面那份基座
            var page = doc.AddPage("主画面", basePage.PageId);
            page.Width = 1600;
            page.Height = 900;
            page.Background = "#FF102030";

            var normalLayer = page.DefaultLayer!;
            var hiddenLayer = page.AddLayer("隐藏层");
            hiddenLayer.IsVisible = false;

            // 10 个不同种类的图元（ZIndex 刻意乱序给，其中 ProgressBar / Indicator 同为 50 用来验稳定排序）
            var specs = new (string Key, double X, double Y, int Z)[]
            {
                ("Hmi.Text",        20,  20,  30),
                ("Hmi.Rectangle",   40,  40,  10),
                ("Hmi.Panel",      100, 100,  20),
                ("Hmi.IOField",    220,  20,  40),
                ("Hmi.ProgressBar", 220, 80,  50),
                ("Hmi.Indicator",  220, 140,  50),
                ("Hmi.Lamp",       380,  20,  70),
                ("Hmi.Tank",       380, 100,  80),
                ("Hmi.Valve",      560,  20,  90),
                ("Hmi.Gauge",      560, 120, 100),
            };

            var elements = new List<ScadaElement>(specs.Length);

            foreach (var spec in specs)
            {
                var element = ElementRegistry.CreateElement(spec.Key, spec.X, spec.Y);
                element.Name = spec.Key;
                element.ZIndex = spec.Z;
                page.TryAddElement(element, out _);
                elements.Add(element);
            }

            var text = elements[0];
            var panel = elements[2];
            var lamp = elements[6];

            // 面板里的子图元（与父同一个 Elements 集合，靠 ParentId 表达归属）
            var panelChild = ElementRegistry.CreateElement("Hmi.Text", 120, 120);
            panelChild.Name = "面板内文字";
            panelChild.ZIndex = 25;
            panelChild.ParentId = panel.ElementId;
            page.TryAddElement(panelChild, out _);

            // 图层归属：Lamp 进隐藏层，Panel 显式进默认层，其余保持"未分层"（LayerId = Guid.Empty）
            page.TryAssignLayer(lamp, hiddenLayer, out _);
            page.TryAssignLayer(panel, normalLayer, out _);

            // 属性袋 + 操作权限 + 绑定 + 动画（都挂在 Text 上，便于逐字段核对）
            text.SetProperty("Text", "液位 42%");
            text.SetProperty("FontSize", "18");
            page.TrySetRequiredRole(text, ScadaRole.Engineer, out _);

            var variableId = Guid.NewGuid();

            text.Bindings.Add(new ScadaBinding
            {
                TargetProperty = "Text",
                VariableId = variableId,
                VariableName = "液位",
                DisplayFormat = "F2",
                IsEnabled = true,
            });

            text.Bindings.Add(new ScadaBinding
            {
                TargetProperty = "FillColor",
                VariableId = Guid.Empty,
                VariableName = "旧绑定",
                IsEnabled = false,
            });

            var animation = text.AddAnimation(ScadaAnimationType.Appearance);
            animation.BindVariable(variableId, "液位");
            animation.RangeLow = "0";
            animation.RangeHigh = "100";
            animation.EndX = 123.5;
            animation.EndY = 45.5;
            animation.VisibleInRange = true;
            animation.States.Add(new ScadaAnimationState
            {
                ValueLow = "0",
                ValueHigh = "49",
                Foreground = "#FF00FF00",
                Fill = "#FF00AA00",
                IsFlashing = true,
            });

            // ==================================================================
            //  ③ 启动会话 → 拍快照
            // ==================================================================

            var session = new ScadaRuntime(doc);
            bool started = session.Start();

            Check("[PS3] 会话启动：当前页是主画面、当前基座是基座画面（基座不参与启动页回落）",
                baseRegistered && started
                && ReferenceEquals(session.CurrentPage, page)
                && ReferenceEquals(session.CurrentBasePage, basePage),
                $"base={baseRegistered}({baseError}) / started={started} / " +
                $"page={session.CurrentPage?.Name ?? "null"} / basePage={session.CurrentBasePage?.Name ?? "null"}");

            // Build 之前把模型的"读数"全部留一份，用来做"Build 不改模型"的前后对照
            var propertiesBefore = text.Properties;
            var propertyCountBefore = text.Properties.Count;
            var textValueBefore = text.GetProperty("Text");
            var versionBeforeBuild = page.Version;

            var snapshot = builder.Build(session);

            Check("[PS4] Build 返回快照，页面元信息与模型一致（PageId 为 Guid \"D\"、名称/尺寸/背景/版本）",
                snapshot is not null
                && snapshot.PageId == page.PageId.ToString("D")
                && snapshot.PageId.Length == 36
                && snapshot.PageName == "主画面"
                && Math.Abs(snapshot.Width - 1600) < 1e-9
                && Math.Abs(snapshot.Height - 900) < 1e-9
                && snapshot.Background == "#FF102030"
                && snapshot.PageVersion == versionBeforeBuild,
                snapshot is null
                    ? "快照为 null"
                    : $"{snapshot.PageId} / {snapshot.PageName} / {snapshot.Width}×{snapshot.Height} / " +
                      $"{snapshot.Background} / v={snapshot.PageVersion}");

            if (snapshot is null)
            {
                // 后续断言全部依赖快照；快照没了就一次性说清，不逐条刷红。
                Check("[PS5~PS14] 快照为 null，后续断言无法执行", false, "Build 返回 null");
                return;
            }

            // ==================================================================
            //  ④ 元素数量 / 基座一层 / ParentId / 图层可见性
            // ==================================================================

            Check("[PS5] 本页元素 11 条（10 个图元 + 面板子图元），基座元素 1 条，且基座自己的 BasePage 为 null（只递归一层）",
                snapshot.Elements.Count == 11
                && snapshot.BasePage is not null
                && snapshot.BasePage.Elements.Count == 1
                && snapshot.BasePage.Elements[0].Name == "基座标题"
                && snapshot.BasePage.BasePage is null,
                $"本页 {snapshot.Elements.Count} 条 / 基座 {snapshot.BasePage?.Elements.Count} 条 / " +
                $"基座的基座={snapshot.BasePage?.BasePage?.PageName ?? "null"}");

            ElementSnapshot? childSnapshot = snapshot.Elements.FirstOrDefault(e => e.Name == "面板内文字");
            ElementSnapshot? panelSnapshot = snapshot.Elements.FirstOrDefault(e => e.Name == "Hmi.Panel");
            ElementSnapshot? textSnapshot = snapshot.Elements.FirstOrDefault(e => e.Name == "Hmi.Text");

            Check("[PS6] ParentId：面板子图元指向面板的 ElementId（Guid \"D\"），顶层图元为 null；" +
                  "已分层图元 LayerId 有值、未分层图元 LayerId 为 null",
                childSnapshot is not null && panelSnapshot is not null
                && childSnapshot.ParentId == panel.ElementId.ToString("D")
                && childSnapshot.ParentId!.Length == 36
                && panelSnapshot.ParentId is null
                && panelSnapshot.LayerId == normalLayer.LayerId.ToString("D")
                && textSnapshot is { ParentId: null, LayerId: null },
                $"child.ParentId={childSnapshot?.ParentId} / panel.ParentId={panelSnapshot?.ParentId ?? "null"} / " +
                $"panel.LayerId={panelSnapshot?.LayerId ?? "null"} / text.LayerId={textSnapshot?.LayerId ?? "null"}");

            ElementSnapshot? lampSnapshot = snapshot.Elements.FirstOrDefault(e => e.Name == "Hmi.Lamp");

            Check("[PS7] 图层可见性：隐藏层上的图元 LayerVisible=false，默认层上的为 true，" +
                  "未分层（找不到图层）的按 true",
                lampSnapshot is { LayerVisible: false }
                && lampSnapshot.LayerId == hiddenLayer.LayerId.ToString("D")
                && panelSnapshot is { LayerVisible: true }
                && textSnapshot is { LayerVisible: true, LayerId: null },
                $"lamp.visible={lampSnapshot?.LayerVisible} / panel.visible={panelSnapshot?.LayerVisible} / " +
                $"text.visible={textSnapshot?.LayerVisible}");

            // ==================================================================
            //  ⑤ Z 序（升序 + 同 Z 保集合次序）
            // ==================================================================

            var order = snapshot.Elements.Select(e => e.Name).ToList();
            bool ascending = true;

            for (int i = 1; i < snapshot.Elements.Count; i++)
            {
                if (snapshot.Elements[i - 1].ZIndex > snapshot.Elements[i].ZIndex)
                {
                    ascending = false;
                    break;
                }
            }

            int progressIndex = order.IndexOf("Hmi.ProgressBar");
            int indicatorIndex = order.IndexOf("Hmi.Indicator");

            Check("[PS8] 元素按 ZIndex 升序排列；ZIndex 相同的两条（ProgressBar/Indicator 同为 50）保持集合次序（稳定排序）",
                ascending
                && progressIndex >= 0 && indicatorIndex > progressIndex
                && order.IndexOf("Hmi.Rectangle") < order.IndexOf("Hmi.Panel")
                && order.IndexOf("Hmi.Panel") < order.IndexOf("面板内文字")
                && order.IndexOf("面板内文字") < order.IndexOf("Hmi.Text"),
                $"ascending={ascending} / progress={progressIndex} < indicator={indicatorIndex} / " +
                $"order={string.Join(",", order)}");

            // ==================================================================
            //  ⑥ 属性袋原样 + 只读包装 + 模型未被改动
            // ==================================================================

            bool readOnlyEnforced = false;

            if (textSnapshot is not null)
            {
                try
                {
                    ((IDictionary<string, string>)textSnapshot.Properties)["Text"] = "被改写";
                }
                catch (NotSupportedException)
                {
                    readOnlyEnforced = true;
                }
            }

            bool bagMatches = textSnapshot is not null
                              && textSnapshot.Properties.Count == propertyCountBefore
                              && text.Properties.All(kv => textSnapshot.Properties.TryGetValue(kv.Key, out var v)
                                                           && string.Equals(v, kv.Value, StringComparison.Ordinal));

            Check("[PS9] 属性袋原样复制（键值/条数与模型一致）、快照侧只读（强转去改抛 NotSupportedException）",
                bagMatches
                && readOnlyEnforced
                && textSnapshot is { } ts
                && ts.Properties["Text"] == "液位 42%"
                && ts.Properties["FontSize"] == "18",
                $"count={textSnapshot?.Properties.Count}/{propertyCountBefore} / " +
                $"readOnly={readOnlyEnforced} / Text={textSnapshot?.Properties.GetValueOrDefault("Text")}");

            Check("[PS10] 操作权限取角色成员名（Engineer）；没配过的图元为 null",
                textSnapshot?.RequiredRole == nameof(ScadaRole.Engineer)
                && panelSnapshot?.RequiredRole is null,
                $"text={textSnapshot?.RequiredRole ?? "null"} / panel={panelSnapshot?.RequiredRole ?? "null"}");

            // ==================================================================
            //  ⑦ 绑定 / 动画字段搬运
            // ==================================================================

            var bindings = textSnapshot?.Bindings ?? Array.Empty<BindingSnapshot>();
            var binding0 = bindings.Count > 0 ? bindings[0] : null;
            var binding1 = bindings.Count > 1 ? bindings[1] : null;

            Check("[PS11] 绑定字段全搬：TargetProperty / VariableId(Guid \"D\") / VariableName / DisplayFormat / Enabled；空 Id 输出空串",
                bindings.Count == 2
                && binding0 is not null
                && binding0.TargetProperty == "Text"
                && binding0.VariableId == variableId.ToString("D")
                && binding0.VariableName == "液位"
                && binding0.DisplayFormat == "F2"
                && binding0.Enabled
                && binding1 is not null
                && binding1.TargetProperty == "FillColor"
                && binding1.VariableId.Length == 0
                && binding1.VariableName == "旧绑定"
                && binding1.DisplayFormat is null
                && !binding1.Enabled,
                $"count={bindings.Count} / " +
                $"0=({binding0?.TargetProperty},{binding0?.VariableId},{binding0?.VariableName},{binding0?.DisplayFormat},{binding0?.Enabled}) / " +
                $"1=({binding1?.TargetProperty},\"{binding1?.VariableId}\",{binding1?.VariableName},{binding1?.Enabled})");

            var animations = textSnapshot?.Animations ?? Array.Empty<AnimationSnapshot>();
            var anim = animations.Count > 0 ? animations[0] : null;
            var state = anim is not null && anim.States.Count > 0 ? anim.States[0] : null;

            Check("[PS12] 动画字段全搬：Type 成员名 / 变量 / 范围原文 / EndX / EndY / VisibleInRange / Enabled",
                animations.Count == 1
                && anim is not null
                && anim.Type == nameof(ScadaAnimationType.Appearance)
                && anim.VariableId == variableId.ToString("D")
                && anim.VariableName == "液位"
                && anim.RangeLow == "0"
                && anim.RangeHigh == "100"
                && Math.Abs(anim.EndX - 123.5) < 1e-9
                && Math.Abs(anim.EndY - 45.5) < 1e-9
                && anim.VisibleInRange
                && anim.Enabled
                && anim.States.Count == 1,
                $"count={animations.Count} / type={anim?.Type} / {anim?.VariableId} / " +
                $"range={anim?.RangeLow}~{anim?.RangeHigh} / end=({anim?.EndX},{anim?.EndY}) / states={anim?.States.Count}");

            Check("[PS13] 档位字段：From/To 取模型原文（ValueLow/ValueHigh）、Fill/Foreground/Flash 落地、Visible 恒为 null",
                state is not null
                && state.From == "0"
                && state.To == "49"
                && state.Fill == "#FF00AA00"
                && state.Foreground == "#FF00FF00"
                && state.Flash
                && state.Visible is null,
                $"from={state?.From} / to={state?.To} / fill={state?.Fill} / fg={state?.Foreground} / " +
                $"flash={state?.Flash} / visible={state?.Visible?.ToString() ?? "null"}");

            // ==================================================================
            //  ⑧ Build 不改模型 + 版本号变化后能重新拍到
            // ==================================================================

            bool modelUntouched = ReferenceEquals(text.Properties, propertiesBefore)
                                  && text.Properties.Count == propertyCountBefore
                                  && string.Equals(text.GetProperty("Text"), textValueBefore, StringComparison.Ordinal)
                                  && page.Elements.Count == 11
                                  && page.Version == versionBeforeBuild
                                  && !ReferenceEquals(textSnapshot!.Properties, text.Properties);

            Check("[PS14] Build 只读：模型属性袋引用/条数/取值不变、元素数不变、Page.Version 不变，且快照用的是副本",
                modelUntouched,
                $"sameBag={ReferenceEquals(text.Properties, propertiesBefore)} / count={text.Properties.Count} / " +
                $"Text=\"{text.GetProperty("Text")}\" / elements={page.Elements.Count} / " +
                $"version={page.Version}/{versionBeforeBuild} / sameDict={ReferenceEquals(textSnapshot!.Properties, text.Properties)}");

            // 版本号推进：改一个图元的坐标（元素属性变更 → ScadaPage.Version++）
            text.X += 10;

            var afterEdit = builder.Build(session);

            Check("[PS15] 页 Version 变大后再 Build，快照 PageVersion 跟着变大且等于模型当前版本",
                afterEdit is not null
                && afterEdit.PageVersion > snapshot.PageVersion
                && afterEdit.PageVersion == page.Version,
                $"before={snapshot.PageVersion} / after={afterEdit?.PageVersion} / model={page.Version}");

            // 停止会话后没有当前页 → 回到 null
            session.Stop();

            Check("[PS16] 会话停止后 Build 回到 null（快照跟着会话生命周期，不缓存上一帧）",
                builder.Build(session) is null,
                $"running={session.IsRunning} / page={session.CurrentPage?.Name ?? "null"}");
        }
    }
}
