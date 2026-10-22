using Core.Interfaces;
using Prism.Ioc;
using VisionMaster.Lifetime;
using VisionMaster.Models;
using VisionMaster.Scada;
using VisionMaster.Services;

namespace VisionMaster.ScadaWeb
{
    /// <summary>
    /// SCADA 运行态 Web 客户端模块（<b>M2a：HTTP + WebSocket + 命令面</b>）。
    ///
    /// <b>本版做到哪一步</b>
    /// ---------
    ///   · <see cref="Register"/> 只把自己登记进容器（RegisterTypes 阶段容器尚未构建，Resolve 会抛）；
    ///   · <see cref="Initialize"/> 解析运行态/账号/目录/日志/配置 + 命令面的四个写路径出口
    ///     （<see cref="IScadaActionDispatcher"/> / <see cref="IScadaValueWriter"/> /
    ///     <see cref="IScadaAlarmCommands"/> / <see cref="IScadaAuditSink"/>），构造 <see cref="ScadaWebHost"/> 并启动；
    ///   · 注册一个退出任务：停服时<b>先</b>停 Web（退出链按注册的相反顺序执行，
    ///     本模块由宿主最后加入、最后 Initialize，所以它的退出任务必然排在第一位）。
    ///
    /// <b>为什么两个入口都做幂等</b>
    /// ---------
    /// 模块是"可选加载"的，装配路径不止一条（宿主 RegisterTypes、断言宿主直接装配、将来的
    /// 热重载/重复装配）。重复 <see cref="Register"/> 在真实容器里是重复登记，重复
    /// <see cref="Initialize"/> 会多注册一个退出任务——两者都必须<b>第二次起静默返回</b>。
    ///
    /// <b>失败只记 Error、不阻断启动</b>
    /// ---------
    /// Web 是可选项：端口被占、权限不足、程序集版本不匹配都不该把宿主带下去。
    /// 唯一"允许不启动"的硬条件是启动断言三连不满足（fail-open 比不启动更危险，
    /// 见 <see cref="ScadaWebHost.Start"/>），那种情况同样只记 Error。
    ///
    /// <b>日志口径</b>
    /// ---------
    /// 前缀固定 <c>[ScadaWeb]</c>：加载器（"缺程序集""出现契约副本"）与运行时用同一个词，
    /// 现场检索一个词就能看到"有没有加载 / 为什么没加载 / 起了没"。
    /// </summary>
    public sealed class ScadaWebModule : IAppModule
    {
        /// <summary>Register 是否已执行（幂等守卫）</summary>
        private bool _registered;

        /// <summary>Initialize 是否已执行（幂等守卫）</summary>
        private bool _initialized;

        /// <inheritdoc/>
        public string Name => "SCADA Web 运行态模块";

        /// <summary>
        /// Initialize 实际执行次数（幂等守卫之后才自增）。
        /// 只给断言宿主观测"连续调两次只执行一次"用，产品代码不读它。
        /// </summary>
        public int InitializeCount { get; private set; }

        /// <summary>
        /// 本模块创建的 Web 宿主；未启用 / 初始化失败时为 <c>null</c>。
        /// 断言宿主与探针用它直接查路由行为（产品代码不需要它）。
        /// </summary>
        public ScadaWebHost? Host { get; private set; }

        /// <inheritdoc/>
        public void Register(IContainerRegistry registry)
        {
            if (_registered) return;
            _registered = true;

            // 只登记自己：Web 宿主的依赖（IScadaRunHub / IScadaUserAuthenticator / IScadaRenderCatalog）
            // 都由宿主注册，模块不重复登记（两份实例 = 两套状态）。
            // 工厂返回 this，保证"被加载的那个实例"与容器里的是同一份。
            registry.RegisterSingleton<ScadaWebModule>(() => this);
        }

        /// <inheritdoc/>
        public void Initialize(IContainerProvider container, AppLifetimeService lifetime)
        {
            if (_initialized) return;
            _initialized = true;
            InitializeCount++;

            ILogService log;
            try
            {
                log = container.Resolve<ILogService>();
            }
            catch
            {
                // 连日志都取不到 = 容器装配出错，此时任何日志都没有落点，只能安静退出
                return;
            }

            ScadaWebSettings? settings;
            try
            {
                // 注意用 ?. ：装配桩（断言宿主）对未登记的类型返回 null 而不是抛异常。
                // "取不到配置"与"配置里 Enabled=false"必须走同一条降级路径（不启动），
                // 区别只在日志文案——绝不能因为读不到配置就去猜一个默认值把端口开起来。
                settings = container.Resolve<AppSettingsService>()?.Current?.ScadaWeb;
            }
            catch (Exception ex)
            {
                log.Warn($"[ScadaWeb] 读取软件配置失败：{ex.GetType().Name}：{ex.Message}");
                settings = null;
            }

            if (settings == null)
            {
                log.Info("[ScadaWeb] 拿不到软件配置（AppConfig.json 不可用）：模块已加载，但不启动 HTTP 服务");
                return;
            }

            // 加载器已经按 Enabled 决定过"要不要加载本模块"（见 ScadaWebModuleLoader）。
            // 这里再判一次是为了"断言宿主 / 将来手工装配"这些绕过加载器的路径：
            // Enabled=false 时模块可以被加载，但不许监听任何端口。
            if (!settings.Enabled)
            {
                log.Info("[ScadaWeb] ScadaWeb.Enabled=false：模块已加载，但不启动 HTTP 服务");
                return;
            }

            // 三个领域出口都按"可缺席"解析：
            //   · IScadaRunHub / IScadaUserAuthenticator 由宿主注册（App.RegisterTypes），正常都在；
            //   · IScadaRenderCatalog 是 M1 才落地的目录出口，缺失时 /api/catalog 回空目录而不是让模块不启动。
            // 缺席不等于失败——但必须留一条 Warn，否则"页面上图元目录是空的"没人知道为什么。
            var hub = TryResolve<IScadaRunHub>(container, log, "IScadaRunHub");
            var authenticator = TryResolve<IScadaUserAuthenticator>(container, log, "IScadaUserAuthenticator");
            var catalog = TryResolve<IScadaRenderCatalog>(container, log, "IScadaRenderCatalog");

            // M1b：值帧通道。缺失时 Web 仍启动（只发结构、值帧为空），但那条 Warn 必须留着——
            // 否则现场看到的就是"画面在、数字全是空的"，无从判断是没值源还是变量没绑上。
            var values = TryResolve<IScadaValueSource>(container, log, "IScadaValueSource");

            // M2a：命令面的四个领域出口。缺失不阻断启动（Web 仍是可用的只读镜像），
            // 但必须记 Error：缺哪个出口，对应那条命令就永远回 E_INTERNAL（"点了没反应"最难查）。
            // 契约名逐一写在日志里，现场照着 App.RegisterTypes 就能对上。
            var actionDispatcher = TryResolveCommandExit<IScadaActionDispatcher>(container, log, "IScadaActionDispatcher", "cmd.element（图元事件）");
            var valueWriter = TryResolveCommandExit<IScadaValueWriter>(container, log, "IScadaValueWriter", "cmd.input（输入写值）");
            var alarmCommands = TryResolveCommandExit<IScadaAlarmCommands>(container, log, "IScadaAlarmCommands", "cmd.alarm.ack（确认报警）");
            var alarmFeed = TryResolve<IScadaAlarmFeed>(container, log, "IScadaAlarmFeed");
            var audit = TryResolveCommandExit<IScadaAuditSink>(container, log, "IScadaAuditSink", "命令级审计（谁按的）");

            // A 范围的安全闸：Web 写变量前必须能判"它是不是流程触发器"。
            // 解析不到时命令层 fail closed（cmd.input / 写变量动作回 E_INTERNAL），
            // 宁可让现场看到"输入不可用"，也不能让一条 Web 写入间接启动含运动步骤的流程。
            var flowTriggerQuery = TryResolve<IScadaFlowTriggerQuery>(container, log, "IScadaFlowTriggerQuery");
            if (settings.AllowCommands && flowTriggerQuery == null)
            {
                log.Error("[ScadaWeb] 未接入 IScadaFlowTriggerQuery：cmd.input 与写变量动作将回 E_INTERNAL（fail closed）");
            }

            // 轴面板/轴控台/点位表的只读数据出口（运动模块注册；无运动模块时为 null，
            // 轴图元仍能显示候选轴名，但实时值显示“—”）。
            var axisSource = TryResolve<IScadaAxisSource>(container, log, "IScadaAxisSource");

            // AllOperators 的监管闸门：缺项不是“少一层”，而是“配置看着放开了、实际没有护栏”。
            // 缺项时拒绝启用该策略，回退 HighestRoleOnly（默认策略，不需要监管项）。
            //
            // ★ 必须在 Normalize **之前**用原始配置判：Normalize 会把 RequireAudit 夹回 true，
            //   夹完再判等于这一项永远通过——"审计被关掉"这种非法配置就再也拦不住了。
            if (settings.ClickPolicy == ScadaWebClickPolicy.AllOperators)
            {
                var probe = new ScadaWebAccessPolicy(settings, () => Array.Empty<ScadaWebSession>());
                if (!probe.ValidateSupervision(out var supervisionError))
                {
                    log.Error($"[ScadaWeb] {supervisionError}；已回退 HighestRoleOnly（AllOperators 监管项不全时不允许放开）");
                    settings.ClickPolicy = ScadaWebClickPolicy.HighestRoleOnly;
                }
            }

            // 配置夹取（RequireAudit 只能是 true；AllOperatorsRequiresLease 不能单独成立）。
            // 夹取本身不能静默：现场永远不知道自己配错了一项，直到出事查审计才发现没有记录。
            foreach (var message in ScadaWebAccessPolicy.Normalize(settings))
                log.Error($"[ScadaWeb] {message}");

            // 写路径出口：审计缺失必须 fail closed（把总闸关掉），不能只 Warn。
            // 没有审计的写操作 = "动作执行了但查不到是谁按的"，比"不能写"危险得多——
            // 所以这里直接把 AllowCommands 置 false，宿主拿到的是一份已关闭命令面的配置。
            if (settings.AllowCommands && audit == null)
            {
                settings.AllowCommands = false;

                log.Error("[ScadaWeb] AllowCommands=true 但未接入审计出口（IScadaAuditSink）："
                    + "已强制关闭命令面（fail closed），cmd.* 一律回 E_POLICY；接入审计后重启生效");
            }
            else if (settings.AllowCommands
                     && (actionDispatcher == null || valueWriter == null || alarmCommands == null))
            {
                log.Error("[ScadaWeb] AllowCommands=true 但写路径出口不全：缺失的那些命令会回 E_INTERNAL"
                    + "（审计已接入，命令级记录会落盘；写路径不会假装成功）");
            }

            // UI Dispatcher：快照构造与取值必须在 UI 线程做（模型集合是单线程语义）。
            // Application.Current 为 null = 非 WPF 宿主（断言/探针），传 null 走"直接调用"分支。
            var uiDispatcher = System.Windows.Application.Current?.Dispatcher;

            var host = new ScadaWebHost(
                hub,
                authenticator,
                catalog,
                log,
                settings,
                values,
                uiDispatcher,
                actionDispatcher,
                valueWriter,
                alarmCommands,
                audit,
                alarmFeed,
                flowTriggerQuery,
                axisSource);

            Host = host;

            host.Start();

            if (host.LastError != null)
            {
                // Start() 内部已经记过 Error；这里只补一条"模块可用但没起"的结论，方便现场判断
                log.Warn("[ScadaWeb] Web 服务未能启动（宿主继续运行）；修改配置后需重启软件生效");
                return;
            }

            // 账号写操作（改密 / 重置密码 / 改角色 / 删号）→ 立刻撤销该账号的全部 Web 会话。
            // 没有这一条，8 小时的会话令牌会在改密/降权/删号之后继续有效（2026-10-21 终审 P0-A）。
            // 通知接口是可选的：断言宿主/旧装配没有它时，Web 仍能启动，只是少了"改密即踢下线"。
            IScadaUserChangeNotifier? userChanges = null;
            try
            {
                userChanges = container.Resolve<IScadaUserChangeNotifier>();
            }
            catch
            {
                // 可选能力，缺席不记 Warn（否则每个断言宿主都会多一行与本次无关的噪音）
            }

            Action<string, string>? userChangedHandler = null;

            if (userChanges != null)
            {
                userChangedHandler = (userName, reason) =>
                {
                    try
                    {
                        var closed = host.RevokeUser(userName);
                        log.Info($"[ScadaWeb] 账号「{userName}」{reason}：已撤销其 Web 会话，关闭 {closed} 条连接");
                    }
                    catch (Exception ex)
                    {
                        // 账号写操作已经成功；撤销失败只影响"旧会话没被立刻踢掉"，不能反过来影响账号操作。
                        log.Error($"[ScadaWeb] 账号「{userName}」{reason}后撤销 Web 会话失败："
                                  + $"{ex.GetType().Name}：{ex.Message}");
                    }
                };

                userChanges.UserChanged += userChangedHandler;
            }
            else
            {
                // 新 Web DLL + 旧宿主（没有该接口）是现实组合；静默降级等于把"改密不踢人"的洞藏起来。
                log.Warn("[ScadaWeb] 未接入 IScadaUserChangeNotifier：改密/重置密码/改角色/删号后，"
                         + "旧 Web 会话不会立刻失效（要等 8 小时令牌自然过期）");
            }

            // 超时 15s：停服要等命令泵把手里的命令跑完（最多 5s）+ 关闭帧（2s）+ 两条循环（各 2s）。
            // 5s 是 M1 只有值帧循环时的口径；M2a 接了命令泵之后不够用（超时会被退出链强制丢下）。
            lifetime.RegisterExitTask(new ExitTask(
                "停止 SCADA Web 服务",
                async () =>
                {
                    if (userChanges != null && userChangedHandler != null)
                        userChanges.UserChanged -= userChangedHandler;

                    await host.StopAsync().ConfigureAwait(false);
                },
                15000));
        }

        /// <summary>
        /// 解析一个可选依赖：取不到时记 Warn 并返回 <c>null</c>（不抛）。
        /// 这里刻意不吞掉"已注册但构造失败"与"根本没注册"的区别——两者对模块的意义相同
        /// （该出口不可用），但日志里把异常原文带上，现场能分辨。
        /// </summary>
        private static T? TryResolve<T>(IContainerProvider container, ILogService log, string contractName)
            where T : class
        {
            try
            {
                return container.Resolve<T>();
            }
            catch (Exception ex)
            {
                log.Warn($"[ScadaWeb] 未能解析 {contractName}（{ex.GetType().Name}）：{ex.Message}"
                    + "。该出口相关功能将降级，Web 服务仍会启动");
                return null;
            }
        }

        /// <summary>
        /// 解析 M2a 命令面的写路径出口：缺失记 <b>Error</b>（不是 Warn）并返回 <c>null</c>。
        ///
        /// 为什么级别更高：这四个出口各自对应一条<b>会改变现场状态</b>的命令
        /// （图元动作 / 写变量 / 确认报警 / 审计）。缺了它们的表现是"网页上点了没反应"或
        /// "动作执行了但审计里查不到是谁按的"——两种都只有翻日志才能定位。
        /// 仍然<b>不阻断启动</b>：只读镜像本身（画面 + 值帧 + 在线列表）不依赖它们。
        /// </summary>
        private static T? TryResolveCommandExit<T>(
            IContainerProvider container,
            ILogService log,
            string contractName,
            string purpose)
            where T : class
        {
            try
            {
                var resolved = container.Resolve<T>();

                if (resolved == null)
                {
                    log.Error($"[ScadaWeb] 命令面出口 {contractName} 解析为 null：{purpose} 将不可用（回 E_INTERNAL）");
                }

                return resolved;
            }
            catch (Exception ex)
            {
                log.Error($"[ScadaWeb] 未能解析命令面出口 {contractName}（{ex.GetType().Name}）：{ex.Message}"
                    + $" → {purpose} 将不可用（回 E_INTERNAL），Web 服务仍会启动");
                return null;
            }
        }
    }
}
