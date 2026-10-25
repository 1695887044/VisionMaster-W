using System;
using System.Collections.Generic;
using VisionMaster.Scada;
using VisionMaster.Services;

namespace ScadaChecks
{
    internal partial class Program
    {
        /// <summary>
        /// [SND] 播放声音动作：枚举/文案、Info/Warning/Critical 映射、未接入/设备不可用、
        /// 未知档位兜底、播放异常。声音是事件触发后的副作用，不改变任何变量。
        /// </summary>
        private static class SoundActionChecks
        {
            public static void Run() => RunChecks();

            private static void RunChecks()
            {
                Section("播放声音动作（PlaySound）");

                Check("[SND1] 枚举末尾追加 PlaySound=27，展示名「播放声音」，已接通且不算危险动作",
                    (int)ScadaActionType.PlaySound == 27
                    && ScadaActionType.PlaySound.DisplayName() == "播放声音"
                    && ScadaActionType.PlaySound.PendingReason() == null
                    && !ScadaActionType.PlaySound.IsDangerous(),
                    $"value={(int)ScadaActionType.PlaySound} / name={ScadaActionType.PlaySound.DisplayName()}");

                var action = new ScadaAction { Type = ScadaActionType.PlaySound, SoundKind = "Warning" };
                Check("[SND2] 文案：Detail=Warning，Describe=「播放声音（Warning）」",
                    action.Detail == "Warning" && action.Describe() == "播放声音（Warning）",
                    $"{action.Detail} / {action.Describe()}");

                var player = new FakeSoundPlayer();
                var log = Dispatch(player, action);
                Check("[SND3] Warning 档：调用一次 Play(Warning, 100)，日志一行 Info",
                    player.Plays.Count == 1
                    && player.Plays[0].Severity == ScadaAlarmSeverity.Warning
                    && player.Plays[0].Volume == 100
                    && log.Lines.Count == 1
                    && log.Lines[0].StartsWith("I ", StringComparison.Ordinal),
                    player.Plays.Count == 1 ? $"{player.Plays[0].Severity}/{player.Plays[0].Volume}" : "no play");

                var critical = new ScadaAction { Type = ScadaActionType.PlaySound, SoundKind = "Critical" };
                var criticalPlayer = new FakeSoundPlayer();
                Dispatch(criticalPlayer, critical);
                Check("[SND4] Critical 档映射到 Critical",
                    criticalPlayer.Plays.Count == 1 && criticalPlayer.Plays[0].Severity == ScadaAlarmSeverity.Critical,
                    criticalPlayer.Plays.Count == 1 ? criticalPlayer.Plays[0].Severity.ToString() : "no play");

                var unknown = new ScadaAction { Type = ScadaActionType.PlaySound, SoundKind = "Loud" };
                var unknownPlayer = new FakeSoundPlayer();
                var unknownLog = Dispatch(unknownPlayer, unknown);
                Check("[SND5] 未知档位按 Info 播放，并在日志里明说，不静默",
                    unknownPlayer.Plays.Count == 1
                    && unknownPlayer.Plays[0].Severity == ScadaAlarmSeverity.Info
                    && unknownLog.Lines.Count == 1
                    && unknownLog.Lines[0].Contains("未知档位", StringComparison.Ordinal),
                    unknownLog.Lines.Count == 1 ? unknownLog.Lines[0] : "no log");

                var noPlayerLog = Dispatch(null, new ScadaAction { Type = ScadaActionType.PlaySound });
                Check("[SND6] 没接声音播放器：Warn 一行「未接入声音播放器」，不假装成功",
                    noPlayerLog.Lines.Count == 1
                    && noPlayerLog.Lines[0].StartsWith("W ", StringComparison.Ordinal)
                    && noPlayerLog.Lines[0].Contains("未接入声音播放器", StringComparison.Ordinal),
                    noPlayerLog.Lines.Count == 1 ? noPlayerLog.Lines[0] : "no log");

                var unavailable = new FakeSoundPlayer { IsAvailable = false, UnavailableReason = "没有音频输出设备" };
                var unavailableLog = Dispatch(unavailable, new ScadaAction { Type = ScadaActionType.PlaySound });
                Check("[SND7] 设备不可用：Warn 带上播放器给的原因，不抛",
                    unavailableLog.Lines.Count == 1
                    && unavailableLog.Lines[0].StartsWith("W ", StringComparison.Ordinal)
                    && unavailableLog.Lines[0].Contains("没有音频输出设备", StringComparison.Ordinal),
                    unavailableLog.Lines.Count == 1 ? unavailableLog.Lines[0] : "no log");

                var throwing = new FakeSoundPlayer { ThrowOnPlay = "声卡被占用" };
                var throwingLog = Dispatch(throwing, new ScadaAction { Type = ScadaActionType.PlaySound });
                Check("[SND8] 播放抛异常：Error 带原因，不把异常抛给事件链",
                    throwingLog.Lines.Count == 1
                    && throwingLog.Lines[0].StartsWith("E ", StringComparison.Ordinal)
                    && throwingLog.Lines[0].Contains("声卡被占用", StringComparison.Ordinal),
                    throwingLog.Lines.Count == 1 ? throwingLog.Lines[0] : "no log");
            }

            private static LoggerStub Dispatch(FakeSoundPlayer? player, ScadaAction action)
            {
                var log = new LoggerStub();
                var hook = new ScadaEventHook { Event = ScadaEventType.Pressed };
                hook.Actions.Add(action);

                new ScadaActionDispatcher(
                    log,
                    new FakeValueSource(),
                    new FakeNavigator(),
                    sounds: player).Dispatch(hook, "按钮");

                return log;
            }

            private sealed class FakeSoundPlayer : IScadaSoundPlayer
            {
                public bool IsAvailable { get; set; } = true;
                public string? UnavailableReason { get; set; }
                public string? ThrowOnPlay { get; set; }
                public List<(ScadaAlarmSeverity Severity, int Volume)> Plays { get; } = new();

                public void Play(ScadaAlarmSeverity severity, int volume)
                {
                    if (ThrowOnPlay != null)
                        throw new InvalidOperationException(ThrowOnPlay);

                    Plays.Add((severity, volume));
                }

                public void Stop()
                {
                }
            }
        }
    }
}
