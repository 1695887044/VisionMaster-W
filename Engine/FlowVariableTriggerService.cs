using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using Core.Interfaces;
using VisionMaster.Models;

namespace VisionMaster.Services
{
    /// <summary>
    /// 变量触发服务：<see cref="FlowModel.TriggerVariable"/> 指定的变量**由假变真**时，
    /// 把勾选了「变量」调用方式的流程跑一遍（单次执行，非调试路径）。
    ///
    /// 触发语义（刻意收窄，避免现场被"刷爆"）：
    ///  · 只在**上升沿**触发一次（假 → 真）。任何变化都触发的话，一个每秒刷新的计数变量
    ///    会把流程刷爆——这是现场最典型的踩法；
    ///  · "真值"判定见 <see cref="IsTruthy"/>：bool 取自身、数值非 0、字符串非空白、其它非空算真；
    ///  · 目标正在运行 → 本拍跳过（不排队、不补跑），与定时调度同一口径。
    ///
    /// 订阅面：工作区全局变量集合（<c>CollectionChanged</c>）+ 每个变量的 <c>ValueChanged</c>。
    /// 集合变化（方案切换是 Replace/Reset，不带明细）统一全量重挂——变量数量级很小，
    /// 而"漏挂一个变量"的代价是"这条触发链路静默失效"，不值得为省这点开销写增量逻辑。
    /// </summary>
    public sealed class FlowVariableTriggerService : IDisposable
    {
        private readonly IWorkspaceManager _workspace;
        private readonly IRuntimeManager _runtime;
        private readonly IFlowEngine _engine;
        private readonly FlowCompiler _compiler;
        private readonly ILogService _log;

        /// <summary>已挂 ValueChanged 的变量（防重复挂：重挂走"先全摘再全挂"）</summary>
        private readonly HashSet<IVariable> _watched = new();

        /// <summary>每条"流程 × 变量"组合的上一次真值（上升沿判据），键 = 流程名 + \n + 变量名</summary>
        private readonly Dictionary<string, bool> _lastTruthy = new(StringComparer.Ordinal);

        private bool _started;

        public FlowVariableTriggerService(
            IWorkspaceManager workspace,
            IRuntimeManager runtime,
            IFlowEngine engine,
            FlowCompiler compiler,
            ILogService log)
        {
            _workspace = workspace;
            _runtime = runtime;
            _engine = engine;
            _compiler = compiler;
            _log = log;
        }

        public void Start()
        {
            if (_started) return;
            _started = true;

            var variables = _workspace?.GlobalVariables;
            if (variables == null) return;

            lock (_gate)
            {
                variables.CollectionChanged += OnVariablesCollectionChanged;
                ResubscribeAll();
            }
        }

        public void Dispose()
        {
            if (!_started) return;
            _started = false;

            var variables = _workspace?.GlobalVariables;
            if (variables != null) variables.CollectionChanged -= OnVariablesCollectionChanged;

            lock (_gate)
            {
                foreach (var v in _watched) v.ValueChanged -= OnVariableValueChanged;
                _watched.Clear();
            }
        }

        private void OnVariablesCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            lock (_gate)
            {
                // 不按 New/OldItems 增量摘挂：方案切换走 Reset/Replace 不带明细，
                // 增量逻辑漏一种情形就是"变量换了、触发还挂在旧对象上"（静默失效）
                foreach (var v in _watched) v.ValueChanged -= OnVariableValueChanged;
                _watched.Clear();

                // 方案换了，上升沿账本一并作废：否则"新方案里同名流程 + 同名变量"的首个上升沿
                // 会因为账本还记着"已经是真"被吞掉（一次都不触发）
                _lastTruthy.Clear();

                ResubscribeAll();
            }
        }

        private void ResubscribeAll()
        {
            var variables = _workspace?.GlobalVariables;
            if (variables == null) return;

            foreach (var v in variables)
            {
                if (v != null && _watched.Add(v))
                    v.ValueChanged += OnVariableValueChanged;
            }
        }

        private void OnVariableValueChanged(object sender, EventArgs e) => Evaluate(sender as IVariable);

        /// <summary>判定与触发的线程闸：事件可能来自 UI / 通信轮询 / 插件执行三类线程</summary>
        private readonly object _gate = new();

        /// <summary>
        /// 变量值变化后的判定与触发。**公开**：检查工程直接喂变量对象驱动，
        /// 不必依赖真实事件时序（真实现由 ValueChanged 事件调进来）。
        ///
        /// 三类线程都会调到这里（UI 线程的变量编辑 / 通信轮询线程的网络变量镜像 / 插件线程的写变量），
        /// 而遍历的流程集合是 UI 可变的 ObservableCollection、上升沿账本是普通字典——
        /// 因此：**整体加锁 + 集合快照 + 异常就地隔离**。异常不能外溢：它会顺着
        /// ValueChanged 抛回变量 setter / 通信回调 / 插件 RunAlgorithm，把一次触发失败升级成一次执行失败。
        /// </summary>
        public void Evaluate(IVariable variable)
        {
            try
            {
                lock (_gate)
                {
                    EvaluateCore(variable);
                }
            }
            catch (Exception ex)
            {
                _log?.Warn($"[变量触发] 判定失败（已隔离，不影响写变量的那一方）：{ex.Message}");
            }
        }

        private void EvaluateCore(IVariable variable)
        {
            if (variable == null) return;

            var variableName = variable.Name;
            if (string.IsNullOrEmpty(variableName)) return;

            var truthy = IsTruthy(variable.Value);

            var flows = _workspace?.CurrentSolution?.Flows;
            if (flows == null) return;

            // 快照再遍历：UI 线程增删流程会让 ObservableCollection 的枚举器抛版本校验异常
            var snapshot = new List<FlowModel>(flows);

            foreach (var flow in snapshot)
            {
                if (flow == null || !flow.IsEnabled) continue;
                if ((flow.InvokeType & FlowInvokeType.Variable) == 0) continue;
                if (!string.Equals(flow.TriggerVariable?.Trim(), variableName, StringComparison.Ordinal)) continue;

                var key = flow.FlowName + "\n" + variableName;
                var wasTruthy = _lastTruthy.TryGetValue(key, out var b) && b;
                _lastTruthy[key] = truthy;

                if (!truthy || wasTruthy) continue;   // 只在上升沿触发，理由见类型注释

                FlowAutoRunner.RunOnce(_workspace, _runtime, _engine, _compiler, _log, flow, "变量触发");
            }
        }

        /// <summary>
        /// "真值"判定（可断言）：bool 直接取；数值非 0；字符串非空白；其它非空对象一律算真。
        /// 刻意把"其它类型算真"放在最后：HImage / 结果对象这类"有值即意味着发生了一件事"，
        /// 判成假会让触发永远不响，比多触发一次更难查。
        /// </summary>
        public static bool IsTruthy(object value)
        {
            switch (value)
            {
                case null: return false;
                case bool b: return b;
                case string s: return !string.IsNullOrWhiteSpace(s);
                case double d: return d != 0;
                case float f: return f != 0;
                case decimal m: return m != 0;
                case int i: return i != 0;
                case long l: return l != 0;
                case short sh: return sh != 0;
                case byte by: return by != 0;
                default: return true;
            }
        }
    }
}
