using System;
using System.Collections.Generic;
using System.Globalization;
using System.Speech.Synthesis;
using System.Threading;
using Core.Interfaces;
using VisionMaster.Scada;

namespace VisionMaster.Services
{
    /// <summary>
    /// 语音引擎抽象。存在的唯一理由：<see cref="SpeechSynthesizer"/> 在断言宿主里既没声卡也可能没语音包，
    /// 而队列/去重/抢占这些逻辑必须能脱离真实语音验证。
    /// </summary>
    public interface ISpeechEngine : IDisposable
    {
        /// <summary>本机已安装的语音名（空 = 一个都没有，只能退化）</summary>
        IReadOnlyList<string> Voices { get; }

        /// <summary>当前语音名（null = 系统默认）</summary>
        string? VoiceName { get; set; }

        /// <summary>语速（-10 ~ 10，0 = 正常）</summary>
        int Rate { get; set; }

        /// <summary>音量（0 ~ 100）</summary>
        int Volume { get; set; }

        /// <summary>阻塞播完一句；返回 false 表示没播出去（无语音包/引擎异常）</summary>
        bool Speak(string text);

        /// <summary>打断当前播报（可重复调用）</summary>
        void Stop();
    }

    /// <summary>SAPI 实现：`System.Speech`，Windows 自带语音包，离线可用。</summary>
    public sealed class SapiSpeechEngine : ISpeechEngine
    {
        private readonly SpeechSynthesizer _synth = new();
        private bool _broken;

        public IReadOnlyList<string> Voices
        {
            get
            {
                var names = new List<string>();

                try
                {
                    foreach (var voice in _synth.GetInstalledVoices())
                    {
                        if (voice.Enabled)
                            names.Add(voice.VoiceInfo.Name);
                    }
                }
                catch (Exception)
                {
                    // 精简版系统上 SAPI 可能直接抛；当成"没有语音"
                }

                return names;
            }
        }

        public string? VoiceName
        {
            get => null;
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                    return;

                try { _synth.SelectVoice(value!); } catch (Exception) { }
            }
        }

        public int Rate
        {
            get => _synth.Rate;
            set => _synth.Rate = Math.Clamp(value, -10, 10);
        }

        public int Volume
        {
            get => _synth.Volume;
            set => _synth.Volume = Math.Clamp(value, 0, 100);
        }

        public bool Speak(string text)
        {
            if (_broken || string.IsNullOrWhiteSpace(text))
                return false;

            try
            {
                _synth.Speak(text);
                return true;
            }
            catch (Exception)
            {
                // 播一次失败就永久停用：现场反复重试只会刷日志，声音也不会自己好
                _broken = true;
                return false;
            }
        }

        public void Stop()
        {
            try { _synth.SpeakAsyncCancelAll(); } catch (Exception) { }
        }

        public void Dispose() => _synth.Dispose();
    }

    /// <summary>
    /// 报警/事件语音播报（TTS）。
    ///
    /// 为什么不是"来一条就 Speak 一句"
    /// ---------
    /// 报警会成串来：十台设备同时报，十句话叠在一起就是噪音，操作员一个字也听不清。
    /// 所以这里是一个**串行队列**：
    /// · <b>去重</b>：同一个 key（报警 Id）在 <see cref="DedupWindow"/> 内只播一次
    ///   （报警引擎会反复发 AlarmChanged，不去重就会念个没完）；
    /// · <b>限流</b>：队列上限 <see cref="MaxQueue"/>=3，满了丢最旧的（现场"最新的最重要"）；
    /// · <b>抢占</b>：正在播低级别时来了更严重的，立刻打断改播新的；
    /// · <b>与提示音互斥</b>：说话期间把报警提示音压住（<c>duck</c> 回调），否则人声和滴滴声糊在一起。
    ///
    /// 线程：队列在一条常驻后台线程上串行消费（<see cref="SpeechSynthesizer"/> 不是线程安全的）；
    /// 调用方可以在任何线程 <see cref="Announce"/>。
    /// </summary>
    public sealed class ScadaAnnouncerService : IScadaAnnouncer, IDisposable
    {
        /// <summary>同一 key 的去重窗口</summary>
        public static readonly TimeSpan DedupWindow = TimeSpan.FromSeconds(30);

        /// <summary>队列上限（含正在播的那条）</summary>
        public const int MaxQueue = 3;

        private readonly ISpeechEngine _engine;
        private readonly Action<bool>? _duck;
        private readonly ILogService? _log;
        private readonly object _gate = new();
        private readonly LinkedList<Announcement> _queue = new();
        private readonly Dictionary<string, DateTime> _recent = new(StringComparer.Ordinal);
        private readonly AutoResetEvent _signal = new(false);

        private Thread? _worker;
        private Announcement? _speaking;
        private bool _disposed;
        private bool _warnedNoVoice;

        public ScadaAnnouncerService(ISpeechEngine engine, Action<bool>? duck = null, ILogService? log = null)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _duck = duck;
            _log = log;
        }

        /// <summary>总开关（关掉后不再播，队列清空）</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>只播到这个级别及以上（默认 Warning：Info 不念）</summary>
        public ScadaAlarmSeverity MinSeverity { get; set; } = ScadaAlarmSeverity.Warning;

        /// <summary>语速（-10~10）</summary>
        public int Rate
        {
            get => _engine.Rate;
            set => _engine.Rate = value;
        }

        /// <summary>音色（系统语音名）。设成本机没有的名字时由引擎忽略</summary>
        public string? VoiceName
        {
            get => _engine.VoiceName;
            set => _engine.VoiceName = value;
        }

        /// <summary>音量（0~100）</summary>
        public int Volume
        {
            get => _engine.Volume;
            set => _engine.Volume = value;
        }

        /// <summary>本机已安装语音（空 = 只能退化到提示音）</summary>
        public IReadOnlyList<string> Voices => _engine.Voices;

        public bool IsAvailable => _engine.Voices.Count > 0;

        /// <inheritdoc/>
        public string? UnavailableReason => IsAvailable
            ? null
            : "本机没有可用语音包（控制面板 → 语音 → 添加中文语音）";

        /// <summary>当前是否在播（断言与界面状态用）</summary>
        public bool IsSpeaking
        {
            get { lock (_gate) return _speaking != null; }
        }

        /// <summary>队列里排着的条数（含正在播的）</summary>
        public int PendingCount
        {
            get { lock (_gate) return _queue.Count; }
        }

        /// <summary>
        /// 播一句。<paramref name="key"/> 用于去重（报警 Id 之类），空则不去重。
        /// 低于 <see cref="MinSeverity"/>、已关闭、没有语音包时静默丢弃（不是错误）。
        /// </summary>
        public bool Announce(string? text, ScadaAlarmSeverity severity, string? key = null)
            => Enqueue(text, severity, key, respectMinSeverity: true);

        /// <summary>
        /// 念一句由组态触发的文本（「语音播报」动作）。
        ///
        /// 与 <see cref="Announce"/> 只差一条：<b>不受 <see cref="MinSeverity"/> 门槛限制</b>。
        /// 那道门槛是为了挡掉报警洪水里的 Info 级，而这里每一句都是组态作者显式写下的、
        /// 操作员刚按下去的一句话——挡掉它等于按钮坏了。总开关 <see cref="Enabled"/> 照样生效。
        /// </summary>
        public bool AnnounceCommand(string? text, ScadaAlarmSeverity severity)
            => Enqueue(text, severity, key: null, respectMinSeverity: false);

        private bool Enqueue(string? text, ScadaAlarmSeverity severity, string? key, bool respectMinSeverity)
        {
            if (!Enabled || string.IsNullOrWhiteSpace(text))
                return false;

            if (respectMinSeverity && severity < MinSeverity)
                return false;

            if (!EnsureVoiceAvailable())
                return false;

            var now = DateTime.UtcNow;

            lock (_gate)
            {
                if (_disposed)
                    return false;

                if (!string.IsNullOrWhiteSpace(key))
                {
                    PruneRecentLocked(now);

                    if (_recent.TryGetValue(key!, out var last) && now - last < DedupWindow)
                        return false;

                    _recent[key!] = now;
                }

                // 抢占：新来的更严重 → 立刻打断当前那条
                if (_speaking != null && severity > _speaking.Value.Severity)
                {
                    _engine.Stop();
                    _speaking = null;
                }

                _queue.AddLast(new Announcement(text!.Trim(), severity));

                while (_queue.Count > MaxQueue)
                    _queue.RemoveFirst();

                Monitor.PulseAll(_gate);
            }

            EnsureWorker();
            _signal.Set();
            return true;
        }

        /// <summary>停止播报并清空队列（切方案/退出时用）</summary>
        public void Stop()
        {
            lock (_gate)
            {
                _queue.Clear();
                _speaking = null;
            }

            _engine.Stop();
            _duck?.Invoke(false);
        }

        private bool EnsureVoiceAvailable()
        {
            if (IsAvailable)
                return true;

            if (!_warnedNoVoice)
            {
                _warnedNoVoice = true;
                _log?.Warn("[语音] 本机没有可用语音包，播报已退化为提示音（控制面板 → 语音 → 添加中文语音）");
            }

            return false;
        }

        private void PruneRecentLocked(DateTime now)
        {
            if (_recent.Count == 0)
                return;

            var stale = new List<string>();
            foreach (var pair in _recent)
            {
                if (now - pair.Value >= DedupWindow)
                    stale.Add(pair.Key);
            }

            foreach (var key in stale)
                _recent.Remove(key);
        }

        private void EnsureWorker()
        {
            lock (_gate)
            {
                if (_worker != null)
                    return;

                _worker = new Thread(WorkerLoop)
                {
                    IsBackground = true,
                    Name = "Scada.Announcer",
                };
                _worker.Start();
            }
        }

        private void WorkerLoop()
        {
            while (true)
            {
                Announcement next;

                lock (_gate)
                {
                    if (_disposed)
                        return;

                    if (_queue.Count == 0)
                    {
                        Monitor.Wait(_gate, 500);
                        if (_queue.Count == 0)
                            continue;
                    }

                    next = _queue.First!.Value;
                    _queue.RemoveFirst();
                    _speaking = next;
                }

                try
                {
                    _duck?.Invoke(true);
                    _engine.Speak(next.Text);
                }
                catch (Exception ex)
                {
                    _log?.Warn("[语音] 播报失败：" + ex.Message);
                }
                finally
                {
                    lock (_gate)
                    {
                        if (ReferenceEquals(_speaking, next))
                            _speaking = null;
                    }

                    // 队列里还有下一句就继续压着提示音，避免"人声-滴滴-人声"来回切
                    bool more;
                    lock (_gate)
                        more = _queue.Count > 0;

                    if (!more)
                        _duck?.Invoke(false);
                }
            }
        }

        public void Dispose()
        {
            lock (_gate)
                _disposed = true;

            Stop();
            _engine.Dispose();
            _signal.Dispose();
        }

        private readonly record struct Announcement(string Text, ScadaAlarmSeverity Severity);
    }
}
