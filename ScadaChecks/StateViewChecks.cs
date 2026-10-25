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
        /// [SV] StateView：一个类型覆盖字指示灯 / 图形 IO / 图形开关。
        /// 覆盖列表字段、解析、缺失/无匹配/图片缺失、三呈现、只读/切换/写定值、权限与版本刷新。
        /// </summary>
        private static class StateViewChecks
        {
            public static void Run() => RunOnSta(RunChecks);

            private static void RunChecks()
            {
                Section("状态图元（Hmi.StateView）");

                var descriptor = ElementRegistry.Find("Hmi.StateView");
                Check("[SV1] 描述符：Hmi.StateView 已注册，默认 180×64，StateListId 为 StateList 型，事件含对象改变/打开/关闭",
                    descriptor != null
                    && descriptor.DefaultWidth == 180
                    && descriptor.DefaultHeight == 64
                    && descriptor.Properties.Any(p => p.Key == "StateListId" && p.Kind == ElementPropertyKind.StateList)
                    && descriptor.Events.Contains(ScadaEventType.ObjectChanged)
                    && descriptor.Events.Contains(ScadaEventType.Opened)
                    && descriptor.Events.Contains(ScadaEventType.Closed),
                    descriptor == null ? "null" : $"{descriptor.DefaultWidth}×{descriptor.DefaultHeight}");

                var list = new ScadaStateList
                {
                    Name = "设备状态",
                    DefaultText = "—",
                    DefaultImage = "Image\\default.png",
                };
                list.Entries.Add(new ScadaStateEntry
                {
                    Value = 0, Text = "停止", Image = "Image\\stop.png", Color = "#FF7C8A99",
                });
                list.Entries.Add(new ScadaStateEntry
                {
                    Value = 1, Text = "运行中", Image = "Image\\run.png", Color = "#FF22C55E",
                });
                list.Entries.Add(new ScadaStateEntry
                {
                    Value = 2, Text = "故障", Image = "Image\\fault.png", Color = "#FFEF4444",
                });

                var clone = list.Clone();
                Check("[SV2] 列表 Clone 保留 Image/Color/DefaultImage",
                    clone.DefaultImage == list.DefaultImage
                    && clone.Entries.Count == 3
                    && clone.Entries[1].Image == "Image\\run.png"
                    && clone.Entries[1].Color == "#FF22C55E",
                    $"default={clone.DefaultImage} / run={clone.Entries[1].Image} / color={clone.Entries[1].Color}");

                var copied = new ScadaStateList();
                copied.CopyFrom(list);
                Check("[SV3] 列表 CopyFrom 原地覆盖且保留新字段",
                    copied.DefaultImage == list.DefaultImage
                    && copied.Entries.Count == 3
                    && copied.Entries[2].Image == "Image\\fault.png"
                    && copied.Entries[2].Color == "#FFEF4444",
                    $"entries={copied.Entries.Count}");

                var element = ElementRegistry.CreateElement("Hmi.StateView")!;
                element.SetProperty("StateListId", list.StateListId.ToString("D"));
                element.SetProperty("Presentation", "TextAndImage");
                element.SetProperty("Interaction", "None");
                element.SetProperty("Value", "1");
                element.GetOrAddBinding("Value").Bind(Guid.NewGuid(), "State");

                var writer = new FakeValueWriter();
                var policy = new FakeAccessPolicy();
                var runImage = FakeImage();
                var defaultImage = FakeImage();
                var view = (StateViewElement)ElementRegistry.CreateControl(element)!;
                view.RuntimeContext = StateViewContext(writer, policy);
                view.StateListResolver = id => id == list.StateListId ? list : null;
                view.ImageAssetResolver = path =>
                    path == "Image\\run.png" ? runImage
                    : path == "Image\\default.png" ? defaultImage
                    : null;
                view.Style = StateViewStyle();
                view.Refresh();
                Layout(view, 260, 80);

                Check("[SV4] 列表命中：文字/图片/状态色都取条目",
                    view.CurrentText == "运行中"
                    && ReferenceEquals(view.CurrentImage, runImage)
                    && view.CurrentColor is SolidColorBrush brush
                    && brush.Color == Color.FromArgb(0xFF, 0x22, 0xC5, 0x5E),
                    $"text={view.CurrentText} / image={(ReferenceEquals(view.CurrentImage, runImage) ? "run" : "other")} / color={view.CurrentColor}");

                view.Presentation = "Text";
                Check("[SV5] Presentation=Text：只出文字，不出图片", view.ShowText && !view.ShowImage,
                    $"text={view.ShowText} / image={view.ShowImage}");

                view.Presentation = "Image";
                Check("[SV6] Presentation=Image：只出图片，不出文字", !view.ShowText && view.ShowImage,
                    $"text={view.ShowText} / image={view.ShowImage}");

                view.Presentation = "TextAndImage";
                Check("[SV7] Presentation=TextAndImage：文字与图片都出", view.ShowText && view.ShowImage,
                    $"text={view.ShowText} / image={view.ShowImage}");

                view.Value = 9;
                Check("[SV8] 值无匹配：DefaultText + DefaultImage，不拿别的状态顶上",
                    view.CurrentText == "—" && ReferenceEquals(view.CurrentImage, defaultImage),
                    $"text={view.CurrentText} / image={(ReferenceEquals(view.CurrentImage, defaultImage) ? "default" : "other")}");

                view.ImageAssetResolver = _ => null;
                Check("[SV8b] 图片路径存在但解析失败：ImageMissing=true（画虚线占位，不拿别的图顶上）",
                    view.ImageMissing && view.CurrentImage == null,
                    $"missing={view.ImageMissing} / image={(view.CurrentImage == null ? "null" : "other")}");

                view.Value = 1;
                view.ImageAssetResolver = path => path == "Image\\run.png" ? runImage : null;
                view.StateListResolver = null;
                Check("[SV8c] 解析器晚注入：先显示缺失，注入后立刻重解析为“运行中”",
                    view.StateListMissing && view.CurrentText == "未找到文本列表",
                    $"before={view.CurrentText} / missing={view.StateListMissing}");

                view.StateListResolver = id => id == list.StateListId ? list : null;
                Check("[SV8d] 解析器注入后恢复：文字/图片都回来",
                    !view.StateListMissing && view.CurrentText == "运行中" && ReferenceEquals(view.CurrentImage, runImage),
                    $"text={view.CurrentText} / missing={view.StateListMissing}");

                view.Value = 1;
                view.StateListId = Guid.NewGuid().ToString("D");
                Check("[SV9] 列表缺失：StateListMissing=true，文字明示“未找到文本列表”，不静默回退",
                    view.StateListMissing && view.CurrentText == "未找到文本列表",
                    $"missing={view.StateListMissing} / text={view.CurrentText}");

                view.StateListId = list.StateListId.ToString("D");
                view.Value = 0;
                Check("[SV10] Interaction=None：只读，CanOperate=false，点击不写",
                    !view.CanOperate,
                    $"canOperate={view.CanOperate} / writes={writer.Writes.Count}");

                var before = writer.Writes.Count;
                view.Activate();
                Check("[SV11] 只读时 Activate 是空操作", writer.Writes.Count == before,
                    $"writes={writer.Writes.Count}");

                var events = new List<ScadaEventType>();
                view.ScadaEvent += (_, e) => events.Add(e.ScadaEvent);

                view.Interaction = "Toggle";
                view.Value = 0;
                Check("[SV12] Interaction=Toggle 且写通道/绑定齐备：CanOperate=true",
                    view.CanOperate,
                    $"canOperate={view.CanOperate}");

                view.Activate();
                Check("[SV13] Toggle 0→1：写 Value=1，发 ObjectChanged + Opened",
                    writer.Writes.Count > 0
                    && writer.Writes[^1].Target == "Value"
                    && writer.Writes[^1].Text == "1"
                    && events.Contains(ScadaEventType.ObjectChanged)
                    && events.Contains(ScadaEventType.Opened),
                    $"last={writer.Writes.LastOrDefault()} / events={string.Join(",", events)}");

                events.Clear();
                view.Value = 1;
                view.Activate();
                Check("[SV14] Toggle 1→0：写 Value=0，发 ObjectChanged + Closed",
                    writer.Writes[^1].Text == "0"
                    && events.Contains(ScadaEventType.ObjectChanged)
                    && events.Contains(ScadaEventType.Closed),
                    $"last={writer.Writes[^1].Text} / events={string.Join(",", events)}");

                events.Clear();
                view.Interaction = "WriteValue";
                view.ClickValue = 2;
                view.Activate();
                Check("[SV15] WriteValue：写 ClickValue=2，只发 ObjectChanged（2 不是二态）",
                    writer.Writes[^1].Text == "2"
                    && events.Count == 1
                    && events[0] == ScadaEventType.ObjectChanged,
                    $"last={writer.Writes[^1].Text} / events={string.Join(",", events)}");

                element.RequiredRole = ScadaRole.Engineer;
                policy.CurrentRole = ScadaRole.Operator;
                view.Refresh();
                var deniedBefore = writer.Writes.Count;
                view.Activate();
                Check("[SV16] 权限不足：CanOperate=false，点击不写",
                    !view.CanOperate && writer.Writes.Count == deniedBefore,
                    $"canOperate={view.CanOperate} / writes={writer.Writes.Count - deniedBefore}");

                policy.CurrentRole = ScadaRole.Engineer;
                element.RequiredRole = null;
                view.Interaction = "None";
                list.Entries[1].Text = "运行";
                Check("[SV17] 列表条目改文字：Version 推高，控件显示跟着刷新",
                    view.CurrentText == "运行",
                    $"text={view.CurrentText} / version={list.Version}");

                var document = new ScadaDocument();
                var page = new ScadaPage { Name = "总览" };
                document.Pages.Add(page);
                document.StateLists.Add(list);
                page.Elements.Add(element);

                var combo = ElementRegistry.CreateElement("Hmi.ComboBox")!;
                combo.SetProperty("StateListId", list.StateListId.ToString("D"));
                page.Elements.Add(combo);

                var manager = new VisionMaster.ViewModels.StateListManagerViewModel(document);
                Check("[SV18] 管理器引用数同时统计 StateView 与 ComboBox",
                    manager.CountReferences(list.StateListId) == 2,
                    $"refs={manager.CountReferences(list.StateListId)}");

                var managerList = manager.Lists.FirstOrDefault(x => x.StateListId == list.StateListId);
                Check("[SV19] 管理器副本带 Image/Color，Commit 后原地写回文档",
                    managerList != null
                    && managerList.Entries[1].Image == "Image\\run.png"
                    && managerList.Entries[1].Color == "#FF22C55E",
                    managerList == null ? "null" : $"entries={managerList.Entries.Count}");

                if (managerList != null)
                {
                    managerList.Entries[1].Image = "Image\\run2.png";
                    managerList.DefaultImage = "Image\\default2.png";
                    manager.Commit();
                }

                Check("[SV20] Commit 后文档里的列表拿到新 Image/DefaultImage（不是只改副本）",
                    list.Entries[1].Image == "Image\\run2.png"
                    && list.DefaultImage == "Image\\default2.png",
                    $"entry={list.Entries[1].Image} / default={list.DefaultImage}");
            }

            private static ScadaRuntimeContext StateViewContext(FakeValueWriter writer, FakeAccessPolicy policy)
            {
                var alarms = new ScadaAlarmEngine(new ScadaDocument(), new FakeValueSource(), () => DateTime.UtcNow);
                var beat = new ScadaBeatSource(Dispatcher.CurrentDispatcher, TimeSpan.FromMilliseconds(20));
                return new ScadaRuntimeContext(alarms, beat, writer, policy);
            }

            private static Style StateViewStyle()
            {
                var theme = new ResourceDictionary
                {
                    Source = new Uri("/VM.Scada.Controls;component/Themes/Generic.xaml", UriKind.Relative),
                };

                return (Style)theme[typeof(StateViewElement)];
            }

            private static ImageSource FakeImage()
            {
                var drawing = new GeometryDrawing(
                    Brushes.SeaGreen, null, new RectangleGeometry(new Rect(0, 0, 12, 12)));
                return new DrawingImage(drawing);
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
                    Check("StateView UI 段执行完成（没有未捕获异常）", false, error.ToString());
            }
        }
    }
}
