using System;
using System.Collections.Generic;
using System.Linq;
using Core.Interfaces;
using VisionMaster.Models;
using VisionMaster.Scada;
using VisionMaster.ScadaWeb;

namespace ScadaWebE2E
{
    /// <summary>
    /// M2b 真实浏览器 E2E 的宿主夹具：一个真实 ScadaWebHost + 六个交互图元 + 两个角色账号。
    /// 只用于本地联调/浏览器验收，不参与发布。
    /// </summary>
    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }

            // --all：租约关（两个账号都能同时点）；
            // 默认租约开 + 必须持租约（`AllOperatorsRequiresLease`）。
            // B3 之后"谁能点"由账号的权限 Key 决定，不再有"最高档"策略可切。
            var allOperators = args.Any(a => string.Equals(a, "--all", StringComparison.OrdinalIgnoreCase));

            var document = new ScadaDocument();
            var page = document.AddPage("E2E 总览");
            document.SetStartupPage(page);

            var button = Add(page, "Hmi.Button", "启动按钮", 40, 40, 160, 64);
            var bit = Add(page, "Hmi.BitButton", "点动按钮", 220, 40, 160, 64);
            var text = Add(page, "Hmi.TextField", "数值输入", 40, 130, 160, 64);
            var combo = Add(page, "Hmi.ComboBox", "模式选择", 220, 130, 160, 64);
            var selector = Add(page, "Hmi.SelectorSwitch", "手自动", 40, 220, 160, 64);
            var ack = Add(page, "Hmi.AckButton", "确认报警", 220, 220, 160, 64);

            text.Properties["Value"] = "10";
            combo.Properties["Items"] = "手动,自动";
            combo.Properties["SelectedIndex"] = "0";
            selector.Properties["Options"] = "手动,自动";
            selector.Properties["SelectedIndex"] = "0";

            var hook = new ScadaEventHook { Event = ScadaEventType.Pressed };
            hook.Actions.Add(new ScadaAction { Type = ScadaActionType.Log, Text = "E2E 点击" });
            button.EventHooks.Add(hook);

            var runtime = new ScadaRuntime(document);
            runtime.Start();

            var hub = new FakeHub(runtime, document, generation: 1);
            var settings = new ScadaWebSettings
            {
                Enabled = true,
                Port = 8899,
                BindAddress = "127.0.0.1",
                AllowAnonymousView = false,
                AllowCommands = true,
                RequireConfirmForDangerous = true,
                // --all = 不用租约（两人都能点）；默认要持租约。
                OperationLeaseEnabled = true,
                AllOperatorsRequiresLease = !allOperators,
                MaxSessions = 16
            };

            var log = new ConsoleLog();
            var dispatcher = new FakeDispatcher();
            var writer = new FakeWriter();
            var alarms = new FakeAlarmCommands();
            var feed = new FakeAlarmFeed();
            var audit = new FakeAudit();

            var host = new ScadaWebHost(
                hub,
                new FakeAuthenticator(),
                catalog: null,
                log,
                settings,
                values: null,
                uiDispatcher: null,
                actionDispatcher: dispatcher,
                valueWriter: writer,
                alarmCommands: alarms,
                audit: audit,
                alarmFeed: feed);

            host.Start();

            Console.WriteLine("SCADA Web E2E host");
            Console.WriteLine($"listening={host.IsListening} url=http://127.0.0.1:{host.Port}/ err={host.LastError ?? "<none>"}");
            Console.WriteLine("users: admin/admin123 (Administrator), op/op123 (Operator)");
            Console.WriteLine("Ctrl+C 停止。");

            using var stop = new System.Threading.CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                stop.Cancel();
            };

            try
            {
                await Task.Delay(Timeout.Infinite, stop.Token);
            }
            catch (OperationCanceledException)
            {
                // 正常收尾
            }

            await host.StopAsync();
            return 0;
        }

        private static ScadaElement Add(ScadaPage page, string type, string name, double x, double y, double w, double h)
        {
            var element = new ScadaElement
            {
                TypeKey = type,
                Name = name,
                X = x,
                Y = y,
                Width = w,
                Height = h,
                ZIndex = 1
            };

            page.Elements.Add(element);
            return element;
        }

        private sealed class FakeHub : IScadaRunHub
        {
            private readonly ScadaRuntime _runtime;
            private readonly ScadaDocument _document;

            public FakeHub(ScadaRuntime runtime, ScadaDocument document, long generation)
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

        private sealed class FakeAuthenticator : IScadaUserAuthenticator
        {
            // B3：身份是「用户组 + 权限 Key」，没有角色。两个演示账号分属两个组，
            // 差别只在 Key 集合上（admin 有全部内置 Key，op 只有看画面 + 运行控制）。
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

        private sealed class FakeDispatcher : IScadaActionDispatcher
        {
            public int Clicks { get; private set; }

            public void Dispatch(ScadaEventHook? hook, string? sourceName, string? operatorName = null)
                => _ = DispatchWithResult(hook, sourceName, operatorName);

            public ScadaActionDispatchResult DispatchWithResult(
                ScadaEventHook? hook,
                string? sourceName,
                string? operatorName = null)
            {
                Clicks++;
                var total = hook?.Actions.Count ?? 0;
                Console.WriteLine($"[E2E] dispatch source={sourceName} operator={operatorName} actions={total} clicks={Clicks}");
                return new ScadaActionDispatchResult(total, total, 0, 0);
            }
        }

        private sealed class FakeWriter : IScadaValueWriter
        {
            public bool TryWriteText(ScadaElement element, string targetProperty, string? text, out string? error)
            {
                error = null;
                Console.WriteLine($"[E2E] write {element.Name}.{targetProperty} = {text}");
                return true;
            }

            public bool TryReadText(ScadaElement element, string targetProperty, out string? text)
            {
                text = element.Properties.TryGetValue(targetProperty, out var value) ? value : null;
                return true;
            }
        }

        private sealed class FakeAlarmCommands : IScadaAlarmCommands
        {
            public bool TryAcknowledge(Guid alarmId, out string? error)
            {
                error = null;
                Console.WriteLine($"[E2E] ack {alarmId:D}");
                return true;
            }

            public int AcknowledgeAll(out string? error)
            {
                error = null;
                return 0;
            }
        }

        private sealed class FakeAlarmFeed : IScadaAlarmFeed
        {
            private readonly Guid _alarmId = Guid.NewGuid();

            public ScadaAlarmFeedSnapshot Snapshot()
                => new(
                    new[]
                    {
                        new ScadaAlarmInfo(
                            _alarmId,
                            "温度高",
                            "温度超过上限",
                            ScadaAlarmSeverity.Critical,
                            DateTime.UtcNow,
                            Acknowledged: false)
                    },
                    UnacknowledgedCount: 1);
        }

        private sealed class FakeAudit : IScadaAuditSink
        {
            public bool Append(
                string actor,
                string sessionId,
                string clientAddress,
                string subject,
                string eventText,
                string action,
                string result,
                string detail)
            {
                Console.WriteLine($"[E2E audit] {actor} {eventText}/{action} {subject} {result} {detail}");
                return true;
            }
        }

        private sealed class ConsoleLog : ILogService
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
}
