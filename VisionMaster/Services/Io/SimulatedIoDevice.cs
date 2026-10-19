using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Core.Interfaces.Io;

namespace VisionMaster.Services.Io
{
    /// <summary>
    /// 内存模拟设备：用于无硬件时的契约测试、UI 联调和变量接入验证。
    /// 不模拟协议帧；协议层测试请用 Modbus/S7 模拟器。
    /// </summary>
    public sealed class SimulatedIoDevice : IoDeviceBase
    {
        private readonly ConcurrentDictionary<string, bool> _bits = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, double> _analogs = new(StringComparer.OrdinalIgnoreCase);

        public SimulatedIoDevice(IoDeviceDescriptor descriptor, IoBusDescriptor bus, IIoRuntimeContext context)
            : base(descriptor, bus, context)
        {
            Capabilities = IoCapabilities.DigitalInput | IoCapabilities.DigitalOutput |
                           IoCapabilities.AnalogInput | IoCapabilities.AnalogOutput;
            foreach (var point in descriptor.Points)
            {
                if (point.Kind is IoPointKind.DigitalInput or IoPointKind.DigitalOutput)
                    _bits[point.PointId] = false;
                else
                    _analogs[point.PointId] = 0;
            }
        }

        /// <summary>测试用：下一次轮询返回失败（模拟链路故障）。</summary>
        public bool FailNextPoll { get; set; }

        /// <summary>测试用：连接失败次数。</summary>
        public int FailConnectTimes { get; set; }

        protected override bool ConnectCore(out string? error)
        {
            if (FailConnectTimes > 0)
            {
                FailConnectTimes--;
                error = "模拟连接失败";
                return false;
            }
            error = null;
            return true;
        }

        protected override void DisconnectCore() { }

        protected override bool PollCore(IReadOnlyList<IoPointDescriptor> points,
                                        IDictionary<string, IoPointValue> values,
                                        out string? error)
        {
            if (FailNextPoll)
            {
                FailNextPoll = false;
                error = "模拟轮询失败";
                return false;
            }

            foreach (var point in points)
            {
                if (point.Kind is IoPointKind.DigitalInput or IoPointKind.DigitalOutput)
                {
                    bool v = _bits.TryGetValue(point.PointId, out var b) && b;
                    values[point.PointId] = IoPointValue.FromBool(v);
                }
                else
                {
                    double v = _analogs.TryGetValue(point.PointId, out var a) ? a : 0;
                    values[point.PointId] = IoPointValue.FromRaw(point.DataType, (long)Math.Round(v), v);
                }
            }

            error = null;
            return true;
        }

        protected override bool WriteCore(IReadOnlyList<IoWriteRequest> writes, out string? error)
        {
            foreach (var write in writes)
            {
                if (!TryGetPoint(write.PointId, out var point))
                {
                    error = $"未配置点位：{write.PointId}";
                    return false;
                }

                if (point.Kind is IoPointKind.DigitalOutput)
                    _bits[write.PointId] = write.Value.Boolean;
                else if (point.Kind is IoPointKind.AnalogOutput)
                    _analogs[write.PointId] = write.Value.Numeric;
            }

            error = null;
            return true;
        }
    }

    [IoDriver("simulated")]
    public sealed class SimulatedIoDriverFactory : IIoDriverFactory
    {
        public string DriverId => "simulated";
        public string DisplayName => "模拟 IO 设备";
        public string Description => "内存模拟，用于无硬件测试";
        public IoCapabilities Capabilities =>
            IoCapabilities.DigitalInput | IoCapabilities.DigitalOutput |
            IoCapabilities.AnalogInput | IoCapabilities.AnalogOutput;
        public Type? BusConfigType => typeof(IoBusDescriptor);
        public Type? DeviceConfigType => typeof(IoDeviceDescriptor);

        public IIoDevice Create(IoDeviceDescriptor descriptor, IoBusDescriptor bus, IIoRuntimeContext context)
            => new SimulatedIoDevice(descriptor, bus, context);
    }
}
