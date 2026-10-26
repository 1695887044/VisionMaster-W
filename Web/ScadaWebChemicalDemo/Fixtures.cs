using System;
using System.Collections.Generic;
using System.Globalization;
using Core.Interfaces;
using VisionMaster.Scada;

namespace ScadaWebChemicalDemo
{
    /// <summary>
    /// 一个模拟工程变量：真实量纲、软性回归的随机游走、可写回。
    ///
    /// <b>为什么带软性回归</b>
    /// ---------
    /// 纯随机游走跑几小时一定会漂出量程（要么贴上限、要么贴下限），
    /// 那时画面上所有仪表都顶死，反而压不出"中间值"的渲染分支。
    /// 每次向设定值回拉 2% 的偏离量，就能让它长期绕着设定值转，
    /// 而短期看仍然是活的数——这正是现场模拟量在做的。
    /// </summary>
    internal sealed class PlantVariable
    {
        private readonly Random _random;
        private double _value;

        public PlantVariable(Guid variableId, string name, string unit, double initial,
            double low, double high, double drift)
        {
            VariableId = variableId;
            Name = name;
            Unit = unit;
            SetPoint = initial;
            Low = low;
            High = high;
            Drift = drift;
            _value = initial;

            // 每个变量自己的随机源（用 Guid 的低 32 位当种子）：
            // 共用一个 Random 会让所有变量同步抖动，画面上看着像"整屏一起跳"。
            _random = new Random(unchecked((int)variableId.GetHashCode()));
        }

        public Guid VariableId { get; }
        public string Name { get; }
        public string Unit { get; }
        public double SetPoint { get; }
        public double Low { get; }
        public double High { get; }
        public double Drift { get; }

        public bool IsText { get; init; }
        public bool IsBoolean { get; init; }

        /// <summary>
        /// 文本变量的当前值（可写：批号、操作备注由操作员填）。
        /// 只能经 <see cref="TryWrite"/> 改——写路径只有一条，
        /// 才能保证"写入成功 → 发 ValueChanged → 数据泵刷新画面"这条链不被绕过。
        /// </summary>
        public string TextValue { get; private set; } = string.Empty;

        /// <summary>文本长度上限（与 TextFieldElement.MaxLength 的默认 64 同一条口径）</summary>
        public int MaxTextLength { get; init; } = 64;

        /// <summary>构造期给文本变量一个初值（只有夹具组装时调用）</summary>
        public void SetInitialText(string text) => TextValue = text ?? string.Empty;

        public Type DataType => IsText ? typeof(string) : IsBoolean ? typeof(bool) : typeof(double);

        public object? Value => IsText ? TextValue : IsBoolean ? _value >= 0.5 : _value;

        public event EventHandler? ValueChanged;

        /// <summary>推进一轮（模拟 PLC 轮询的新值）</summary>
        public void Advance()
        {
            if (IsText || Drift <= 0)
                return;

            // 软性回归 + 抖动：偏离越大，回拉越强
            var pull = (SetPoint - _value) * 0.02;
            var jitter = (_random.NextDouble() - 0.5) * 2 * Drift;
            var next = _value + pull + jitter;

            if (IsBoolean)
            {
                // 开关量：偶尔翻一次，不要每拍都跳
                if (_random.NextDouble() > 0.995)
                    next = _value >= 0.5 ? 0 : 1;
                else
                    next = _value;
            }

            _value = Math.Clamp(next, Low, High);
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }

        public bool TryWrite(object? value, out string? error)
        {
            error = null;

            if (IsText)
            {
                var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;

                // 文本变量**可写**：批号 / 操作备注在真实现场就是操作员填的。
                // 只做长度约束（与 TextFieldElement 的 MaxLength 同一条口径）：
                // 空串表示"清除文本"，是合法操作，不当失败。
                if (text.Length > MaxTextLength)
                {
                    error = $"「{Name}」的文本长度 {text.Length} 超过上限 {MaxTextLength}";
                    return false;
                }

                if (string.Equals(text, TextValue, StringComparison.Ordinal))
                    return true; // 同值不刷事件

                TextValue = text;
                ValueChanged?.Invoke(this, EventArgs.Empty);
                return true;
            }

            double parsed;

            if (value is bool flag)
            {
                parsed = flag ? 1 : 0;
            }
            else if (!double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture),
                         NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            {
                error = $"「{Name}」的值「{value}」不是有效数字";
                return false;
            }

            if (parsed < Low || parsed > High)
            {
                error = $"「{Name}」的值 {parsed:0.###} 超出量程 [{Low:0.###}, {High:0.###}]";
                return false;
            }

            _value = parsed;
            ValueChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        /// <summary>当前值的一句话描述（控制台日志用）</summary>
        public string Describe()
        {
            var value = Value;
            var text = value is double number
                ? number.ToString("0.###", CultureInfo.InvariantCulture)
                : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;

            return string.IsNullOrEmpty(Unit) ? text : text + " " + Unit;
        }
    }

    /// <summary>值源：把变量名/Id 解析成运行态句柄</summary>
    internal sealed class PlantValueSource : IScadaValueSource
    {
        private readonly Dictionary<Guid, PlantHandle> _byId = new();
        private readonly Dictionary<string, PlantHandle> _byName = new(StringComparer.Ordinal);

        public PlantValueSource(IReadOnlyList<PlantVariable> variables)
        {
            foreach (var variable in variables)
            {
                var handle = new PlantHandle(variable);
                _byId[variable.VariableId] = handle;
                _byName[variable.Name] = handle;
            }
        }

        public bool TryResolve(Guid variableId, string? fallbackName, out IScadaValueHandle? handle)
        {
            if (variableId != Guid.Empty && _byId.TryGetValue(variableId, out var byId))
            {
                handle = byId;
                return true;
            }

            if (!string.IsNullOrWhiteSpace(fallbackName)
                && _byName.TryGetValue(fallbackName!, out var byName))
            {
                handle = byName;
                return true;
            }

            handle = null;
            return false;
        }
    }

    /// <summary>值句柄：把 PlantVariable 的读写转给运行态</summary>
    internal sealed class PlantHandle : IScadaValueHandle
    {
        private readonly PlantVariable _variable;

        public PlantHandle(PlantVariable variable)
        {
            _variable = variable;
            _variable.ValueChanged += (_, _) => ValueChanged?.Invoke(this, EventArgs.Empty);
        }

        public Guid VariableId => _variable.VariableId;
        public string Name => _variable.Name;
        public Type DataType => _variable.DataType;
        public object? Value => _variable.Value;

        public event EventHandler? ValueChanged;

        public bool TryWrite(object? value, out string? error)
            => _variable.TryWrite(value, out error);
    }

    /// <summary>
    /// 值写入器（<c>cmd.input</c> 的落点）：把"用户在网页上改了一个数"写回模拟变量。
    ///
    /// 这是夹具与真实宿主唯一的差别：真实宿主写的是工程变量注册表，
    /// 这里写的是模拟量。接口语义一致，所以命令链路（权限→确认→幂等→审计→回执）全都是真的。
    /// </summary>
    internal sealed class PlantValueWriter : IScadaValueWriter
    {
        private readonly Dictionary<Guid, PlantVariable> _byElement = new();

        public PlantValueWriter(ChemicalPlant plant)
        {
            // 从变量表建索引：图元绑定里带着 VariableId，写入时按它反查
            foreach (var variable in plant.Variables)
                _byElement[variable.VariableId] = variable;
        }

        public bool TryWriteText(ScadaElement element, string targetProperty, string? text, out string? error)
        {
            error = null;

            if (element == null)
            {
                error = "图元为空";
                return false;
            }

            var binding = element.FindBinding(targetProperty);
            if (binding == null)
            {
                error = $"图元「{element.Name}」的 {targetProperty} 没有绑定变量";
                return false;
            }

            if (!_byElement.TryGetValue(binding.VariableId, out var variable))
            {
                error = $"找不到变量「{binding.VariableName}」";
                return false;
            }

            var ok = variable.TryWrite(text, out error);
            if (ok)
                Console.WriteLine($"[写入] {variable.Name} := {text}");

            return ok;
        }

        public bool TryReadText(ScadaElement element, string targetProperty, out string? text)
        {
            text = null;

            var binding = element?.FindBinding(targetProperty);
            if (binding == null)
                return false;

            if (!_byElement.TryGetValue(binding.VariableId, out var variable))
                return false;

            text = Convert.ToString(variable.Value, CultureInfo.InvariantCulture);
            return true;
        }
    }

    /// <summary>报警确认出口（<c>cmd.alarm.ack</c> 的落点）：真转给报警引擎</summary>
    internal sealed class PlantAlarmCommands : IScadaAlarmCommands
    {
        private readonly ScadaAlarmEngine _engine;

        public PlantAlarmCommands(ScadaAlarmEngine engine) => _engine = engine;

        public bool TryAcknowledge(Guid alarmId, out string? error)
        {
            error = null;

            var changed = _engine.Acknowledge(alarmId);
            if (changed > 0)
            {
                Console.WriteLine($"[报警确认] {alarmId:D}");
                return true;
            }

            error = $"没有编号为 {alarmId:D} 的待确认报警";
            return false;
        }

        public int AcknowledgeAll(out string? error)
        {
            error = null;

            var count = _engine.AcknowledgeAll();
            if (count > 0)
                Console.WriteLine($"[报警确认] 全部确认 {count} 条");

            return count;
        }
    }

    /// <summary>报警只读快照出口（AckButton 的"最早一条"与未确认计数）</summary>
    internal sealed class PlantAlarmFeed : IScadaAlarmFeed
    {
        private readonly ScadaAlarmEngine _engine;

        public PlantAlarmFeed(ScadaAlarmEngine engine) => _engine = engine;

        public ScadaAlarmFeedSnapshot Snapshot()
        {
            var active = _engine.ActiveAlarms;
            if (active.Count == 0)
                return ScadaAlarmFeedSnapshot.Empty;

            var alarms = new List<ScadaAlarmInfo>(active.Count);

            foreach (var record in active)
            {
                alarms.Add(new ScadaAlarmInfo(
                    record.AlarmId,
                    record.Name,
                    record.Message,
                    record.Severity,
                    record.ActivatedAtUtc,
                    record.State == ScadaAlarmState.Acknowledged));
            }

            return new ScadaAlarmFeedSnapshot(alarms, _engine.UnacknowledgedCount);
        }
    }

    /// <summary>运行态出口：把 Web 的快照/命令接到底座上这个会话</summary>
    internal sealed class DemoHub : IScadaRunHub
    {
        private readonly ScadaRuntime _runtime;
        private readonly ScadaDocument _document;

        public DemoHub(ScadaRuntime runtime, ScadaDocument document, long generation)
        {
            _runtime = runtime;
            _document = document;
            RunGeneration = generation;
        }

        public bool IsRunning => _runtime.IsRunning;
        public ScadaRuntime? Session => _runtime;
        public ScadaDocument? Document => _document;
        public long RunGeneration { get; }

        public bool TryGetCurrent(out ScadaRuntime? session, out ScadaDocument? document, out long generation)
        {
            session = _runtime;
            document = _document;
            generation = RunGeneration;
            return _runtime.IsRunning;
        }

        public event Action<ScadaRuntime, ScadaDocument, long>? RunStarted;
        public event Action<long>? RunStopped;
    }

    /// <summary>
    /// 动作分发器：按钮按下后真执行动作表。
    ///
    /// 与真实宿主（<c>VisionMaster\Services\ScadaActionDispatcher</c>）的分工一致：
    /// 这里只做"记日志 + 跳页"两件事，因为它要压的是 Web 命令链路，
    /// 不是动作执行器本身（那一条由桌面端的断言与流程覆盖）。
    /// </summary>
    internal sealed class PlantActionDispatcher : IScadaActionDispatcher
    {
        private readonly ScadaRuntime _runtime;
        private readonly ScadaDocument _document;
        private readonly PlantValueSource _values;
        private readonly ILogService _log;
        private int _count;

        /// <summary>累计执行过的动作条数（压测用：证明命令真的到了分发器）</summary>
        public int Count => Volatile.Read(ref _count);

        public PlantActionDispatcher(ScadaRuntime runtime, ScadaDocument document,
            PlantValueSource values, ILogService log)
        {
            _runtime = runtime;
            _document = document;
            _values = values;
            _log = log;
        }

        public void Dispatch(ScadaEventHook? hook, string? sourceName, string? operatorName = null)
            => _ = DispatchWithResult(hook, sourceName, operatorName);

        public ScadaActionDispatchResult DispatchWithResult(ScadaEventHook? hook,
            string? sourceName, string? operatorName = null)
        {
            if (hook == null || hook.Actions.Count == 0)
                return ScadaActionDispatchResult.Empty;

            var succeeded = 0;
            var failed = 0;

            foreach (var action in hook.Actions)
            {
                try
                {
                    if (Execute(action, sourceName, operatorName))
                        succeeded++;
                    else
                        failed++;
                }
                catch (Exception ex)
                {
                    failed++;
                    _log.Warn($"[动作] {action.Type.DisplayName()} 执行异常：{ex.Message}");
                }
            }

            return new ScadaActionDispatchResult(hook.Actions.Count, succeeded, 0, failed);
        }

        private bool Execute(ScadaAction action, string? sourceName, string? operatorName)
        {
            Interlocked.Increment(ref _count);

            var who = string.IsNullOrWhiteSpace(operatorName) ? "未登录" : operatorName;

            switch (action.Type)
            {
                case ScadaActionType.Log:
                    _log.Info($"[操作] {who} · {sourceName} · {action.Text}");
                    return true;

                case ScadaActionType.Navigate:
                {
                    var ok = _runtime.Navigate(action.TargetPageId, action.TargetPageName, out var reason);

                    if (ok)
                        _log.Info($"[操作] {who} · {sourceName} · 切换画面 → {action.TargetPageName}");
                    else
                        _log.Warn($"[操作] {who} · {sourceName} · 切换画面失败：{reason}");

                    return ok;
                }

                case ScadaActionType.NavigateBack:
                {
                    var ok = ((IScadaNavigator)_runtime).GoBack(out var reason);

                    if (ok)
                        _log.Info($"[操作] {who} · {sourceName} · 返回上一页");
                    else
                        _log.Warn($"[操作] {who} · {sourceName} · 返回失败：{reason}");

                    return ok;
                }

                case ScadaActionType.WriteVariable:
                {
                    if (!_values.TryResolve(action.VariableId, action.VariableName, out var handle)
                        || handle == null)
                    {
                        _log.Warn($"[操作] 写变量失败：找不到变量「{action.VariableName}」");
                        return false;
                    }

                    var ok = handle.TryWrite(action.Value, out var error);

                    if (ok)
                        _log.Info($"[操作] {who} · {sourceName} · {handle.Name} := {action.Value}");
                    else
                        _log.Warn($"[操作] 写变量失败：{error}");

                    return ok;
                }

                default:
                    // 流程/运动类动作在这个夹具里没有接线：如实说"未接入"，
                    // 而不是静默成功（静默成功会让"点了没反应"变成谜）。
                    _log.Warn($"[操作] {action.Type.DisplayName()}：夹具未接入该动作的宿主实现");
                    return false;
            }
        }
    }

    /// <summary>
    /// 账号：一个管理员、一个操作员。
    /// B3 之后身份是「用户组 + 权限 Key」——管理员拿全部内置 Key，操作员只有看画面 + 运行控制，
    /// 所以"谁能点哪个按钮"由按钮上的 <c>RequiredPermission</c> 决定，不再有"最高角色"这回事。
    /// </summary>
    internal sealed class PlantAuthenticator : IScadaUserAuthenticator
    {
        private static readonly string[] AdminPerms = ScadaPermissionKeys.BuiltInKeys.ToArray();

        private static readonly string[] OperatorPerms =
        {
            ScadaPermissionKeys.ViewRuntime,
            ScadaPermissionKeys.ControlRun,
        };

        public bool TryValidate(string? userName, string? password, out ScadaUserInfo? user, out string? error)
        {
            user = null;
            error = null;

            if (string.Equals(userName?.Trim(), "admin", StringComparison.OrdinalIgnoreCase)
                && password == "admin123")
            {
                user = new ScadaUserInfo("admin", "tpl-admin", AdminPerms, "模板·管理员");
                return true;
            }

            if (string.Equals(userName?.Trim(), "op", StringComparison.OrdinalIgnoreCase)
                && password == "op123")
            {
                user = new ScadaUserInfo("op", "G-lineop", OperatorPerms, "产线操作员");
                return true;
            }

            error = "用户名或密码不正确";
            return false;
        }

        public bool IsUsingDefaultPassword(string? userName) => false;
    }

    /// <summary>审计出口：打到控制台（现场看的是审计文件，这里够看"每条命令都到了"）</summary>
    internal sealed class ConsoleAudit : IScadaAuditSink
    {
        public bool Append(string actor, string sessionId, string clientAddress, string subject,
            string eventText, string action, string result, string detail)
        {
            Console.WriteLine($"[审计] {actor}@{clientAddress} {subject} {eventText}/{action} {result} {detail}");
            return true;
        }
    }

    /// <summary>日志：打到控制台，格式与宿主 LogService 的级别前缀一致</summary>
    internal sealed class ConsoleLog : ILogService
    {
        public void Success(params string[] messages) => Write("OK", messages);
        public void Error(params Exception[] messages) => Write("ERR", messages.Select(e => e.Message));
        public void Error(params string[] messages) => Write("ERR", messages);
        public void Info(params string[] messages) => Write("INFO", messages);
        public void Warn(params string[] messages) => Write("WARN", messages);

        private static void Write(string level, IEnumerable<string> messages)
        {
            foreach (var message in messages)
                Console.WriteLine($"[{level}] {message}");
        }
    }
}
