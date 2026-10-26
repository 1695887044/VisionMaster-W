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
        /// [AN] 语音播报：去重 / 级别过滤 / 抢占 / 队列上限 / 与提示音互斥。
        ///
        /// 用假语音引擎（可阻塞）验证队列行为——真 `SpeechSynthesizer` 在断言宿主里既没声卡也可能没语音包，
        /// 而"十台设备一起报会不会糊成一片"恰恰只能在没有声音的情况下验证。
        /// </summary>
        private static class AnnouncerChecks
        {
            public static void Run() => RunOnSta(RunChecks);

            private static void RunChecks()
            {
                Section("[AN] 语音播报：去重 / 级别 / 抢占 / 队列 / 提示音让路");

                // ---------------- 去重 ----------------

                var engine = new FakeSpeechEngine();
                using (var announcer = new ScadaAnnouncerService(engine))
                {
                    bool first = announcer.Announce("警告，一号罐液位高", ScadaAlarmSeverity.Warning, "alarm-1");
                    bool second = announcer.Announce("警告，一号罐液位高", ScadaAlarmSeverity.Warning, "alarm-1");

                    WaitUntil(() => engine.Spoken.Count >= 1, 2000);
                    Thread.Sleep(120);

                    Check("[AN1] 同一个报警 Id 在 30 秒内只播一次（AlarmChanged 会反复来）",
                        first && !second && engine.Spoken.Count == 1,
                        $"first={first} second={second} spoken={engine.Spoken.Count}");
                }

                // ---------------- 级别过滤 / 总开关 / 无语音包 ----------------

                var quietEngine = new FakeSpeechEngine();
                using (var announcer = new ScadaAnnouncerService(quietEngine))
                {
                    bool info = announcer.Announce("提示，设备就绪", ScadaAlarmSeverity.Info);
                    announcer.Enabled = false;
                    bool disabled = announcer.Announce("警告，测试", ScadaAlarmSeverity.Warning);
                    Thread.Sleep(120);

                    Check("[AN2] 低于门槛（Info）不播；总开关关掉后一律不播",
                        !info && !disabled && quietEngine.Spoken.Count == 0,
                        $"info={info} disabled={disabled} spoken={quietEngine.Spoken.Count}");
                }

                var muteEngine = new FakeSpeechEngine { Voices = Array.Empty<string>() };
                var muteLog = new LoggerStub();
                using (var announcer = new ScadaAnnouncerService(muteEngine, log: muteLog))
                {
                    bool spoken = announcer.Announce("警告，没有语音包", ScadaAlarmSeverity.Warning);
                    Thread.Sleep(120);

                    Check("[AN3] 本机没有语音包：不播、不抛，并落一条 Warn（退化到提示音）",
                        !spoken && muteEngine.Spoken.Count == 0 && !announcer.IsAvailable
                        && muteLog.Lines.Any(line => line.Contains("语音包")),
                        $"spoken={spoken} available={announcer.IsAvailable}");
                }

                // ---------------- 抢占 ----------------

                var blockingEngine = new FakeSpeechEngine { Block = true };
                using (var announcer = new ScadaAnnouncerService(blockingEngine))
                {
                    announcer.Announce("警告，低优先级", ScadaAlarmSeverity.Warning);
                    WaitUntil(() => blockingEngine.Spoken.Count >= 1, 2000);

                    announcer.Announce("紧急，高优先级", ScadaAlarmSeverity.Critical);

                    Check("[AN4] 正在播警告时来了紧急：立刻打断当前播报（Stop 被调用）",
                        blockingEngine.StopCalls >= 1,
                        $"stop={blockingEngine.StopCalls} spoken={blockingEngine.Spoken.Count}");

                    blockingEngine.Release();
                }

                // ---------------- 队列上限 + 提示音让路 ----------------

                var queueEngine = new FakeSpeechEngine { Block = true };
                var duckLog = new List<bool>();
                using (var announcer = new ScadaAnnouncerService(queueEngine, suspended =>
                {
                    lock (duckLog) duckLog.Add(suspended);
                }))
                {
                    for (var i = 0; i < 6; i++)
                        announcer.Announce("警告，" + i, ScadaAlarmSeverity.Warning, "k" + i);

                    WaitUntil(() => queueEngine.Spoken.Count >= 1, 2000);
                    var pending = announcer.PendingCount;

                    Check("[AN5] 队列上限 3：连推 6 条只留 3 条（丢最旧的，现场最新最重要）",
                        pending <= ScadaAnnouncerService.MaxQueue,
                        $"pending={pending}");

                    bool ducked;
                    lock (duckLog) ducked = duckLog.Count > 0 && duckLog[0];

                    queueEngine.Release();
                    WaitUntil(() => !announcer.IsSpeaking, 3000);
                    Thread.Sleep(120);

                    bool restored;
                    lock (duckLog) restored = duckLog.Count > 1 && duckLog[duckLog.Count - 1] == false;

                    Check("[AN6] 播报期间压住提示音、播完恢复（人声与滴滴声不叠在一起）",
                        ducked && restored,
                        string.Join(",", duckLog));
                }

                // ---------------- 文本模板（「语音播报」动作的插值） ----------------

                var filled = ScadaTextTemplate.Format("一号罐液位 {T101} 毫米", name => name == "T101" ? "1234.5" : null);
                var missingOne = ScadaTextTemplate.Format("缺 {Nope} 了", _ => null);
                var escaped = ScadaTextTemplate.Format("花括号 {{x}} 与未闭合 {abc", _ => null);

                Check("[AN7] 文本模板：{变量} 按当前值替换；找不到的从文本里去掉并记名；{{ }} 转义、未闭合按字面量",
                    filled.Text == "一号罐液位 1234.5 毫米" && filled.PlaceholderCount == 1 && !filled.HasMissing
                    && missingOne.Text == "缺  了" && missingOne.HasMissing && missingOne.Missing.Count == 1
                    && missingOne.Missing[0] == "Nope"
                    && escaped.Text == "花括号 {x} 与未闭合 {abc",
                    $"filled=[{filled.Text}] missing=[{missingOne.Text}] escaped=[{escaped.Text}]");

                // ---------------- 分发器：「语音播报」动作 ----------------

                var values = new FakeValueSource();
                var level = new FakeValueHandle { VariableId = Guid.NewGuid(), Name = "T101", DataType = typeof(double), Value = 1234.5 };
                values.ByName["T101"] = level;

                var speaker = new FakeAnnouncer();
                var speakLog = new LoggerStub();
                var speakDispatcher = new ScadaActionDispatcher(speakLog, values, new FakeNavigator())
                {
                    Announcer = speaker,
                };

                var speakHook = new ScadaEventHook { Event = ScadaEventType.Pressed };
                speakHook.Actions.Add(new ScadaAction
                {
                    Type = ScadaActionType.SpeakText,
                    Text = "一号罐液位 {T101} 毫米，{Nope} 未配",
                    SoundKind = "Warning",
                });
                speakDispatcher.Dispatch(speakHook, "按钮");

                Check("[AN8] 「语音播报」：模板按当前值渲染后交给播报器；找不到的变量从话里去掉并在日志里点名",
                    speaker.Commands.Count == 1
                    && speaker.Commands[0].Text == "一号罐液位 1234.5 毫米， 未配"
                    && speaker.Commands[0].Severity == ScadaAlarmSeverity.Warning
                    && speakLog.Lines.Any(line => line.Contains("Nope")),
                    $"spoken=[{string.Join(" | ", speaker.Commands.Select(c => c.Text))}] log={string.Join(" ⏎ ", speakLog.Lines)}");

                var offSpeaker = new FakeAnnouncer { Enabled = false };
                var offLog = new LoggerStub();
                var offDispatcher = new ScadaActionDispatcher(offLog, values, new FakeNavigator()) { Announcer = offSpeaker };
                offDispatcher.Dispatch(SpeakHook("说点什么"), "按钮");

                var muteSpeaker = new FakeAnnouncer { Available = false };
                var muteLog2 = new LoggerStub();
                var muteDispatcher = new ScadaActionDispatcher(muteLog2, values, new FakeNavigator()) { Announcer = muteSpeaker };
                muteDispatcher.Dispatch(SpeakHook("说点什么"), "按钮");

                var noAnnouncerLog = new LoggerStub();
                new ScadaActionDispatcher(noAnnouncerLog, values, new FakeNavigator())
                    .Dispatch(SpeakHook("说点什么"), "按钮");

                var emptyLog = new LoggerStub();
                var emptySpeaker = new FakeAnnouncer();
                var emptyDispatcher = new ScadaActionDispatcher(emptyLog, values, new FakeNavigator()) { Announcer = emptySpeaker };
                emptyDispatcher.Dispatch(SpeakHook("   "), "按钮");

                Check("[AN9] 未接入 / 已关闭 / 没语音包 / 内容为空：一律跳过并说清原因，不静默空跑",
                    offSpeaker.Commands.Count == 0 && muteSpeaker.Commands.Count == 0
                    && emptySpeaker.Commands.Count == 0
                    && noAnnouncerLog.Lines.Any(line => line.Contains("未接入语音播报"))
                    && offLog.Lines.Any(line => line.Contains("语音播报已关闭"))
                    && muteLog2.Lines.Any(line => line.Contains("语音包"))
                    && emptyLog.Lines.Any(line => line.Contains("播报内容未填")),
                    $"noannouncer={string.Join(" ⏎ ", noAnnouncerLog.Lines)} | off={string.Join(" ⏎ ", offLog.Lines)} | mute={string.Join(" ⏎ ", muteLog2.Lines)} | empty={string.Join(" ⏎ ", emptyLog.Lines)}");

                // ---------------- 组态触发不受"最低级别"门槛限制 ----------------

                var cmdEngine = new FakeSpeechEngine();
                using (var announcer = new ScadaAnnouncerService(cmdEngine))
                {
                    bool alarmInfo = announcer.Announce("提示，报警播报按门槛挡掉", ScadaAlarmSeverity.Info);
                    bool commandInfo = announcer.AnnounceCommand("提示，操作员按按钮", ScadaAlarmSeverity.Info);

                    WaitUntil(() => cmdEngine.Spoken.Count >= 1, 2000);
                    Thread.Sleep(120);

                    Check("[AN10] 报警播报仍受最低级别门槛限制；组态触发的「语音播报」不受（按钮不能被门槛挡成坏的）",
                        !alarmInfo && commandInfo && cmdEngine.Spoken.Count == 1
                        && cmdEngine.Spoken[0] == "提示，操作员按按钮",
                        $"alarmInfo={alarmInfo} commandInfo={commandInfo} spoken=[{string.Join(" | ", cmdEngine.Spoken)}]");
                }
            }

            private static ScadaEventHook SpeakHook(string text)
            {
                var hook = new ScadaEventHook { Event = ScadaEventType.Pressed };
                hook.Actions.Add(new ScadaAction { Type = ScadaActionType.SpeakText, Text = text });
                return hook;
            }

            /// <summary>假播报器：只记"念了什么、多急"，队列行为由 ScadaAnnouncerService 那组断言负责</summary>
            private sealed class FakeAnnouncer : IScadaAnnouncer
            {
                public bool Enabled { get; set; } = true;

                public bool Available { get; set; } = true;

                public bool IsAvailable => Available;

                public string? UnavailableReason => Available ? null : "本机没有可用语音包";

                public List<(string Text, ScadaAlarmSeverity Severity)> Commands { get; } = new();

                public bool AnnounceCommand(string? text, ScadaAlarmSeverity severity)
                {
                    if (!Enabled || !Available || string.IsNullOrWhiteSpace(text))
                        return false;

                    Commands.Add((text!, severity));
                    return true;
                }
            }

            private static void WaitUntil(Func<bool> condition, int timeoutMs)
            {
                var deadline = Environment.TickCount64 + timeoutMs;
                while (Environment.TickCount64 < deadline)
                {
                    if (condition())
                        return;

                    Thread.Sleep(20);
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
                    Check("语音播报断言全程未抛异常", false, error.ToString());
            }

            /// <summary>假语音引擎：可阻塞（模拟"正在播"）、记录播了什么、Stop 被调了几次</summary>
            private sealed class FakeSpeechEngine : ISpeechEngine
            {
                private readonly ManualResetEventSlim _gate = new(true);

                public IReadOnlyList<string> Voices { get; set; } = new[] { "Microsoft Huihui Desktop" };

                IReadOnlyList<string> ISpeechEngine.Voices => Voices;

                public List<string> Spoken { get; } = new();

                public int StopCalls { get; private set; }

                public bool Block
                {
                    set
                    {
                        if (value) _gate.Reset();
                        else _gate.Set();
                    }
                }

                public void Release() => _gate.Set();

                public string? VoiceName { get; set; }

                public int Rate { get; set; }

                public int Volume { get; set; }

                public bool Speak(string text)
                {
                    Spoken.Add(text);
                    _gate.Wait(5000);
                    return true;
                }

                public void Stop() => StopCalls++;

                public void Dispose() => _gate.Dispose();
            }
        }
    }
}
