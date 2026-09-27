using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Core.Interfaces;
using cszmcaux;

namespace Plugin.Motion.ZMotion
{
    /// <summary>
    /// 正运动（ZMotion）网口运动控制卡驱动。
    ///
    /// 覆盖关系（一张卡带多轴）：
    ///   本类 = **一张卡**；卡上的每一根轴由 <see cref="MotionDescriptor.Axes"/> 里的
    ///   <see cref="AxisMapping"/> 描述（逻辑名 ↔ 物理轴号）。流程与调试面板都按逻辑名引用轴，
    ///   所以换卡/改接线只需改映射表，不必动流程。
    ///
    /// 与板卡无关的部分（命令队列、急停插队、门禁、看门狗、故障分级）全在
    /// <see cref="MotionDeviceBase"/> 里，本类只写"这台设备怎么说话"这四件事：
    ///   <see cref="ConnectCore"/> / <see cref="DisconnectCore"/> /
    ///   <see cref="PollStatusCore"/> / <see cref="ExecuteCommandCore"/>（外加安全停止）。
    ///
    /// 三条容易写错、且写错后果很重的约定（都在下面就地注释）：
    ///   ① <c>SetUnits</c> 之后位置/速度 API **一律是用户单位（mm）**，不要再拿脉冲当量去除；
    ///   ② 回零的完成判据是 <c>GetHomeStatus == 1</c>，homemode 数字只能来自手册；
    ///   ③ 位置模式运动是"下发即返回"，等到位是上层「等待到位」步骤的事。
    /// </summary>
    [Display(
        Name = "正运动运动控制卡",
        GroupName = "运动卡",
        Description = "正运动（ZMotion）ECI/ZMC 系列网口控制器：点位运动 / 回零 / 点动 / IO / 急停",
        ShortName = "\uf085")]
    public sealed class ZMotionCard : MotionDeviceBase
    {
        /// <summary>控制器连接句柄（0 = 未连接）</summary>
        private IntPtr _handle = IntPtr.Zero;

        /// <summary>是否总线型（EtherCAT）：决定回零走 BusCmd 还是脉冲型的 Single_Datum</summary>
        private bool _isBusType = true;

        /// <summary>机型能力（轴数/IO 数），连接时确定</summary>
        private ZMotionModelInfo _model = ZMotionModels.Resolve(null, null);

        /// <summary>输入点数（轮询批量读用）</summary>
        private int _inputCount;

        /// <summary>
        /// 正运动 SDK 除系统库外的 native 依赖（由 <c>zauxdll.dll</c> 的导入表解析得出，不是猜的）。
        ///
        /// 为什么要把它写进代码：缺其中任何一个时，Windows 与 .NET 给出的是
        ///     DllNotFoundException: Unable to load DLL 'zauxdll.dll' or one of its dependencies:
        ///     找不到指定的模块。(0x8007007E)
        /// —— 它**不会**说缺的是 zmotion.dll，看上去就像"zauxdll.dll 没放对位置"。
        /// 实测确认过：只放 zauxdll.dll（不放 zmotion.dll）时，报错与"路径不对"一模一样。
        /// 有了这张清单，连接失败时就能直接点名缺哪个文件。
        /// </summary>
        private static readonly string[] SdkNativeDependencies = { "zauxdll.dll", "zmotion.dll" };

        public ZMotionCard(MotionDescriptor descriptor) : base(descriptor) { }

        /// <summary>本卡连接时识别出的机型名（界面与日志用）</summary>
        public override string ModelName => _model.Name;

        #region 连接 / 断开

        protected override bool ConnectCore()
        {
            if (string.IsNullOrWhiteSpace(Descriptor.Address))
            {
                SetDetail("未填写控制器 IP 地址");
                return false;
            }

            // 网口连接：正运动用 ZAux_OpenEth（IP 直连，端口固定）。
            // 注意它可能阻塞数百毫秒到数秒（取决于网络），基类已把 ConnectCore 放在锁外调用。
            int openRet;
            IntPtr handle;
            try
            {
                openRet = zmcaux.ZAux_OpenEth(Descriptor.Address, out handle);
            }
            catch (DllNotFoundException ex)
            {
                // P/Invoke 在第一次调用时才去加载 native 库，找不到时抛的就是它。
                // 这里必须给出"缺哪个文件"——默认消息只会说"zauxdll.dll 或其依赖找不到"
                SetDetail(BuildMissingSdkMessage(ex));
                ReportFault(new MotionFault
                {
                    Severity = MotionFaultSeverity.RequiresService,
                    Message = "运动卡 SDK 加载失败（native 库或其依赖缺失）",
                    Suggestion = "确认 zauxdll.dll 与 zmotion.dll 都已放在程序目录（VisionMaster.exe 同级），"
                                 + "且与程序位数一致（本程序为 64 位）",
                });
                return false;
            }
            catch (BadImageFormatException ex)
            {
                // 位数不符时 Windows 报的同样可能是"找不到模块"(0x7E)，必须单独点明，
                // 否则现场会一直在"文件放没放对"上打转
                SetDetail($"运动卡 SDK 位数不符：{ex.Message}。请换成 64 位版的 zauxdll.dll / zmotion.dll");
                return false;
            }

            if (openRet != 0 || handle == IntPtr.Zero)
            {
                var fault = ZMotionFaults.Interpret("连接控制器", openRet);
                SetDetail($"{fault.Message}。{fault.Suggestion}");
                ReportFault(fault);
                return false;
            }

            _handle = handle;

            // 控制器信息（机型 / 固件版本）——机型决定轴数与 IO 数
            var softType = ReadControllerInfo(out var version);
            _model = ZMotionModels.Resolve(Descriptor.CardModel, softType);
            _isBusType = _model.IsBusType;
            _inputCount = _model.InputCount;

            // 脉冲当量：SetUnits 之后，本卡所有位置/速度 API 都以"用户单位"（mm）计。
            // 这一步必须在任何运动之前做，否则后面读到的 Mpos 与下发的目标不是同一套单位。
            foreach (var axis in EnabledAxes())
            {
                var units = (float)Math.Max(0.0001, axis.UnitsPerMm);
                var unitsRet = zmcaux.ZAux_Direct_SetUnits(handle, axis.PhysicalIndex, units);
                if (unitsRet != 0)
                {
                    Log?.Warn($"[ZMotion:{Descriptor.Caption}] 轴 {axis.LogicalName} 设置脉冲当量失败"
                              + $"（码 {unitsRet}）——位置与目标将按控制器原单位解释，请核对");
                }
            }

            SetCapabilities(new MotionCapabilities
            {
                AxisCount = _model.AxisCount,
                DigitalInputCount = _model.InputCount,
                DigitalOutputCount = _model.OutputCount,
                // 如实上报"IO 点数是显示上限而非核实值"，界面据此标注（见 ZMotionModelInfo.IoCountApproximate）
                IsIoCountApproximate = _model.IoCountApproximate,
                // 限位开关接在控制器上、由控制器直接封锁运动
                SupportsHardLimit = true,
                // 保守取 false：正运动的轴是不是绝对值编码器取决于所配伺服，SDK 没有统一的查询接口。
                // 取 false 的后果是"每次连接后都要回零"——多回一次零，好过在位置不可信时按错误坐标运动。
                // 现场若确定是绝对式，应在流程里按需省略回零步骤（安全默认宁严勿松）。
                SupportsAbsoluteEncoder = true,
                SupportsLineInterpolation = false,
                SupportsArcInterpolation = false,
                SupportsJog = true,
                SupportedHomeModes = _isBusType
                    ? new[]
                    {
                        HomeMode.NegativeLimitIndex, HomeMode.PositiveLimitIndex, HomeMode.Origin,
                        HomeMode.NegativeLimitOrigin, HomeMode.PositiveLimitOrigin,
                        HomeMode.PresetZero, HomeMode.NegativeLimit, HomeMode.PositiveLimit,
                    }
                    : new[] { HomeMode.NegativeLimitIndex, HomeMode.PositiveLimitIndex, HomeMode.Origin },
            });

            // 连上不等于位置可信：本驱动声明为增量式，必须回零后才能按绝对坐标运动
            MarkHomed(false);

            SetDetail($"已连接 {_model.Name} @ {Descriptor.Address}"
                      + (string.IsNullOrWhiteSpace(version) ? string.Empty : $"（固件 {version}）")
                      + $"｜{_model.AxisCount} 轴"
                      + $"｜IO {_model.InputCount} 入 / {_model.OutputCount} 出"
                      // 界面说实话：IO 点数是显示上限而非核实值，用户才知道该去哪儿确认
                      + (_model.IoCountApproximate ? "（点数未核实，按显示上限列出）" : string.Empty));

            if (!_model.Recognized)
            {
                // 机型不认识时**必须说清**：能力表是按兜底值给的，用户可能配了 8 轴的卡
                Log?.Warn($"[ZMotion:{Descriptor.Caption}] 未识别机型「{_model.Name}」，本次按 {_model.AxisCount} 轴 "
                          + $"/ {_model.InputCount} 入 / {_model.OutputCount} 出 处理。"
                          + "请在「运动卡设置」的机型栏填写准确型号（如 ECI3428 / ECI3828 / ZMC408SCAN），"
                          + "并核对轴映射里启用的轴号");
            }

            return true;
        }

        protected override void DisconnectCore()
        {
            var handle = _handle;
            _handle = IntPtr.Zero;
            if (handle == IntPtr.Zero) return;

            try { zmcaux.ZAux_Close(handle); }
            catch (Exception ex) { Log?.Warn($"[ZMotion:{Descriptor.Caption}] 关闭控制器句柄异常（已忽略）：{ex.Message}"); }
        }

        /// <summary>
        /// 安全停止：断开、失联、命令失败时由基类调用。
        /// 用**减速停**（mode=2）而不是立即停 —— 这里是"收尾"，不是"急停"；
        /// 真正的急停走 <see cref="MotionCommandKind.Stop"/> 且 StopMode=3。
        /// </summary>
        protected override void SafeStopCore()
        {
            var handle = _handle;
            if (handle == IntPtr.Zero) return;

            foreach (var axis in EnabledAxes())
            {
                try { zmcaux.ZAux_Direct_Single_Cancel(handle, axis.PhysicalIndex, 2); }
                catch { /* 安全停止路径不再上抛 */ }
            }
        }

        #endregion

        #region 状态轮询

        protected override void PollStatusCore(CancellationToken ct)
        {
            var handle = _handle;
            if (handle == IntPtr.Zero) throw new InvalidOperationException("控制器句柄无效（可能已断开）");

            foreach (var mapping in EnabledAxes())
            {
                ct.ThrowIfCancellationRequested();

                var axis = mapping.PhysicalIndex;
                var status = new AxisStatus { PhysicalIndex = axis };

                // 轴状态位域：一次读回所有信号（报警/限位/到位/急停）
                int state = 0;
                var stateRet = zmcaux.ZAux_Direct_GetAxisStatus(handle, axis, ref state);
                if (stateRet != 0)
                {
                    // 读不到状态 = 通信有问题。抛出去让基类按"一次轮询失败"累计，
                    // 连续失败由看门狗判失联并安全停机（不要把异常吞掉变成"轴状态一直不变"）
                    throw new InvalidOperationException($"读取轴 {mapping.LogicalName} 状态失败（码 {stateRet}）");
                }

                // 位域解析走 ZMotionModels.Parse（纯函数）：一处实现、可被断言钉死每个位的含义。
                // 位写错的后果不是"少亮一个灯"，而是把报警读成到位 —— 安全停机就不会触发
                status = ZMotionStatusParser.Parse(state, axis);

                int idle = 1;
                if (zmcaux.ZAux_Direct_GetIfIdle(handle, axis, ref idle) == 0) status.Moving = idle == 0;

                // SetUnits 之后 Mpos 已经是用户单位（mm）——**不要再除脉冲当量**。
                // 除一次会让所有位置缩小到 1/UnitsPerMm，而且目标位置也会同样缩小，
                // 于是"看起来能跑"，只是行程全错——这类错误极难在现场发现。
                float mpos = 0;
                if (zmcaux.ZAux_Direct_GetMpos(handle, axis, ref mpos) == 0) status.PositionMm = mpos;

                int enabled = 0;
                if (zmcaux.ZAux_Direct_GetAxisEnable(handle, axis, ref enabled) == 0) status.Enabled = enabled != 0;

                UpdateAxisStatus(status);
            }

            ReadInputsInto(handle);
        }

        /// <summary>
        /// 批量读数字输入。用 GetInMulti 一次读完，而不是逐点问：
        /// 10ms 轮询周期下逐点问 24 个点 = 每周期 24 次往返，网口卡会被自己打满。
        /// </summary>
        private void ReadInputsInto(IntPtr handle)
        {
            var count = _inputCount;
            if (count <= 0) return;

            try
            {
                var values = new int[count];
                if (zmcaux.ZAux_Direct_GetInMulti(handle, 0, count - 1, values) != 0) return;
                for (var i = 0; i < count; i++) UpdateInput(i, values[i] > 0);
            }
            catch
            {
                // 输入读失败不判失联：轴状态才是安全相关的关键信号，
                // 输入点少刷一次不影响安全判断（真断了轴状态那边会先抛）
            }
        }

        #endregion

        #region 命令执行

        protected override MotionCommandOutcome ExecuteCommandCore(
            MotionCommand command, CancellationToken ct, out string error)
        {
            error = string.Empty;

            var handle = _handle;
            if (handle == IntPtr.Zero)
            {
                error = "控制器未连接";
                return MotionCommandOutcome.Failed;
            }

            var axisName = LogicalNameOf(command.PhysicalAxis);

            switch (command.Kind)
            {
                case MotionCommandKind.Enable:
                {
                    var ret = zmcaux.ZAux_Direct_SetAxisEnable(handle, command.PhysicalAxis, 1);
                    return AsOutcome(ret, "轴使能", axisName, out error);
                }

                case MotionCommandKind.Disable:
                {
                    // 失能即停：先撤掉当前运动再断电，避免"失能瞬间还带着速度"
                    zmcaux.ZAux_Direct_Single_Cancel(handle, command.PhysicalAxis, 2);
                    var ret = zmcaux.ZAux_Direct_SetAxisEnable(handle, command.PhysicalAxis, 0);
                    return AsOutcome(ret, "轴失能", axisName, out error);
                }

                case MotionCommandKind.MoveAbsolute:
                {
                    SetSpeedIfNeeded(handle, command);
              ApplyAccelAndCurve(handle, command);
                    // 位置模式：**下发即返回**，不等到位。
                    // 等到位是上层「等待到位」步骤的职责 —— 这样用户才能让多轴同时起步。
                    var ret = zmcaux.ZAux_Direct_Single_MoveAbs(handle, command.PhysicalAxis, (float)command.TargetMm);
                    return AsOutcome(ret, "绝对运动", axisName, out error);
                }

                case MotionCommandKind.MoveRelative:
                {
                    SetSpeedIfNeeded(handle, command);
              ApplyAccelAndCurve(handle, command);
                    var ret = zmcaux.ZAux_Direct_Single_Move(handle, command.PhysicalAxis, (float)command.TargetMm);
                    return AsOutcome(ret, "相对运动", axisName, out error);
                }

                case MotionCommandKind.Jog:
                {
                    // 速度模式点动：方向 ±1，轴按当前速度持续走，直到收到停止命令。
                    // 这是**手动对位**用的：调用方（调试面板）必须保证"松手即停"，
                    // 驱动层另有心跳看门狗兜底（见 MotionDebugViewModel）。
                    SetSpeedIfNeeded(handle, command);
              ApplyAccelAndCurve(handle, command);
                    var direction = command.JogDirection >= 0 ? 1 : -1;
                    var ret = zmcaux.ZAux_Direct_Single_Vmove(handle, command.PhysicalAxis, direction);
                    return AsOutcome(ret, "点动", axisName, out error);
                }

                case MotionCommandKind.Home:
                    return ExecuteHome(command, ct, out error);

                case MotionCommandKind.Stop:
                    return ExecuteStop(handle, command, out error);

                case MotionCommandKind.SetOutput:
                {
                    var ret = zmcaux.ZAux_Direct_SetOp(handle, command.IoPort, command.IoValue ? 1u : 0u);
                    return AsOutcome(ret, "写输出", string.Empty, out error);
                }

                case MotionCommandKind.ClearAlarm:
                {
                    // 报警是轴级的，而"清报警"这个动作不知道该清哪根轴 —— 把所有启用轴都清一遍。
                    // **不自动重新使能**：使能会给伺服上电（有机械后果），
                    // 应由操作员在调试面板上明确点一次"使能"。
                    var failed = new List<string>();
                    foreach (var axis in EnabledAxes())
                    {
                        var ret = zmcaux.ZAux_BusCmd_DriveClear(handle, (uint)axis.PhysicalIndex, 0);
                        if (ret != 0) failed.Add($"{axis.LogicalName}(码 {ret})");
                    }

                    if (failed.Count > 0)
                    {
                        error = "部分轴清除报警失败：" + string.Join("、", failed);
                        return MotionCommandOutcome.Failed;
                    }

                    return MotionCommandOutcome.Done;
                }

                default:
                    error = $"正运动驱动不支持命令 {command.Kind}";
                    return MotionCommandOutcome.NotSupported;
            }
        }

        /// <summary>
        /// 回零（阻塞到完成/超时/取消）。
        ///
        /// 两条路径：总线型（EtherCAT）走 <c>BusCmd_Datum</c> 并以 <c>GetHomeStatus == 1</c> 判完成；
        /// 脉冲型走 <c>Single_Datum</c> 并以"轴空闲"判完成（正运动脉冲卡的回零参数在控制器内设定）。
        ///
        /// 回零速度与回零偏移**沿用控制器里的既有设置**，不在这里覆盖：
        /// 现场通常已用厂商调试软件把回零高速/低速/偏移调好，驱动擅自改掉会破坏已经调好的机构行为。
        /// （要按方案配置的话，应扩展 MotionParams 并显式暴露到界面，而不是在这里写死。）
        /// </summary>
        private MotionCommandOutcome ExecuteHome(MotionCommand command, CancellationToken ct, out string error)
        {
            error = string.Empty;

            var handle = _handle;
            var axis = command.PhysicalAxis;
            var axisName = LogicalNameOf(axis);

            var mode = ZMotionHomeModes.ToSdk(command.HomeMode);
            if (mode == null)
            {
                error = $"正运动驱动不支持回零方式 {command.HomeMode}";
                return MotionCommandOutcome.NotSupported;
            }

            var timeout = command.Timeout > TimeSpan.Zero
                ? command.Timeout
                : TimeSpan.FromMilliseconds(Math.Max(1000, Params.CommandTimeoutMs));
            var deadline = DateTime.UtcNow + timeout;

            if (_isBusType)
            {
                var startRet = zmcaux.ZAux_BusCmd_Datum(handle, (uint)axis, mode.Value);
                if (startRet != 0)
                {
                    var fault = ZMotionFaults.Interpret("回零", startRet, axisName);
                    error = $"{fault.Message}。{fault.Suggestion}";
                    return MotionCommandOutcome.Failed;
                }

                while (true)
                {
                    if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);

                    if (DateTime.UtcNow > deadline)
                    {
                        // 超时后**先停轴**：不能把一个还在寻零的轴丢在那儿自己去执行下一条命令
                        zmcaux.ZAux_Direct_Single_Cancel(handle, axis, 2);
                        error = $"回零超时（>{timeout.TotalSeconds:F0}s）：请检查原点/限位开关、回零方式与回零速度";
                        return MotionCommandOutcome.TimedOut;
                    }

                    uint homeState = 0;
                    zmcaux.ZAux_BusCmd_GetHomeStatus(handle, (uint)axis, ref homeState);

                    // 1 = 回零完成（厂家约定）。其余值（含"正在回零"与"失败"）继续等，
                    // 直到超时 —— 这里刻意不猜每个状态码的含义，只认能确定的那一个。
                    if (homeState == 1) return MotionCommandOutcome.Done;

                    ct.WaitHandle.WaitOne(20);
                }
            }

            // 脉冲型：Datum 启动 + 等轴空闲
            var pulseRet = zmcaux.ZAux_Direct_Single_Datum(handle, axis, (int)mode.Value);
            if (pulseRet != 0)
            {
                var fault = ZMotionFaults.Interpret("回零", pulseRet, axisName);
                error = $"{fault.Message}。{fault.Suggestion}";
                return MotionCommandOutcome.Failed;
            }

            while (true)
            {
                if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);

                if (DateTime.UtcNow > deadline)
                {
                    zmcaux.ZAux_Direct_Single_Cancel(handle, axis, 2);
                    error = $"回零超时（>{timeout.TotalSeconds:F0}s）";
                    return MotionCommandOutcome.TimedOut;
                }

                int idle = 0;
                if (zmcaux.ZAux_Direct_GetIfIdle(handle, axis, ref idle) == 0 && idle != 0)
                    return MotionCommandOutcome.Done;

                ct.WaitHandle.WaitOne(20);
            }
        }

        /// <summary>
        /// 停止。PhysicalAxis &lt; 0 表示全部轴（契约约定，急停走这条）。
        /// StopMode：2 = 减速停，3 = 立即停（与正运动 SDK 的约定一致）。
        /// </summary>
        private MotionCommandOutcome ExecuteStop(IntPtr handle, MotionCommand command, out string error)
        {
            error = string.Empty;
            var mode = command.StopMode <= 0 ? 2 : command.StopMode;

            if (command.PhysicalAxis < 0)
            {
                // 正运动没有"停所有轴"的单一调用，只能逐轴取消。
                // 急停场景下这仍然是"逐条下发"，但每条都是立即停指令，且不排队（基类走紧急通道）
                var failed = CollectStopFailures(handle, mode);
                if (failed.Count > 0)
                {
                    error = "部分轴停止命令下发失败：" + string.Join("、", failed);
                    return MotionCommandOutcome.Failed;
                }
                return MotionCommandOutcome.Done;
            }

            var ret = zmcaux.ZAux_Direct_Single_Cancel(handle, command.PhysicalAxis, mode);
            return AsOutcome(ret, "轴停止", LogicalNameOf(command.PhysicalAxis), out error);
        }

        private List<string> CollectStopFailures(IntPtr handle, int mode)
        {
            var failed = new List<string>();
            foreach (var axis in EnabledAxes())
            {
                var ret = zmcaux.ZAux_Direct_Single_Cancel(handle, axis.PhysicalIndex, mode);
                if (ret != 0) failed.Add($"{axis.LogicalName}(码 {ret})");
            }
            return failed;
        }

        /// <summary>命令给了速度就设速度；没给（&lt;=0）就沿用控制器内的既有速度</summary>
        private void SetSpeedIfNeeded(IntPtr handle, MotionCommand command)
        {
            if (command.VelocityMmPerS <= 0) return;
            var ret = zmcaux.ZAux_Direct_SetSpeed(handle, command.PhysicalAxis, (float)command.VelocityMmPerS);
            if (ret != 0)
            {
                Log?.Warn($"[ZMotion:{Descriptor.Caption}] 轴 {LogicalNameOf(command.PhysicalAxis)} "
                          + $"设置速度失败（码 {ret}），本次将沿用控制器内的既有速度");
            }
        }

        /// <summary>
        /// 应用加减速与曲线类型（定位类命令）。
        ///
        /// 正运动卡的曲线由 SetSramp（S 平滑时间，ms）决定：
        ///   梯形   → Sramp=0：按 Accel/Decel 直角加减（默认形态）；
        ///   S 曲线 → Sramp&gt;0：两端圆滑、中段保持速度（平滑时长取 200ms 固定值 ——
        ///            点位表只让用户选"形状"不调平滑时长，参数面越小现场越不容易配坏）。
        /// Accel/Decel ≤0 时不覆盖：现场在卡内调好的加减速度不该被软件擅自改写。
        /// 失败只告警不中断 —— 曲线设置失败时运动仍按控制器内既有参数执行，
        /// 与 SetSpeedIfNeeded 的容错策略一致（宁可慢一点也别把一次定位变成故障停机）。
        /// </summary>
        private void ApplyAccelAndCurve(IntPtr handle, MotionCommand command)
        {
            var axisName = LogicalNameOf(command.PhysicalAxis);

            if (command.AccelMmPerS2 > 0)
            {
                var accelRet = zmcaux.ZAux_Direct_SetAccel(handle, command.PhysicalAxis, (float)command.AccelMmPerS2);
                var decelRet = zmcaux.ZAux_Direct_SetDecel(handle, command.PhysicalAxis, (float)command.AccelMmPerS2);
                if (accelRet != 0 || decelRet != 0)
                {
                    Log?.Warn($"[ZMotion:{Descriptor.Caption}] 轴 {axisName} "
                              + $"设置加减速失败（码 {accelRet}/{decelRet}），沿用控制器内既有参数");
                }
            }

            // S 平滑：梯形=0（关），S 曲线=200ms（开）。每次定位前都设，避免上一条命令的设置残留。
            var sramp = command.Curve == MotionCurve.SCurve ? 200f : 0f;
            var srampRet = zmcaux.ZAux_Direct_SetSramp(handle, command.PhysicalAxis, sramp);
            if (srampRet != 0)
            {
                Log?.Warn($"[ZMotion:{Descriptor.Caption}] 轴 {axisName} "
                          + $"设置 S 平滑失败（码 {srampRet}），沿用控制器内既有曲线");
            }
        }

        /// <summary>把 SDK 返回码转成命令结果；非 0 时把"码 + 上下文 + 建议"写进 error</summary>
        private MotionCommandOutcome AsOutcome(int code, string operation, string axisName, out string error)
        {
            if (code == 0)
            {
                error = string.Empty;
                return MotionCommandOutcome.Done;
            }

            var fault = ZMotionFaults.Interpret(operation, code, axisName);
            error = $"{fault.Message}。{fault.Suggestion}";
            return MotionCommandOutcome.Failed;
        }

        #endregion

        #region 辅助

        /// <summary>已启用的轴映射（所有对轴的遍历都走它，避免"操作到用户明确关掉的轴"）</summary>
        private IEnumerable<AxisMapping> EnabledAxes()
            => Descriptor.Axes.Where(a => a.Enabled);

        private string LogicalNameOf(int physicalIndex)
            => Descriptor.FindAxis(physicalIndex)?.LogicalName ?? $"轴{physicalIndex}";

        /// <summary>
        /// 组装"SDK 不完整"的说明：逐个检查 native 文件在不在，直接点名缺哪些。
        ///
        /// 这条诊断的价值是把一次真实故障变成可自解释的信息：
        /// 否则现场看到的只有"找不到指定的模块"，而从那条消息到"缺 zmotion.dll"之间
        /// 隔着一次 PE 导入表解析（本轮就是这么查出来的）。
        /// </summary>
        private static string BuildMissingSdkMessage(DllNotFoundException ex)
        {
            var missing = SdkNativeDependencies.Where(f => !NativeFileExists(f)).ToList();

            if (missing.Count == 0)
            {
                return $"加载运动卡 SDK 失败：{ex.Message}。"
                       + "文件都在，请确认 SDK 与程序位数一致（本程序为 64 位），"
                       + "以及是否被杀毒软件 / 权限拦截";
            }

            return $"运动卡 SDK 不完整：缺少 {string.Join("、", missing)}。"
                   + "正运动 SDK 最少需要 zauxdll.dll（辅助层）与 zmotion.dll（核心层）两个文件，"
                   + "请把它们一起放到程序目录（VisionMaster.exe 同级）后重新连接";
        }

        /// <summary>native 库是否可见（查 DllImport 实际会搜的目录）</summary>
        private static bool NativeFileExists(string fileName)
        {
            foreach (var dir in NativeSearchDirectories())
            {
                try
                {
                    if (File.Exists(Path.Combine(dir, fileName))) return true;
                }
                catch
                {
                    // 目录不可读：当作没有
                }
            }

            return false;
        }

        private static IEnumerable<string> NativeSearchDirectories()
        {
            // exe 旁：DllImport 的默认搜索起点
            yield return AppContext.BaseDirectory;

            // 插件所在目录（Modules\）：DllImport 默认**不**搜这里，
            // 列出来只是为了让诊断更准确（"文件其实在 Modules\ 但加载不到"是另一种情形）
            var assemblyDir = Path.GetDirectoryName(typeof(ZMotionCard).Assembly.Location);
            if (!string.IsNullOrEmpty(assemblyDir)) yield return assemblyDir!;
        }

        /// <summary>读控制器信息（机型 / 固件版本）。失败不致命：能力会退到兜底值并提示核对</summary>
        private string ReadControllerInfo(out string version)
        {
            version = string.Empty;
            try
            {
                var softType = new StringBuilder(64);
                var softVersion = new StringBuilder(64);
                var controllerId = new StringBuilder(64);

                if (zmcaux.ZAux_GetControllerInfo(_handle, softType, softVersion, controllerId) == 0)
                {
                    version = softVersion.ToString();
                    return softType.ToString();
                }
            }
            catch (Exception ex)
            {
                Log?.Warn($"[ZMotion:{Descriptor.Caption}] 读取控制器信息失败（已忽略，将按配置/兜底机型处理）：{ex.Message}");
            }

            return string.Empty;
        }

        #endregion
    }
}
