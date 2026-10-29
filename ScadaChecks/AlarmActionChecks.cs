using System;
using System.Collections.Generic;
using VisionMaster.Scada;
using VisionMaster.Services;

namespace ScadaChecks
{
    internal partial class Program
    {
        /// <summary>
        /// [ALM] 报警两条动作（v7）：确认报警（<see cref="ScadaActionType.AcknowledgeAlarm"/>）与
        /// 静音（<see cref="ScadaActionType.MuteAlarm"/>）。
        ///
        /// 两条都是"接线判断 + 结果翻译"：分发器不认识引擎（那是宿主的
        /// <see cref="IScadaAlarmCommands"/> 出口），也不认识会话里的声音服务
        /// （<see cref="IScadaAlarmMute"/>）。断言要钉住的正是这条分工：
        /// · 没接线时<b>如实跳过</b>并说清是哪一档没接（不是静默空跑）；
        /// · 接上线时把出口的结果（条数 / 失败原因 / 实际静音分钟数）一字不差地落到日志上；
        /// · <b>"没有待确认的报警"必须记 Info</b>——它是正常结果，记成 Error 会让现场去查一件没坏的事。
        /// </summary>
        private static class AlarmActionChecks
        {
            public static void Run() => RunChecks();

            private static void RunChecks()
            {
                Section("报警动作（确认报警 / 静音）");

                // ---------------- ① 枚举与文案：面板、日志、审计三处同源 ----------------
                Check("[ALM1] 枚举末尾追加 AcknowledgeAlarm=33 / MuteAlarm=34，展示名「确认报警」「静音」，都已接通且不算危险动作",
                    (int)ScadaActionType.AcknowledgeAlarm == 33
                    && (int)ScadaActionType.MuteAlarm == 34
                    && ScadaActionType.AcknowledgeAlarm.DisplayName() == "确认报警"
                    && ScadaActionType.MuteAlarm.DisplayName() == "静音"
                    && ScadaActionType.AcknowledgeAlarm.PendingReason() == null
                    && ScadaActionType.MuteAlarm.PendingReason() == null
                    && !ScadaActionType.AcknowledgeAlarm.IsDangerous()
                    && !ScadaActionType.MuteAlarm.IsDangerous(),
                    $"value={(int)ScadaActionType.AcknowledgeAlarm}/{(int)ScadaActionType.MuteAlarm}"
                    + $" / name={ScadaActionType.AcknowledgeAlarm.DisplayName()}/{ScadaActionType.MuteAlarm.DisplayName()}");

                var ackRow = new ScadaAction { Type = ScadaActionType.AcknowledgeAlarm };
                var muteRow = new ScadaAction { Type = ScadaActionType.MuteAlarm, MuteMinutes = 30 };
                var muteBlankRow = new ScadaAction { Type = ScadaActionType.MuteAlarm };
                Check("[ALM2] 行内摘要与描述同源：确认报警无参数；静音带分钟数，没填时 Describe 明说「按默认」",
                    ackRow.Detail == string.Empty
                    && ackRow.Describe() == "确认全部待确认报警"
                    && muteRow.Detail == "30 分钟"
                    && muteRow.Describe() == "静音 30 分钟"
                    && muteBlankRow.Detail == string.Empty
                    && muteBlankRow.Describe().Contains("按默认", StringComparison.Ordinal),
                    $"{ackRow.Describe()} / {muteRow.Describe()} / {muteBlankRow.Describe()}");

                // ---------------- ② 确认报警：未接线 / 有条数 / 0 条 / 失败 ----------------
                // 未接线档两个出口都传 null（真的"宿主没装配"）。别塞一个假出口进去——
                // 那样测的是"接上了的路径"，[ALM4] 已经在测它了。
                var unwiredAckLog = Dispatch(null, null, new ScadaAction { Type = ScadaActionType.AcknowledgeAlarm });
                Check("[ALM3] 确认报警：没接报警命令出口 → Warn 一行「未接入报警命令出口」，不假装成功",
                    unwiredAckLog.Lines.Count == 1
                    && unwiredAckLog.Lines[0].StartsWith("W ", StringComparison.Ordinal)
                    && unwiredAckLog.Lines[0].Contains("未接入报警命令出口", StringComparison.Ordinal),
                    unwiredAckLog.Lines.Count == 1 ? unwiredAckLog.Lines[0] : "no log");

                var ackThree = new FakeAlarmCommands { AcknowledgeAllResult = 3 };
                var ackThreeLog = Dispatch(ackThree, null, new ScadaAction { Type = ScadaActionType.AcknowledgeAlarm });
                Check("[ALM4] 确认报警：出口回了 3 条 → Info「确认了 3 条报警」，条数照出口给的说",
                    ackThree.AcknowledgeAllCalls == 1
                    && ackThreeLog.Lines.Count == 1
                    && ackThreeLog.Lines[0].StartsWith("I ", StringComparison.Ordinal)
                    && ackThreeLog.Lines[0].Contains("确认了 3 条", StringComparison.Ordinal),
                    ackThreeLog.Lines.Count == 1 ? ackThreeLog.Lines[0] : "no log");

                var ackNone = new FakeAlarmCommands { AcknowledgeAllResult = 0 };
                var ackNoneLog = Dispatch(ackNone, null, new ScadaAction { Type = ScadaActionType.AcknowledgeAlarm });
                Check("[ALM5] 确认报警：出口回 0 条（本来就没有待确认）→ 记 Info「没有待确认的报警」，不是 Error、不是 Warn",
                    ackNoneLog.Lines.Count == 1
                    && ackNoneLog.Lines[0].StartsWith("I ", StringComparison.Ordinal)
                    && ackNoneLog.Lines[0].Contains("没有待确认", StringComparison.Ordinal),
                    ackNoneLog.Lines.Count == 1 ? ackNoneLog.Lines[0] : "no log");

                var ackBroken = new FakeAlarmCommands { Error = "运行态未启动，无法确认报警" };
                var ackBrokenLog = Dispatch(ackBroken, null, new ScadaAction { Type = ScadaActionType.AcknowledgeAlarm });
                Check("[ALM6] 确认报警：出口给了原因 → Error 原样带上那句原因，不加工、不吞",
                    ackBrokenLog.Lines.Count == 1
                    && ackBrokenLog.Lines[0].StartsWith("E ", StringComparison.Ordinal)
                    && ackBrokenLog.Lines[0].Contains("运行态未启动，无法确认报警", StringComparison.Ordinal),
                    ackBrokenLog.Lines.Count == 1 ? ackBrokenLog.Lines[0] : "no log");

                // ---------------- ③ 静音：未接线 / 成功 / 默认时长 / 失败 ----------------
                var unwiredMuteLog = Dispatch(null, null, new ScadaAction { Type = ScadaActionType.MuteAlarm });
                Check("[ALM7] 静音：没接静音出口 → Warn 一行「未接入静音出口」，不假装静音了",
                    unwiredMuteLog.Lines.Count == 1
                    && unwiredMuteLog.Lines[0].StartsWith("W ", StringComparison.Ordinal)
                    && unwiredMuteLog.Lines[0].Contains("未接入静音出口", StringComparison.Ordinal),
                    unwiredMuteLog.Lines.Count == 1 ? unwiredMuteLog.Lines[0] : "no log");

                var mute30 = new FakeAlarmMute();
                var mute30Log = Dispatch(null, mute30, new ScadaAction { Type = ScadaActionType.MuteAlarm, MuteMinutes = 30 });
                Check("[ALM8] 静音：填了 30 分钟 → 出口收到 30，日志写清分钟数与「紧急报警不受静音约束」",
                    mute30.Requested.Count == 1
                    && mute30.Requested[0] == 30
                    && mute30Log.Lines.Count == 1
                    && mute30Log.Lines[0].StartsWith("I ", StringComparison.Ordinal)
                    && mute30Log.Lines[0].Contains("已静音 30 分钟", StringComparison.Ordinal)
                    && mute30Log.Lines[0].Contains("紧急报警不受静音约束", StringComparison.Ordinal),
                    mute30Log.Lines.Count == 1 ? mute30Log.Lines[0] : "no log");

                // 没填 / 0 / 负数三档都按默认 5 分钟，而且日志必须写明"按默认"——
                // 否则现场看到"我没填它却静音了"，会当成一次故障去查。
                var blankCases = new (string Label, int? Value)[]
                {
                    ("留空", null),
                    ("填 0", 0),
                    ("填 -3", -3),
                };

                var blankOk = true;
                var blankDetail = string.Empty;
                foreach (var (label, value) in blankCases)
                {
                    var fake = new FakeAlarmMute();
                    var log = Dispatch(null, fake, new ScadaAction { Type = ScadaActionType.MuteAlarm, MuteMinutes = value });
                    var pass = fake.Requested.Count == 1
                               && fake.Requested[0] == 5
                               && log.Lines.Count == 1
                               && log.Lines[0].StartsWith("I ", StringComparison.Ordinal)
                               && log.Lines[0].Contains("已静音 5 分钟", StringComparison.Ordinal)
                               && log.Lines[0].Contains("按默认", StringComparison.Ordinal);

                    if (!pass)
                    {
                        blankOk = false;
                        blankDetail = $"{label}: requested={string.Join("/", fake.Requested)} / "
                                      + (log.Lines.Count == 1 ? log.Lines[0] : "no log");
                        break;
                    }
                }

                Check("[ALM9] 静音：时长留空 / 0 / 负数一律按默认 5 分钟，且日志写明「按默认」",
                    blankOk, blankDetail);

                var muteBroken = new FakeAlarmMute { Error = "当前有紧急级未确认报警，不允许静音" };
                var muteBrokenLog = Dispatch(null, muteBroken,
                    new ScadaAction { Type = ScadaActionType.MuteAlarm, MuteMinutes = 10 });
                Check("[ALM10] 静音：出口拒绝（典型是紧急级未确认报警）→ Error 原样带上原因",
                    muteBrokenLog.Lines.Count == 1
                    && muteBrokenLog.Lines[0].StartsWith("E ", StringComparison.Ordinal)
                    && muteBrokenLog.Lines[0].Contains("当前有紧急级未确认报警，不允许静音", StringComparison.Ordinal),
                    muteBrokenLog.Lines.Count == 1 ? muteBrokenLog.Lines[0] : "no log");

                // 两条出口互不影响：只接静音出口时，确认报警仍按"未接入"跳过（反之亦然）。
                // 参数位置错开过一次就会串味，这条专门钉住。
                var crossWired = new FakeAlarmMute();
                var crossLog = Dispatch(null, crossWired, new ScadaAction { Type = ScadaActionType.AcknowledgeAlarm });
                Check("[ALM11] 两个出口各认各的：只接静音出口时，「确认报警」仍按未接入跳过，不会误调静音出口",
                    crossWired.Requested.Count == 0
                    && crossLog.Lines.Count == 1
                    && crossLog.Lines[0].StartsWith("W ", StringComparison.Ordinal)
                    && crossLog.Lines[0].Contains("未接入报警命令出口", StringComparison.Ordinal),
                    crossLog.Lines.Count == 1 ? crossLog.Lines[0] : "no log");
            }

            /// <summary>
            /// 造一台分发器 + 日志桩，跑单条动作。
            /// 两个出口按<b>命名参数</b>传：它们都是可选参数，位置传参一旦错开就会把静音出口
            /// 传成报警出口（症状是"确认报警把报警静音了"，而编译期完全看不出来）。
            /// </summary>
            private static LoggerStub Dispatch(
                FakeAlarmCommands? commands,
                FakeAlarmMute? mute,
                ScadaAction action)
            {
                var log = new LoggerStub();
                var hook = new ScadaEventHook { Event = ScadaEventType.Pressed };
                hook.Actions.Add(action);

                new ScadaActionDispatcher(
                    log,
                    new FakeValueSource(),
                    new FakeNavigator(),
                    alarmCommands: commands,
                    alarmMute: mute).Dispatch(hook, "按钮");

                return log;
            }

            /// <summary>假的报警命令出口：只回答"确认全部回了多少条 / 有没有原因"，并记账调用次数。</summary>
            private sealed class FakeAlarmCommands : IScadaAlarmCommands
            {
                public int AcknowledgeAllResult { get; set; }

                public string? Error { get; set; }

                public int AcknowledgeAllCalls { get; private set; }

                public bool TryAcknowledge(Guid alarmId, out string? error)
                {
                    error = Error;
                    return string.IsNullOrEmpty(Error);
                }

                public int AcknowledgeAll(out string? error)
                {
                    AcknowledgeAllCalls++;
                    error = Error;
                    return AcknowledgeAllResult;
                }
            }

            /// <summary>假的静音出口：把每次请求的分钟数原样记下来（"到底传了几分钟"是本组断言的核心）。</summary>
            private sealed class FakeAlarmMute : IScadaAlarmMute
            {
                public bool IsMuted { get; set; }

                public int MuteMinutes { get; set; } = 5;

                public string? Error { get; set; }

                public List<int> Requested { get; } = new();

                public bool TryMute(int minutes, out string? error)
                {
                    Requested.Add(minutes);
                    error = Error;
                    return string.IsNullOrEmpty(Error);
                }

                public bool TryUnmute(out string? error)
                {
                    error = null;
                    return true;
                }
            }
        }
    }
}
