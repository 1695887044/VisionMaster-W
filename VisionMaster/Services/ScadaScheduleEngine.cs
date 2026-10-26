using System;
using System.Collections.Generic;
using System.Globalization;
using Core.Interfaces;
using VisionMaster.Scada;

namespace VisionMaster.Services
{
    /// <summary>
    /// 计划表执行器：按秒评估"到点没到点 / 条件满足了多久"，命中就把动作交给分发器。
    ///
    /// 三条纪律
    /// ---------
    /// ① <b>同一次到点只执行一次</b>：按 <c>yyyyMMddHHmm</c> 记账（<see cref="ScadaScheduleClock.OccurrenceKey"/>），
    ///    节拍快慢不会让 08:00 的班次动作连发两次。
    /// ② <b>错过不补</b>：窗口只有 1 分钟。程序没开、画面没跑，那一分钟过去了就过去了——
    ///    只记一条日志，不把昨天漏掉的十条一起补执行（补执行在工业现场比漏执行更危险）。
    /// ③ <b>事件计划是"连续"不是"累计"</b>：条件一断就清计时，重新从 0 开始。
    ///
    /// 线程：<see cref="Tick"/> 由宿主的 UI 节拍调用，动作分发也在 UI 线程（分发器不加锁）。
    /// </summary>
    public sealed class ScadaScheduleEngine
    {
        /// <summary>评估间隔：秒级足够（计划表不承担毫秒时序）</summary>
        public const int EvaluateIntervalMilliseconds = 1000;

        /// <summary>到点窗口（错过这个窗口就不再补）</summary>
        public static readonly TimeSpan DueWindow = TimeSpan.FromMinutes(1);

        private readonly ScadaDocument _document;
        private readonly IScadaValueSource _values;
        private readonly IScadaActionDispatcher _dispatcher;
        private readonly ILogService _log;

        private readonly Dictionary<Guid, string> _lastTimeKey = new();
        private readonly Dictionary<Guid, DateTime> _conditionSince = new();
        private readonly Dictionary<Guid, object?> _lastValue = new();
        private readonly HashSet<Guid> _firedInEpisode = new();

        private DateTime _lastEvaluate = DateTime.MinValue;
        private bool _attached;

        public ScadaScheduleEngine(
            ScadaDocument document,
            IScadaValueSource values,
            IScadaActionDispatcher dispatcher,
            ILogService log)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
            _values = values ?? throw new ArgumentNullException(nameof(values));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        /// <summary>一次触发（计划, 原因）；断言与"上次执行"显示用</summary>
        public event Action<ScadaSchedule, string>? Fired;

        /// <summary>启用中的计划条数</summary>
        public int EnabledCount
        {
            get
            {
                var count = 0;
                foreach (var schedule in _document.Schedules)
                {
                    if (schedule != null && schedule.IsEnabled)
                        count++;
                }

                return count;
            }
        }

        /// <summary>挂上：只记基线，不补发历史</summary>
        public void Attach()
        {
            if (_attached)
                return;

            _attached = true;
            _lastEvaluate = DateTime.MinValue;

            if (EnabledCount > 0)
                _log.Info($"[计划表] 执行器已挂载：{EnabledCount} 条启用");
        }

        public void Detach()
        {
            _attached = false;
            _lastTimeKey.Clear();
            _conditionSince.Clear();
            _lastValue.Clear();
            _firedInEpisode.Clear();
        }

        /// <summary>
        /// 一拍。宿主每 100ms 推一次，这里自己节流到 1s（计划表不需要 10Hz 评估）。
        /// </summary>
        public void Tick(DateTime now)
        {
            if (!_attached)
                return;

            if (_lastEvaluate != DateTime.MinValue
                && (now - _lastEvaluate).TotalMilliseconds < EvaluateIntervalMilliseconds)
            {
                return;
            }

            _lastEvaluate = now;

            foreach (var schedule in _document.Schedules)
            {
                if (schedule == null || !schedule.IsEnabled)
                    continue;

                if (string.Equals(schedule.Kind, ScadaScheduleKinds.Event, StringComparison.OrdinalIgnoreCase))
                    EvaluateEvent(schedule, now);
                else
                    EvaluateTime(schedule, now);
            }
        }

        private void EvaluateTime(ScadaSchedule schedule, DateTime now)
        {
            if (!ScadaScheduleClock.TryDueOccurrence(schedule, now, DueWindow, out var due))
                return;

            var key = ScadaScheduleClock.OccurrenceKey(due);
            if (_lastTimeKey.TryGetValue(schedule.ScheduleId, out var last) && last == key)
                return;

            _lastTimeKey[schedule.ScheduleId] = key;
            Fire(schedule, $"到点 {due:yyyy-MM-dd HH:mm}");
        }

        private void EvaluateEvent(ScadaSchedule schedule, DateTime now)
        {
            if (schedule.VariableId == Guid.Empty && string.IsNullOrWhiteSpace(schedule.VariableName))
                return;

            if (!_values.TryResolve(schedule.VariableId, schedule.VariableName, out var handle) || handle == null)
                return;

            var value = handle.Value;
            var condition = schedule.Condition;

            // 「值改变」没有"持续"语义：一变就触发，不看 DurationSeconds
            if (string.Equals(condition, ScadaScheduleConditions.Changed, StringComparison.OrdinalIgnoreCase))
            {
                if (_lastValue.TryGetValue(schedule.ScheduleId, out var previous)
                    && !Equals(previous, value))
                {
                    _lastValue[schedule.ScheduleId] = value;
                    Fire(schedule, "值改变");
                    return;
                }

                _lastValue[schedule.ScheduleId] = value;
                return;
            }

            var satisfied = IsSatisfied(schedule, value);

            if (!satisfied)
            {
                _conditionSince.Remove(schedule.ScheduleId);
                _firedInEpisode.Remove(schedule.ScheduleId);
                return;
            }

            if (!_conditionSince.TryGetValue(schedule.ScheduleId, out var since))
            {
                _conditionSince[schedule.ScheduleId] = now;
                return;
            }

            if (_firedInEpisode.Contains(schedule.ScheduleId))
                return;

            var required = TimeSpan.FromSeconds(Math.Max(0, schedule.DurationSeconds));
            if (now - since < required)
                return;

            _firedInEpisode.Add(schedule.ScheduleId);
            Fire(schedule, $"条件持续 {required.TotalSeconds:0} 秒");
        }

        /// <summary>条件判定（数值比较用不变文化；非数值按"不满足"处理，不抛）</summary>
        private static bool IsSatisfied(ScadaSchedule schedule, object? value)
        {
            switch (schedule.Condition)
            {
                case ScadaScheduleConditions.IsFalse:
                    return !IsTruthy(value);

                case ScadaScheduleConditions.AboveLimit:
                    return TryToDouble(value, out var above) && above > schedule.UpperLimit;

                case ScadaScheduleConditions.BelowLimit:
                    return TryToDouble(value, out var below) && below < schedule.LowerLimit;

                default:
                    return IsTruthy(value);
            }
        }

        private static bool IsTruthy(object? value) => value switch
        {
            null => false,
            bool flag => flag,
            double number => Math.Abs(number) > double.Epsilon,
            float number => Math.Abs(number) > float.Epsilon,
            int number => number != 0,
            long number => number != 0,
            string text => text.Trim() is "1" or "true" or "True" or "on" or "是",
            _ => false,
        };

        private static bool TryToDouble(object? value, out double number)
        {
            number = 0;
            switch (value)
            {
                case double d when double.IsFinite(d): number = d; return true;
                case float f when float.IsFinite(f): number = f; return true;
                case int i: number = i; return true;
                case long l: number = l; return true;
                case bool b: number = b ? 1 : 0; return true;
                case string text when double.TryParse(text.Trim(), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var parsed):
                    number = parsed;
                    return true;
                default:
                    return false;
            }
        }

        private void Fire(ScadaSchedule schedule, string reason)
        {
            var hook = schedule.FindEventHook(ScadaEventType.ScheduleFired);
            var name = string.IsNullOrWhiteSpace(schedule.Name) ? "未命名计划" : schedule.Name;

            if (hook == null || hook.Actions.Count == 0)
            {
                _log.Warn($"[计划表] 「{name}」{reason}，但没有配动作（跳过）");
                Fired?.Invoke(schedule, reason + "（无动作）");
                return;
            }

            var result = _dispatcher.DispatchWithResult(hook, $"计划表：{name}", "系统（计划表）");

            _log.Info($"[计划表] 「{name}」{reason}：动作 {result.Succeeded}/{result.Total} 成功"
                      + (result.Failed > 0 ? $" / {result.Failed} 失败" : string.Empty));

            Fired?.Invoke(schedule, reason);
        }
    }
}
