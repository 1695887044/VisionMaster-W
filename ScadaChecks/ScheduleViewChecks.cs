using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VisionMaster.Scada;
using VisionMaster.Views.DialogViews;

namespace ScadaChecks
{
    internal partial class Program
    {
        /// <summary>
        /// [SV] 计划表视图 v2：空态 / 分段 / 星期芯片 / 校验 / 动作表 / 只读 / 预览 + 真实渲染证据。
        ///
        /// 为什么要有这一段
        /// =========
        /// 2026-10-27 走查发现旧视图有 17 条 UI 问题，其中三条是"看不出来"的类型：
        /// · 宿主卡片 MaxWidth=700 把动作表的操作列裁成一条缝（渲染才知道）；
        /// · 视图里的 ValidateAndShow() 没有任何调用方（"确定"直接关窗，校验永远不跑）；
        /// · 「每周」的星期靠逗号文本手填，界面与 Days 字段没有结构化映射。
        /// 这一段的断言都钉在"用户能看见/能点到"的那一层：面板可见性、行号、错误旗标、芯片回显。
        /// </summary>
        private static class ScheduleViewChecks
        {
            public static void Run() => RunOnSta(RunChecks);

            private static void RunChecks()
            {
                Section("[SV] 计划表视图 v2：空态 / 分段 / 芯片 / 校验 / 动作表 / 只读 / 渲染");

                // ---------------- 空文档 ----------------

                var emptyView = new ScadaScheduleManagerView(new ScadaDocument());
                Check("[SV1] 空文档：左列空态可见、右列空选中、计数 0 条、删除不可见",
                    emptyView.NoSchedulesVisibility == Visibility.Visible
                    && emptyView.NoSelectionVisibility == Visibility.Visible
                    && emptyView.HasSelectionVisibility == Visibility.Collapsed
                    && emptyView.CountText == "0 条"
                    && emptyView.DeleteVisibility == Visibility.Collapsed,
                    $"count={emptyView.CountText} noSched={emptyView.NoSchedulesVisibility} noSel={emptyView.NoSelectionVisibility}");

                // ---------------- 单条计划 ----------------

                var document = new ScadaDocument();
                var schedule = new ScadaSchedule
                {
                    Name = "早班开机",
                    TimeOfDay = "08:00",
                    Period = ScadaSchedulePeriods.Daily,
                    Days = "1,2,3,4,5",
                };
                schedule.GetOrAddEventHook(ScadaEventType.ScheduleFired)
                    .Actions.Add(new ScadaAction { Type = ScadaActionType.Log, Text = "早班开机完成" });
                document.Schedules.Add(schedule);

                var view = new ScadaScheduleManagerView(document);
                Check("[SV2] 单条计划：自动选中、表单可见、计数「1 条 · 1 启用」、动作 1 行且行号 1",
                    ReferenceEquals(view.SelectedSchedule, schedule)
                    && view.HasSelectionVisibility == Visibility.Visible
                    && view.CountText == "1 条 · 1 启用"
                    && view.Actions.Count == 1
                    && view.Actions[0].Index == 1,
                    $"count={view.CountText} actions={view.Actions.Count} index={(view.Actions.Count > 0 ? view.Actions[0].Index : -1)}");

                // R184：固定高度的输入框，内容区必须容得下整段文字（"08:00" 只显示 "08" 就是裁字）。
                view.Measure(new Size(980, 600));
                view.Arrange(new Rect(0, 0, 980, 600));
                view.UpdateLayout();

                var timeBox = view.FindName("TimeBox") as TextBox;
                timeBox!.ApplyTemplate();
                var timeHost = timeBox.Template.FindName("PART_ContentHost", timeBox) as ScrollViewer;

                Check("[SV2b] 时刻输入框：08:00 不被裁（viewport ≥ extent，R184）",
                    timeHost != null && timeHost.ViewportWidth + 0.5 >= timeHost.ExtentWidth,
                    $"text={timeBox.Text} w={timeBox.ActualWidth:F0} viewport={timeHost?.ViewportWidth:F1} extent={timeHost?.ExtentWidth:F1}");

                Check("[SV3] 时间计划·每天：时间块可见、事件块隐藏、星期与日期都不显示",
                    view.TimePanelVisibility == Visibility.Visible
                    && view.EventPanelVisibility == Visibility.Collapsed
                    && view.WeekPanelVisibility == Visibility.Collapsed
                    && view.DatePanelVisibility == Visibility.Collapsed,
                    $"{view.TimePanelVisibility}/{view.EventPanelVisibility}/{view.WeekPanelVisibility}/{view.DatePanelVisibility}");

                schedule.Kind = ScadaScheduleKinds.Event;
                Check("[SV4] 切事件计划：时间块整体隐藏、事件块出现（不是叠在一起）",
                    view.TimePanelVisibility == Visibility.Collapsed
                    && view.EventPanelVisibility == Visibility.Visible,
                    $"{view.TimePanelVisibility}/{view.EventPanelVisibility}");

                schedule.Kind = ScadaScheduleKinds.Time;
                schedule.Period = ScadaSchedulePeriods.Weekly;
                Check("[SV5] 切每周：星期芯片出现、日期输入隐藏",
                    view.WeekPanelVisibility == Visibility.Visible
                    && view.DatePanelVisibility == Visibility.Collapsed,
                    $"{view.WeekPanelVisibility}/{view.DatePanelVisibility}");

                schedule.Period = ScadaSchedulePeriods.Date;
                Check("[SV6] 切指定日期：日期输入出现、星期隐藏",
                    view.DatePanelVisibility == Visibility.Visible
                    && view.WeekPanelVisibility == Visibility.Collapsed,
                    $"{view.DatePanelVisibility}/{view.WeekPanelVisibility}");

                // ---------------- 星期芯片 ----------------

                schedule.Period = ScadaSchedulePeriods.Weekly;
                schedule.Days = "1,3,5";
                var chips = new[] { "WeekChip1", "WeekChip2", "WeekChip3", "WeekChip4", "WeekChip5", "WeekChip6", "WeekChip7" }
                    .Select(name => view.FindName(name) as ToggleButton)
                    .ToArray();

                Check("[SV7] 芯片回显：Days=1,3,5 时「一/三/五」选中，「二/四」未选",
                    chips.All(chip => chip != null)
                    && chips[0]!.IsChecked == true && chips[2]!.IsChecked == true && chips[4]!.IsChecked == true
                    && chips[1]!.IsChecked == false && chips[3]!.IsChecked == false,
                    string.Join(",", chips.Select(chip => chip?.IsChecked == true ? "1" : "0")));

                chips[1]!.IsChecked = true;
                chips[1]!.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

                Check("[SV8] 点「二」：Days 变成 1,2,3,5（升序、逗号分隔，落盘口径不变）",
                    schedule.Days == "1,2,3,5",
                    schedule.Days);

                // ---------------- 校验 ----------------

                var badDocument = new ScadaDocument();
                var badSchedule = new ScadaSchedule
                {
                    Name = string.Empty,
                    TimeOfDay = "8:0",
                    Period = ScadaSchedulePeriods.Daily,
                };
                badDocument.Schedules.Add(badSchedule);

                var badView = new ScadaScheduleManagerView(badDocument);
                var failed = badView.ValidateAndShow();

                Check("[SV9] 校验失败：返回 false、名称/时刻/动作三处红框、汇总写「有 3 处需要修正」",
                    !failed
                    && badView.NameError && badView.TimeError && badView.ActionsError
                    && badView.NameErrorVisibility == Visibility.Visible
                    && badView.ActionsErrorVisibility == Visibility.Visible
                    && badView.ErrorSummary.StartsWith("有 3 处需要修正", StringComparison.Ordinal),
                    $"failed={failed} summary={badView.ErrorSummary}");

                badSchedule.Name = "补上名字";
                badSchedule.TimeOfDay = "08:00";
                badSchedule.GetOrAddEventHook(ScadaEventType.ScheduleFired)
                    .Actions.Add(new ScadaAction { Type = ScadaActionType.Log, Text = "x" });

                Check("[SV10] 补齐后校验通过、错误旗标与汇总全部清空",
                    badView.ValidateAndShow()
                    && !badView.NameError && !badView.TimeError && !badView.ActionsError
                    && badView.ErrorSummary.Length == 0,
                    badView.ErrorSummary);

                // ---------------- 动作表 ----------------

                var logRow = view.Actions[0];
                Check("[SV11] 日志动作：参数列显示「—」（不是空输入框）",
                    logRow.SecondaryVisibility == Visibility.Collapsed
                    && logRow.SecondaryPlaceholderVisibility == Visibility.Visible,
                    $"{logRow.SecondaryVisibility}/{logRow.SecondaryPlaceholderVisibility}");

                logRow.Type = nameof(ScadaActionType.WriteVariable);
                Check("[SV12] 写变量：参数列切成输入框、「—」隐藏",
                    logRow.SecondaryVisibility == Visibility.Visible
                    && logRow.SecondaryPlaceholderVisibility == Visibility.Collapsed,
                    $"{logRow.SecondaryVisibility}/{logRow.SecondaryPlaceholderVisibility}");

                var addAction = view.FindName("AddActionButton") as Button;
                addAction!.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                addAction.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

                Check("[SV13] 连点两次「添加动作」：模型与界面各 3 条，行号 1/2/3 连续",
                    schedule.Actions.Count == 3
                    && view.Actions.Count == 3
                    && view.Actions[0].Index == 1 && view.Actions[1].Index == 2 && view.Actions[2].Index == 3,
                    $"model={schedule.Actions.Count} view={view.Actions.Count} indexes={string.Join(",", view.Actions.Select(row => row.Index))}");

                var moved = view.MoveActionAt(2, -1);
                Check("[SV14] 第 3 行上移：顺序换成 1/3/2，行号仍连续",
                    moved
                    && view.Actions[0].Index == 1 && view.Actions[1].Index == 2 && view.Actions[2].Index == 3
                    && ReferenceEquals(schedule.Actions[1], view.Actions[1].Source),
                    moved ? "ok" : "move=false");

                // ---------------- 只读态 ----------------

                view.IsReadOnly = true;
                Check("[SV15] 只读态：提示条可见、删除隐藏、表单不可编辑且降透明度",
                    view.ReadOnlyBannerVisibility == Visibility.Visible
                    && view.DeleteVisibility == Visibility.Collapsed
                    && !view.IsEditable
                    && view.FormOpacity < 1.0,
                    $"{view.ReadOnlyBannerVisibility}/{view.DeleteVisibility}/{view.IsEditable}/{view.FormOpacity}");
                view.IsReadOnly = false;

                // ---------------- 预览与计数 ----------------

                schedule.Period = ScadaSchedulePeriods.Daily;
                schedule.TimeOfDay = "08:00";
                var timePreview = view.NextTriggerText;

                schedule.Kind = ScadaScheduleKinds.Event;
                schedule.Condition = ScadaScheduleConditions.IsTrue;
                schedule.DurationSeconds = 10;
                var eventPreview = view.NextTriggerText;

                Check("[SV16] 预览：时间计划给「HH:mm（周X）」，事件计划给「持续 N 秒」",
                    timePreview.Contains("08:00") && timePreview.Contains("（周")
                    && eventPreview.Contains("10 秒") && view.NextTriggerLabel == "执行时机",
                    $"{timePreview} / {eventPreview}");

                schedule.IsEnabled = false;
                Check("[SV17] 停用一条：计数立刻变「1 条 · 0 启用」",
                    view.CountText == "1 条 · 0 启用",
                    view.CountText);

                // ---------------- 删除回空态 ----------------

                var deleteDocument = new ScadaDocument();
                deleteDocument.Schedules.Add(new ScadaSchedule { Name = "只有一条", TimeOfDay = "09:00" });
                var deleteView = new ScadaScheduleManagerView(deleteDocument);
                (deleteView.FindName("DeleteButton") as Button)!.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

                Check("[SV18] 删掉最后一条：回到空列表态、右列空选中、计数 0 条",
                    deleteDocument.Schedules.Count == 0
                    && deleteView.SelectedSchedule == null
                    && deleteView.NoSchedulesVisibility == Visibility.Visible
                    && deleteView.NoSelectionVisibility == Visibility.Visible
                    && deleteView.CountText == "0 条",
                    $"count={deleteView.CountText}");

                // ---------------- 真实渲染（证据） ----------------

                RenderStates();
            }

            /// <summary>把五种状态渲染成 PNG（落在 artifacts\render\&lt;yyyyMMdd&gt;\scada-schedule\）。</summary>
            private static void RenderStates()
            {
                var root = FindRepoRoot();
                if (root == null)
                {
                    Check("[SV19] 渲染证据：找得到仓库根目录", false, "从输出目录向上没找到 AGENTS.md");
                    return;
                }

                var dir = EvidencePaths.Dir(root, "scada-schedule");

                var timeView = BuildTimeView();
                Render(timeView, Path.Combine(dir, "schedule-v2-default.png"), 900, 500);

                var eventView = BuildTimeView();
                var eventSchedule = eventView.SelectedSchedule!;
                eventSchedule.Kind = ScadaScheduleKinds.Event;
                eventSchedule.VariableName = "液位_1#罐";
                eventSchedule.Condition = ScadaScheduleConditions.AboveLimit;
                eventSchedule.UpperLimit = 90;
                eventSchedule.DurationSeconds = 10;
                Render(eventView, Path.Combine(dir, "schedule-v2-event.png"), 900, 500);

                var invalidView = new ScadaScheduleManagerView(new ScadaDocument());
                (invalidView.FindName("AddScheduleButton") as Button)!.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                var invalidSchedule = invalidView.SelectedSchedule!;
                invalidSchedule.Name = string.Empty;
                invalidSchedule.TimeOfDay = "8:0";
                invalidSchedule.FindEventHook(ScadaEventType.ScheduleFired)!.Actions.Clear();

                var changedDuringRender = new List<string>();
                invalidView.PropertyChanged += (_, e) => changedDuringRender.Add(e.PropertyName ?? string.Empty);
                var modelChangedDuringRender = new List<string>();
                invalidSchedule.PropertyChanged += (_, e) => modelChangedDuringRender.Add(e.PropertyName ?? string.Empty);

                var invalidOk = invalidView.ValidateAndShow();
                changedDuringRender.Clear();
                modelChangedDuringRender.Clear();
                Render(invalidView, Path.Combine(dir, "schedule-v2-invalid.png"), 900, 500);

                Check("[SV20] 校验态：ValidateAndShow=false，且渲染后三处错误仍在（红框/空动作块/汇总都能看见）",
                    !invalidOk && invalidView.NameError && invalidView.TimeError && invalidView.ActionsError
                    && invalidView.ErrorVisibility == Visibility.Visible
                    && invalidView.ActionsErrorVisibility == Visibility.Visible,
                    $"ok={invalidOk} name={invalidView.NameError} time={invalidView.TimeError} actions={invalidView.ActionsError}"
                    + $" summary={invalidView.ErrorSummary} modelChanged={string.Join(",", modelChangedDuringRender.Distinct())}");

                var emptyView = new ScadaScheduleManagerView(new ScadaDocument());
                Render(emptyView, Path.Combine(dir, "schedule-v2-empty.png"), 900, 500);

                var readOnlyView = BuildTimeView();
                readOnlyView.IsReadOnly = true;
                Render(readOnlyView, Path.Combine(dir, "schedule-v2-readonly.png"), 900, 500);
            }

            private static ScadaScheduleManagerView BuildTimeView()
            {
                var document = new ScadaDocument();
                var schedule = new ScadaSchedule
                {
                    Name = "早班开机",
                    TimeOfDay = "08:00",
                    Period = ScadaSchedulePeriods.Daily,
                    Days = "1,2,3,4,5",
                };

                var hook = schedule.GetOrAddEventHook(ScadaEventType.ScheduleFired);
                hook.Actions.Add(new ScadaAction { Type = ScadaActionType.Navigate, TargetPageName = "运行总览" });
                hook.Actions.Add(new ScadaAction { Type = ScadaActionType.WriteVariable, VariableName = "班次", Value = "白班" });
                hook.Actions.Add(new ScadaAction { Type = ScadaActionType.Log, Text = "早班开机完成" });

                document.Schedules.Add(schedule);
                document.Schedules.Add(new ScadaSchedule
                {
                    Name = "液位高停泵",
                    Kind = ScadaScheduleKinds.Event,
                    VariableName = "液位_1#罐",
                    Condition = ScadaScheduleConditions.AboveLimit,
                    UpperLimit = 90,
                    DurationSeconds = 10,
                });
                document.Schedules.Add(new ScadaSchedule
                {
                    Name = "夜间停线",
                    Period = ScadaSchedulePeriods.Date,
                    DateText = "2026-11-01",
                    TimeOfDay = "23:00",
                    IsEnabled = false,
                });

                return new ScadaScheduleManagerView(document);
            }

            /// <summary>离屏真窗口渲染（R53：Left=-4000 + STA + Dispatcher 泵），不抢焦点。</summary>
            private static void Render(FrameworkElement view, string path, int minWidth, int minHeight)
            {
                Window? window = null;
                try
                {
                    window = new Window
                    {
                        WindowStyle = WindowStyle.None,
                        ShowInTaskbar = false,
                        ShowActivated = false,
                        ResizeMode = ResizeMode.NoResize,
                        Background = Brushes.White,
                        Left = -4000,
                        Top = -4000,
                        SizeToContent = SizeToContent.WidthAndHeight,
                        Content = view,
                    };

                    window.Show();
                    Pump();

                    var width = (int)Math.Ceiling(view.ActualWidth);
                    var height = (int)Math.Ceiling(view.ActualHeight);

                    var bitmap = new RenderTargetBitmap(Math.Max(width, 1), Math.Max(height, 1), 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(view);
                    bitmap.Freeze();

                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using (var stream = File.Create(path))
                        encoder.Save(stream);

                    Check($"[SV19] 渲染 {Path.GetFileName(path)}（{width}×{height}）",
                        width >= minWidth && height >= minHeight,
                        $"{width}×{height}");
                }
                catch (Exception ex)
                {
                    Check($"[SV19] 渲染 {Path.GetFileName(path)}", false, ex.Message);
                }
                finally
                {
                    try { window?.Close(); } catch { /* 关窗失败不影响断言结论 */ }
                    Pump();
                }
            }

            private static void Pump()
            {
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(
                    DispatcherPriority.ApplicationIdle,
                    new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
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
                    Check("计划表视图断言全程未抛异常", false, error.ToString());
            }
        }
    }
}
