using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using VisionMaster.Scada;
using VisionMaster.Scada.Controls;
using VisionMaster.Services;
using VisionMaster.ViewModels;

namespace ScadaChecks
{
    internal partial class Program
    {
        /// <summary>
        /// v6 四项修复的回归（进常规闸门，每次 run_gates 都会跑）。
        ///
        /// 与 <see cref="V6StressChecks"/> 的分工：那边是"乱来也不崩"的暴力组（只在 --stress-v6 跑），
        /// 这边只钉四条**会被改坏且改坏不报错**的语义：
        ///   F1 首拍只建立基线、不发激活/取消激活；
        ///   F2 链式延时的日志对象名只带一个「（延时后）」；
        ///   F4 容量是整数类型，且手改文件里的 "200.0" 能读回 200；
        ///   F3 位号模式的提示里写清"负数按补码看位"。
        /// </summary>
        private static class V6RegressionChecks
        {
            public static void Run() => RunOnSta(RunChecks);

            private static void RunOnSta(Action body)
            {
                Exception? failure = null;
                var thread = new Thread(() =>
                {
                    try { body(); }
                    catch (Exception ex) { failure = ex; }
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join();

                if (failure != null)
                    Check("[V6R-0] 回归主体不抛异常", false, failure.ToString());
            }

            private static void RunChecks()
            {
                Section("[V6R] v6 修复回归：首拍基线 / 延时日志幂等 / 容量整数化 / 位号提示");
                ScadaEditHistory.Clear();

                ActivationBaseline();
                DelayMarkerIdempotent();
                CapacityIsInteger();
                BitIndexHint();
            }

            // ---------------------------------------------------------- F1
            private static void ActivationBaseline()
            {
                var engine = new ScadaAlarmEngine(new ScadaDocument(), new FakeValueSource(), () => DateTime.UtcNow);
                var beat = new ScadaBeatSource(Dispatcher.CurrentDispatcher);
                var context = new ScadaRuntimeContext(engine, beat);
                var animation = new ScadaAnimation { Type = ScadaAnimationType.Enable };

                var control = ElementRegistry.CreateControl(new ScadaElement { TypeKey = "Hmi.Button" });
                var raised = new List<ScadaEventType>();
                control.AddHandler(ScadaElementBase.ScadaEventEvent, new RoutedEventHandler((_, e) =>
                {
                    if (e is ScadaElementEventArgs args) raised.Add(args.ScadaEvent);
                }));
                control.RuntimeContext = context;

                // 首拍：启动即禁用 → 状态落成禁用，但不发事件
                control.TryApplyAnimationValue(animation, 0, out _);
                Check("[V6R-1] 首拍只建立基线：启动即禁用时落成禁用、但**不发** Deactivated",
                    raised.Count == 0 && !control.IsEnabled,
                    $"count={raised.Count} enabled={control.IsEnabled}");

                // 基线之后：真翻转才发
                control.TryApplyAnimationValue(animation, 1, out _);
                bool activated = raised.Count == 1 && raised[0] == ScadaEventType.Activated;
                control.TryApplyAnimationValue(animation, 0, out _);
                Check("[V6R-2] 基线之后：0→1 发激活、1→0 发取消激活，各一次",
                    activated && raised.Count == 2 && raised[1] == ScadaEventType.Deactivated,
                    $"seq={string.Join(",", raised)}");

                // 换运行上下文 → 基线重置 → 新一轮首拍同样不发
                raised.Clear();
                control.RuntimeContext = null;
                control.RuntimeContext = context;
                control.TryApplyAnimationValue(animation, 0, out _);
                Check("[V6R-3] 换运行上下文后基线重置：新一轮首拍仍不发事件（两轮行为一致）",
                    raised.Count == 0, $"count={raised.Count}");

                // 设计期仍然一条都不发
                raised.Clear();
                var design = ElementRegistry.CreateControl(new ScadaElement { TypeKey = "Hmi.Button" });
                design.AddHandler(ScadaElementBase.ScadaEventEvent, new RoutedEventHandler((_, e) =>
                {
                    if (e is ScadaElementEventArgs args) raised.Add(args.ScadaEvent);
                }));
                design.TryApplyAnimationValue(animation, 0, out _);
                design.TryApplyAnimationValue(animation, 1, out _);
                Check("[V6R-4] 设计期（无运行上下文）：首拍与翻转都不发事件",
                    raised.Count == 0, $"count={raised.Count}");
            }

            // ---------------------------------------------------------- F2
            private static void DelayMarkerIdempotent()
            {
                var log = new CapturingLog();
                var dispatcher = new ScadaActionDispatcher(log, new FakeValueSource(), new FakeNavigator());

                var hook = new ScadaEventHook { Event = ScadaEventType.Pressed };
                hook.Actions.Add(new ScadaAction { Type = ScadaActionType.Delay, DelayMs = 20 });
                hook.Actions.Add(new ScadaAction { Type = ScadaActionType.Log, Text = "甲" });
                hook.Actions.Add(new ScadaAction { Type = ScadaActionType.Delay, DelayMs = 20 });
                hook.Actions.Add(new ScadaAction { Type = ScadaActionType.Log, Text = "乙" });

                dispatcher.Dispatch(hook, "按钮");
                PumpDispatcher(400);

                var lines = log.Lines;
                Check("[V6R-5] 链式延时：四步全部执行，且日志对象名只带一个「（延时后）」",
                    lines.Count == 4
                    && lines[1].Contains("甲") && lines[3].Contains("乙")
                    && CountOf(lines[3], "（延时后）") == 1,
                    string.Join(" | ", lines));
            }

            // ---------------------------------------------------------- F4
            private static void CapacityIsInteger()
            {
                var descriptor = ElementRegistry.Find("Hmi.XYChart");
                var maxPoints = descriptor?.Properties.FirstOrDefault(p => p.Key == "MaxPoints");

                Check("[V6R-6] XY 曲线「最多点数」是整数类型（与报警表 MaxRows 一致，容量没有小数语义）",
                    maxPoints?.TargetProperty?.PropertyType == typeof(int),
                    maxPoints?.TargetProperty?.PropertyType.Name ?? "(缺属性)");

                var rounded = ScadaElementBase.ConvertFromString(typeof(int), "200.0");
                var roundedUp = ScadaElementBase.ConvertFromString(typeof(int), "5000.5");
                bool garbageThrew = false;
                try { ScadaElementBase.ConvertFromString(typeof(int), "不是数"); }
                catch { garbageThrew = true; }

                Check("[V6R-7] 整数宽容解析：\"200.0\"→200、\"5000.5\"→5001（四舍五入），非数值仍照常抛",
                    Equals(rounded, 200) && Equals(roundedUp, 5001) && garbageThrew,
                    $"200.0→{rounded} / 5000.5→{roundedUp} / garbageThrew={garbageThrew}");

                // 手改 .vms 写成 "300.0"：刷新后必须读成 300，而不是静默留在默认 200
                var model = new ScadaElement { TypeKey = "Hmi.XYChart" };
                model.SetProperty("MaxPoints", "300.0");
                var control = (XYChartElement)ElementRegistry.CreateControl(model);
                control.Refresh();
                Check("[V6R-8] 旧文件里写成 \"300.0\"：读回 300（不静默回落到默认 200）",
                    control.MaxPoints == 300, $"MaxPoints={control.MaxPoints}");
            }

            // ---------------------------------------------------------- F3
            private static void BitIndexHint()
            {
                var document = new ScadaDocument();
                document.StateLists.Add(new ScadaStateList { Name = "工位状态" });

                var vm = new StateListManagerViewModel(document) { Mode = ScadaStateList.ModeBitIndex };
                Check("[V6R-9] 位号模式的界面提示写清「变量为负时按补码看位」",
                    vm.ModeHint.Contains("补码"), vm.ModeHint);
            }

            private static int CountOf(string text, string token)
            {
                if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(token))
                    return 0;

                int count = 0, index = 0;
                while ((index = text.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
                {
                    count++;
                    index += token.Length;
                }

                return count;
            }

            /// <summary>线程安全的日志捕获（延时回调可能落在别的线程上）</summary>
            private sealed class CapturingLog : Core.Interfaces.ILogService
            {
                private readonly object _gate = new();
                private readonly List<string> _lines = new();

                public IReadOnlyList<string> Lines { get { lock (_gate) return _lines.ToArray(); } }

                private void Add(string level, string message) { lock (_gate) _lines.Add(level + " " + message); }

                public void Success(params string[] messages) { foreach (var m in messages) Add("S", m); }
                public void Error(params Exception[] messages) { foreach (var m in messages) Add("E", m?.Message ?? "null"); }
                public void Error(params string[] messages) { foreach (var m in messages) Add("E", m); }
                public void Info(params string[] messages) { foreach (var m in messages) Add("I", m); }
                public void Warn(params string[] messages) { foreach (var m in messages) Add("W", m); }
            }
        }
    }
}
