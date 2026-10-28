using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using VisionMaster.Scada;
using VisionMaster.Services;

namespace ScadaChecks
{
    internal partial class Program
    {
        /// <summary>
        /// [SC] 计划表：时间算术 / 模型落盘 / 执行器（到点只发一次、持续判定、条件中断重计时）。
        ///
        /// 引擎的时间来源是 <c>Tick(DateTime now)</c> 的参数（不是 <c>DateTime.Now</c>），
        /// 所以这里能把"昨天 08:00 / 今天 08:00 / 明天 08:00"逐拍喂进去，不必等真钟。
        /// </summary>
        private static class ScheduleChecks
        {
            public static void Run() => RunOnSta(RunChecks);

            private static void RunChecks()
            {
                Section("[SC] 计划表：时间算术 / 落盘 / 到点与持续判定");

                // ---------------- 时间算术 ----------------

                Check("[SC1] 时刻解析：HH:mm 认、非法回落 false、秒被丢弃、单数字分钟（8:0 / 8:5）拒绝",
                    ScadaScheduleClock.TryParseTimeOfDay("08:00", out var eight) && eight == TimeSpan.FromHours(8)
                    && ScadaScheduleClock.TryParseTimeOfDay("20:30:45", out var half) && half == new TimeSpan(20, 30, 0)
                    && !ScadaScheduleClock.TryParseTimeOfDay("25:00", out _)
                    && !ScadaScheduleClock.TryParseTimeOfDay("abc", out _)
                    && !ScadaScheduleClock.TryParseTimeOfDay("8:0", out _)
                    && !ScadaScheduleClock.TryParseTimeOfDay("8:5", out _),
                    eight.ToString());

                var days = ScadaScheduleClock.ParseDays("1,3,5,9,x");
                Check("[SC2] 星期解析：ISO 口径（1=周一…7=周日），越界/垃圾项丢弃",
                    days.Count == 3 && days[0] == DayOfWeek.Monday && days[2] == DayOfWeek.Friday
                    && ScadaScheduleClock.ToIsoDay(DayOfWeek.Sunday) == 7,
                    string.Join(",", days));

                var daily = new ScadaSchedule { TimeOfDay = "08:00", Period = ScadaSchedulePeriods.Daily };
                bool dailyOk = ScadaScheduleClock.TryNextOccurrence(daily, new DateTime(2026, 10, 25, 7, 0, 0), out var dailyNext);

                var weekly = new ScadaSchedule
                {
                    TimeOfDay = "08:00",
                    Period = ScadaSchedulePeriods.Weekly,
                    Days = "1,3,5",
                };
                // 2026-10-27 是周二 → 下一次应是周三 10-28 08:00
                bool weeklyOk = ScadaScheduleClock.TryNextOccurrence(weekly, new DateTime(2026, 10, 27, 9, 0, 0), out var weeklyNext);

                var dated = new ScadaSchedule
                {
                    TimeOfDay = "08:00",
                    Period = ScadaSchedulePeriods.Date,
                    DateText = "2026-11-01",
                };
                bool datedOk = ScadaScheduleClock.TryNextOccurrence(dated, new DateTime(2026, 10, 25, 9, 0, 0), out var datedNext);
                bool datedPast = ScadaScheduleClock.TryNextOccurrence(dated, new DateTime(2026, 11, 2, 0, 0, 0), out _);

                Check("[SC3] 下一次触发：每天/每周/指定日期 三条都对；指定日期过了就不再触发",
                    dailyOk && dailyNext == new DateTime(2026, 10, 25, 8, 0, 0)
                    && weeklyOk && weeklyNext == new DateTime(2026, 10, 28, 8, 0, 0)
                    && datedOk && datedNext == new DateTime(2026, 11, 1, 8, 0, 0)
                    && !datedPast,
                    $"{dailyNext:MM-dd HH:mm} / {weeklyNext:MM-dd HH:mm} / {datedNext:MM-dd HH:mm}");

                bool dueNow = ScadaScheduleClock.TryDueOccurrence(
                    daily, new DateTime(2026, 10, 25, 8, 0, 30), TimeSpan.FromMinutes(1), out var due);
                bool dueMissed = ScadaScheduleClock.TryDueOccurrence(
                    daily, new DateTime(2026, 10, 25, 8, 5, 0), TimeSpan.FromMinutes(1), out _);

                Check("[SC4] 到点窗口 1 分钟：08:00:30 命中、08:05 已错过（错过不补）",
                    dueNow && due == new DateTime(2026, 10, 25, 8, 0, 0) && !dueMissed,
                    $"{due:HH:mm:ss} / missed={dueMissed}");

                Check("[SC5] 摘要文案：每天/每周/事件三种读法一致（列表与日志共用一句）",
                    new ScadaSchedule { Period = ScadaSchedulePeriods.Daily, TimeOfDay = "08:00" }.Describe == "每天 08:00"
                    && new ScadaSchedule { Period = ScadaSchedulePeriods.Weekly, Days = "1,3", TimeOfDay = "20:00" }.Describe == "每周一、三 20:00"
                    && new ScadaSchedule
                    {
                        Kind = ScadaScheduleKinds.Event,
                        VariableName = "T101.液位",
                        Condition = ScadaScheduleConditions.AboveLimit,
                        DurationSeconds = 600,
                    }.Describe == "T101.液位 高于上限 持续 600 秒",
                    "");

                // ---------------- 落盘 ----------------

                var document = new ScadaDocument();
                document.Schedules.Add(new ScadaSchedule
                {
                    Name = "白班开班",
                    TimeOfDay = "08:00",
                    Period = ScadaSchedulePeriods.Daily,
                });
                document.Schedules[0].GetOrAddEventHook(ScadaEventType.ScheduleFired)
                    .Actions.Add(new ScadaAction { Type = ScadaActionType.Log, Text = "白班开班" });

                var beforeActions = document.Schedules[0].Actions.Count;
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(document);
                var restored = Newtonsoft.Json.JsonConvert.DeserializeObject<ScadaDocument>(json);

                Check("[SC6] 落盘往返：计划与它的动作都回来（走 .vms 同一套序列化）",
                    restored != null
                    && restored.Schedules.Count == 1
                    && restored.Schedules[0].Name == "白班开班"
                    && restored.Schedules[0].Actions.Count == 1
                    && restored.Schedules[0].Actions[0].Text == "白班开班",
                    restored == null
                        ? "null"
                        : $"before={beforeActions} / {restored.Schedules.Count} 条"
                          + $" / hooks={restored.Schedules[0].EventHooks.Count}"
                          + $" / actions={restored.Schedules[0].Actions.Count}"
                          + $" / text={(restored.Schedules[0].Actions.Count > 0 ? restored.Schedules[0].Actions[0].Text : "-")}");

                // ---------------- 执行器 ----------------

                var dispatcher = new FakeScheduleDispatcher();
                var log = new LoggerStub();

                var timeDocument = new ScadaDocument();
                var timeSchedule = new ScadaSchedule { Name = "到点", TimeOfDay = "08:00", Period = ScadaSchedulePeriods.Daily };
                timeSchedule.GetOrAddEventHook(ScadaEventType.ScheduleFired)
                    .Actions.Add(new ScadaAction { Type = ScadaActionType.Log, Text = "到点" });
                timeDocument.Schedules.Add(timeSchedule);

                var engine = new ScadaScheduleEngine(timeDocument, new FakeValueSource(), dispatcher, log);
                engine.Attach();

                engine.Tick(new DateTime(2026, 10, 25, 7, 59, 0));
                int beforeDue = dispatcher.Calls;

                engine.Tick(new DateTime(2026, 10, 25, 8, 0, 1));
                int afterDue = dispatcher.Calls;

                engine.Tick(new DateTime(2026, 10, 25, 8, 0, 2));
                int afterSecond = dispatcher.Calls;

                engine.Tick(new DateTime(2026, 10, 26, 8, 0, 1));
                int nextDay = dispatcher.Calls;

                Check("[SC7] 到点：08:00 前不响、到点响一次、同一分钟不重发、第二天再响",
                    beforeDue == 0 && afterDue == 1 && afterSecond == 1 && nextDay == 2,
                    $"{beforeDue}/{afterDue}/{afterSecond}/{nextDay}");

                Check("[SC8] 触发的署名是「系统（计划表）」，来源带计划名（日志与审计要认得出是谁）",
                    dispatcher.LastActor == "系统（计划表）" && dispatcher.LastSource == "计划表：到点",
                    $"{dispatcher.LastActor} / {dispatcher.LastSource}");

                timeSchedule.IsEnabled = false;
                engine.Tick(new DateTime(2026, 10, 27, 8, 0, 1));
                Check("[SC9] 停用的计划不执行（配置保留）",
                    dispatcher.Calls == 2 && engine.EnabledCount == 0,
                    $"{dispatcher.Calls} / enabled={engine.EnabledCount}");

                // 事件计划：持续 10 秒
                var eventDocument = new ScadaDocument();
                var values = new FakeValueSource();
                var handle = new FakeValueHandle { VariableId = Guid.NewGuid(), Name = "T101.液位" };
                values.ById[handle.VariableId] = handle;

                var eventSchedule = new ScadaSchedule
                {
                    Name = "液位高",
                    Kind = ScadaScheduleKinds.Event,
                    VariableId = handle.VariableId,
                    VariableName = handle.Name,
                    Condition = ScadaScheduleConditions.IsTrue,
                    DurationSeconds = 10,
                };
                eventSchedule.GetOrAddEventHook(ScadaEventType.ScheduleFired)
                    .Actions.Add(new ScadaAction { Type = ScadaActionType.Log, Text = "液位高" });
                eventDocument.Schedules.Add(eventSchedule);

                var eventDispatcher = new FakeScheduleDispatcher();
                var eventEngine = new ScadaScheduleEngine(eventDocument, values, eventDispatcher, log);
                eventEngine.Attach();

                handle.Value = true;
                eventEngine.Tick(new DateTime(2026, 10, 25, 9, 0, 0));    // 起计时
                eventEngine.Tick(new DateTime(2026, 10, 25, 9, 0, 5));    // 5 秒，未到
                int beforeDuration = eventDispatcher.Calls;
                eventEngine.Tick(new DateTime(2026, 10, 25, 9, 0, 11));   // 11 秒，到
                int afterDuration = eventDispatcher.Calls;
                eventEngine.Tick(new DateTime(2026, 10, 25, 9, 0, 20));   // 仍在持续，不重发
                int afterHold = eventDispatcher.Calls;

                Check("[SC10] 事件计划：持续 10 秒才触发，之后保持不重发",
                    beforeDuration == 0 && afterDuration == 1 && afterHold == 1,
                    $"{beforeDuration}/{afterDuration}/{afterHold}");

                handle.Value = false;
                eventEngine.Tick(new DateTime(2026, 10, 25, 9, 0, 21));
                handle.Value = true;
                eventEngine.Tick(new DateTime(2026, 10, 25, 9, 0, 22));   // 重新起计时
                eventEngine.Tick(new DateTime(2026, 10, 25, 9, 0, 30));   // 8 秒，未到
                int restarted = eventDispatcher.Calls;

                Check("[SC11] 条件中断即重新计时（不累计）：断开再满足 8 秒时不触发",
                    restarted == 1,
                    restarted.ToString());

                eventSchedule.Condition = ScadaScheduleConditions.Changed;
                eventEngine.Tick(new DateTime(2026, 10, 25, 9, 0, 31));
                handle.Value = 42;
                eventEngine.Tick(new DateTime(2026, 10, 25, 9, 0, 32));

                Check("[SC12] 「值改变」不等持续：值一变就触发",
                    eventDispatcher.Calls == 2,
                    eventDispatcher.Calls.ToString());

                eventSchedule.Condition = ScadaScheduleConditions.AboveLimit;
                eventSchedule.UpperLimit = 50;
                handle.Value = 60;
                eventEngine.Tick(new DateTime(2026, 10, 25, 9, 0, 33));
                eventEngine.Tick(new DateTime(2026, 10, 25, 9, 0, 44));

                Check("[SC13] 「高于上限」按阈值判：60 > 50 且持续够 → 触发",
                    eventDispatcher.Calls == 3,
                    eventDispatcher.Calls.ToString());

                // 没配动作：不调用分发器，但事件照发（"配了却没动作"要能在日志里看出来）
                var emptyDocument = new ScadaDocument();
                emptyDocument.Schedules.Add(new ScadaSchedule { Name = "空计划", TimeOfDay = "08:00" });
                var emptyDispatcher = new FakeScheduleDispatcher();
                var emptyEngine = new ScadaScheduleEngine(emptyDocument, new FakeValueSource(), emptyDispatcher, log);
                int fired = 0;
                emptyEngine.Fired += (_, _) => fired++;
                emptyEngine.Attach();
                emptyEngine.Tick(new DateTime(2026, 10, 25, 8, 0, 1));

                Check("[SC14] 没配动作：不调用分发器，但触发事件照发（不静默）",
                    emptyDispatcher.Calls == 0 && fired == 1,
                    $"dispatcher={emptyDispatcher.Calls} fired={fired}");

                // 节流：同一秒内连推两拍，只评估一次
                var throttleDocument = new ScadaDocument();
                throttleDocument.Schedules.Add(new ScadaSchedule { Name = "节流", TimeOfDay = "08:00" });
                throttleDocument.Schedules[0].GetOrAddEventHook(ScadaEventType.ScheduleFired)
                    .Actions.Add(new ScadaAction { Type = ScadaActionType.Log, Text = "x" });
                var throttleDispatcher = new FakeScheduleDispatcher();
                var throttleEngine = new ScadaScheduleEngine(throttleDocument, new FakeValueSource(), throttleDispatcher, log);
                throttleEngine.Attach();
                throttleEngine.Tick(new DateTime(2026, 10, 25, 8, 0, 0, 100));
                throttleEngine.Tick(new DateTime(2026, 10, 25, 8, 0, 0, 300));

                Check("[SC15] 评估节流 1 秒：同一秒内连推两拍只算一次（100ms 节拍不会把计划表跑成 10Hz）",
                    throttleDispatcher.Calls == 1,
                    throttleDispatcher.Calls.ToString());
            }

            /// <summary>假分发器：只数调用次数与最后来源，不执行任何动作</summary>
            private sealed class FakeScheduleDispatcher : IScadaActionDispatcher
            {
                public int Calls { get; private set; }

                public string? LastSource { get; private set; }

                public string? LastActor { get; private set; }

                public void Dispatch(ScadaEventHook? hook, string? sourceName, string? operatorName = null)
                {
                    Calls++;
                    LastSource = sourceName;
                    LastActor = operatorName;
                }

                public ScadaActionDispatchResult DispatchWithResult(
                    ScadaEventHook? hook, string? sourceName, string? operatorName = null)
                {
                    Dispatch(hook, sourceName, operatorName);
                    var count = hook?.Actions.Count ?? 0;
                    return new ScadaActionDispatchResult(count, count, 0, 0);
                }
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
                    Check("计划表断言全程未抛异常", false, error.ToString());
            }
        }
    }
}
