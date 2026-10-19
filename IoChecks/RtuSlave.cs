using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace IoChecks
{
    /// <summary>
    /// 最小 Modbus RTU 从站（HIL 联调用）。
    ///
    /// <para><b>为什么自己写</b>：HSL 只带 <c>ModbusTcpServer</c>，没有 RTU 从站；
    /// 而 RTU 的联调又必须"两端都是真实串口"。所以这里补一个够用的从站，
    /// 支持 01/02/03/04/05/06/0F/10 八个功能码。</para>
    ///
    /// <para><b>分层</b>：帧处理 <see cref="Handle"/> 是<b>纯函数</b>（输入请求字节、输出响应字节），
    /// 不依赖串口，因此可以无条件做单元测试；<see cref="Start"/> 只是把串口读到的字节喂给它。
    /// 这样"没装 com0com 时整块逻辑无法验证"的问题就被拆掉了——编解码是真测过的，
    /// 未验证的只剩几十行串口收发。</para>
    ///
    /// <para><b>帧约定</b>：CRC16/MODBUS（poly 0xA001，初值 0xFFFF），低字节在前；
    /// 站号不匹配不应答；CRC 错不应答；广播（站号 0）本实现不应答。</para>
    /// </summary>
    internal sealed class RtuSlave : IDisposable
    {
        public const byte ExceptionIllegalFunction = 0x01;
        public const byte ExceptionIllegalAddress = 0x02;
        public const byte ExceptionIllegalValue = 0x03;

        public RtuSlave(byte unitId) => UnitId = unitId;

        public byte UnitId { get; }

        public bool[] Coils { get; } = new bool[65536];
        public bool[] DiscreteInputs { get; } = new bool[65536];
        public ushort[] HoldingRegisters { get; } = new ushort[65536];
        public ushort[] InputRegisters { get; } = new ushort[65536];

        /// <summary>收到的合法请求数（CRC 正确且站号匹配）。</summary>
        public int RequestCount;
        /// <summary>CRC 校验失败的帧数。</summary>
        public int CrcErrorCount;

        private SerialPort? _port;
        private Thread? _thread;
        private volatile bool _running;

        #region 帧处理（纯函数，可直接单测）

        /// <summary>
        /// 处理一帧请求。返回应答字节；返回 null 表示"不应答"（CRC 错 / 站号不符 / 广播）。
        /// </summary>
        public byte[]? Handle(byte[] frame)
        {
            if (frame == null || frame.Length < 4) return null;

            ushort crc = Crc16(frame, 0, frame.Length - 2);
            if ((byte)(crc & 0xFF) != frame[^2] || (byte)(crc >> 8) != frame[^1])
            {
                Interlocked.Increment(ref CrcErrorCount);
                return null;
            }

            byte unit = frame[0];
            byte function = frame[1];

            // 站号不符不应答；广播（0）本模拟器不处理。
            if (unit != UnitId) return null;

            Interlocked.Increment(ref RequestCount);

            switch (function)
            {
                case 0x01: return ReadBits(frame, Coils);
                case 0x02: return ReadBits(frame, DiscreteInputs);
                case 0x03: return ReadRegisters(frame, HoldingRegisters);
                case 0x04: return ReadRegisters(frame, InputRegisters);
                case 0x05: return WriteSingleCoil(frame);
                case 0x06: return WriteSingleRegister(frame);
                case 0x0F: return WriteMultipleCoils(frame);
                case 0x10: return WriteMultipleRegisters(frame);
                default: return ExceptionResponse(unit, function, ExceptionIllegalFunction);
            }
        }

        private byte[]? ReadBits(byte[] frame, bool[] store)
        {
            if (frame.Length < 6) return null;
            ushort address = ReadUInt16(frame, 2);
            ushort quantity = ReadUInt16(frame, 4);

            if (quantity < 1 || quantity > 2000)
                return ExceptionResponse(frame[0], frame[1], ExceptionIllegalValue);
            if (address + quantity > store.Length)
                return ExceptionResponse(frame[0], frame[1], ExceptionIllegalAddress);

            int byteCount = (quantity + 7) / 8;
            var body = new byte[1 + byteCount];
            body[0] = (byte)byteCount;
            for (int i = 0; i < quantity; i++)
            {
                if (store[address + i])
                    body[1 + i / 8] |= (byte)(1 << (i % 8));
            }
            return Build(frame[0], frame[1], body);
        }

        private byte[]? ReadRegisters(byte[] frame, ushort[] store)
        {
            if (frame.Length < 6) return null;
            ushort address = ReadUInt16(frame, 2);
            ushort quantity = ReadUInt16(frame, 4);

            if (quantity < 1 || quantity > 125)
                return ExceptionResponse(frame[0], frame[1], ExceptionIllegalValue);
            if (address + quantity > store.Length)
                return ExceptionResponse(frame[0], frame[1], ExceptionIllegalAddress);

            var body = new byte[1 + quantity * 2];
            body[0] = (byte)(quantity * 2);
            for (int i = 0; i < quantity; i++)
            {
                body[1 + i * 2] = (byte)(store[address + i] >> 8);
                body[2 + i * 2] = (byte)(store[address + i] & 0xFF);
            }
            return Build(frame[0], frame[1], body);
        }

        private byte[]? WriteSingleCoil(byte[] frame)
        {
            if (frame.Length < 6) return null;
            ushort address = ReadUInt16(frame, 2);
            ushort value = ReadUInt16(frame, 4);

            if (value != 0xFF00 && value != 0x0000)
                return ExceptionResponse(frame[0], frame[1], ExceptionIllegalValue);
            if (address >= Coils.Length)
                return ExceptionResponse(frame[0], frame[1], ExceptionIllegalAddress);

            Coils[address] = value == 0xFF00;
            return Build(frame[0], frame[1], new[] { frame[2], frame[3], frame[4], frame[5] });
        }

        private byte[]? WriteSingleRegister(byte[] frame)
        {
            if (frame.Length < 6) return null;
            ushort address = ReadUInt16(frame, 2);
            ushort value = ReadUInt16(frame, 4);

            if (address >= HoldingRegisters.Length)
                return ExceptionResponse(frame[0], frame[1], ExceptionIllegalAddress);

            HoldingRegisters[address] = value;
            return Build(frame[0], frame[1], new[] { frame[2], frame[3], frame[4], frame[5] });
        }

        private byte[]? WriteMultipleCoils(byte[] frame)
        {
            if (frame.Length < 7) return null;
            ushort address = ReadUInt16(frame, 2);
            ushort quantity = ReadUInt16(frame, 4);
            int byteCount = frame[6];

            if (quantity < 1 || quantity > 1968)
                return ExceptionResponse(frame[0], frame[1], ExceptionIllegalValue);
            if (address + quantity > Coils.Length)
                return ExceptionResponse(frame[0], frame[1], ExceptionIllegalAddress);
            if (byteCount != (quantity + 7) / 8 || frame.Length < 7 + byteCount)
                return ExceptionResponse(frame[0], frame[1], ExceptionIllegalValue);

            for (int i = 0; i < quantity; i++)
                Coils[address + i] = (frame[7 + i / 8] & (1 << (i % 8))) != 0;

            return Build(frame[0], frame[1], new[] { frame[2], frame[3], frame[4], frame[5] });
        }

        private byte[]? WriteMultipleRegisters(byte[] frame)
        {
            if (frame.Length < 7) return null;
            ushort address = ReadUInt16(frame, 2);
            ushort quantity = ReadUInt16(frame, 4);
            int byteCount = frame[6];

            if (quantity < 1 || quantity > 123)
                return ExceptionResponse(frame[0], frame[1], ExceptionIllegalValue);
            if (address + quantity > HoldingRegisters.Length)
                return ExceptionResponse(frame[0], frame[1], ExceptionIllegalAddress);
            if (byteCount != quantity * 2 || frame.Length < 7 + byteCount)
                return ExceptionResponse(frame[0], frame[1], ExceptionIllegalValue);

            for (int i = 0; i < quantity; i++)
                HoldingRegisters[address + i] = (ushort)((frame[7 + i * 2] << 8) | frame[8 + i * 2]);

            return Build(frame[0], frame[1], new[] { frame[2], frame[3], frame[4], frame[5] });
        }

        private static byte[] ExceptionResponse(byte unit, byte function, byte code)
            => Build(unit, (byte)(function | 0x80), new[] { code });

        private static byte[] Build(byte unit, byte function, byte[] body)
        {
            var result = new byte[body.Length + 4];
            result[0] = unit;
            result[1] = function;
            Array.Copy(body, 0, result, 2, body.Length);
            ushort crc = Crc16(result, 0, result.Length - 2);
            result[^2] = (byte)(crc & 0xFF);
            result[^1] = (byte)(crc >> 8);
            return result;
        }

        /// <summary>CRC16/MODBUS：poly 0xA001、初值 0xFFFF、右移、低字节先发。</summary>
        public static ushort Crc16(byte[] data, int offset, int count)
        {
            ushort crc = 0xFFFF;
            for (int i = 0; i < count; i++)
            {
                crc ^= data[offset + i];
                for (int bit = 0; bit < 8; bit++)
                    crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
            }
            return crc;
        }

        /// <summary>拼一帧请求（自动补 CRC），测试用。</summary>
        public static byte[] Request(byte unit, byte function, params byte[] body)
        {
            var frame = new byte[body.Length + 4];
            frame[0] = unit;
            frame[1] = function;
            Array.Copy(body, 0, frame, 2, body.Length);
            ushort crc = Crc16(frame, 0, frame.Length - 2);
            frame[^2] = (byte)(crc & 0xFF);
            frame[^1] = (byte)(crc >> 8);
            return frame;
        }

        /// <summary>校验一帧的 CRC 是否自洽（响应断言用）。</summary>
        public static bool IsCrcValid(byte[] frame)
        {
            if (frame == null || frame.Length < 4) return false;
            ushort crc = Crc16(frame, 0, frame.Length - 2);
            return (byte)(crc & 0xFF) == frame[^2] && (byte)(crc >> 8) == frame[^1];
        }

        private static ushort ReadUInt16(byte[] frame, int index)
            => (ushort)((frame[index] << 8) | frame[index + 1]);

        #endregion

        #region 串口收发（HIL：需要一对可互通的串口）

        /// <summary>
        /// 在指定串口上开始应答。⚠ 本机没有可用串口对时这条路径无法验证（见 RT 段跳过说明）。
        /// </summary>
        public void Start(string portName, int baudRate = 9600, Parity parity = Parity.None,
                          int dataBits = 8, StopBits stopBits = StopBits.One)
        {
            if (_running) throw new InvalidOperationException("从站已在运行");

            _port = new SerialPort(portName, baudRate, parity, dataBits, stopBits)
            {
                ReadTimeout = 20,
                WriteTimeout = 500
            };
            _port.Open();

            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "RtuSlave" };
            _thread.Start();
        }

        private void Loop()
        {
            var buffer = new List<byte>(256);
            while (_running)
            {
                try
                {
                    int value = _port!.ReadByte();
                    if (value < 0) continue;

                    buffer.Add((byte)value);
                    if (buffer.Count > 512) buffer.Clear();   // 防噪声把缓冲撑爆
                }
                catch (TimeoutException)
                {
                    // 3.5 字符静默 = 一帧结束。用 20ms 读超时近似（9600bps 下 3.5 字符 ≈ 4ms，
                    // 20ms 对"应答不及时"更宽容；真实设备上若响应极快可再调小）。
                    if (buffer.Count >= 4)
                    {
                        var response = Handle(buffer.ToArray());
                        buffer.Clear();
                        if (response != null)
                        {
                            try { _port!.Write(response, 0, response.Length); } catch { /* 单帧失败不影响下一帧 */ }
                        }
                    }
                    else
                    {
                        buffer.Clear();
                    }
                }
                catch
                {
                    if (!_running) break;
                    Thread.Sleep(10);
                }
            }
        }

        #endregion

        public void Dispose()
        {
            _running = false;
            try { _thread?.Join(500); } catch { }
            try { _port?.Close(); } catch { }
            try { _port?.Dispose(); } catch { }
            _port = null;
            _thread = null;
        }
    }

    /// <summary>
    /// 把 <see cref="RtuSlave"/> 挂在 TCP 上，跑"RTU over TCP"（串口服务器/网关的常见形态）。
    ///
    /// <para><b>为什么测试需要它</b>：RTU 真实帧的端到端验证原本必须有一对可互通串口，
    /// 本机没有 com0com。改成 TCP 后，<b>帧本身仍然是标准 RTU 帧</b>（带 CRC、站号、功能码），
    /// 只是承载层换成 socket——于是"我们的从站编解码能否与真实 Modbus 客户端互通"
    /// 这件事现在可以无条件验证。串口收发那几十行仍需硬件。</para>
    /// </summary>
    internal sealed class RtuOverTcpServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly RtuSlave _slave;
        private readonly Thread _thread;
        private volatile bool _running = true;
        private int _frames;
        private readonly List<string> _frameLog = new();

        public RtuOverTcpServer(RtuSlave slave, int port)
        {
            _slave = slave ?? throw new ArgumentNullException(nameof(slave));
            _listener = new TcpListener(IPAddress.Loopback, port);
            _listener.Start();
            _thread = new Thread(Loop) { IsBackground = true, Name = "RtuOverTcp" };
            _thread.Start();
        }

        /// <summary>收到的完整请求帧数（断言"真的走了多帧交互"）。</summary>
        public int Frames => Volatile.Read(ref _frames);

        /// <summary>最近若干请求帧的十六进制（失败时打印，便于判读地址/功能码）。</summary>
        public string FrameLog
        {
            get { lock (_frameLog) return string.Join(" | ", _frameLog); }
        }

        private void Loop()
        {
            try
            {
                using var client = _listener.AcceptTcpClient();
                using var stream = client.GetStream();
                var buffer = new List<byte>(256);
                var chunk = new byte[256];

                while (_running)
                {
                    int read = stream.Read(chunk, 0, chunk.Length);
                    if (read <= 0) break;

                    for (int i = 0; i < read; i++) buffer.Add(chunk[i]);

                    while (TryTakeFrame(buffer, out var frame))
                    {
                        Interlocked.Increment(ref _frames);
                        lock (_frameLog)
                        {
                            if (_frameLog.Count < 16) _frameLog.Add(BitConverter.ToString(frame));
                        }
                        var response = _slave.Handle(frame);
                        if (response != null) stream.Write(response, 0, response.Length);
                    }
                }
            }
            catch
            {
                // 测试夹具：连接断开/监听停止即结束，不往线程池抛
            }
        }

        /// <summary>按功能码算请求长度（RTU 请求长度是确定的，不必靠空闲间隔判帧）。</summary>
        private static bool TryTakeFrame(List<byte> buffer, out byte[] frame)
        {
            frame = Array.Empty<byte>();
            if (buffer.Count < 2) return false;

            int length = buffer[1] switch
            {
                0x01 or 0x02 or 0x03 or 0x04 or 0x05 or 0x06 => 8,
                0x0F or 0x10 => buffer.Count >= 7 ? 9 + buffer[6] : -1,
                _ => 4
            };

            if (length < 0 || buffer.Count < length) return false;

            frame = buffer.GetRange(0, length).ToArray();
            buffer.RemoveRange(0, length);
            return true;
        }

        public void Dispose()
        {
            _running = false;
            try { _listener.Stop(); } catch { }
            try { _thread.Join(500); } catch { }
        }
    }
}
