using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Core.Interfaces;
using VisionMaster.Binding;
using VisionMaster.EventModel;
using VisionMaster.Models;

namespace VisionMaster.Services
{
    /// <summary>
    /// 流程运行统计来源：订阅引擎事件，维护每流程的节拍 / OK / NG / 未判定 / 当前节点，
    /// 投影给 <see cref="Binding.FlowRuntimeVariableCatalog"/> 与底栏。
    ///
    /// 【L2 起的分工】
    /// - 引擎每轮产出一条 <see cref="FlowRoundResult"/>（判定由宿主 <see cref="IFlowVerdictResolver"/> 给）；
    /// - 本类只做"消费记录 + 维护当前节点/快照"，不再自己解析判定、不再自己算节拍；
    /// - 轮次台账 <see cref="FlowRoundLedger"/> 直接存引擎记录，下钻读它。
    ///
    /// 【归属】按 <c>FlowId</c>（= FlowModel.FlowID）；取不到才按流程名兜底。
    /// 不维护"最后开轮的那条流程"——多流程并发时那会把 A 的轮次记到 B 头上。
    /// </summary>
    public sealed class FlowRuntimeVariableSource : IFlowRuntimeSource, IDisposable
    {
        private const int CycleSampleCapacity = 20;
        private const int RoundHistoryCapacity = 50;
        private const int TickIntervalMs = 200;

        private sealed class FlowStat
        {
            public Guid FlowId;
            public string FlowName = string.Empty;
            public string Role = "普通";
            public string State = "停止";
            public bool IsRunning;
            public bool IsDebug;
            public bool HasRound;
            public double LastCycleMs;
            public readonly Queue<double> CycleSamples = new();
            public long RoundCount;
            public long OkCount;
            public long NgCount;
            public long UnknownCount;
            public string CurrentStep = string.Empty;
            public int CurrentStepIndex;
            public int StepCount;
            public string LastRoundAt = string.Empty;
            public string LastReason = string.Empty;
            public string VerdictSource = "未配置";
        }

        private readonly object _gate = new();
        private readonly FlowEngineService _engine;
        private readonly IWorkspaceManager _workspace;
        private readonly ILogService _log;
        private readonly Timer _timer;
        private readonly Dictionary<Guid, FlowStat> _stats = new();
        private readonly FlowRoundLedger _rounds = new(RoundHistoryCapacity);
        private readonly Dictionary<string, FlowSession> _sessions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Guid> _sessionFlow = new(StringComparer.Ordinal);

        private SolutionModel _lastSolution;
        private IReadOnlyList<FlowRuntimeSnapshot> _snapshot = Array.Empty<FlowRuntimeSnapshot>();
        private bool _disposed;

        public FlowRuntimeVariableSource(
            FlowEngineService engine,
            IWorkspaceManager workspace,
            ILogService log)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            _log = log ?? throw new ArgumentNullException(nameof(log));

            _engine.FlowRunStarted += OnFlowRunStarted;
            _engine.FlowRoundCompleted += OnFlowRoundCompleted;
            _engine.SessionStateChanged += OnSessionStateChanged;

            _timer = new Timer(Tick, null, TickIntervalMs, TickIntervalMs);
        }

        public event EventHandler Changed;

        public IReadOnlyList<FlowRuntimeSnapshot> Snapshot()
        {
            lock (_gate) return _snapshot;
        }

        /// <summary>最近一轮记录（没有则 null）。轮次下钻用。</summary>
        public FlowRoundResult? GetLatestRound(Guid flowId)
        {
            lock (_gate) return _rounds.Latest(flowId);
        }
        /// <summary>最近 N 轮记录（旧 → 新）。</summary>
        public IReadOnlyList<FlowRoundResult> GetRecentRounds(Guid flowId, int max)
        {
            lock (_gate) return _rounds.Recent(flowId, max);
        }

        private void OnFlowRunStarted(FlowSession session)
        {
            if (session == null) return;
            Guid flowId = ResolveFlowId(session);
            if (flowId == Guid.Empty) return;

            lock (_gate)
            {
                var stat = GetOrCreateStat(flowId);
                stat.FlowName = session.FlowName ?? stat.FlowName;
                stat.IsRunning = true;
                stat.IsDebug = session.DebugEnabled;
                stat.State = "运行";
                stat.CurrentStep = string.Empty;
                stat.CurrentStepIndex = 0;
                _sessions[session.SessionID] = session;
                _sessionFlow[session.SessionID] = flowId;
            }
        }

        /// <summary>
        /// 引擎每轮记录：计数/节拍/原因/台账都从这里来。
        /// 调试轮只更新"调试轮"标记，不计数、不落台账。
        /// </summary>
        private void OnFlowRoundCompleted(FlowRoundResult result)
        {
            if (result == null) return;

            Guid flowId = result.FlowId != Guid.Empty
                ? result.FlowId
                : ResolveFlowIdByName(result.FlowName);
            if (flowId == Guid.Empty) return;

            lock (_gate)
            {
                var stat = GetOrCreateStat(flowId);
                stat.FlowName = result.FlowName ?? stat.FlowName;
                stat.LastCycleMs = result.CycleTimeMs;
                stat.CycleSamples.Enqueue(result.CycleTimeMs);
                while (stat.CycleSamples.Count > CycleSampleCapacity)
                    stat.CycleSamples.Dequeue();
                stat.LastRoundAt = result.CompletedAtUtc.ToLocalTime().ToString("HH:mm:ss");
                stat.LastReason = result.Reason ?? string.Empty;
                stat.VerdictSource = result.VerdictSourceText;

                if (result.IsDebug)
                {
                    stat.VerdictSource = "调试轮";
                    return;
                }

                stat.RoundCount++;
                stat.HasRound = true;
                switch (result.Outcome)
                {
                    case FlowRoundOutcome.Ok: stat.OkCount++; break;
                    case FlowRoundOutcome.Ng: stat.NgCount++; break;
                    default: stat.UnknownCount++; break;
                }

                _rounds.Add(result);
            }
        }

        private void OnSessionStateChanged(object sender, SessionStateChangedEventArgs e)
        {
            if (e == null) return;

            lock (_gate)
            {
                if (!_sessionFlow.TryGetValue(e.SessionId ?? string.Empty, out var flowId))
                    return;

                var stat = GetOrCreateStat(flowId);
                stat.State = e.NewState switch
                {
                    SessionState.Running => "运行",
                    SessionState.Paused => "暂停",
                    SessionState.Faulted => "故障",
                    _ => "停止"
                };
                stat.IsRunning = e.NewState is SessionState.Running or SessionState.Paused;

                if (!stat.IsRunning)
                {
                    _sessions.Remove(e.SessionId ?? string.Empty);
                    _sessionFlow.Remove(e.SessionId ?? string.Empty);
                    stat.CurrentStep = string.Empty;
                    stat.CurrentStepIndex = 0;
                }
            }
        }

        private void Tick(object state)
        {
            if (_disposed) return;
            bool changed = false;

            try
            {
                lock (_gate)
                {
                    var solution = _workspace.CurrentSolution;
                    if (!ReferenceEquals(solution, _lastSolution))
                    {
                        // 换方案：计数/台账归零（系统变量不落盘，重启/换方案从头开始）。
                        _stats.Clear();
                        _rounds.Clear();
                        _sessions.Clear();
                        _sessionFlow.Clear();
                        _lastSolution = solution;
                    }

                    // 当前节点：只读主线程焦点；并行分支按设计不碰 FocusedStep（不冒充全部）。
                    foreach (var pair in _sessions.ToList())
                    {
                        var session = pair.Value;
                        if (session == null || !session.IsRunning)
                        {
                            _sessions.Remove(pair.Key);
                            _sessionFlow.Remove(pair.Key);
                            continue;
                        }

                        if (_sessionFlow.TryGetValue(pair.Key, out var flowId))
                        {
                            var stat = GetOrCreateStat(flowId);
                            stat.CurrentStep = session.FocusedStep?.StepName ?? string.Empty;
                            stat.CurrentStepIndex = IndexOfFocusedStep(session);
                        }
                    }

                    var flows = solution?.Flows;
                    var alive = new HashSet<Guid>();
                    var list = new List<FlowRuntimeSnapshot>(flows?.Count ?? 0);

                    if (flows != null)
                    {
                        foreach (var flow in flows)
                        {
                            if (flow == null || string.IsNullOrEmpty(flow.FlowID)) continue;
                            if (!Guid.TryParse(flow.FlowID, out var flowId) || flowId == Guid.Empty) continue;

                            alive.Add(flowId);
                            var stat = GetOrCreateStat(flowId);
                            stat.FlowName = flow.FlowName ?? stat.FlowName;
                            stat.Role = RoleText(flow);
                            stat.StepCount = CountSteps(flow.Steps);

                            if (!stat.IsRunning)
                            {
                                stat.State = "停止";
                                stat.CurrentStep = string.Empty;
                                stat.CurrentStepIndex = 0;
                            }

                            list.Add(new FlowRuntimeSnapshot(
                                flowId,
                                stat.FlowName,
                                stat.Role,
                                stat.State,
                                stat.IsRunning,
                                stat.IsDebug,
                                stat.HasRound,
                                stat.LastCycleMs,
                                stat.CycleSamples.Count == 0 ? 0 : stat.CycleSamples.Average(),
                                stat.RoundCount,
                                stat.OkCount,
                                stat.NgCount,
                                stat.UnknownCount,
                                stat.CurrentStep,
                                stat.CurrentStepIndex,
                                stat.StepCount,
                                stat.LastRoundAt,
                                stat.LastReason,
                                stat.VerdictSource));
                        }
                    }

                    foreach (var stale in _stats.Keys.Where(id => !alive.Contains(id)).ToList())
                    {
                        _stats.Remove(stale);
                        _rounds.Remove(stale);
                    }

                    if (!_snapshot.SequenceEqual(list))
                    {
                        _snapshot = list;
                        changed = true;
                    }
                }
            }
            catch (Exception ex)
            {
                // 定时器线程上的异常绝不能外抛：ThreadPool 未处理异常会直接退进程。
                // 采集失败保留上一拍快照，下一拍再试。
                _log.Warn($"[流程统计] 采样失败（保留上一拍）：{ex.Message}");
                return;
            }

            if (changed)
            {
                try { Changed?.Invoke(this, EventArgs.Empty); }
                catch (Exception ex) { _log.Warn($"[流程统计] 通知订阅方失败：{ex.Message}"); }
            }
        }

        private Guid ResolveFlowId(FlowSession session)
        {
            if (!string.IsNullOrEmpty(session.CompiledFlowId)
                && Guid.TryParse(session.CompiledFlowId, out var compiledId)
                && compiledId != Guid.Empty)
            {
                return compiledId;
            }

            return ResolveFlowIdByName(session.FlowName);
        }

        private Guid ResolveFlowIdByName(string flowName)
        {
            var flows = _workspace.CurrentSolution?.Flows;
            if (flows == null || string.IsNullOrEmpty(flowName)) return Guid.Empty;

            foreach (var flow in flows)
            {
                if (flow == null) continue;
                if (!string.Equals(flow.FlowName, flowName, StringComparison.Ordinal)) continue;
                return Guid.TryParse(flow.FlowID, out var id) ? id : Guid.Empty;
            }

            return Guid.Empty;
        }

        private FlowStat GetOrCreateStat(Guid flowId)
        {
            if (_stats.TryGetValue(flowId, out var stat)) return stat;
            stat = new FlowStat { FlowId = flowId };
            _stats[flowId] = stat;
            return stat;
        }

        private static int IndexOfFocusedStep(FlowSession session)
        {
            var focused = session.FocusedStep;
            if (focused == null) return 0;
            int index = session.Blueprints.IndexOf(focused);
            return index < 0 ? 0 : index + 1;
        }

        private static string RoleText(FlowModel flow)
        {
            // 整线成员优先于"子程序"：工位流程通常同时勾「子程序」与「整线」，
            // 它是要参与生产的（底栏计入、行按整线排在最前），不是纯子程序。
            if (flow.Role == FlowRole.None && flow.IsLineStartMember)
                return "整线";

            // 子程序流程（Role=None 且勾了「子程序调用」）不产生产品，底栏不计入；
            // 仍投影成系统变量，SCADA 想单独看它的调用统计时可以绑。
            if (flow.Role == FlowRole.None && (flow.InvokeType & FlowInvokeType.Subroutine) != 0)
                return "子程序";

            return flow.Role switch
            {
                FlowRole.Home => "回原点",
                FlowRole.End => "收尾",
                _ => "普通"
            };
        }

        private static int CountSteps(IEnumerable<StepModel> steps)
        {
            if (steps == null) return 0;
            int count = 0;
            foreach (var step in steps)
            {
                if (step == null) continue;
                count++;
                if (step is IContainerStep container && container.Children != null)
                {
                    foreach (var branch in container.Children)
                        count += CountSteps(branch?.Steps);
                }
            }
            return count;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Dispose();
            _engine.FlowRunStarted -= OnFlowRunStarted;
            _engine.FlowRoundCompleted -= OnFlowRoundCompleted;
            _engine.SessionStateChanged -= OnSessionStateChanged;
        }
    }
}
