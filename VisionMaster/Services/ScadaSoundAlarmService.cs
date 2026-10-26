using System;
using System.Collections.Generic;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using VisionMaster.Scada;
using VisionMaster.Scada.Controls;

namespace VisionMaster.Services
{
    /// <summary>
    /// 报警声音出口：把"响不响、响多久"从报警引擎里隔开。
    ///
    /// 为什么要有这一层接口
    /// ---------
    /// 报警引擎只发事实（<see cref="ScadaAlarmEngine.AlarmRaised"/>），不该知道有没有声卡；
    /// 断言宿主里也没有声卡。把发声收在一个接口后面，测试就能用假播放器数"响了几次、停了几次"，
    /// 而真机上换 <see cref="SystemSoundPlayer"/> 即可——两种环境验的是同一段状态机。
    ///
    /// 音型口径（对齐设计基线 <c>docs/UI设计基线/scada-controls-batch3</c> 的声音报警一节）
    /// ---------
    /// · 紧急（Critical）：连续音，每 <see cref="CriticalRepeat"/> 一遍，静音不拦；
    /// · 高（Warning）：间歇音，每 <see cref="WarningRepeat"/> 一遍，可静音；
    /// · 提示（Info）：每次新报警响一次；
    /// · 确认即停：最后一条未确认报警被确认后停止；仍有未确认则继续。
    ///
    /// 线程
    /// ---------
    /// 引擎事件可能来自变量轮询线程，节拍回调也跑在线程池上；状态与播放调用统一过
    /// <see cref="_gate"/>。对外事件 <see cref="StateChanged"/> 可能在非 UI 线程触发，
    /// 订阅方自己切线程（运行窗口就是这么做的）。
    /// </summary>
    public sealed class ScadaSoundAlarmService : IDisposable
    {
        /// <summary>音量默认值（0~100）。设计基线取 55：够听见，又不至于盖住现场说话</summary>
        public const int DefaultVolume = 55;

        /// <summary>静音默认时长（分钟）。设计基线：静音 5 分钟后自动恢复</summary>
        public const int DefaultMuteMinutes = 5;

        /// <summary>紧急音重复间隔。音本身 900ms，间隔 950ms 听起来是"连续"</summary>
        public static readonly TimeSpan CriticalRepeat = TimeSpan.FromMilliseconds(950);

        /// <summary>警告音重复间隔。设计基线写的是"间歇 2s"</summary>
        public static readonly TimeSpan WarningRepeat = TimeSpan.FromSeconds(2);

        /// <summary>节拍粒度：够细到 2 秒的重复不漂，又不至于每秒唤醒几百次</summary>
        private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(250);

        private readonly ScadaAlarmEngine _engine;
        private readonly IScadaSoundPlayer _player;
        private readonly Action<ScadaDiagnosticLevel, string>? _diagnostic;
        private readonly Func<DateTime> _clock;
        private readonly bool _autoTick;
        private readonly object _gate = new();
        private readonly Timer _timer;

        private bool _enabled;
        private bool _suspended;
        private int _volume = DefaultVolume;
        private int _muteMinutes = DefaultMuteMinutes;
        private DateTime? _muteUntilUtc;
        private DateTime _nextPlayUtc = DateTime.MaxValue;
        private bool _sounding;
        private bool _reportedFailure;
        private bool _disposed;

        public ScadaSoundAlarmService(
            ScadaAlarmEngine engine,
            IScadaSoundPlayer player,
            Action<ScadaDiagnosticLevel, string>? diagnostic = null,
            Func<DateTime>? clock = null,
            bool autoTick = true)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _diagnostic = diagnostic;
            _clock = clock ?? (() => DateTime.UtcNow);

            // autoTick=false 只给断言用：真实定时器会和假时钟打架（它按墙上时间到点，
            // 而断言把时钟拨到未来），同一拍可能被算两次。断言改为手动 Tick。
            _autoTick = autoTick;

            _timer = new Timer(OnTick, null, Timeout.Infinite, Timeout.Infinite);

            _engine.AlarmRaised += OnAlarmRaised;
            _engine.AlarmChanged += OnAlarmChanged;
            _engine.AlarmCleared += OnAlarmCleared;

            if (!_player.IsAvailable)
                ReportUnavailable();
        }

        /// <summary>开关、静音、发声状态发生变化时触发（可能在非 UI 线程）</summary>
        public event EventHandler? StateChanged;

        /// <summary>启用开关变化时触发；宿主据此把偏好落盘（不要拿 <see cref="StateChanged"/> 落盘，它太频繁）</summary>
        public event EventHandler? EnabledChanged;

        /// <summary>是否启用声音。默认关闭（设计基线 Q4：由工程设置打开）</summary>
        public bool Enabled
        {
            get { lock (_gate) return _enabled; }
            set
            {
                bool changed;
                lock (_gate)
                {
                    changed = _enabled != value;
                    _enabled = value;
                    if (!value)
                        StopPlaybackLocked();
                }

                if (!changed)
                    return;

                if (value)
                    EvaluateNow("启用声音");

                EnabledChanged?.Invoke(this, EventArgs.Empty);
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>音量 0~100。0 = 静默但不改变启用状态</summary>
        public int Volume
        {
            get { lock (_gate) return _volume; }
            set
            {
                var clamped = Math.Clamp(value, 0, 100);
                lock (_gate)
                {
                    if (_volume == clamped)
                        return;
                    _volume = clamped;
                }

                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>静音时长（分钟）。改它不影响正在生效的这一次静音</summary>
        public int MuteMinutes
        {
            get { lock (_gate) return _muteMinutes; }
            set
            {
                lock (_gate)
                    _muteMinutes = Math.Clamp(value, 1, 1440);
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>此刻是否处于静音期（紧急报警不受它约束，见 <see cref="HighestUnacknowledgedSeverity"/>）</summary>
        public bool IsMuted
        {
            get { lock (_gate) return IsMutedLocked(); }
        }

        /// <summary>静音还剩多久；未静音时为 <see cref="TimeSpan.Zero"/></summary>
        public TimeSpan MuteRemaining
        {
            get
            {
                lock (_gate)
                {
                    if (_muteUntilUtc == null)
                        return TimeSpan.Zero;

                    var left = _muteUntilUtc.Value - _clock();
                    return left > TimeSpan.Zero ? left : TimeSpan.Zero;
                }
            }
        }

        /// <summary>音频设备是否可用</summary>
        public bool IsAvailable => _player.IsAvailable;

        /// <summary>
        /// 让路开关：true = 暂停提示音（语音播报期间用）。与 <see cref="Enabled"/> 是两件事——
        /// 这里不改变用户的"声音开/关"，只是暂时不发声；恢复后按当前报警状态自然接上。
        /// </summary>
        public bool Suspended
        {
            get
            {
                lock (_gate)
                    return _suspended;
            }
            set
            {
                lock (_gate)
                {
                    if (_suspended == value)
                        return;

                    _suspended = value;
                    if (_suspended)
                        StopPlaybackLocked();
                }
            }
        }

        /// <summary>不可用原因；可用时为 null</summary>
        public string? UnavailableReason => _player.UnavailableReason;

        /// <summary>此刻是否正在发声（UI 的"报警中"指示灯用）</summary>
        public bool IsSounding
        {
            get { lock (_gate) return _sounding; }
        }

        /// <summary>等确认的报警条数（UI 徽标用）</summary>
        public int UnacknowledgedCount => _engine.UnacknowledgedCount;

        /// <summary>未确认报警里最高的严重度；没有则为 null</summary>
        public ScadaAlarmSeverity? HighestUnacknowledgedSeverity
        {
            get
            {
                lock (_gate)
                    return HighestUnacknowledgedLocked();
            }
        }

        /// <summary>
        /// 静音一段时间。返回 false = 拒绝（当前有紧急级未确认报警，紧急不可静音）。
        /// 静音不是确认：报警仍在、计数照旧，只是不再发声。
        /// </summary>
        public bool Mute()
        {
            lock (_gate)
            {
                if (HighestUnacknowledgedLocked() == ScadaAlarmSeverity.Critical)
                    return false;

                _muteUntilUtc = _clock().AddMinutes(_muteMinutes);
                MuteSilenceLocked();
            }

            StateChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        /// <summary>取消静音；若仍有未确认报警，立刻恢复发声</summary>
        public void Unmute()
        {
            lock (_gate)
                _muteUntilUtc = null;

            StateChanged?.Invoke(this, EventArgs.Empty);
            EvaluateNow("取消静音");
        }

        /// <summary>
        /// 试听。这是<b>用户显式动作</b>，所以不看 <see cref="Enabled"/>、也不看静音期——
        /// 否则"关了声音想听一下是什么样"就永远听不到。设备不可用时返回 false。
        /// </summary>
        public bool Test()
        {
            if (!_player.IsAvailable)
            {
                ReportUnavailable();
                return false;
            }

            lock (_gate)
            {
                var severity = HighestUnacknowledgedLocked() ?? ScadaAlarmSeverity.Warning;
                PlayLocked(severity);
            }

            StateChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        /// <summary>推进一拍。定时器与断言共用同一条路径（与 <see cref="ScadaAlarmEngine.Tick()"/> 同一口径）</summary>
        public void Tick() => OnTick(null);

        private void OnAlarmRaised(ScadaAlarmRecord record)
        {
            lock (_gate)
            {
                if (_disposed || !_enabled || !_player.IsAvailable)
                    return;

                // 提示级只响一次；它不该因为"还挂着一条未确认"就每 2 秒叫一遍。
                if (record.Severity == ScadaAlarmSeverity.Info)
                {
                    if (IsMutedLocked())
                        return;

                    PlayLocked(record.Severity);
                    return;
                }

                // 紧急不可静音：静音期只拦警告与提示。
                if (record.Severity != ScadaAlarmSeverity.Critical && IsMutedLocked())
                    return;

                PlayLocked(record.Severity);
                ScheduleNextLocked(record.Severity);
            }

            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnAlarmChanged(ScadaAlarmRecord record)
        {
            // 确认/恢复不重新起响：确认一条之后剩下的那条不该立刻再叫一遍。
            // 只在"全部确认完"时停，以及在静音期结束后由节拍自然恢复。
            if (_engine.UnacknowledgedCount == 0)
                StopPlayback();
        }

        private void OnAlarmCleared(ScadaAlarmRecord record)
        {
            if (_engine.UnacknowledgedCount == 0)
                StopPlayback();
        }

        /// <summary>
        /// 立刻按当前状态评估一次：有未确认报警就响，没有就停。
        /// 只用于"状态刚变"的入口（启用、取消静音），不用于节拍。
        /// </summary>
        private void EvaluateNow(string reason)
        {
            var changed = false;
            lock (_gate)
            {
                if (_disposed || !_enabled || !_player.IsAvailable)
                {
                    changed = StopPlaybackLocked();
                }
                else
                {
                    var highest = HighestUnacknowledgedLocked();
                    if (highest == null)
                    {
                        changed = StopPlaybackLocked();
                    }
                    else if (highest == ScadaAlarmSeverity.Critical || !IsMutedLocked())
                    {
                        changed = PlayLocked(highest.Value);
                        ScheduleNextLocked(highest.Value);
                    }
                    else
                    {
                        // 静音期内启用声音：闭嘴，但节拍留着等静音到期。
                        changed = MuteSilenceLocked();
                    }
                }
            }

            if (changed)
                StateChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 节拍：静音到期、报警被确认、以及"重复音到点了"都在这里收敛。
        /// 不在引擎事件里排下一次：引擎不会因为"时间过去了"再发事件，而静音到期正是时间事件。
        /// </summary>
        private void OnTick(object? state)
        {
            try
            {
                var changed = false;
                lock (_gate)
                {
                    if (_disposed)
                        return;

                    if (!_enabled || !_player.IsAvailable)
                    {
                        changed = StopPlaybackLocked();
                    }
                    else
                    {
                        var highest = HighestUnacknowledgedLocked();
                        if (highest == null)
                        {
                            changed = StopPlaybackLocked();
                        }
                        else if (highest != ScadaAlarmSeverity.Critical && IsMutedLocked())
                        {
                            // 静音期内不发声，但节拍必须继续跑：静音到期是一次"时间事件"，
                            // 停了定时器就没人来发现它已经到期（表现为"静音之后再也不响"）。
                            changed = MuteSilenceLocked();
                        }
                        else if (_clock() >= _nextPlayUtc)
                        {
                            changed = PlayLocked(highest.Value);
                            ScheduleNextLocked(highest.Value);
                        }
                    }
                }

                if (changed)
                    StateChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                // 节拍线程上的异常没人接：落一行诊断后停掉定时器，绝不让它带崩进程。
                ReportOnce($"[报警] 声音节拍异常：{ex.Message}");
                lock (_gate)
                    StopPlaybackLocked();
            }
        }

        /// <summary>真正发声。返回 true = 发声状态发生了变化（供上层决定要不要刷 UI）</summary>
        private bool PlayLocked(ScadaAlarmSeverity severity)
        {
            // 语音播报期间压住提示音：人声和滴滴声同时响，现场一个字也听不清。
            // 由播报服务通过 Suspend 开合；这里只是"让路"，不改用户的开关状态。
            if (_suspended)
            {
                StopPlaybackLocked();
                return false;
            }

            try
            {
                _player.Play(severity, _volume);

                // 提示级是"响一下"就结束：它不排下一拍，也不该让顶条一直亮着"报警中"。
                // 但若此刻还有紧急/警告在响，状态不能被这条提示音降下来。
                var stillRepeating = HighestUnacknowledgedLocked() > ScadaAlarmSeverity.Info;
                return SetSoundingLocked(severity != ScadaAlarmSeverity.Info || stillRepeating);
            }
            catch (Exception ex)
            {
                ReportOnce($"[报警] 声音播放失败：{ex.Message}；后续不再尝试发声，报警仍照常记录与上屏");
                StopPlaybackLocked();
                return false;
            }
        }

        private void ScheduleNextLocked(ScadaAlarmSeverity severity)
        {
            if (severity == ScadaAlarmSeverity.Info)
            {
                _nextPlayUtc = DateTime.MaxValue;
                return;
            }

            var gap = severity == ScadaAlarmSeverity.Critical ? CriticalRepeat : WarningRepeat;
            _nextPlayUtc = _clock() + gap;
            if (_autoTick)
                _timer.Change(TickInterval, TickInterval);
        }

        /// <summary>
        /// 静音期间"闭嘴但保持节拍"：停掉当前声音，把下一次判定排到此刻，
        /// 于是静音一到期，下一拍就会立刻恢复发声。
        /// </summary>
        private bool MuteSilenceLocked()
        {
            _nextPlayUtc = _clock();

            // 一条未确认报警都没有时不必空转定时器；Mute() 允许"提前静音"，
            // 那时等真有报警进来，OnAlarmRaised 会自己排节拍。
            if (_autoTick && HighestUnacknowledgedLocked() != null)
                _timer.Change(TickInterval, TickInterval);

            try
            {
                _player.Stop();
            }
            catch
            {
                // 停声失败不改变状态。
            }

            return SetSoundingLocked(false);
        }

        private void StopPlayback()
        {
            bool changed;
            lock (_gate)
                changed = StopPlaybackLocked();

            if (changed)
                StateChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>停声并停节拍。返回 true = 发声状态发生了变化</summary>
        private bool StopPlaybackLocked()
        {
            _nextPlayUtc = DateTime.MaxValue;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);

            try
            {
                _player.Stop();
            }
            catch
            {
                // 停声失败没有补救手段；状态照样归零，否则 UI 会一直显示"报警中"。
            }

            return SetSoundingLocked(false);
        }

        private bool SetSoundingLocked(bool value)
        {
            if (_sounding == value)
                return false;

            _sounding = value;
            return true;
        }

        private bool IsMutedLocked()
        {
            if (_muteUntilUtc == null)
                return false;

            if (_clock() < _muteUntilUtc.Value)
                return true;

            // 静音到期：清掉记忆，下一次评估自然恢复发声。
            _muteUntilUtc = null;
            return false;
        }

        private ScadaAlarmSeverity? HighestUnacknowledgedLocked()
        {
            ScadaAlarmSeverity? highest = null;

            foreach (var record in _engine.ActiveAlarms)
            {
                if (!record.NeedsAcknowledge)
                    continue;

                if (highest == null || record.Severity > highest)
                    highest = record.Severity;
            }

            return highest;
        }

        private void ReportUnavailable()
        {
            ReportOnce($"[报警] 声音不可用：{_player.UnavailableReason ?? "未知原因"}；报警仍照常记录与上屏");
        }

        private void ReportOnce(string message)
        {
            if (_reportedFailure)
                return;

            _reportedFailure = true;
            _diagnostic?.Invoke(ScadaDiagnosticLevel.Warning, message);
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                    return;

                _disposed = true;
                StopPlaybackLocked();
            }

            _engine.AlarmRaised -= OnAlarmRaised;
            _engine.AlarmChanged -= OnAlarmChanged;
            _engine.AlarmCleared -= OnAlarmCleared;

            _timer.Dispose();

            try
            {
                _player.Stop();
            }
            catch
            {
                // 收场路径不抛。
            }

            if (_player is IDisposable disposable)
                disposable.Dispose();
        }
    }

    /// <summary>
    /// 声音出口。实现方负责把"严重度 + 音量"变成一个可听见的声音；
    /// 状态机（何时响、重复多久、静音）一律留在 <see cref="ScadaSoundAlarmService"/>，
    /// 这样断言用的假播放器只需要记调用，不用复刻时间逻辑。
    /// </summary>
    public interface IScadaSoundPlayer
    {
        /// <summary>音频输出是否可用</summary>
        bool IsAvailable { get; }

        /// <summary>不可用原因；可用时为 null</summary>
        string? UnavailableReason { get; }

        /// <summary>播放一遍该严重度对应的提示音（非阻塞）</summary>
        void Play(ScadaAlarmSeverity severity, int volume);

        /// <summary>停止当前播放（可重复调用）</summary>
        void Stop();
    }

    /// <summary>
    /// 真机播放器：现场合成 16 位 PCM WAV，用 <see cref="SoundPlayer"/> 异步播放。
    ///
    /// 为什么不带 wav 资源文件
    /// ---------
    /// 声音要随音量变，而 <see cref="SoundPlayer"/> 没有音量接口——只能把振幅烘进波形。
    /// 现成 wav 改不了振幅，等于音量滑杆是个摆设；现场合成一段几十 KB 的波形反而更简单，
    /// 也省掉了"资源没打进 DLL"这一类静默故障。
    ///
    /// 设备不可用
    /// ---------
    /// 先用 <c>waveOutGetNumDevs</c> 判断有没有输出设备（没有声卡时 <see cref="SoundPlayer.Play()"/>
    /// 可能静默什么都不做，那会让"声音怎么不响"变成猜谜）。播放过程中再失败一次就永久标记不可用，
    /// 由服务落一行诊断并停掉节拍。
    /// </summary>
    public sealed class SystemSoundPlayer : IScadaSoundPlayer, IDisposable
    {
        private readonly SoundPlayer _player = new();
        private readonly object _gate = new();
        private readonly Dictionary<(ScadaAlarmSeverity Severity, int Volume), byte[]> _cache = new();

        private MemoryStream? _stream;
        private bool? _available;
        private string? _reason;

        public bool IsAvailable
        {
            get
            {
                lock (_gate)
                    return EnsureAvailableLocked();
            }
        }

        public string? UnavailableReason
        {
            get
            {
                lock (_gate)
                {
                    EnsureAvailableLocked();
                    return _reason;
                }
            }
        }

        public void Play(ScadaAlarmSeverity severity, int volume)
        {
            lock (_gate)
            {
                if (!EnsureAvailableLocked())
                    return;

                if (!_cache.TryGetValue((severity, volume), out var wav))
                {
                    wav = ScadaAlarmTone.BuildWav(severity, volume);
                    _cache[(severity, volume)] = wav;
                }

                var stream = new MemoryStream(wav, writable: false);
                try
                {
                    _player.Stop();
                    _player.Stream = stream;
                    _player.Play();
                }
                catch (Exception ex)
                {
                    stream.Dispose();
                    _available = false;
                    _reason = ex.Message;
                    throw;
                }

                // 上一段流的字节已经由 SoundPlayer 读进内存；这里只释放句柄。
                _stream?.Dispose();
                _stream = stream;
            }
        }

        public void Stop()
        {
            lock (_gate)
                _player.Stop();
        }

        private bool EnsureAvailableLocked()
        {
            if (_available != null)
                return _available.Value;

            try
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    _available = false;
                    _reason = "当前系统不支持 WPF 声音输出";
                    return false;
                }

                _available = WaveOutGetNumDevs() > 0;
                if (!_available.Value)
                    _reason = "系统没有可用的音频输出设备";
            }
            catch (Exception ex)
            {
                _available = false;
                _reason = ex.Message;
            }

            return _available.Value;
        }

        [DllImport("winmm.dll", EntryPoint = "waveOutGetNumDevs")]
        private static extern uint WaveOutGetNumDevs();

        public void Dispose()
        {
            lock (_gate)
            {
                try
                {
                    _player.Stop();
                }
                catch
                {
                    // 收场路径不抛。
                }

                _player.Dispose();
                _stream?.Dispose();
                _stream = null;
            }
        }
    }

    /// <summary>
    /// 提示音合成：严重度 → 波形。单独成类是为了断言能直接检查 WAV 结构（时长、位深、峰值），
    /// 不必真的去听——"能听见"靠真机试听，"波形对不对"靠断言。
    /// </summary>
    public static class ScadaAlarmTone
    {
        public const int SampleRate = 44100;

        /// <summary>峰值振幅：满音量取 0.6，留 4dB 余量，避免叠加系统音效时削顶</summary>
        public const double PeakAtFullVolume = 0.6;

        /// <summary>
        /// 生成一段 16 位单声道 PCM WAV。
        /// 紧急 = 880/660Hz 交替的长音（听起来是连续警笛）；警告 = 单声 320ms；提示 = 短促 130ms。
        /// </summary>
        public static byte[] BuildWav(ScadaAlarmSeverity severity, int volume)
        {
            var clampedVolume = Math.Clamp(volume, 0, 100);

            var (durationMs, tones) = severity switch
            {
                ScadaAlarmSeverity.Critical => (900, new[]
                {
                    (Frequency: 880.0, Milliseconds: 150),
                    (Frequency: 660.0, Milliseconds: 150),
                    (Frequency: 880.0, Milliseconds: 150),
                    (Frequency: 660.0, Milliseconds: 150),
                    (Frequency: 880.0, Milliseconds: 150),
                    (Frequency: 660.0, Milliseconds: 150),
                }),
                ScadaAlarmSeverity.Warning => (320, new[] { (Frequency: 880.0, Milliseconds: 320) }),
                _ => (130, new[] { (Frequency: 1175.0, Milliseconds: 130) }),
            };

            var sampleCount = SampleRate * durationMs / 1000;
            var samples = new short[sampleCount];
            var amplitude = short.MaxValue * PeakAtFullVolume * clampedVolume / 100.0;
            var position = 0;

            foreach (var tone in tones)
            {
                var count = SampleRate * tone.Milliseconds / 1000;

                // 6ms 淡入淡出：不淡的话每个音头/音尾都是一个"咔哒"。
                var fade = Math.Min(count / 2, SampleRate * 6 / 1000);

                for (var i = 0; i < count && position < sampleCount; i++, position++)
                {
                    var envelope = 1.0;
                    if (i < fade)
                        envelope = (double)i / fade;
                    else if (i > count - fade)
                        envelope = (double)(count - i) / fade;

                    var value = Math.Sin(2 * Math.PI * tone.Frequency * i / SampleRate) * amplitude * envelope;
                    samples[position] = (short)Math.Round(value);
                }
            }

            return BuildWavFile(samples);
        }

        private static byte[] BuildWavFile(short[] samples)
        {
            var dataBytes = samples.Length * 2;
            using var buffer = new MemoryStream(44 + dataBytes);
            using var writer = new BinaryWriter(buffer, Encoding.ASCII, leaveOpen: true);

            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataBytes);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));

            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);                 // fmt 块长度
            writer.Write((short)1);           // PCM
            writer.Write((short)1);           // 单声道
            writer.Write(SampleRate);         // 采样率
            writer.Write(SampleRate * 2);     // 每秒字节数 = 采样率 × 声道 × 位深/8
            writer.Write((short)2);           // 块对齐
            writer.Write((short)16);          // 位深

            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(dataBytes);

            foreach (var sample in samples)
                writer.Write(sample);

            writer.Flush();
            return buffer.ToArray();
        }
    }
}
