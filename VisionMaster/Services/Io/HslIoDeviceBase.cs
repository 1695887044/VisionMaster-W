using System;
using System.Collections.Generic;
using Core.Interfaces.Io;
using HslCommunication;
using HslCommunication.Core;

namespace VisionMaster.Services.Io
{
    /// <summary>
    /// 基于 HSL 的 IO 设备公共实现：只负责按点位类型读写原始值。
    /// 缩放/偏移/死区由 IoDeviceBase 统一处理。
    /// </summary>
    public abstract class HslIoDeviceBase : IoDeviceBase
    {
        protected IReadWriteNet? Client;

        protected HslIoDeviceBase(IoDeviceDescriptor descriptor, IoBusDescriptor bus, IIoRuntimeContext context)
            : base(descriptor, bus, context) { }

        protected abstract IReadWriteNet? Open(out string? error);
        protected abstract void Close();

        protected override bool ConnectCore(out string? error)
        {
            Client = Open(out error);
            return Client != null;
        }

        protected override void DisconnectCore()
        {
            try { Close(); } catch { }
            Client = null;
        }

        protected override bool PollCore(IReadOnlyList<IoPointDescriptor> points,
                                        IDictionary<string, IoPointValue> values,
                                        out string? error)
        {
            error = null;
            if (Client == null)
            {
                error = "设备未连接";
                return false;
            }

            bool linkFault = false;
            foreach (var point in points)
            {
                if (point.Kind is IoPointKind.DigitalOutput or IoPointKind.AnalogOutput)
                    continue; // 输出点默认不轮询回读；需要回读时由驱动扩展

                if (TryRead(Client, point, out var value, out var readError))
                {
                    values[point.PointId] = value;
                    continue;
                }

                if (readError != null && readError.StartsWith("[LINK]", StringComparison.Ordinal))
                {
                    linkFault = true;
                    error = readError.Substring(6);
                    break;
                }
            }

            return !linkFault;
        }

        /// <summary>单点即时读：输出点也允许回读（轮询路径默认跳过输出，ReadNow 不跳过）。</summary>
        protected override bool ReadPointCore(IoPointDescriptor point, out IoPointValue value, out string? error)
        {
            if (Client == null)
            {
                value = IoPointValue.Bad(point.DataType);
                error = "设备未连接";
                return false;
            }
            return TryRead(Client, point, out value, out error);
        }

        protected override bool WriteCore(IReadOnlyList<IoWriteRequest> writes, out string? error)
        {
            error = null;
            if (Client == null)
            {
                error = "设备未连接";
                return false;
            }

            foreach (var write in writes)
            {
                if (!TryGetPoint(write.PointId, out var point))
                {
                    error = $"未配置点位：{write.PointId}";
                    return false;
                }

                if (!TryWrite(Client, point, write.Value, out error))
                    return false;
            }

            return true;
        }

        protected static bool TryRead(IReadWriteNet client, IoPointDescriptor point,
                                      out IoPointValue value, out string? error)
        {
            error = null;
            OperateResult result;
            switch (point.DataType)
            {
                case IoDataType.Bool:
                {
                    var r = client.ReadBool(point.Address);
                    if (!r.IsSuccess) { error = Describe(r); value = IoPointValue.Bad(point.DataType); return false; }
                    value = IoPointValue.FromBool(r.Content);
                    return true;
                }
                case IoDataType.Int16:
                {
                    var r = client.ReadInt16(point.Address);
                    if (!r.IsSuccess) { error = Describe(r); value = IoPointValue.Bad(point.DataType); return false; }
                    value = IoPointValue.FromRaw(point.DataType, r.Content, r.Content);
                    return true;
                }
                case IoDataType.UInt16:
                {
                    var r = client.ReadUInt16(point.Address);
                    if (!r.IsSuccess) { error = Describe(r); value = IoPointValue.Bad(point.DataType); return false; }
                    value = IoPointValue.FromRaw(point.DataType, r.Content, r.Content);
                    return true;
                }
                case IoDataType.Int32:
                {
                    var r = client.ReadInt32(point.Address);
                    if (!r.IsSuccess) { error = Describe(r); value = IoPointValue.Bad(point.DataType); return false; }
                    value = IoPointValue.FromRaw(point.DataType, r.Content, r.Content);
                    return true;
                }
                case IoDataType.UInt32:
                {
                    var r = client.ReadUInt32(point.Address);
                    if (!r.IsSuccess) { error = Describe(r); value = IoPointValue.Bad(point.DataType); return false; }
                    value = IoPointValue.FromRaw(point.DataType, r.Content, r.Content);
                    return true;
                }
                case IoDataType.Float:
                {
                    var r = client.ReadFloat(point.Address);
                    if (!r.IsSuccess) { error = Describe(r); value = IoPointValue.Bad(point.DataType); return false; }
                    value = IoPointValue.FromRaw(point.DataType, (long)Math.Round(r.Content), r.Content);
                    return true;
                }
                case IoDataType.Double:
                {
                    var r = client.ReadDouble(point.Address);
                    if (!r.IsSuccess) { error = Describe(r); value = IoPointValue.Bad(point.DataType); return false; }
                    value = IoPointValue.FromRaw(point.DataType, (long)Math.Round(r.Content), r.Content);
                    return true;
                }
                default:
                    error = $"不支持的数据类型：{point.DataType}";
                    value = IoPointValue.Bad(point.DataType);
                    return false;
            }
        }

        protected static bool TryWrite(IReadWriteNet client, IoPointDescriptor point,
                                       IoPointValue value, out string? error)
        {
            error = null;
            OperateResult result;
            switch (point.DataType)
            {
                case IoDataType.Bool:
                    result = client.Write(point.Address, value.Boolean);
                    break;
                case IoDataType.Int16:
                    result = client.Write(point.Address, (short)value.Raw);
                    break;
                case IoDataType.UInt16:
                    result = client.Write(point.Address, (ushort)value.Raw);
                    break;
                case IoDataType.Int32:
                    result = client.Write(point.Address, (int)value.Raw);
                    break;
                case IoDataType.UInt32:
                    result = client.Write(point.Address, (uint)value.Raw);
                    break;
                case IoDataType.Float:
                    result = client.Write(point.Address, (float)value.Numeric);
                    break;
                case IoDataType.Double:
                    result = client.Write(point.Address, value.Numeric);
                    break;
                default:
                    error = $"不支持的数据类型：{point.DataType}";
                    return false;
            }

            if (!result.IsSuccess)
            {
                error = Describe(result);
                return false;
            }
            return true;
        }

        protected static string Describe(OperateResult result)
        {
            // HSL 约定：ErrorCode < 0 = 网络/链路问题；其余是协议错误码。
            string prefix = result.ErrorCode < 0 ? "[LINK] " : string.Empty;
            return $"{prefix}{result.ErrorCode}: {result.Message}";
        }
    }
}
