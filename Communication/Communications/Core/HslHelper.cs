using HslCommunication;
using HslCommunication.Core;
using System;
using System.Text.RegularExpressions;

namespace VisionMaster.Communications
{
    /// <summary>
    /// <para>写链路分发用的协议族。</para>
    /// <para>Modbus 按寄存器（2 字节字）语义写；S7 按字节流语义写，两者对窄类型的处理不同。</para>
    /// </summary>
    internal enum WriteProtocolFamily
    {
        Modbus,
        SiemensS7
    }

    /// <summary>
    /// HslCommunication 辅助类，提供数据类型转换功能。
    /// ⚠ 字节序纪律：Modbus/S7 设备侧字节流是【大端】（寄存器内 Hi,Lo；多寄存器 ABCD 字序），
    /// 而 BitConverter 在 x86/x64 上按【小端】解释——旧实现直接 BitConverter 转换，
    /// 导致与标准设备交换数据时字节系统性颠倒（实测暗号：写 21=0x0015 设备存成 0x1500=5376；
    /// 设备写 1221=0x04C5 软件读成 0xC504=50436）。软件自己写自己读"自洽"，接真 PLC 必炸。
    /// 统一修复：读写两侧都先反转字节 = 按大端解释/编码。
    /// 32/64 位按 ABCD 字序（与 HSL DataFormat 默认一致；对端模拟器/PLC 若用 CDAB 等需对齐）。
    /// </summary>
    internal static class HslHelper
    {
        /// <summary>S7 位地址特征：以 ".0"~".7" 结尾（如 M10.6 / DB1.DBX0.3）</summary>
        private static readonly Regex S7BitAddressRegex = new(@"\.[0-7]$", RegexOptions.Compiled);

        /// <summary>
        /// 目标 CLR 类型解码所需的最小字节数；不支持的类型返回 0
        /// </summary>
        public static int MinByteCount(Type type) => Type.GetTypeCode(type) switch
        {
            TypeCode.Boolean or TypeCode.Byte or TypeCode.SByte => 1,
            TypeCode.Int16 or TypeCode.UInt16 => 2,
            TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Single => 4,
            TypeCode.Int64 or TypeCode.UInt64 or TypeCode.Double => 8,
            _ => 0
        };

        /// <summary>
        /// 将设备返回的大端字节数组转换为指定类型。
        /// 修复点：旧实现要求 data 至少 2 字节，导致 1 字节的 bool/byte 读取恒返回 default；
        /// 且整体反转在数据长于需求时（批量段切片）会取错位置——现在只取类型所需的头部字节。
        /// </summary>
        public static T ConvertTo<T>(byte[] data) where T : struct
        {
            int need = MinByteCount(typeof(T));
            if (need == 0 || data == null || data.Length < need)
                return default;

            // 只截取本类型所需的头部字节（大端序，高位在前），反转成小端交给 BitConverter
            var be = new byte[need];
            Array.Copy(data, 0, be, 0, need);
            if (BitConverter.IsLittleEndian && need > 1)
                Array.Reverse(be);

            var typeCode = Type.GetTypeCode(typeof(T));
            return typeCode switch
            {
                TypeCode.Boolean => (T)(object)(be[0] != 0),
                TypeCode.Byte => (T)(object)be[0],
                TypeCode.SByte => (T)(object)(sbyte)be[0],
                TypeCode.Int16 => (T)(object)BitConverter.ToInt16(be, 0),
                TypeCode.UInt16 => (T)(object)BitConverter.ToUInt16(be, 0),
                TypeCode.Int32 => (T)(object)BitConverter.ToInt32(be, 0),
                TypeCode.UInt32 => (T)(object)BitConverter.ToUInt32(be, 0),
                TypeCode.Single => (T)(object)BitConverter.ToSingle(be, 0),
                TypeCode.Double => (T)(object)BitConverter.ToDouble(be, 0),
                _ => default
            };
        }

        /// <summary>
        /// 非泛型解码入口（批量规划器用，避免每轮反射 MakeGenericMethod）；
        /// 仅覆盖 struct 数值类型，string/byte[] 由调用方单独处理。
        /// </summary>
        public static object? ConvertToByType(byte[] data, Type type)
        {
            var t = Nullable.GetUnderlyingType(type) ?? type;
            return Type.GetTypeCode(t) switch
            {
                TypeCode.Boolean => ConvertTo<bool>(data),
                TypeCode.Byte => ConvertTo<byte>(data),
                TypeCode.SByte => ConvertTo<sbyte>(data),
                TypeCode.Int16 => ConvertTo<short>(data),
                TypeCode.UInt16 => ConvertTo<ushort>(data),
                TypeCode.Int32 => ConvertTo<int>(data),
                TypeCode.UInt32 => ConvertTo<uint>(data),
                TypeCode.Single => ConvertTo<float>(data),
                TypeCode.Int64 => ConvertTo<long>(data),
                TypeCode.UInt64 => ConvertTo<ulong>(data),
                TypeCode.Double => ConvertTo<double>(data),
                _ => null
            };
        }

        /// <summary>
        /// 按目标类型计算 Modbus 读取寄存器数量（每寄存器 2 字节）
        /// </summary>
        public static ushort RegisterCount<T>() where T : struct
        {
            var code = Type.GetTypeCode(typeof(T));
            return code switch
            {
                TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Single => 2,
                TypeCode.Int64 or TypeCode.UInt64 or TypeCode.Double => 4,
                _ => 1
            };
        }

        /// <summary>
        /// <para>按值类型分发到 HSL 强类型 Write 重载（读写两条链路共用大端 ByteTransform）。</para>
        /// <para>协议差异集中在窄类型上：</para>
        /// <para>1) Modbus 布尔必须走线圈/离散位区（Write(address,bool) → FC5/15，地址 x=1;/x=2;），
        /// 寄存器区 Boolean 已在地址层禁止；byte/sbyte 补齐为单寄存器（FC6），避免奇数字节 FC16 报错。</para>
        /// <para>2) S7 布尔按地址形态分流：位地址（M10.6 / DB1.DBX0.3）走 WriteBit 指令；
        /// 字节地址（MB7 / DB1.DBB0）按 1 字节 0/1 写——HSL 会把 "MB7" 误解析成 M7.0 位地址，绝不能走 bool 重载。</para>
        /// <para>3) S7 的 byte/sbyte 按 1 字节写（TypeSize=1），不能升格为 short——那会多覆盖相邻字节。</para>
        /// </summary>
        public static void WriteTyped(IReadWriteNet device, string address, object value, WriteProtocolFamily family)
        {
            OperateResult result;
            switch (value)
            {
                case bool b:
                    if (family == WriteProtocolFamily.SiemensS7 && !S7BitAddressRegex.IsMatch(address))
                        result = device.Write(address, new byte[] { (byte)(b ? 1 : 0) });
                    else
                        result = device.Write(address, b);
                    break;

                case byte by when family == WriteProtocolFamily.Modbus:
                    result = device.Write(address, (ushort)by);
                    break;

                case sbyte sb when family == WriteProtocolFamily.Modbus:
                    result = device.Write(address, (short)sb);
                    break;

                case byte by:
                    result = device.Write(address, new[] { by });
                    break;

                case sbyte sb:
                    result = device.Write(address, new[] { unchecked((byte)sb) });
                    break;

                case short sh: result = device.Write(address, sh); break;
                case ushort us: result = device.Write(address, us); break;
                case int i: result = device.Write(address, i); break;
                case uint ui: result = device.Write(address, ui); break;
                case float f: result = device.Write(address, f); break;
                case double d: result = device.Write(address, d); break;
                case long l: result = device.Write(address, l); break;
                case ulong ul: result = device.Write(address, ul); break;

                // string/byte[] 等无端序语义的值原样传输
                default:
                    result = device.Write(address, GetValueArray(value));
                    break;
            }

            if (!result.IsSuccess)
                throw new InvalidOperationException(result.Message);
        }

        /// <summary>
        /// 将对象值编码为大端字节数组（下发设备用；仅作为强类型分发失败后的兜底路径）
        /// </summary>
        public static byte[] GetValueArray(object value)
        {
            // 字符串/单字节无端序语义，原样传输（反转会乱码/错位）
            if (value is string s)
                return System.Text.Encoding.ASCII.GetBytes(s);
            if (value is bool b)
                return new[] { (byte)(b ? 1 : 0) };
            if (value is byte by)
                return new[] { by };

            byte[] raw = value switch
            {
                short v => BitConverter.GetBytes(v),
                ushort v => BitConverter.GetBytes(v),
                int v => BitConverter.GetBytes(v),
                uint v => BitConverter.GetBytes(v),
                float v => BitConverter.GetBytes(v),
                double v => BitConverter.GetBytes(v),
                _ => BitConverter.GetBytes(Convert.ToInt64(value))
            };

            if (BitConverter.IsLittleEndian)
                Array.Reverse(raw); // 小端内存序 → 大端传输序
            return raw;
        }
    }
}
