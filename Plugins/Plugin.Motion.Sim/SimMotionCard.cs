using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using Core.Interfaces;

namespace Plugin.Motion.Sim
{
    /// <summary>
    /// 产线仿真运动卡：把上位机的运动命令转成 <c>LineSim</c> 仿真器的运控协议（TCP JSON-lines，默认 19510）。
    /// <c>LineSim</c> 是**独立产品、独立仓库**（默认 <c>D:\C#\LineSim</c>），不在本仓；本驱动与它之间只有网络协议，零程序集引用。
    ///
    /// 它与「虚拟运动卡（仿真）」的分工是刻意分开的，不要合并：
    ///   · <c>VirtualMotionCard</c> 是<b>进程内</b>的假卡：世界只在卡自己肚子里，只够验命令队列/门禁/看门狗。
    ///   · 本驱动是<b>跨进程</b>的真客户端：世界在 LineSim 里，与视觉、IO、通讯共享同一个时钟与同一批工件。
    ///     相机拍到的位置、PLC 读到的到位信号、轴自己报的位置，必须是同一份真相 —— 这正是产线闭环要验的东西。
    ///
    /// 三条不可让步的语义（写错任何一条，"仿真通过"就毫无意义）：
    ///   1. <b>下发 ≠ 到位</b>：<c>enqueue</c> 只回答"收下了吗"；轴什么时候到位由仿真器的 tick 决定，
    ///      上位机只能靠 poll 看。这里绝不把 accepted 当成 Done（设计 §6.1）。
    ///   2. <b>位置只来自 poll</b>：驱动不自己维护位置，也不做插值 —— 一旦本地补算，
    ///      上位机看到的轨迹就与相机拍到的、PLC 读到的不是同一条。
    ///   3. <b>轴数与 IO 数由仿真器 hello 决定</b>：不写死 3 轴 8 点，P2 扩到 44 轴 / 192 IO 时驱动一行不改。
    ///
    /// 它<b>不实现</b>门禁（未使能/未回零/正在运动/软限位）—— 那是 <see cref="MotionDeviceBase"/> 的职责，
    /// 驱动里再写一遍就会出现两套判据、两套文案（设计 §6.1 A 表）。仿真器侧也判一遍是"独立进程也要自保"，
    /// 两边判据口径一致，故仿真器拒绝时这里如实转成 Failed/NotSupported，不掩盖。
    /// </summary>
    [Display(
        Name = "产线仿真运动卡",
        GroupName = "运动卡",
        Description = "连接 LineSim 产线仿真器的运控通道（TCP 19510）：轴数/IO 数由仿真器决定，世界与视觉、IO 共享同一时钟",
        ShortName = "\uf085")]
    public sealed class SimMotionCard : MotionDeviceBase
    {
        /// <summary>仿真器运控通道默认端口（与设计 §8.1 端口规划、LineSim 的 <c>--motion-port</c> 默认值一致）</summary>
        public const int DefaultPort = 19510;

        /// <summary>运控协议版本。与 LineSim <c>MotionServer.ProtoVersion</c> 必须一致，否则握手被拒</summary>
        public const int ProtoVersion = 1;

        /// <summary>
        /// 一条连接上的请求/应答互斥。
        /// 轮询线程与命令线程都会往同一个 socket 写 —— 不加锁就会把两条命令的报文交错，
        /// 表现为"偶发的 bad_json / 应答错位"，且只在忙的时候出现。
        /// </summary>
        private readonly object _io = new();

        private TcpClient? _client;
        private StreamReader? _reader;
        private StreamWriter? _writer;

        private int _simAxes;
        private int _simDin;
        private int _simDout;
        private long _seq;

        /// <summary>最近一次 poll 看到的仿真器状态（"Online" / "SafeStopped"），只用于状态说明</summary>
        private string _simState = "Online";

        public SimMotionCard(MotionDescriptor descriptor) : base(descriptor) { }

        /// <inheritdoc />
        public override string ModelName => "LineSim 产线仿真器";

        #region 连接与断开

        protected override bool ConnectCore()
        {
            (string host, int port) = ParseAddress(Descriptor.Address);

            try
            {
                var client = new TcpClient { NoDelay = true };
                if (!client.ConnectAsync(host, port).Wait(3000))
                {
                    client.Dispose();
                    SetDetail($"连接仿真器超时（{host}:{port}）。请先启动 LineSim --serve，再确认端口未被占用");
                    return false;
                }

                NetworkStream stream = client.GetStream();
                stream.ReadTimeout = 3000;
                stream.WriteTimeout = 3000;

                var reader = new StreamReader(stream, new UTF8Encoding(false), false, 8192, leaveOpen: true);
                var writer = new StreamWriter(stream, new UTF8Encoding(false), 8192, leaveOpen: true)
                {
                    AutoFlush = true,
                    NewLine = "\n",
                };

                lock (_io)
                {
                    _client = client;
                    _reader = reader;
                    _writer = writer;
                }

                string? hello = Request(
                    JsonSerializer.Serialize(new { op = "hello", proto = ProtoVersion, client = "Plugin.Motion.Sim" }),
                    3000);

                if (hello is null)
                {
                    CloseQuietly();
                    SetDetail($"仿真器没有回应握手（{host}:{port}）");
                    return false;
                }

                using JsonDocument doc = JsonDocument.Parse(hello);
                JsonElement root = doc.RootElement;

                if (!IsOk(root))
                {
                    string err = GetString(root, "err");
                    CloseQuietly();
                    SetDetail(err == "proto_mismatch"
                        ? $"仿真器协议版本不符（仿真器 {GetInt(root, "simProto")} / 本驱动 {ProtoVersion}）"
                        : $"仿真器拒绝握手：{(string.IsNullOrEmpty(err) ? hello : err)}");
                    return false;
                }

                _simAxes = GetInt(root, "axes");
                _simDin = GetInt(root, "din");
                _simDout = GetInt(root, "dout");

                bool absolute = true;
                if (root.TryGetProperty("caps", out JsonElement caps))
                {
                    absolute = GetBool(caps, "supportsAbsoluteEncoder", true);
                }

                SetCapabilities(new MotionCapabilities
                {
                    AxisCount = _simAxes,
                    DigitalInputCount = _simDin,
                    DigitalOutputCount = _simDout,
                    SupportsHardLimit = true,
                    SupportsAbsoluteEncoder = absolute,
                    SupportsLineInterpolation = false,
                    SupportsArcInterpolation = false,
                    SupportsJog = true,
                    // 仿真器实现了全部八种回零方式（Axis.StartHome），故照实上报 ——
                    // 报少了会让上位机在「轴回零」步骤里直接拒绝用户选的方式（基类会按能力位协商）。
                    SupportedHomeModes = new[]
                    {
                        HomeMode.NegativeLimitIndex,
                        HomeMode.PositiveLimitIndex,
                        HomeMode.Origin,
                        HomeMode.NegativeLimitOrigin,
                        HomeMode.PositiveLimitOrigin,
                        HomeMode.PresetZero,
                        HomeMode.NegativeLimit,
                        HomeMode.PositiveLimit,
                    },
                });

                // 仿真器自报增量式编码器（supportsAbsoluteEncoder=false）：连上后位置不可信，必须先回零。
                // 这条与真机一致，也是「轴回零」步骤存在的理由；不要为了让流程好跑就改成 true。
                MarkHomed(absolute);
                ClearSnapshots();

                SetDetail($"已连接产线仿真器（{host}:{port}，{_simAxes} 轴 / {_simDin} 进 / {_simDout} 出"
                          + (absolute ? "，绝对编码器" : "，增量编码器，需先回零") + "）");
                return true;
            }
            catch (Exception ex)
            {
                CloseQuietly();
                SetDetail($"连接仿真器失败（{host}:{port}）：{ex.Message}");
                return false;
            }
        }

        protected override void DisconnectCore() => CloseQuietly();

        #endregion

        #region 状态轮询

        protected override void PollStatusCore(CancellationToken ct)
        {
            string? line = Request("{\"op\":\"poll\"}", 2000);
            if (line is null)
            {
                // 抛异常 = "一次轮询失败"：基类据此累计失败次数并走看门狗 → 安全停机。
                // 不要在这里自己改状态机（那会绕过看门狗与故障分级）。
                throw new IOException("仿真器无响应（poll 超时或连接已断）");
            }

            using JsonDocument doc = JsonDocument.Parse(line);
            JsonElement root = doc.RootElement;

            if (!IsOk(root))
            {
                throw new IOException($"仿真器拒绝 poll：{GetString(root, "err")}");
            }

            if (root.TryGetProperty("axes", out JsonElement axes) && axes.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement a in axes.EnumerateArray())
                {
                    ct.ThrowIfCancellationRequested();

                    UpdateAxisStatus(new AxisStatus
                    {
                        PhysicalIndex = GetInt(a, "PhysicalIndex"),
                        PositionMm = GetDouble(a, "PositionMm"),
                        VelocityMmPerS = GetDouble(a, "VelocityMmPerS"),
                        InPosition = GetBool(a, "InPosition"),
                        Moving = GetBool(a, "Moving"),
                        Enabled = GetBool(a, "Enabled"),
                        PositiveLimit = GetBool(a, "PositiveLimit"),
                        NegativeLimit = GetBool(a, "NegativeLimit"),
                        Origin = GetBool(a, "Origin"),
                        // 设计 §6.1：这个位只由上位机判（急停是上位机的动作），仿真器恒给 false，
                        // 这里如实转写 —— 点亮它会让"人工急停"被记成设备故障。
                        EmergencyStop = GetBool(a, "EmergencyStop"),
                        Alarm = GetBool(a, "Alarm"),
                        AlarmMessage = GetString(a, "AlarmMessage"),
                    });
                }
            }

            if (root.TryGetProperty("din", out JsonElement din) && din.ValueKind == JsonValueKind.String)
            {
                UpdateInputsFromHex(din.GetString(), _simDin);
            }

            _simState = GetString(root, "state");

            // 说明文字只在变化时写：SetDetail 会进状态机通知，10ms 一拍的重复写入是纯噪音。
            string detail = _simState == "SafeStopped"
                ? "仿真器处于安全停机（仿真侧触发了急停，需复位后继续）"
                : $"已连接产线仿真器（tick {GetLong(root, "tick")}，{_simAxes} 轴，工件 {GetInt(root, "wp")} 件）";

            if (!string.Equals(StateDetail, detail, StringComparison.Ordinal))
            {
                SetDetail(detail);
            }
        }

        #endregion

        #region 命令执行

        protected override MotionCommandOutcome ExecuteCommandCore(
            MotionCommand command, CancellationToken ct, out string error)
        {
            error = string.Empty;

            string? kind = command.Kind switch
            {
                MotionCommandKind.Enable => "Enable",
                MotionCommandKind.Disable => "Disable",
                MotionCommandKind.MoveAbsolute => "MoveAbsolute",
                MotionCommandKind.MoveRelative => "MoveRelative",
                MotionCommandKind.Home => "Home",
                MotionCommandKind.Stop => "Stop",
                MotionCommandKind.SetOutput => "SetOutput",
                MotionCommandKind.ClearAlarm => "ClearAlarm",
                MotionCommandKind.Jog => "Jog",
                _ => null,
            };

            if (kind is null)
            {
                error = $"产线仿真运动卡不支持命令：{command.Kind}";
                return MotionCommandOutcome.NotSupported;
            }

            string id = Interlocked.Increment(ref _seq).ToString(CultureInfo.InvariantCulture);

            string payload = JsonSerializer.Serialize(new
            {
                op = "enqueue",
                id,
                kind,
                axis = command.PhysicalAxis,
                pos = command.TargetMm,
                speed = command.VelocityMmPerS,
                acc = command.AccelMmPerS2,
                port = command.IoPort,
                value = command.IoValue,
                dir = command.JogDirection,
                stopMode = (int)command.StopMode,
                homeMode = command.HomeMode.ToString(),
            });

            string? line = Request(payload, 3000);
            if (line is null)
            {
                error = "仿真器无响应（enqueue 超时或连接已断）";
                return MotionCommandOutcome.Failed;
            }

            MotionCommandOutcome verdict;
            using (JsonDocument doc = JsonDocument.Parse(line))
            {
                JsonElement root = doc.RootElement;

                if (!IsOk(root))
                {
                    error = $"仿真器拒绝命令：{GetString(root, "err")}";
                    return MotionCommandOutcome.Failed;
                }

                if (!GetBool(root, "accepted", false))
                {
                    string reason = GetString(root, "reason");
                    string result = GetString(root, "result");

                    error = string.IsNullOrWhiteSpace(reason) ? $"仿真器拒绝命令（{result}）" : reason;

                    // 仿真器说"不支持"要如实转成 NotSupported：上位机据此给出"换个回零方式/换张卡"的建议，
                    // 而不是当成通信失败去重连。
                    // 线上码是中立词（unsupported），翻译成本工程的 MotionCommandOutcome 是**适配器的职责**：
                    // 仿真器是独立产品，它不认识我们的枚举名。
                    return string.Equals(result, "unsupported", StringComparison.Ordinal)
                        ? MotionCommandOutcome.NotSupported
                        : MotionCommandOutcome.Failed;
                }
            }

            verdict = MotionCommandOutcome.Done;

            // 下发即完成（Move/Jog/Enable/SetOutput/Stop）：立刻返回，等到位由流程里的等待步骤负责。
            if (!command.WaitsForCompletion)
            {
                return verdict;
            }

            // 回零 / 清报警这类"必须等到做完"的命令：这里等仿真世界真的做完。
            // 取消必须抛 OperationCanceledException（契约），返回 Failed 会把"人工急停"记成设备故障。
            MotionCommandOutcome waited = WaitCompletion(command, ct, ref error);
            if (waited == MotionCommandOutcome.Done && command.Kind == MotionCommandKind.Home)
            {
                MarkHomed(true);
            }

            return waited;
        }

        /// <summary>轮询到该轴"不再运动且到位"，或超时。只用于 <see cref="MotionCommand.WaitsForCompletion"/> 的命令。</summary>
        private MotionCommandOutcome WaitCompletion(MotionCommand command, CancellationToken ct, ref string error)
        {
            long timeoutMs = command.Timeout > TimeSpan.Zero
                ? (long)command.Timeout.TotalMilliseconds
                : Descriptor.Params.CommandTimeoutMs;

            long deadline = Environment.TickCount64 + Math.Max(1, timeoutMs);
            int axisIndex = command.PhysicalAxis;

            while (true)
            {
                if (ct.IsCancellationRequested)
                {
                    throw new OperationCanceledException(ct);
                }

                if (Environment.TickCount64 > deadline)
                {
                    error = $"{command.Kind} 超时（>{timeoutMs}ms，仿真器仍未报到位）";
                    return MotionCommandOutcome.TimedOut;
                }

                string? line = Request("{\"op\":\"poll\"}", 2000);
                if (line is null)
                {
                    error = "等待完成时仿真器无响应";
                    return MotionCommandOutcome.Failed;
                }

                bool done = false;
                using (JsonDocument doc = JsonDocument.Parse(line))
                {
                    JsonElement root = doc.RootElement;
                    if (root.TryGetProperty("axes", out JsonElement axes) && axes.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement a in axes.EnumerateArray())
                        {
                            if (GetInt(a, "PhysicalIndex") != axisIndex)
                            {
                                continue;
                            }

                            done = !GetBool(a, "Moving") && GetBool(a, "InPosition");
                            break;
                        }
                    }
                }

                if (done)
                {
                    return MotionCommandOutcome.Done;
                }

                ct.WaitHandle.WaitOne(5);
            }
        }

        #endregion

        #region 安全停机与故障分级

        protected override void SafeStopCore()
        {
            // 断开/失联/急停收尾时调用：让仿真世界里的轴立刻停住。
            // 用 cancel（减速停全部轴）而不是 estop —— estop 会置仿真世界的安全停机态，
            // 下次连接必须先复位，等于把"通信抖一下"升级成"要人工复位"。
            // 失败一律吞掉：这里已经在异常路径上，再抛只会掩盖原始故障。
            try
            {
                _ = Request("{\"op\":\"cancel\"}", 500);
            }
            catch (Exception)
            {
                // 安全停机的收尾路径，忽略
            }
        }

        protected override MotionFault ClassifyOutcome(
            MotionCommandOutcome outcome, MotionCommand command, string error)
        {
            // "未使能 / 未回零"是流程编排问题（漏了「轴使能」/「轴回零」步骤），不是设备故障：
            // 记成需维修会让现场去查硬件，实际改流程即可。
            if (error.Contains("未使能") || error.Contains("未回零"))
            {
                return new MotionFault
                {
                    Severity = MotionFaultSeverity.Recoverable,
                    Message = error,
                    Suggestion = "在流程里把「轴使能」/「轴回零」步骤放在运动步骤之前",
                    Axis = command.LogicalAxis,
                    CommandId = command.CommandId,
                };
            }

            return base.ClassifyOutcome(outcome, command, error);
        }

        #endregion

        #region 协议收发

        /// <summary>发一行 JSON、读一行应答。返回 null 表示"这条连接已经不能用了"（调用方据此走失败路径）。</summary>
        private string? Request(string json, int timeoutMs)
        {
            lock (_io)
            {
                if (_client is null || _reader is null || _writer is null)
                {
                    return null;
                }

                try
                {
                    _client.ReceiveTimeout = timeoutMs;
                    _writer.WriteLine(json);
                    return _reader.ReadLine();
                }
                catch (IOException)
                {
                    CloseLocked();
                    return null;
                }
                catch (SocketException)
                {
                    CloseLocked();
                    return null;
                }
                catch (ObjectDisposedException)
                {
                    return null;
                }
            }
        }

        private void CloseQuietly()
        {
            lock (_io)
            {
                CloseLocked();
            }
        }

        /// <summary>关闭连接。调用方必须已持有 <see cref="_io"/>（Monitor 可重入，故 CloseQuietly 里再进一次也安全）</summary>
        private void CloseLocked()
        {
            try
            {
                _writer?.Dispose();
            }
            catch (Exception)
            {
                // 关闭期忽略
            }

            try
            {
                _reader?.Dispose();
            }
            catch (Exception)
            {
                // 关闭期忽略
            }

            try
            {
                _client?.Dispose();
            }
            catch (Exception)
            {
                // 关闭期忽略
            }

            _writer = null;
            _reader = null;
            _client = null;
        }

        /// <summary>把 "127.0.0.1:19510" 或 "127.0.0.1" 解析成端点。省略端口时用 <see cref="DefaultPort"/></summary>
        private static (string Host, int Port) ParseAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                return ("127.0.0.1", DefaultPort);
            }

            string text = address.Trim();
            int colon = text.LastIndexOf(':');

            if (colon > 0 && colon < text.Length - 1
                && int.TryParse(text.AsSpan(colon + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int port)
                && port > 0 && port <= 65535)
            {
                return (text[..colon], port);
            }

            return (text, DefaultPort);
        }

        /// <summary>十六进制串（每 4 位一个字符、低位在前）→ 逐个输入点。</summary>
        private void UpdateInputsFromHex(string? hex, int count)
        {
            if (string.IsNullOrEmpty(hex))
            {
                return;
            }

            for (int d = 0; d < hex!.Length; d++)
            {
                int nibble = HexValue(hex[d]);
                if (nibble < 0)
                {
                    continue;
                }

                for (int b = 0; b < 4; b++)
                {
                    int port = (d * 4) + b;
                    if (port >= count)
                    {
                        return;
                    }

                    UpdateInput(port, (nibble & (1 << b)) != 0);
                }
            }
        }

        private static int HexValue(char c) => c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'a' and <= 'f' => c - 'a' + 10,
            >= 'A' and <= 'F' => c - 'A' + 10,
            _ => -1,
        };

        private static bool IsOk(JsonElement root)
            => root.TryGetProperty("ok", out JsonElement ok) && ok.ValueKind == JsonValueKind.True;

        private static string GetString(JsonElement root, string name)
            => root.TryGetProperty(name, out JsonElement el) && el.ValueKind == JsonValueKind.String
                ? el.GetString() ?? string.Empty
                : string.Empty;

        private static int GetInt(JsonElement root, string name)
            => root.TryGetProperty(name, out JsonElement el) && el.ValueKind == JsonValueKind.Number
                && el.TryGetInt32(out int value)
                    ? value
                    : 0;

        private static long GetLong(JsonElement root, string name)
            => root.TryGetProperty(name, out JsonElement el) && el.ValueKind == JsonValueKind.Number
                && el.TryGetInt64(out long value)
                    ? value
                    : 0;

        private static double GetDouble(JsonElement root, string name)
            => root.TryGetProperty(name, out JsonElement el) && el.ValueKind == JsonValueKind.Number
                && el.TryGetDouble(out double value)
                    ? value
                    : 0.0;

        private static bool GetBool(JsonElement root, string name, bool fallback = false)
        {
            if (!root.TryGetProperty(name, out JsonElement el))
            {
                return fallback;
            }

            return el.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => fallback,
            };
        }

        #endregion
    }
}
