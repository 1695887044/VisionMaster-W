using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Core.Interfaces;
using VisionMaster.Models;
using VisionMaster.Scada;
using VisionMaster.ScadaWeb;

namespace ScadaWebChemicalDemo
{
    /// <summary>
    /// 化工装置多画面暴力测试夹具：一套"现场量级"的 HMI，用真实 ScadaWebHost 供数。
    ///
    /// <b>它模拟的是什么现场</b>
    /// ---------
    /// 6 张画面的化工装置：总览 / 反应釜 / 精馏塔 / 换热站 / 报警与趋势 / 配方与联锁。
    /// 工艺量按真实量纲与量程走（温度 ℃、压力 MPa、流量 m³/h、液位 %、转速 rpm），
    /// 位号按 ISA-5.1 的习惯给（TT-1201 温度变送器、FV-1103 调节阀、P-1102 泵、E-1301 换热器）。
    /// 上百个图元挂实时变量、跨页基座画顶栏与导航、报警按限值真触发、按钮真跳页。
    ///
    /// <b>为什么要做成这个形状，而不是"随便摆一千个矩形"</b>
    /// ---------
    /// 纯数量堆出来的快照只能压出带宽与序列化；现场真正压人的是
    /// ① 每页都不同的图元组合（渲染器分支全走到）、② 跨页基座（改一次全页重发）、
    /// ③ 高频刷新的模拟量（值帧体积与合并逻辑）、④ 切页（结构重建 + 定时器回收）、
    /// ⑤ 命令（权限 + 审计 + 幂等 + 确认）。
    /// 这五条只有"像真画面"的夹具才压得到；摆一万个同型矩形反而只会测出 JSON 有多大。
    ///
    /// <b>用法</b>
    /// <code>
    /// ScadaWebChemicalDemo.exe                    # 默认 8899 端口，管理员+操作员
    /// ScadaWebChemicalDemo.exe --port 9000        # 换端口
    /// ScadaWebChemicalDemo.exe --all              # 所有登录用户都能操作（默认只有最高角色）
    /// ScadaWebChemicalDemo.exe --still            # 变量不自动刷新（要静态截图时用）
    /// ScadaWebChemicalDemo.exe --tick 200         # 变量刷新周期（毫秒，默认 500）
    /// </code>
    /// 浏览器打开 http://127.0.0.1:8899/ ，用 admin/admin123 或 op/op123 登录。
    /// </summary>
    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }

            // 暴力测试模式：用同一套化工画面压协议层（不渲染浏览器）
            if (args.Any(a => string.Equals(a, "--stress", StringComparison.OrdinalIgnoreCase)))
                return await StressRunner.RunAsync(args);

            var port = ReadArg(args, "--port", 8899);
            var tickMs = ReadArg(args, "--tick", 500);
            var autoPageMs = ReadArg(args, "--auto-page", 0);
            var startPage = ReadStringArg(args, "--start-page");
            var allOperators = args.Any(a => string.Equals(a, "--all", StringComparison.OrdinalIgnoreCase));
            var still = args.Any(a => string.Equals(a, "--still", StringComparison.OrdinalIgnoreCase));

            var plant = new ChemicalPlant();
            var document = plant.Build();

            // ── 变量：按量程造真实工程值，每个都挂 ValueChanged ──────────────────
            var values = new PlantValueSource(plant.Variables);

            // ── 运行态会话：在专用 STA/Dispatcher 线程上起，Web 的快照与命令都投到这里 ──
            Dispatcher? ui = null;
            var uiReady = new ManualResetEventSlim();
            var uiThread = new Thread(() =>
            {
                ui = Dispatcher.CurrentDispatcher;
                uiReady.Set();
                Dispatcher.Run();
            })
            {
                IsBackground = true,
                Name = "ChemicalDemo.UI"
            };
            uiThread.SetApartmentState(ApartmentState.STA);
            uiThread.Start();
            uiReady.Wait();

            var runtime = new ScadaRuntime(document);
            ui!.Invoke(() => runtime.Start());

            // 起手页（可选）：镜像模式下切页只能由宿主发起，所以"想从某一页开始看"
            // 也必须是宿主动作。断言/截图脚本靠它稳定停在目标页，不必靠轮播等运气。
            if (!string.IsNullOrWhiteSpace(startPage))
            {
                var target = document.FindNavigablePageByName(startPage)
                             ?? document.Pages.FirstOrDefault(p =>
                                 p.Name.Contains(startPage!, StringComparison.OrdinalIgnoreCase));

                if (target != null)
                    ui.Invoke(() => runtime.Navigate(target));
                else
                    Console.WriteLine($"[警告] --start-page「{startPage}」没有匹配的画面，仍从启动页开始");
            }

            // ── 报警引擎：真跑（Attach 会先做一次初判，启动即越限的报警当场就报）──
            var alarmEngine = new ScadaAlarmEngine(document, values);
            alarmEngine.Attach();

            var hub = new DemoHub(runtime, document, generation: 1);
            var settings = new ScadaWebSettings
            {
                Enabled = true,
                Port = port,
                BindAddress = "127.0.0.1",
                AllowAnonymousView = true,
                AllowCommands = true,
                RequireConfirmForDangerous = true,
                // --all = 不用租约（两个账号都能同时点）；默认要持租约。
                // B3 之后"谁能点"由账号的权限 Key 决定，不再有"最高档"策略可切。
                OperationLeaseEnabled = true,
                AllOperatorsRequiresLease = !allOperators,
                MaxSessions = 64,
                CommandRatePerSecond = 20,
                ElementRatePerSecond = 40
            };

            var log = new ConsoleLog();
            var dispatcher = new PlantActionDispatcher(runtime, document, values, log);
            var writer = new PlantValueWriter(plant);
            var alarms = new PlantAlarmCommands(alarmEngine);
            var feed = new PlantAlarmFeed(alarmEngine);
            var audit = new ConsoleAudit();

            var host = new ScadaWebHost(
                hub,
                new PlantAuthenticator(),
                catalog: null,
                log,
                settings,
                values: values,
                uiDispatcher: ui,
                actionDispatcher: dispatcher,
                valueWriter: writer,
                alarmCommands: alarms,
                audit: audit,
                alarmFeed: feed);

            host.Start();

            if (!host.IsListening)
            {
                Console.WriteLine($"启动失败：port={port} err={host.LastError}");
                return 2;
            }

            // ── 模拟量刷新：在 UI 线程上按 tickMs 推进，模拟 PLC 轮询 ──────────
            var plantTimer = new DispatcherTimer(DispatcherPriority.Background, ui)
            {
                Interval = TimeSpan.FromMilliseconds(Math.Max(50, tickMs))
            };
            plantTimer.Tick += (_, _) => plant.Advance();

            var alarmTimer = new DispatcherTimer(DispatcherPriority.Background, ui)
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            alarmTimer.Tick += (_, _) => alarmEngine.Tick();

            if (!still)
            {
                plantTimer.Start();
                alarmTimer.Start();
            }

            // ── 页面轮播（可选）：镜像模式下**唯一**能切页的路径 ──────────────
            //
            // 为什么切页只能由宿主发起：Web 侧是镜像模式（方案 §7），
            // `ScadaActionFilter.IsMirrorOnlyPageAction` 把 Navigate / NavigateBack 无条件剔除，
            // 所以浏览器点不出"切页"。真正会让所有浏览器一起换页的是桌面运行端的
            // `ScadaRuntime.Navigate` —— Web 端在下一拍（≤100ms）比较 PageKey 后收到整份 `page`。
            //
            // 这条定时器就是那个"现场切页"的替身：它压的是结构重建、基座重发、
            // 旧页渲染器（含时钟定时器）的回收，以及客户端在切页瞬间收到的在途值帧。
            DispatcherTimer? pager = null;

            if (autoPageMs > 0)
            {
                var ordered = document.Pages.Where(p => !document.IsBasePage(p)).ToList();
                var cursor = 0;

                pager = new DispatcherTimer(DispatcherPriority.Background, ui)
                {
                    Interval = TimeSpan.FromMilliseconds(Math.Max(500, autoPageMs))
                };

                pager.Tick += (_, _) =>
                {
                    if (ordered.Count == 0) return;

                    cursor = (cursor + 1) % ordered.Count;
                    var target = ordered[cursor];

                    if (runtime.Navigate(target))
                        log.Info($"[轮播] 切到「{target.Name}」");
                    else
                        log.Warn($"[轮播] 切到「{target.Name}」失败（运行可能已结束）");
                };

                pager.Start();
            }

            Console.WriteLine("================ 化工装置 SCADA Web 演示 ================");
            Console.WriteLine($"地址      http://127.0.0.1:{host.Port}/");
            Console.WriteLine($"账号      admin / admin123（管理员，可操作）");
            Console.WriteLine($"          op    / op123   （产线操作员：看画面 + 运行控制）");
            Console.WriteLine($"画面      {string.Join(" / ", document.Pages.Select(p => p.Name))}");
            Console.WriteLine($"变量      {plant.Variables.Count} 个（刷新 {tickMs}ms{(still ? "，已停用" : string.Empty)}）");
            Console.WriteLine($"报警      {document.Alarms.Count} 条定义，当前 {alarmEngine.ActiveAlarms.Count} 条激活");
            Console.WriteLine($"策略      {(allOperators ? "租约关（都可点）" : "必须持租约才能点")} · 危险动作需确认 · 判权按权限 Key");
            Console.WriteLine($"切页      {(autoPageMs > 0 ? $"宿主轮播 {autoPageMs}ms（镜像跟随）" : "仅手动（--auto-page N 可开轮播）")}");
            Console.WriteLine("Ctrl+C 停止。");
            Console.WriteLine("========================================================");

            using var stop = new CancellationTokenSource();
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

            Console.WriteLine("正在停止…");
            pager?.Stop();
            plantTimer.Stop();
            alarmTimer.Stop();
            alarmEngine.Detach();

            await host.StopAsync();
            ui.InvokeShutdown();

            return 0;
        }

        private static int ReadArg(string[] args, string name, int fallback)
        {
            for (var i = 0; i + 1 < args.Length; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(args[i + 1], out var value) && value > 0)
                    return value;
            return fallback;
        }

        private static string? ReadStringArg(string[] args, string name)
        {
            for (var i = 0; i + 1 < args.Length; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(args[i + 1]))
                    return args[i + 1];
            return null;
        }
    }
}
