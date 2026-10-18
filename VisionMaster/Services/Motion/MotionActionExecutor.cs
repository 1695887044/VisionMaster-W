using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using Core.Interfaces;
using VisionMaster.Scada;

namespace VisionMaster.Services.Motion
{
    /// <summary>
    /// 组态动作表里"运动命令族"的执行器：把一条 <see cref="ScadaAction"/> 翻成一条
    /// <see cref="MotionCommand"/> 并交给目标卡的 <see cref="IMotionDevice.Enqueue"/>。
    ///
    /// <b>下发成功 ≠ 到位</b>（与 <c>MotionMovePlugin</c> 同一口径）
    /// ---------
    /// <see cref="IMotionDevice.Enqueue"/> 的返回值只回答"命令有没有被收进队列"。
    /// 轴真正走到位是卡的命令线程 + 轮询线程之后的事，本类<b>不等待</b>、也不代表动作已完成。
    /// 这不是省事：组态按钮后面常常跟着"两轴同时动"，一旦在这里阻塞等到位，
    /// 两条轴就只能串行跑，而现场只会看到"点一下要等很久"却找不出原因。
    /// 要等轴到位，正解是在流程里放「等待到位」步骤，或在画面里读轴位置做后续判断。
    ///
    /// <b>为什么执行器要单独一层，而不是让分发器直接碰 IMotionDevice</b>
    /// ---------
    /// ① 分发器只认"动作语义"（日志/变量/画面/流程/运动），不该同时长出"轴号怎么解、
    ///    门禁怎么过、点动看门狗怎么管"这一整套运动知识——那会让它变成一个什么都懂的上帝类；
    /// ② 点动看门狗是<b>有状态</b>的（要记住"哪根轴正在点动"），而分发器是无状态单例；
    /// ③ 断言工程可以在没有运动卡的情况下把分发器整条链跑完（不注入本类即可），
    ///    与 <see cref="IScadaFlowRunner"/> 可选参数同一个取舍。
    ///
    /// <b>取轴口径</b>
    /// ---------
    /// 轴解析先按稳定身份 <see cref="ScadaAction.AxisId"/> 在 <see cref="MotionAxisRegistry.Snapshot"/>
    /// 里找，找不到再按轴名兜底（<see cref="MotionAxisRegistry.TryResolve"/>）。
    /// 这正是 <see cref="MotionAxisResolution"/> 定下的口径：<b>Id 优先、名字兜底</b>——
    /// 轴改名不会打断已配好的按钮，而旧数据（只有名字）也还能跑。
    ///
    /// <b>门禁</b>
    /// ---------
    /// "能不能动"一律问 <see cref="MotionCommandGate"/>（唯一实现），本类不另写判断：
    /// 门禁散落各处的结果必然是"加一条闸只加了一个界面"，而漏掉的那次就是对着报警中的轴下发 Move。
    /// 两处刻意的例外，都写在各方法上：
    ///   · <b>清轴报警</b>不能用 <see cref="MotionCommandGate.CheckDevice"/>（它要求 Online，
    ///     而报警态正是最需要清报警的时刻）；
    ///   · <b>点动结束</b>不过门禁（停是安全动作，门禁那条"未连接"恰是最该把停止发下去的时候）。
    ///
    /// <b>点动看门狗</b>
    /// ---------
    /// 点动没有终点，"松手即停"完全依赖调用方记得发停止。组态画面没有"按住"这种手势
    /// （一个按钮只触发一次），所以这里的兜底比调试面板更必要：<c>点动开始</c> 之后若在
    /// <see cref="MotionJogGuard.PulseTimeoutMs"/> × 2 内没有等到 <c>点动结束</c>，
    /// 200ms 的定时器会直接下发停止并记一条 Warn。
    /// 判据复用 <see cref="MotionJogGuard.IsPulseTimedOut"/>（安全判断只有一份），
    /// 定时器只负责"什么时候问"。
    /// </summary>
    public sealed class MotionActionExecutor : IDisposable
    {
        /// <summary>
        /// 点动看门狗的扫描周期。取 200ms：判据本身是 500ms×2=1000ms，
        /// 扫描粒度比阈值小一个量级，超时后最多再走 200ms 就会被停住；
        /// 再快只会空转（点动是慢动作，机械上多走 200ms 不构成风险）。
        /// </summary>
        private static readonly TimeSpan WatchdogPeriod = TimeSpan.FromMilliseconds(200);

        /// <summary>
        /// 回零命令的超时下限。回零要先找开关再找 Index，慢是正常的；
        /// 卡级默认的 30s（<see cref="MotionParams.CommandTimeoutMs"/>）对回零经常不够，
        /// 与「轴回零」步骤默认 120s 是同一个理由。取<b>较大者</b>而不是覆盖：
        /// 用户把卡级超时调大说明现场确实更慢，那也该生效。
        /// </summary>
        private const int HomeTimeoutFloorMs = 120_000;

        /// <summary>
        /// 一根"正在点动"的轴。为什么要连设备引用一起记住、而不是只记时刻：
        /// 看门狗触发时轴可能已经改名甚至被删（注册表里查不到了），此时"按现在的名字再解析一次"
        /// 会解析失败 → 停止发不出去 → 轴一直走。记住当时那份设备 + 轴号，兜底才真能停住。
        /// </summary>
        private sealed class JogLease
        {
            public DateTime StartedUtc { get; init; }

            public IMotionDevice Device { get; init; } = null!;

            public int PhysicalAxis { get; init; }

            public string AxisName { get; init; } = string.Empty;
        }

        private readonly MotionAxisRegistry _registry;
        private readonly IMotionProvider _motions;
        private readonly ILogService _log;

        /// <summary>当前正在点动的轴（键 = <c>AxisId</c>）。并发字典：写入在 UI 线程、扫描在计时器线程</summary>
        private readonly ConcurrentDictionary<Guid, JogLease> _activeJogs = new();

        private readonly Timer _watchdog;

        private int _disposed;

        /// <param name="registry">轴注册表（全局唯一轴名 → 卡 + 轴号）</param>
        /// <param name="motions">运行态设备仓库（按卡 Id 取设备）</param>
        /// <param name="log">日志（看门狗与释放路径要留痕；正常执行的成功/失败由分发器记）</param>
        public MotionActionExecutor(MotionAxisRegistry registry, IMotionProvider motions, ILogService log)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _motions = motions ?? throw new ArgumentNullException(nameof(motions));
            _log = log ?? throw new ArgumentNullException(nameof(log));

            // 建好就开跑：构造点由宿主注册（单例），没有"启动"这一步要记得调——
            // 忘了调的表现是"点动按钮点下去轴一直走"，而那正是最不能靠人记住的事。
            _watchdog = new Timer(OnWatchdog, null, WatchdogPeriod, WatchdogPeriod);
        }

        /// <summary>
        /// 执行一条运动动作。
        /// </summary>
        /// <param name="action">要执行的动作（类型必须是运动九条之一）</param>
        /// <param name="message">
        /// 给日志/审计的一句话（可直接上屏）。
        /// <b>返回 false 且 message 为空串 = 静默忽略</b>：调用方据此<b>不要</b>记日志也不要落审计。
        /// 目前只有"点动时轴正忙"这一种（门禁的 Silent 档）——按住/连点期间它会反复发生，
        /// 每次都记一行 Warning 会把真正该看的告警淹掉，而用户什么都没做错。
        /// </param>
        /// <returns>命令是否已被卡接受（<b>不代表已到位</b>，见类注释）</returns>
        public bool TryExecute(ScadaAction action, out string message)
        {
            message = string.Empty;

            if (action == null)
            {
                message = "动作是空的（数据损坏），本条未执行";
                return false;
            }

            if (!TryResolveAxis(action, out var binding, out var device, out message))
                return false;

            // 读一次状态快照（轮询缓存，不发起通信）。拿不到 = 卡还没轮询出这一轴：
            // 门禁会据此报"未连接"，比在这里编一个"假定空闲"安全得多。
            var status = device.GetAxisStatus(binding.AxisIndex);

            switch (action.Type)
            {
                case ScadaActionType.MotionEnable:
                    return ExecuteEnable(action, binding, device, enable: true, out message);

                case ScadaActionType.MotionDisable:
                    return ExecuteEnable(action, binding, device, enable: false, out message);

                case ScadaActionType.MotionMoveAbsolute:
                    return ExecuteMove(action, binding, device, status, relative: false, out message);

                case ScadaActionType.MotionMoveRelative:
                    return ExecuteMove(action, binding, device, status, relative: true, out message);

                case ScadaActionType.MotionHome:
                    return ExecuteHome(action, binding, device, status, out message);

                case ScadaActionType.MotionStop:
                    return ExecuteStop(action, binding, device, out message);

                case ScadaActionType.MotionClearAlarm:
                    return ExecuteClearAlarm(action, binding, device, out message);

                case ScadaActionType.MotionJogStart:
                    return ExecuteJogStart(action, binding, device, status, out message);

                case ScadaActionType.MotionJogStop:
                    return ExecuteJogStop(action, binding, device, out message);

                default:
                    // 编译期不该走到：调用方（分发器）只把运动九条路由过来。
                    // 留着是防"以后加了运动类型忘了改这里"——如实报，不要静默返回 true。
                    message = $"{action.Type.DisplayName()} 不是运动动作，本执行器不处理";
                    return false;
            }
        }

        /// <summary>
        /// 轴解析：Id 优先、名字兜底（见类注释的取轴口径）。
        /// 失败原因参照 <see cref="MotionAxisResolution"/> 的措辞口径——同一件事在流程侧与组态侧
        /// 必须说同一句话，否则现场要学两套排查话术。
        /// </summary>
        private bool TryResolveAxis(
            ScadaAction action, out MotionAxisBinding binding, out IMotionDevice device, out string error)
        {
            binding = null!;
            device = null!;

            if (!action.HasAxis)
            {
                error = "这条动作还没选轴：请点动作行上的「选轴」挑一根";
                return false;
            }

            MotionAxisBinding? found = null;

            if (action.AxisId != Guid.Empty)
                found = _registry.Snapshot().FirstOrDefault(b => b.AxisId == action.AxisId);

            // 名字兜底：旧数据（只有名字）与"轴被删后重建、Id 变了但名字没变"这两种情况都靠它
            if (found == null
                && !string.IsNullOrWhiteSpace(action.AxisName)
                && _registry.TryResolve(action.AxisName, out var byName))
            {
                found = byName;
            }

            if (found == null)
            {
                error = string.IsNullOrWhiteSpace(action.AxisName)
                    ? "找不到这条动作配的轴：它可能已被删除，请重新选轴"
                    : $"找不到轴「{action.AxisName}」：它可能已被删除或改名，请重新选轴";
                return false;
            }

            // 停用的轴一律不动：与流程侧、调试面板同一口径（注册表知道它存在，但"能不能用"归调用方判）
            if (!found.Enabled)
            {
                error = $"轴「{found.Name}」在卡「{found.CardCaption}」上处于停用状态："
                        + "请到「运动卡设置」把它启用，或换一个轴名";
                return false;
            }

            // 按卡 Id 取设备，不走地址：地址在方案里不保证唯一（见 MotionAxisBinding.CardAddress 的说明）。
            // 先落到局部变量再判 null：TryGetDevice 的 out 参数在"取不到"时是 null，
            // 而它的签名没有可空标注——直接拿 device 用等于把空引用留到下面某一处才炸。
            if (!_motions.TryGetDevice(found.CardId, out var resolved) || resolved == null)
            {
                error = $"找不到运动卡「{found.CardCaption}」的运行态设备："
                        + "请确认该卡已在「运动卡设置」里配置并保存到方案";
                return false;
            }

            device = resolved;
            binding = found;
            error = string.Empty;
            return true;
        }

        /// <summary>使能 / 失能：设备级门禁（未连接、报警、安全停机都不许）。</summary>
        private bool ExecuteEnable(
            ScadaAction action, MotionAxisBinding binding, IMotionDevice device, bool enable, out string message)
        {
            var gate = MotionCommandGate.CheckDevice(device);
            if (!gate.Passed)
            {
                message = $"{action.Describe()}：{gate.Reason}，本条未执行";
                return false;
            }

            var command = NewAxisCommand(
                enable ? MotionCommandKind.Enable : MotionCommandKind.Disable, binding, device);

            return Enqueue(device, command, action, out message);
        }

        /// <summary>
        /// 定位（绝对 / 相对）：门禁 + 下发。
        ///
        /// <b>相对定位的软限位判据是"走完之后在哪"，不是增量本身</b>：
        /// 门禁的 <see cref="MotionCommandGate.CheckMove"/> 只有一个"目标位置"参数，
        /// 而相对运动的增量直接当目标交给它，会放过"当前 190 mm、再走 50 mm"这种越界
        /// （增量 50 落在 [0,200] 内，实际终点 240 却已经出界）。所以这里先把增量投影成
        /// 绝对终点再交给同一道门禁；真正下发的仍是增量（<see cref="MotionCommand.TargetMm"/> 的语义）。
        /// 位置读不到时退回用增量判——那是"卡还没轮询出位置"，设备级门禁本来就会先拒。
        /// </summary>
        private bool ExecuteMove(
            ScadaAction action, MotionAxisBinding binding, IMotionDevice device,
            AxisStatus? status, bool relative, out string message)
        {
            if (!action.TargetMm.HasValue)
            {
                message = $"{action.Type.DisplayName()}：还没填目标位置，本条未执行";
                return false;
            }

            var target = action.TargetMm.Value;
            var gateTarget = relative && status != null ? status.PositionMm + target : target;

            var gate = MotionCommandGate.CheckMove(
                device, binding.Mapping, status, gateTarget, relative ? "相对定位" : "定位");

            if (!gate.Passed)
            {
                message = $"{action.Describe()}：{gate.Reason}，本条未执行";
                return false;
            }

            var command = NewAxisCommand(
                relative ? MotionCommandKind.MoveRelative : MotionCommandKind.MoveAbsolute, binding, device,
                targetMm: target, velocityMmPerS: VelocityOf(action));

            return Enqueue(device, command, action, out message);
        }

        /// <summary>
        /// 回零：门禁只挡"未连接 / 未选轴 / 忙"（回零本身就是为了解决未回零）。
        ///
        /// <b>回零方式取卡级默认</b>：动作表不提供逐条选择（那是流程步骤的参数，按钮上多一个下拉
        /// 只会让每个按钮都可能配错）。具体取值是"该卡上报支持的第一种方式"，卡没上报能力时
        /// 退回契约默认 <see cref="HomeMode.NegativeLimitIndex"/> —— 不是新发明的配置项，
        /// 而是"卡说支持什么就用什么"，避免按钮在只支持别的模式的卡上必然失败。
        /// </summary>
        private bool ExecuteHome(
            ScadaAction action, MotionAxisBinding binding, IMotionDevice device, AxisStatus? status, out string message)
        {
            var gate = MotionCommandGate.CheckHome(device, binding.Mapping, status, "回零");
            if (!gate.Passed)
            {
                message = $"{action.Describe()}：{gate.Reason}，本条未执行";
                return false;
            }

            var command = new MotionCommand
            {
                Kind = MotionCommandKind.Home,
                PhysicalAxis = binding.AxisIndex,
                LogicalAxis = binding.Name,
                HomeMode = ResolveHomeMode(device),
                WaitsForCompletion = true,
                // 回零绝不允许盲目重试：中途失败可能正卡在原点开关上，再来一次可能撞机械原点
                Retryable = false,
                Timeout = TimeSpan.FromMilliseconds(
                    Math.Max(HomeTimeoutFloorMs, device.Descriptor.Params.CommandTimeoutMs)),
            };

            return Enqueue(device, command, action, out message);
        }

        /// <summary>卡上报的第一种回零方式；卡没上报能力时退回契约默认。</summary>
        private static HomeMode ResolveHomeMode(IMotionDevice device)
        {
            var supported = device.Capabilities.SupportedHomeModes;
            return supported is { Count: > 0 } ? supported[0] : HomeMode.NegativeLimitIndex;
        }

        /// <summary>
        /// 轴停止：减速停（<see cref="MotionCommand.StopMode"/> 默认 2），与流程侧「轴停止」的默认档一致。
        /// 立即停对机械冲击很大，只该由急停通道（<see cref="IMotionDevice.EmergencyStop"/>）做。
        ///
        /// 门禁用 <see cref="MotionCommandGate.CheckDevice"/>（规格：停止同样要卡在线才下发）：
        /// 离线的卡上本来就没有在走的轴，如实报"运动卡未连接"比"发出去但没人收"更准确。
        /// </summary>
        private bool ExecuteStop(ScadaAction action, MotionAxisBinding binding, IMotionDevice device, out string message)
        {
            var gate = MotionCommandGate.CheckDevice(device);
            if (!gate.Passed)
            {
                message = $"{action.Describe()}：{gate.Reason}，本条未执行";
                return false;
            }

            var command = NewStopCommand(device, binding.AxisIndex, binding.Name);

            return Enqueue(device, command, action, out message);
        }

        /// <summary>
        /// 清轴报警。
        ///
        /// <b>这里刻意不用 <see cref="MotionCommandGate.CheckDevice"/></b>：那道门禁要求
        /// <see cref="MotionCardState.Online"/>，而"报警中"正是最需要清报警的时刻——
        /// 照抄的结果是这条动作在唯一有用的场景下永远报"运动卡未连接"。
        /// 判据与设备自己的门禁（<c>MotionDeviceBase.CheckGateLocked</c>）逐字一致：
        /// <see cref="MotionCardState.Online"/> 与 <see cref="MotionCardState.Alarm"/> 放行，其余拒绝。
        /// </summary>
        private bool ExecuteClearAlarm(
            ScadaAction action, MotionAxisBinding binding, IMotionDevice device, out string message)
        {
            if (device.State != MotionCardState.Online && device.State != MotionCardState.Alarm)
            {
                message = $"{action.Describe()}：运动卡「{device.Descriptor.Caption}」当前状态不允许清除报警"
                          + $"（{device.StateDetail}），本条未执行";
                return false;
            }

            var command = NewAxisCommand(MotionCommandKind.ClearAlarm, binding, device);

            return Enqueue(device, command, action, out message);
        }

        /// <summary>
        /// 点动开始：门禁 <see cref="MotionCommandGate.CheckJog"/>（含"该卡未上报点动能力"），
        /// 方向由 <see cref="ScadaAction.TargetMm"/> 的正负号决定（缺省正向，见 <see cref="ScadaAction.IsJogForward"/>）。
        ///
        /// 成功后登记"点动截止时刻"，看门狗据此兜底。
        /// </summary>
        private bool ExecuteJogStart(
            ScadaAction action, MotionAxisBinding binding, IMotionDevice device,
            AxisStatus? status, out string message)
        {
            var gate = MotionCommandGate.CheckJog(device, binding.Mapping, status, device.Capabilities.SupportsJog);
            if (!gate.Passed)
            {
                // 静默档（轴正忙）：不记日志、不落审计。message 留空是这条约定的表达方式，
                // 调用方据此直接收手（见 TryExecute 的 message 说明）。
                if (gate.Silent)
                {
                    message = string.Empty;
                    return false;
                }

                message = $"{action.Describe()}：{gate.Reason}，本条未执行";
                return false;
            }

            var command = new MotionCommand
            {
                Kind = MotionCommandKind.Jog,
                PhysicalAxis = binding.AxisIndex,
                LogicalAxis = binding.Name,
                JogDirection = action.IsJogForward ? 1 : -1,
                VelocityMmPerS = VelocityOf(action),
                // 点动没有终点，重试一次就是"再走一段"——绝不允许自动重试
                Retryable = false,
                Timeout = TimeSpan.FromMilliseconds(device.Descriptor.Params.CommandTimeoutMs),
            };

            if (!Enqueue(device, command, action, out message))
                return false;

            // 先登记再返回：登记与下发之间的窗口越小，"轴在走但看门狗不知道"的时间越短
            _activeJogs[binding.AxisId] = new JogLease
            {
                StartedUtc = DateTime.UtcNow,
                Device = device,
                PhysicalAxis = binding.AxisIndex,
                AxisName = binding.Name,
            };

            message = $"{action.Describe()} 已下发（轴会一直走，直到「点动结束」或看门狗兜底停止）";
            return true;
        }

        /// <summary>
        /// 点动结束：清登记 + 下发停止。
        ///
        /// <b>不要求门禁通过</b>：停是安全动作。门禁里那条"运动卡未连接"恰恰是最该把停止
        /// 发下去的时候（连接断了也要尽力发），挡在门禁后面等于"越危险越不发"。
        /// 设备若真的拒收，<see cref="Enqueue"/> 会把拒绝原因原样带上来。
        ///
        /// 走的是与看门狗/释放路径同一个 <see cref="NewStopCommand"/>：
        /// 三条"兜底停止"的参数必须逐字一致，各写一份迟早分叉。
        /// </summary>
        private bool ExecuteJogStop(
            ScadaAction action, MotionAxisBinding binding, IMotionDevice device, out string message)
        {
            // 先清登记：即使停止被拒，也不该让看门狗在下一拍再停一次（它会把日志刷成两条）
            _activeJogs.TryRemove(binding.AxisId, out _);

            var command = NewStopCommand(device, binding.AxisIndex, binding.Name);

            return Enqueue(device, command, action, out message);
        }

        /// <summary>
        /// 下发一条命令并翻译结果。所有"没被收下"的出口都在这里，理由只有一处，
        /// 免得八个动作各拼一句"命令被拒绝"，措辞分叉后现场就要学八套话术。
        /// </summary>
        private bool Enqueue(IMotionDevice device, MotionCommand command, ScadaAction action, out string message)
        {
            MotionCommandResult result;
            try
            {
                result = device.Enqueue(command);
            }
            finally
            {
                // 本类从不等待命令完成（下发 ≠ 到位），命令对象用完即释放——
                // 与流程侧「轴运动」「轴停止」步骤 `using var command` 同一口径。
                // 设备侧所有 Completion.Set 都包在 catch(ObjectDisposedException) 里，
                // 所以"入队后立刻释放"是安全的（执行状态看 State/Error，不看这个信号）。
                command.Dispose();
            }

            if (result != MotionCommandResult.Accepted)
            {
                // 拒绝原因取设备最近一次故障的"建议"：那是唯一一处把厂商错误码翻成人话的地方
                // （驱动把码映射成"该怎么办"，这里再包一层"失败"就把可行动的信息盖掉了）
                var suggestion = device.LastFault?.Suggestion;
                message = $"{action.Describe()}：命令被拒绝（{result}）"
                          + (string.IsNullOrWhiteSpace(suggestion) ? string.Empty : $"：{suggestion}")
                          + $"，本条未执行（{device.StateDetail}）";
                return false;
            }

            // 成功也不说"已到位"：Enqueue 只代表收下了。措辞与 MotionMovePlugin 的日志保持一致。
            message = $"{action.Describe()} 已下发（未等到位）";
            return true;
        }

        /// <summary>
        /// 按轴映射造一条"轴级"命令（使能 / 失能 / 定位 / 清报警这类）。
        ///
        /// <c>Retryable</c>：使能、失能、定位保持契约默认（true）——重发都是安全的；
        /// 清报警给 false（与 <c>MotionDeviceBase.ClearAlarm</c> 的取值逐字一致：
        /// 清报警失败多半是"报警原因还在"，盲目重试没意义）。
        /// 停止与回零<b>不</b>走这里——那两条各自显式给 false（回零可能撞机械原点、
        /// 停止重发会对着已经在停的轴再停一次）。
        ///
        /// <c>WaitsForCompletion</c> 只有使能/失能/清报警给 true（与「轴使能」步骤、
        /// <see cref="IMotionDevice.ClearAlarm"/> 的实现同一口径）：这几条"做完才算数"，
        /// 定位则天然是"下发即返回"（等到位是上层的事）。本类<b>不</b>消费这个信号，
        /// 它只用于告诉驱动/诊断"这条命令是不是一段过程"。
        /// </summary>
        private static MotionCommand NewAxisCommand(
            MotionCommandKind kind, MotionAxisBinding binding, IMotionDevice device,
            double targetMm = 0, double velocityMmPerS = 0)
            => new()
            {
                Kind = kind,
                PhysicalAxis = binding.AxisIndex,
                LogicalAxis = binding.Name,
                TargetMm = targetMm,
                VelocityMmPerS = velocityMmPerS,
                WaitsForCompletion = kind is MotionCommandKind.ClearAlarm
                    or MotionCommandKind.Enable or MotionCommandKind.Disable,
                Retryable = kind != MotionCommandKind.ClearAlarm,
                Timeout = TimeSpan.FromMilliseconds(device.Descriptor.Params.CommandTimeoutMs),
            };

        /// <summary>速度：没填 / 填了非正数一律 0 = 用卡级默认（与 <see cref="MotionCommand.VelocityMmPerS"/> 的约定一致）</summary>
        private static double VelocityOf(ScadaAction action)
            => action.VelocityMmPerS is > 0 ? action.VelocityMmPerS.Value : 0;

        /// <summary>
        /// 点动看门狗：扫"登记过点动开始、但一直没等到点动结束"的轴，超时就直接停。
        ///
        /// 为什么判据要 ×2：<see cref="MotionJogGuard.PulseTimeoutMs"/> 是"界面按住期间重复脉冲"的
        /// 心跳周期（500ms），组态按钮没有重复脉冲，只发一次开始——所以这里给一个心跳周期的余量，
        /// 让"点一下开始、过一会儿点结束"这种正常用法不会误停。
        ///
        /// 整个方法吞掉所有异常：计时器回调里抛出去会直接终结进程（没有 UI 线程兜底），
        /// 而"看门狗自己崩了"比"多走一段"严重得多。
        /// </summary>
        private void OnWatchdog(object? state)
        {
            if (Volatile.Read(ref _disposed) != 0) return;

            try
            {
                var now = DateTime.UtcNow;
                var timeoutMs = MotionJogGuard.PulseTimeoutMs * 2;

                foreach (var pair in _activeJogs)
                {
                    var lease = pair.Value;
                    if (!MotionJogGuard.IsPulseTimedOut(now, lease.StartedUtc, timeoutMs)) continue;

                    // 抢不到（别的线程刚清掉）就跳过：说明点动结束已经处理过了
                    if (!_activeJogs.TryRemove(pair.Key, out var taken)) continue;

                    try
                    {
                        // using：命令对象带一个 ManualResetEventSlim，没人等它也必须释放
                        using var command = NewStopCommand(taken.Device, taken.PhysicalAxis, taken.AxisName);
                        var result = taken.Device.Enqueue(command);

                        _log.Warn(
                            $"[组态运动] 点动看门狗：轴「{taken.AxisName}」超过 {timeoutMs}ms 未收到「点动结束」，"
                            + $"已直接下发停止（{result}）");
                    }
                    catch (Exception ex)
                    {
                        _log.Error($"[组态运动] 点动看门狗下发停止时异常（轴「{taken.AxisName}」）：{ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error($"[组态运动] 点动看门狗自身异常（已忽略，下一拍重试）：{ex.Message}");
            }
        }

        /// <summary>
        /// 释放：停表 + 把还在点动的轴停掉。
        ///
        /// <b>不能只清表</b>：表里有登记说明那根轴可能还在走，只清表等于"放手让它走完"。
        /// 关软件/切方案时这条路径一定要发停止（与 MotionModule 退出时 Disconnect 先安全停轴同一口径）。
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            try { _watchdog.Dispose(); }
            catch { /* 释放路径上不再抛 */ }

            foreach (var pair in _activeJogs)
            {
                if (!_activeJogs.TryRemove(pair.Key, out var lease)) continue;

                try
                {
                    // 释放路径同样要释放命令对象（它带一个 ManualResetEventSlim）
                    using var command = NewStopCommand(lease.Device, lease.PhysicalAxis, lease.AxisName);
                    lease.Device.Enqueue(command);

                    _log.Warn($"[组态运动] 执行器释放：轴「{lease.AxisName}」仍处于点动状态，已下发停止");
                }
                catch (Exception ex)
                {
                    _log.Error($"[组态运动] 执行器释放时停止轴「{lease.AxisName}」失败：{ex.Message}");
                }
            }

            _activeJogs.Clear();
        }

        /// <summary>
        /// 造一条"停这一根轴"的命令。看门狗与释放路径共用一处：
        /// 两处都是"没人在等结果"的兜底停止，参数必须逐字一致（减速停、不重试、不等到位），
        /// 各写一份迟早会分叉成"看门狗减速停、释放立即停"这种说不清的行为差异。
        /// </summary>
        private static MotionCommand NewStopCommand(IMotionDevice device, int physicalAxis, string axisName)
            => new()
            {
                Kind = MotionCommandKind.Stop,
                PhysicalAxis = physicalAxis,
                LogicalAxis = axisName,
                StopMode = 2,
                WaitsForCompletion = false,
                Retryable = false,
                Timeout = TimeSpan.FromMilliseconds(device.Descriptor.Params.CommandTimeoutMs),
            };
    }
}
