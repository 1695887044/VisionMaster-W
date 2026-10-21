using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using Core.Interfaces;
using VisionMaster.Scada;
using VisionMaster.Services;
using VisionMaster.Views;

namespace ScadaChecks
{
    internal partial class Program
    {
        /// <summary>
        /// [RH] 运行态启停广播（<see cref="IScadaRunHub"/>）与审计落盘出口（<see cref="IScadaAuditSink"/>）。
        ///
        /// 覆盖的六件事
        /// ---------
        /// ① <see cref="ScadaRunHubState"/> 的启停门禁：未启动不发、重复启动不发不递增、
        ///    启动→停止只发一次、重复停止不重复、generation 单调递增、失败启动不占轮次、Reset 不清轮次。
        /// ② <b>真实</b> <see cref="ScadaRuntimeHost"/> 的"未启动 Stop"兜底路径：不发任何信号、不抛、
        ///    Session/Document 为 null、generation 为 0（这条走的是产品代码，不是断言里的模型）。
        /// ③ 真实宿主 <c>Start</c> 的失败路径（空文档 → <c>session.Start()</c> 返回 false）：
        ///    不发 Started/Stopped、字段回 null、IsRunning=false，且第二次 Start 仍走同一失败路径
        ///    （证明没有卡在一个"从未显示过"的窗口上）。
        /// ④ <see cref="IScadaRunHub.TryGetCurrent"/>：未启动 false / 运行中 true / 停止后 false。
        /// ⑤ <c>RunStopped</c> 的发布顺序（处理器里 Session/Document 必须已经是 null）与
        ///    "订阅方抛异常不打断其它订阅方"。
        /// ⑥ 审计 sink 的映射：说明列带 <c>[web s=… ip=…]</c>，CSV 仍是 7 列（表头未变）。
        ///
        /// 为什么窗口只造不 Show
        /// ---------
        /// <c>ScadaRuntimeWindow</c> 可以在 STA 线程上构造（它自带 <c>Window.Resources</c>，
        /// 不依赖应用级主题字典——与 [AB] 段的窗口夹具同款），但 <c>Show()</c> 需要消息泵，
        /// 而且会弹出一个全屏窗口。Start 的失败路径恰好不需要 Show（<c>session.Start()</c>
        /// 返回 false 就收场），所以这一段的断言能对着<b>真实宿主</b>跑，而不是对着状态机的复制品。
        /// "运行中"的字段状态（TryGetCurrent 正例、RunStopped 顺序）用反射直接置入：
        /// 那是断言宿主里唯一能造出"Show 已成功"的方式，而这两个字段本来也只有一处写入。
        /// </summary>
        public static class RunHubChecks
        {
            /// <summary>接线一行：<c>RunHubChecks.Run();</c></summary>
            public static void Run() => RunRunHubChecks();
        }

        private static void RunRunHubChecks()
        {
            Section("[RH] 运行态启停广播：状态机门禁 / 真实宿主 Start·Stop / TryGetCurrent / 审计 sink 映射");

            RunHubStateChecks();
            RunHubHostChecks();
            RunHubPublishIsolationChecks();
            RunHubHostWindowChecks();
            RunHubHostStartChecks();
            RunHubAuditSinkChecks();
            RunHubAuditConcurrencyCheck();
        }

        // ==================================================================
        //  ① 状态机门禁（与宿主逐字相同的两条 if）
        // ==================================================================

        private static void RunHubStateChecks()
        {
            var state = new ScadaRunHubState();

            var started = 0;
            var stopped = 0;
            long lastStartedGen = -1;
            long lastStoppedGen = -1;

            // 这两句就是宿主里决定"发不发"的唯一判据（ScadaRuntimeHost 的三处发布点）
            void GateStart()
            {
                if (state.TryStarted(out var g))
                {
                    started++;
                    lastStartedGen = g;
                }
            }

            void GateStop()
            {
                if (state.TryStopped(out var g))
                {
                    stopped++;
                    lastStoppedGen = g;
                }
            }

            // ---- RH1：从没启动就 Stop ----
            GateStop();

            Check("[RH1] 未启动就 Stop：不发 Started/Stopped，generation 停在 0",
                started == 0 && stopped == 0 && state.Generation == 0 && !state.IsStarted,
                $"started={started} / stopped={stopped} / generation={state.Generation} / isStarted={state.IsStarted}");

            // ---- RH2：启动一次 + 重复启动 ----
            GateStart();
            var afterFirst = state.Generation;
            GateStart();

            Check("[RH2] 启动一次只发一次 Started、generation 0→1；重复启动不再发、也不递增",
                started == 1 && stopped == 0
                && afterFirst == 1 && lastStartedGen == 1
                && state.Generation == 1 && state.IsStarted,
                $"started={started} / afterFirst={afterFirst} / lastGen={lastStartedGen} / generation={state.Generation}");

            // ---- RH3：启动 → 停止（+ 重复停止） ----
            GateStop();
            var genAtStop = lastStoppedGen;
            GateStop();

            Check("[RH3] 启动→停止发一次 Stopped（带本轮 generation），重复 Stop 不再发",
                stopped == 1 && genAtStop == 1
                && !state.IsStarted && state.Generation == 1,
                $"stopped={stopped} / stopGen={genAtStop} / generation={state.Generation} / isStarted={state.IsStarted}");

            // ---- RH4：多轮 generation 单调递增 ----
            var generations = new List<long>();
            for (var round = 0; round < 3; round++)
            {
                GateStart();
                generations.Add(state.Generation);
                GateStop();
            }

            Check("[RH4] 三轮启停 generation 单调递增（2,3,4），每轮各发一次 Started/Stopped",
                generations.SequenceEqual(new long[] { 2, 3, 4 })
                && started == 4 && stopped == 4
                && lastStartedGen == 4 && lastStoppedGen == 4,
                $"gens={string.Join(",", generations)} / started={started} / stopped={stopped}");

            // ---- RH5：启动失败（只走收尾，从不调 TryStarted） ----
            var failed = new ScadaRunHubState();
            var failedStarted = 0;
            var failedStopped = 0;

            if (failed.TryStopped(out _)) failedStopped++;   // 宿主：session.Start() 返回 false → AbortStart → OnWindowClosed 收尾

            var nextGeneration = failed.TryStarted(out var firstGen) ? firstGen : -1;

            Check("[RH5] 启动失败只走收尾：不发 Started、也不发 Stopped，且不占轮次（下一轮仍是 1）",
                failedStarted == 0 && failedStopped == 0 && nextGeneration == 1,
                $"started={failedStarted} / stopped={failedStopped} / 下一轮 generation={nextGeneration}");

            // ---- RH6：Reset 只清"起没起"，不清轮次 ----
            var resetState = new ScadaRunHubState();
            resetState.TryStarted(out _);
            var genBeforeReset = resetState.Generation;
            resetState.Reset();
            var stopAfterReset = resetState.TryStopped(out _);
            var genAfterResetStart = resetState.TryStarted(out var resumed) ? resumed : -1;

            Check("[RH6] Reset 只清「起没起」：generation 不回退（1），Reset 后 Stop 不发、再启动递增到 2",
                genBeforeReset == 1 && !stopAfterReset
                && genAfterResetStart == 2,
                $"reset 前 generation={genBeforeReset} / reset 后 Stop 发出={stopAfterReset} / 再启动 generation={genAfterResetStart}");
        }

        // ==================================================================
        //  ② 真实宿主：未启动 Stop 的兜底路径 + TryGetCurrent 未启动
        // ==================================================================

        private static void RunHubHostChecks()
        {
            var host = RhNewHost(new RhRecordingLog(), new RhNullNotifier(), out var ctorError);

            if (host == null)
            {
                Check("[RH7] 真实宿主：未启动 Stop（含重复 Stop）不发 Started/Stopped、不抛、字段为 null",
                    false, "宿主构造失败：" + (ctorError?.Message ?? "未知原因"));
                Check("[RH10] TryGetCurrent：未启动 → false，三个 out 为 null/null/0",
                    false, "宿主构造失败：" + (ctorError?.Message ?? "未知原因"));
                return;
            }

            var startedCount = 0;
            var stoppedCount = 0;
            long stoppedGeneration = -1;

            host.RunStarted += (_, _, _) => startedCount++;
            host.RunStopped += generation =>
            {
                stoppedCount++;
                stoppedGeneration = generation;
            };

            var stopThrew = false;
            string? stopError = null;

            try
            {
                host.Stop();
                host.Stop();
            }
            catch (Exception ex)
            {
                stopThrew = true;
                stopError = ex.Message;
            }

            var stoppedGenText = stoppedGeneration.ToString();

            Check("[RH7] 真实宿主：未启动 Stop（含重复 Stop）不发 Started/Stopped、不抛、字段为 null",
                !stopThrew
                && startedCount == 0 && stoppedCount == 0
                && host.Session is null && host.Document is null
                && host.RunGeneration == 0 && !host.IsRunning,
                stopThrew
                    ? "抛了：" + stopError
                    : $"started={startedCount} / stopped={stoppedCount}(gen={stoppedGenText}) / " +
                      $"session={(host.Session is null ? "null" : "非 null")} / " +
                      $"document={(host.Document is null ? "null" : "非 null")} / " +
                      $"generation={host.RunGeneration} / isRunning={host.IsRunning}");

            // ---- RH10：TryGetCurrent 未启动 ----
            var current = host.TryGetCurrent(out var currentSession, out var currentDocument, out var currentGeneration);

            Check("[RH10] TryGetCurrent：未启动 → false，三个 out 为 null/null/0（不返回上一轮残留）",
                !current && currentSession is null && currentDocument is null && currentGeneration == 0,
                $"running={current} / session={(currentSession is null ? "null" : "非 null")} / " +
                $"document={(currentDocument is null ? "null" : "非 null")} / generation={currentGeneration}");
        }

        // ==================================================================
        //  ③ 订阅方异常隔离：一个处理器抛，不许打断后面的、也不许冒回宿主
        // ==================================================================

        private static void RunHubPublishIsolationChecks()
        {
            var log = new RhRecordingLog();
            var host = RhNewHost(log, new RhNullNotifier(), out var ctorError);

            if (host == null)
            {
                Check("[RH14] RunStarted 订阅方抛异常：第一个抛、第二个仍收到，Publish 不把异常冒出去",
                    false, "宿主构造失败：" + (ctorError?.Message ?? "未知原因"));
                Check("[RH15] RunStopped 订阅方抛异常：第一个抛、第二个仍收到，Publish 不把异常冒出去",
                    false, "宿主构造失败：" + (ctorError?.Message ?? "未知原因"));
                return;
            }

            var document = new ScadaDocument();
            var session = new ScadaRuntime(document);

            // ---- RH14：RunStarted ----
            var startedOrder = new List<string>();
            host.RunStarted += (_, _, _) =>
            {
                startedOrder.Add("first");
                throw new InvalidOperationException("订阅方一炸了");
            };
            host.RunStarted += (_, _, _) => startedOrder.Add("second");

            Exception? startedError = null;
            try
            {
                RhInvoke(host, "PublishStarted", session, document, 3L);
            }
            catch (Exception ex)
            {
                startedError = ex;
            }

            Check("[RH14] RunStarted 订阅方抛异常：第一个抛、第二个仍收到，Publish 不把异常冒出去（只记 Warn）",
                startedError == null
                && startedOrder.SequenceEqual(new[] { "first", "second" })
                && log.Warnings.Count >= 1,
                startedError != null
                    ? "抛了：" + startedError.Message
                    : $"order={string.Join(",", startedOrder)} / warnings={log.Warnings.Count}");

            // ---- RH15：RunStopped ----
            var stoppedOrder = new List<string>();
            host.RunStopped += _ =>
            {
                stoppedOrder.Add("first");
                throw new InvalidOperationException("订阅方一炸了");
            };
            host.RunStopped += _ => stoppedOrder.Add("second");

            Exception? stoppedError = null;
            try
            {
                RhInvoke(host, "PublishStopped", 3L);
            }
            catch (Exception ex)
            {
                stoppedError = ex;
            }

            Check("[RH15] RunStopped 订阅方抛异常：第一个抛、第二个仍收到，Publish 不把异常冒出去（只记 Warn）",
                stoppedError == null
                && stoppedOrder.SequenceEqual(new[] { "first", "second" })
                && log.Warnings.Count >= 2,
                stoppedError != null
                    ? "抛了：" + stoppedError.Message
                    : $"order={string.Join(",", stoppedOrder)} / warnings={log.Warnings.Count}");
        }

        // ==================================================================
        //  ④ TryGetCurrent 正例 + RunStopped 发布顺序（需要真窗口对象，单开 STA）
        // ==================================================================

        private static void RunHubHostWindowChecks()
        {
            RhRunSta(RunHubHostWindowChecksCore,
                "[RH11] TryGetCurrent / RunStopped 顺序：STA 夹具全程未抛异常");
        }

        private static void RunHubHostWindowChecksCore()
        {
            var host = RhNewHost(new RhRecordingLog(), new RhNullNotifier(), out var ctorError);
            if (host == null)
            {
                Check("[RH11] TryGetCurrent：运行中 → true（session/document 非 null）",
                    false, "宿主构造失败：" + (ctorError?.Message ?? "未知原因"));
                Check("[RH12] RunStopped 在字段清空之后发：处理器里 Session/Document 为 null",
                    false, "宿主构造失败：" + (ctorError?.Message ?? "未知原因"));
                Check("[RH13] TryGetCurrent：停止后 → false，三个 out 为 null/null/0",
                    false, "宿主构造失败：" + (ctorError?.Message ?? "未知原因"));
                return;
            }

            var document = new ScadaDocument();
            var session = new ScadaRuntime(document);
            var window = new ScadaRuntimeWindow(session);

            // 正常路径里 _session/_window 只在 Show() 成功之后才有值；断言宿主里不能真 Show
            // （需要消息泵，还会弹出一个全屏窗口）。这里用反射直接造出"已经在跑"的字段状态——
            // 这两个字段在宿主里本来也只有 Start/OnWindowClosed 两处写入。
            RhSetPrivateField(host, "_session", session);
            RhSetPrivateField(host, "_window", window);

            // 让 RunStopped 真的会发：正常路径里由 Start 末尾的 TryStarted 置位。
            RhHubState(host).TryStarted(out var generation);

            var got = host.TryGetCurrent(out var s, out var d, out var g);

            Check("[RH11] TryGetCurrent：运行中 → true，session/document 非 null，generation 与本轮一致",
                got && ReferenceEquals(s, session) && ReferenceEquals(d, document)
                && generation > 0 && g == generation && host.RunGeneration == generation,
                $"ok={got} / session={(s is null ? "null" : "非 null")} / " +
                $"document={(d is null ? "null" : "非 null")} / generation={g} / 期望={generation}");

            bool? sessionWasNull = null;
            bool? documentWasNull = null;
            bool? runningInHandler = null;
            var stoppedCount = 0;
            long stoppedGeneration = -1;

            host.RunStopped += stopped =>
            {
                stoppedCount++;
                stoppedGeneration = stopped;
                sessionWasNull = host.Session is null;
                documentWasNull = host.Document is null;
                runningInHandler = host.IsRunning;
            };

            Exception? closeError = null;
            try
            {
                RhInvoke(host, "OnWindowClosed", window, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                closeError = ex;
            }

            Check("[RH12] RunStopped 在字段清空之后发：处理器里 Session/Document 为 null、IsRunning=false（带本轮 generation）",
                closeError == null && stoppedCount == 1 && stoppedGeneration == generation
                && sessionWasNull == true && documentWasNull == true && runningInHandler == false,
                closeError != null
                    ? "抛了：" + closeError.Message
                    : $"stopped={stoppedCount}(gen={stoppedGeneration}) / sessionNull={sessionWasNull} / " +
                      $"documentNull={documentWasNull} / isRunningInHandler={runningInHandler}");

            var afterStop = host.TryGetCurrent(out var s2, out var d2, out var g2);

            Check("[RH13] TryGetCurrent：停止后 → false，三个 out 为 null/null/0（不返回上一轮残留）",
                !afterStop && s2 is null && d2 is null && g2 == 0,
                $"running={afterStop} / session={(s2 is null ? "null" : "非 null")} / " +
                $"document={(d2 is null ? "null" : "非 null")} / generation={g2}");
        }

        // ==================================================================
        //  ⑤ 真实宿主 Start 的失败路径（空文档，需要真窗口对象，单开 STA）
        //
        //  这一节钉的是"点了运行没反应"的根：Start 半路失败后必须把字段与状态全部归零，
        //  否则下一次「运行」会在开头早退到一个从未显示过的窗口上（Activate 静默 no-op）。
        // ==================================================================

        private static void RunHubHostStartChecks()
        {
            RhRunSta(RunHubHostStartChecksCore,
                "[RH16] 真实宿主 Start（空文档）：STA 夹具全程未抛异常");
        }

        private static void RunHubHostStartChecksCore()
        {
            var log = new RhRecordingLog();
            var notifier = new RhRecordingNotifier();
            var host = RhNewHost(log, notifier, out var ctorError);

            if (host == null)
            {
                Check("[RH16] 真实宿主 Start（空文档）：session.Start() 返回 false → 不发 Started/Stopped、字段回 null、IsRunning=false",
                    false, "宿主构造失败：" + (ctorError?.Message ?? "未知原因"));
                Check("[RH17] 失败后第二次 Start 仍走同一失败路径（不早退到未显示的窗口）",
                    false, "宿主构造失败：" + (ctorError?.Message ?? "未知原因"));
                return;
            }

            var startedCount = 0;
            var stoppedCount = 0;
            host.RunStarted += (_, _, _) => startedCount++;
            host.RunStopped += _ => stoppedCount++;

            // 空文档 = 一页都没有 → ScadaDocument.ResolveStartupPage() 返回 null → session.Start() 返回 false。
            var document = new ScadaDocument();

            Exception? firstError = null;
            try
            {
                host.Start(document);
            }
            catch (Exception ex)
            {
                firstError = ex;
            }

            var firstCurrent = host.TryGetCurrent(out _, out _, out _);

            // "返回 false 也走 AbortStart"这一条必须有独立证据：只判字段为 null 的话，
            // 旧形态（直接 window.Close()）也会因为 Closed → OnWindowClosed 把字段清掉而假绿。
            // AbortStart 的第一行就是那条 Warn，它不在就说明收场根本没走统一入口。
            var abortedThroughAbortStart = log.Warnings.Any(
                w => w.Contains("启动运行会话（当前方案里没有可运行的画面）失败", StringComparison.Ordinal));

            Check("[RH16] 真实宿主 Start（空文档）：session.Start() 返回 false → 走 AbortStart、不发 Started/Stopped、字段回 null、IsRunning=false",
                firstError == null
                && startedCount == 0 && stoppedCount == 0
                && host.Session is null && host.Document is null
                && !host.IsRunning && host.RunGeneration == 0
                && !firstCurrent
                && notifier.Warnings.Count == 1
                && abortedThroughAbortStart,
                firstError != null
                    ? "抛了：" + firstError.Message
                    : $"started={startedCount} / stopped={stoppedCount} / " +
                      $"session={(host.Session is null ? "null" : "非 null")} / " +
                      $"document={(host.Document is null ? "null" : "非 null")} / " +
                      $"isRunning={host.IsRunning} / generation={host.RunGeneration} / " +
                      $"tryGetCurrent={firstCurrent} / 提示={notifier.Warnings.Count} / " +
                      $"走AbortStart={abortedThroughAbortStart}");

            // 第二次必须还能走到同一条失败路径：若字段没清干净，这里会早退到 _window.Activate()
            // （不抛、不发信号、也不弹"没有可运行的画面"），提示次数就停在 1。
            Exception? secondError = null;
            try
            {
                host.Start(document);
            }
            catch (Exception ex)
            {
                secondError = ex;
            }

            var secondCurrent = host.TryGetCurrent(out _, out _, out _);

            Check("[RH17] 失败后第二次 Start 仍走同一失败路径（不早退到未显示的窗口）：不抛、不发信号、字段仍为 null、提示两次",
                secondError == null && firstError == null
                && startedCount == 0 && stoppedCount == 0
                && host.Session is null && host.Document is null && !host.IsRunning
                && host.RunGeneration == 0
                && !secondCurrent
                && notifier.Warnings.Count == 2,
                secondError != null
                    ? "抛了：" + secondError.Message
                    : $"started={startedCount} / stopped={stoppedCount} / isRunning={host.IsRunning} / " +
                      $"generation={host.RunGeneration} / tryGetCurrent={secondCurrent} / 提示={notifier.Warnings.Count}");
        }

        // ==================================================================
        //  ⑥ 审计 sink：映射到既有 CSV（7 列，说明列带 web 前缀）
        // ==================================================================

        private static void RunHubAuditSinkChecks()
        {
            var directory = Path.Combine(Path.GetTempPath(), "vm-scada-audit-" + Guid.NewGuid().ToString("N"));

            try
            {
                // retentionDays: 0 = 不清理（临时目录里也没有别的文件）
                IScadaAuditSink sink = new ScadaAuditWriter(directory, retentionDays: 0);

                var ok1 = sink.Append("张三", "s1", "10.0.0.5", "泵1", "点击", "写变量", "成功", "1 → 2");
                var ok2 = sink.Append("李四", "s2", "10.0.0.6", "泵2", "点击", "启动流程", "拒绝", "权限不足");
                var ok3 = sink.Append("王五", "s3", "10.0.0.7", "泵3", "点击", "写变量", "weird-result", string.Empty);

                var files = Directory.Exists(directory)
                    ? Directory.GetFiles(directory, "Audit-*.csv")
                    : Array.Empty<string>();

                var lines = files.Length == 1
                    ? File.ReadAllLines(files[0], Encoding.UTF8)
                    : Array.Empty<string>();

                var header = lines.Length > 0 ? lines[0] : string.Empty;
                var row1 = lines.Length > 1 ? lines[1] : string.Empty;
                var row2 = lines.Length > 2 ? lines[2] : string.Empty;
                var row3 = lines.Length > 3 ? lines[3] : string.Empty;

                Check("[RH8] IScadaAuditSink 落盘：说明列带 [web s=… ip=…]，CSV 仍是 7 列（表头未被改动）",
                    ok1 && ok2 && ok3
                    && files.Length == 1
                    && header == "时间,操作者,对象,事件,动作,结果,说明"
                    && lines.Length == 4
                    && row1.Contains("[web s=s1 ip=10.0.0.5]", StringComparison.Ordinal)
                    && row2.Contains("[web s=s2 ip=10.0.0.6]", StringComparison.Ordinal)
                    && RhColumnCount(row1) == 7
                    && RhColumnCount(row2) == 7
                    && RhColumnCount(row3) == 7,
                    $"ok={ok1}/{ok2}/{ok3} / files={files.Length} / lines={lines.Length} / " +
                    $"header=\"{header}\" / row1=\"{row1}\"");

                Check("[RH9] 结果词映射：成功→成功、拒绝→被拒绝、认不出的词→失败（不冒充成功）",
                    RhColumn(row1, 5) == "成功"
                    && RhColumn(row2, 5) == "被拒绝"
                    && RhColumn(row3, 5) == "失败",
                    $"row1={RhColumn(row1, 5)} / row2={RhColumn(row2, 5)} / row3={RhColumn(row3, 5)}");
            }
            finally
            {
                try
                {
                    if (Directory.Exists(directory))
                        Directory.Delete(directory, true);
                }
                catch (Exception)
                {
                    // 临时目录删不掉不影响断言结论（本段写入的都是无逗号/无引号的数据，
                    // 因此下面按 ',' 切分即可，不需要完整的 RFC 4180 解析）。
                }
            }
        }

        /// <summary>
        /// 审计写锁：多条线程共用同一份 <see cref="ScadaAuditWriter"/> 并发落盘（Web 后台线程的真实形状）。
        ///
        /// 判据落在"每一行都完整"上：没有锁时，两条线程同时以 <c>FileMode.Append</c> 打开同一个文件，
        /// 轻则一部分写入返回 false（FileShare 拒绝），重则文件里出现撕裂行（列数不对）。
        /// 所以这里同时钉住"全部成功"与"每行仍是 7 列"。
        /// </summary>
        private static void RunHubAuditConcurrencyCheck()
        {
            var directory = Path.Combine(Path.GetTempPath(), "vm-scada-audit-mt-" + Guid.NewGuid().ToString("N"));

            try
            {
                // retentionDays: 0 = 不清理（临时目录里也没有别的文件）
                var writer = new ScadaAuditWriter(directory, retentionDays: 0);

                const int threadCount = 6;
                const int perThread = 40;
                var okCount = 0;
                var failures = new List<string>();
                var threads = new Thread[threadCount];

                for (var t = 0; t < threadCount; t++)
                {
                    var index = t;
                    threads[t] = new Thread(() =>
                    {
                        for (var i = 0; i < perThread; i++)
                        {
                            try
                            {
                                if (writer.Append(new ScadaAuditEntry(
                                        $"线程{index}", "并发", "点击", "写变量", ScadaAuditOutcome.Success, $"#{i}")))
                                {
                                    Interlocked.Increment(ref okCount);
                                }
                            }
                            catch (Exception ex)
                            {
                                lock (failures) failures.Add(ex.Message);
                            }
                        }
                    });
                    threads[t].IsBackground = true;
                    threads[t].Start();
                }

                foreach (var thread in threads)
                    thread.Join();

                var files = Directory.Exists(directory)
                    ? Directory.GetFiles(directory, "Audit-*.csv")
                    : Array.Empty<string>();

                var lines = files.Length == 1
                    ? File.ReadAllLines(files[0], Encoding.UTF8)
                    : Array.Empty<string>();

                var rows = lines.Length > 0 ? lines.Skip(1).ToArray() : Array.Empty<string>();
                var malformed = rows.Count(row => RhColumnCount(row) != 7);
                var expected = threadCount * perThread;

                Check("[RH18] 审计写锁：6 线程 × 40 条并发落盘全部成功，文件里 240 行且每行仍是 7 列（无撕裂行）",
                    failures.Count == 0
                    && okCount == expected
                    && files.Length == 1
                    && rows.Length == expected
                    && malformed == 0,
                    $"ok={okCount}/{expected} / files={files.Length} / rows={rows.Length} / " +
                    $"列数异常={malformed} / 异常={failures.Count}");
            }
            finally
            {
                try
                {
                    if (Directory.Exists(directory))
                        Directory.Delete(directory, true);
                }
                catch (Exception)
                {
                    // 临时目录删不掉不影响断言结论
                }
            }
        }

        /// <summary>按 ',' 切分后的列数（本段写入的数据不含逗号与引号，朴素切分即可）</summary>
        private static int RhColumnCount(string row) => row.Split(',').Length;

        /// <summary>取第 <paramref name="index"/> 列（同上：朴素切分）</summary>
        private static string RhColumn(string row, int index)
        {
            var columns = row.Split(',');
            return index >= 0 && index < columns.Length ? columns[index] : string.Empty;
        }

        // ==================================================================
        //  反射小工具：只读/置入宿主私有成员，不改任何公开 API 语义
        // ==================================================================

        /// <summary>造一个真实宿主（不接任何真实 UI）。构造失败时返回 null 并把异常交出来。</summary>
        private static ScadaRuntimeHost? RhNewHost(ILogService log, IUserNotifier notifier, out Exception? error)
        {
            try
            {
                error = null;
                return new ScadaRuntimeHost(
                    log,
                    notifier,
                    new WorkspaceContext(),
                    new RhNullDispatcher(),
                    new FakeValueSource(),
                    new FakeValueWriter(),
                    new ScadaNavigator(),
                    DefaultScadaAccessPolicy.Instance,
                    // 这个目录只作构造参数（本轮不写审计），不建目录、不留垃圾
                    new ScadaAuditWriter(
                        Path.Combine(Path.GetTempPath(), "vm-scada-hub-" + Guid.NewGuid().ToString("N")),
                        retentionDays: 0),
                    new AppSettingsService());
            }
            catch (Exception ex)
            {
                error = ex;
                return null;
            }
        }

        /// <summary>在一条 STA 线程上跑（Window 是 DispatcherObject；样板与 [AB]/[S]/[T] 段一致）</summary>
        private static void RhRunSta(Action action, string failureName)
        {
            Exception? failure = null;

            var thread = new Thread(() =>
            {
                try { action(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            thread.Join();

            if (failure != null)
                Check(failureName, false, failure.ToString());
        }

        /// <summary>置入宿主的私有字段（本文件只用于构造"已经在跑"的状态）。</summary>
        private static void RhSetPrivateField(object target, string fieldName, object? value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(target.GetType().FullName, fieldName);
            field.SetValue(target, value);
        }

        /// <summary>取宿主内部的轮次状态机（用它置"已启动"，让 RunStopped 真的会发）。</summary>
        private static ScadaRunHubState RhHubState(ScadaRuntimeHost host)
        {
            var field = typeof(ScadaRuntimeHost).GetField("_hubState", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(typeof(ScadaRuntimeHost).FullName, "_hubState");
            return (ScadaRunHubState)field.GetValue(host)!;
        }

        /// <summary>
        /// 调宿主的私有发布方法。反射壳（<see cref="TargetInvocationException"/>）要剥掉：
        /// 断言要看的是被测代码抛了什么，而不是"反射调用失败"。
        /// </summary>
        private static object? RhInvoke(object target, string methodName, params object?[] args)
        {
            var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(target.GetType().FullName, methodName);

            try
            {
                return method.Invoke(target, args);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;   // 不可达：上面那行一定会抛
            }
        }

        // ---- 本段用到的假件（只做"不抛、不落地"的最低限度） ----

        /// <summary>记录 Warn 的日志出口（本轮断言只关心"没把异常冒回调用方"，日志内容仅作证据）</summary>
        private sealed class RhRecordingLog : ILogService
        {
            public List<string> Warnings { get; } = new();

            public void Success(params string[] messages) { }
            public void Error(params Exception[] messages) { }
            public void Error(params string[] messages) { }
            public void Info(params string[] messages) { }
            public void Warn(params string[] messages) => Warnings.AddRange(messages);
        }

        /// <summary>记录警告气泡的出口：用来证明第二次 Start 真的又走到了"没有可运行的画面"那一支。</summary>
        private sealed class RhRecordingNotifier : IUserNotifier
        {
            public List<string> Warnings { get; } = new();

            public void ShowInfo(string message) { }
            public void ShowWarn(string message) => Warnings.Add(message);
            public void ShowError(string message) { }
        }

        private sealed class RhNullNotifier : IUserNotifier
        {
            public void ShowInfo(string message) { }
            public void ShowWarn(string message) { }
            public void ShowError(string message) { }
        }

        private sealed class RhNullDispatcher : IScadaActionDispatcher
        {
            public void Dispatch(ScadaEventHook? hook, string? sourceName, string? operatorName = null) { }
        }
    }
}
