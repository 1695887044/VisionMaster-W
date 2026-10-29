using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using VisionMaster.Communications;

namespace VisionMaster.Models
{
    /// <summary>
    /// 变量持久化快照 DTO：
    /// 本地变量与网络变量统一用此快照随方案落盘（.vms）。
    /// 为什么用 DTO 而不直接序列化 IVariable：IVariable/DeviceAddressBase 是抽象接口，
    /// JSON 反序列化无法多态还原具体地址类，且具体地址类在 Communication 程序集（Core 不能反向引用）。
    /// </summary>
    public class VariableDto
    {
        /// <summary>"Local" | "Network"</summary>
        public string VarType { get; set; } = "Local";

        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 变量稳定身份（GUID）：随方案落盘并按原值还原，使连线/监视项/SCADA 绑定的锚点
        /// 在方案存取往返、变量改名后依然有效。
        /// 旧方案数据无此字段 → Guid.Empty，由持久化层补发新 Id（迁移，下次保存即回写）
        /// </summary>
        public Guid VariableId { get; set; }

        /// <summary>TypeCache 类型键（如 "Int32"、"String[]"）</summary>
        public string DataTypeString { get; set; } = "Int32";

        public string Description { get; set; } = string.Empty;

        /// <summary>初始值（JSON 原生编码：数字/字符串/布尔/数组）</summary>
        public object? DefaultValue { get; set; }

        /// <summary>当前值快照（JSON 原生编码）</summary>
        public object? Value { get; set; }

        // ---------- 以下仅网络变量使用 ----------

        public string? ConnectionName { get; set; }

        /// <summary>通信协议（ModbusTcp/SiemensS7）——决定重建哪种地址对象</summary>
        public string? Protocol { get; set; }

        /// <summary>存储区名（ModbusArea/S7Area 枚举名字符串）</summary>
        public string? Area { get; set; }

        public string Offset { get; set; } = "0";

        public int BitOffset { get; set; } = -1;

        /// <summary>
        /// 数据长度：数值/布尔 = 元素个数（标量 1、数组 N）；String/ByteArray = 总字节数。
        /// <para>数组变量必须随方案落盘，否则重新打开后 Length 回到默认 1，数组静默变回标量。</para>
        /// </summary>
        public int Length { get; set; }

        /// <summary>地址偏移（在基地址上再加 N 个地址单位；0 = 不偏移）</summary>
        public int AddressOffset { get; set; }

        /// <summary>字符串数组：每个元素的固定字节数（0 = 非字符串数组）</summary>
        public int ElementByteLength { get; set; }

        /// <summary>
        /// 标量工程量换算（地址对象上的那一份）。
        /// <para>旧实现只把这些参数留在内存里的 <see cref="DeviceAddressBase"/> 上、没随方案落盘：
        /// 用户配好的缩放/偏移/单位在下次打开方案时静默回到默认（不换算）。
        /// 旧方案无这些字段 → EnableConversion=false / Scale=1 / Unit 空，行为不变。</para>
        /// </summary>
        public bool EnableConversion { get; set; }

        public double Scale { get; set; } = 1;

        public double EngineeringOffset { get; set; }

        public string Unit { get; set; } = string.Empty;

        /// <summary>标量换算的显示小数位（0~10；旧方案缺省 2，与 DeviceAddressBase 默认一致）</summary>
        public int DecimalPlaces { get; set; } = 2;

        /// <summary>
        /// 数组逐元素工程量换算档案（下标与元素一一对应；null/缺省 = 该元素不换算）。
        /// <para>必须随方案落盘：不存的话下次打开方案，数组换算全部丢失，
        /// 界面显示的是原始值、写回设备的也是原始值——而且没有任何报错。</para>
        /// <para>旧方案无此字段 → null → 不换算，行为与改造前一致。</para>
        /// </summary>
        public ElementConversion?[]? ElementConversions { get; set; }

        /// <summary>
        /// 是否允许与相邻地址合并成一次区域读（默认 true）。
        /// <para>旧实现只把它抄进 <c>CommunicationVariable</c>、没随方案落盘：
        /// 用户关掉合并读后，下次打开方案静默恢复成 true（"只读自己这一小块"的配置丢失）。
        /// 旧方案无此字段 → 初始值 true，零回归。</para>
        /// </summary>
        public bool MergeAdjacentRead { get; set; } = true;

        /// <summary>Modbus 传统 5 位地址写法开关（40001 → 0 基 0；默认 false，旧数据零影响）</summary>
        public bool UseLegacyAddress { get; set; }

        /// <summary>DataValueType 枚举名（Boolean/Int16/Int32/Float/...）</summary>
        public string? DataValueType { get; set; }

        /// <summary>S7 专用：DB 块编号</summary>
        public int DbNumber { get; set; } = 1;

        /// <summary>
        /// 所属扫描组名（仅网络变量使用；空 = 默认组，周期取连接配置的 ReadCycleMs）。
        /// <para>为什么必须随方案落盘：组名是"这个点由哪个节拍采集"的唯一依据，而组表本身存在
        /// 连接配置（communications.json）里、不随方案走。丢这个字段的后果是——用户在变量管理里
        /// 挂好的快组，下次打开方案时**静默退回默认组**：联锁点从 100ms 变成 1s，界面上一点异常都看不出。</para>
        /// <para>旧方案文件无此字段 → 反序列化为空串 → 正好等于"默认组"，与改造前行为一致（零回归）。</para>
        /// </summary>
        public string ScanGroup { get; set; } = string.Empty;

        /// <summary>
        /// 显示分组 Id（仅网络变量使用；Guid.Empty = 未分组）。
        /// 只用于变量管理的归类与筛选，不参与采集/轮询/写入；旧方案无此字段 → Guid.Empty，零回归。
        /// </summary>
        public Guid GroupId { get; set; }

        /// <summary>
        /// 从 JSON 令牌/装箱值按目标类型还原值。
        /// Newtonsoft 反序列化 object 属性的产物是 JValue/JArray（JToken 树），需转回 CLR 值；
        /// 内存内传递场景（已是 CLR 对象）原样返回。
        /// </summary>
        public static object? JsonToValue(object? jsonValue, Type targetType)
        {
            if (jsonValue == null) return null;

            if (jsonValue is JToken token)
            {
                if (token.Type == JTokenType.Null) return null;
                var t = Nullable.GetUnderlyingType(targetType) ?? targetType;
                // int/double/string/bool/数组等 JToken 直转 CLR；
                // JToken 树读回后即弃，TargetType 不匹配时抛异常由调用方捕获
                return token.ToObject(t);
            }

            return jsonValue; // 已是普通 CLR 对象（内存内传递场景）
        }

        /// <summary>
        /// 按协议重建地址配置对象。
        /// 具体地址类（ModbusAddress/S7Address）在 Communication 程序集，Core 不能反向引用，
        /// 运行时该程序集必已加载（ShellViewModel 依赖通信管理器），故用反射按全名创建。
        /// </summary>
        public DeviceAddressBase? BuildAddressConfig()
        {
            if (string.IsNullOrEmpty(Protocol) || string.IsNullOrEmpty(DataValueType)) return null;
            if (!Enum.TryParse<CommunicationType>(Protocol, out var protocol)) return null;
            if (!Enum.TryParse<DataValueType>(DataValueType, out var dataType)) return null;

            var typeName = protocol switch
            {
                // Modbus RTU 与 TCP 共用 ModbusAddress（只是链路不同）。漏掉 RTU 是既有缺陷：
                // 串口变量保存后重开 AddressConfig 为 null，当前值永不刷新，且只有一行 Debug 日志。
                CommunicationType.ModbusTcp or CommunicationType.ModbusRtu => "ModbusAddress",
                CommunicationType.SiemensS7 => "S7Address",
                CommunicationType.MitsubishiMc => "McAddress",
                CommunicationType.OmronFins => "FinsAddress",
                // 漏这一支的后果：方案存得下、重开时 AddressConfig 为 null，
                // NetworkVariableBridge 只 Warn 一句「未接入轮询」，所有 EasyNet 变量永远不刷新
                CommunicationType.EasyNet => "EasyNetAddress",
                _ => null
            };
            if (typeName == null) return null;

            var addressType = FindCommunicationType(typeName);
            if (addressType == null) return null;
            if (Activator.CreateInstance(addressType) is not DeviceAddressBase address) return null;

            // Area 属性在泛型派生类 DeviceAddressBase<TAreaEnum> 上，按属性实际枚举类型解析
            if (!string.IsNullOrEmpty(Area))
            {
                var areaProp = addressType.GetProperty("Area");
                if (areaProp != null && Enum.TryParse(areaProp.PropertyType, Area, out var areaValue))
                    areaProp.SetValue(address, areaValue);
            }

            // S7 专用 DB 块号（ModbusAddress 无此属性，跳过）
            var dbProp = addressType.GetProperty("DbNumber");
            if (dbProp != null && dbProp.PropertyType == typeof(int))
                dbProp.SetValue(address, DbNumber);

            address.Offset = Offset;
            address.BitOffset = BitOffset;
            address.DataType = dataType; // 注意：DataType setter 会把 Length 重置为类型默认值
            if (Length > 0) address.Length = Length; // 旧方案无此字段 → 0 → 保留类型默认值
            address.AddressOffset = AddressOffset;
            address.ElementByteLength = ElementByteLength;

            // 标量换算参数：漏还原 = 配好的缩放/偏移在下次打开方案时静默失效
            address.EnableConversion = EnableConversion;
            address.Scale = Scale;
            address.EngineeringOffset = EngineeringOffset;
            address.Unit = Unit;
            address.DecimalPlaces = DecimalPlaces;

            // UseLegacyAddress 只在 ModbusAddress 上（S7 没有 5 位写法）；按反射设置，Core 不反向引用通信程序集。
            var legacyProp = addressType.GetProperty("UseLegacyAddress");
            if (legacyProp != null && legacyProp.PropertyType == typeof(bool))
                legacyProp.SetValue(address, UseLegacyAddress);

            return address;
        }

        /// <summary>在已加载程序集中查找 Communication 层类型</summary>
        private static Type? FindCommunicationType(string typeName)
        {
            var fullName = $"VisionMaster.Communications.{typeName}";
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName, throwOnError: false);
                if (t != null) return t;
            }
            return null;
        }

        /// <summary>
        /// 从地址配置提取 DTO 网络字段（协议由调用方传入）
        /// </summary>
        public static VariableDto FromNetwork(NetworkVariableModel nv, CommunicationType protocol)
        {
            var addr = nv.AddressConfig;
            var dto = new VariableDto
            {
                VarType = "Network",
                Name = nv.Name,
                VariableId = nv.VariableId,
                DataTypeString = nv.DataTypeString,
                Description = nv.Description,
                DefaultValue = nv.DefaultValue,
                Value = nv.Value,
                ConnectionName = nv.ConnectionName,
                Protocol = protocol.ToString(),
                Offset = addr?.Offset ?? "0",
                BitOffset = addr?.BitOffset ?? -1,
                Length = addr?.Length ?? 0,
                AddressOffset = addr?.AddressOffset ?? 0,
                ElementByteLength = addr?.ElementByteLength ?? 0,
                // 逐元素换算与区域合并读必须随方案落盘（漏拷 = 下次打开静默退回默认行为）
                ElementConversions = nv.ElementConversions
                    ?.Select(c => c?.Clone())
                    .ToArray(),
                MergeAdjacentRead = nv.MergeAdjacentRead,
                // 标量换算随方案落盘（旧实现漏拷 = 下次打开静默回到"不换算"）
                EnableConversion = addr?.EnableConversion ?? false,
                Scale = addr?.Scale ?? 1,
                EngineeringOffset = addr?.EngineeringOffset ?? 0,
                Unit = addr?.Unit ?? string.Empty,
                DecimalPlaces = addr?.DecimalPlaces ?? 2,
                DataValueType = addr?.DataType.ToString(),
                // 扫描组随变量落盘（漏拷 = 挂好的快组在下次打开方案时静默退回默认组）
                ScanGroup = nv.ScanGroup ?? string.Empty,
                // 显示分组同样随变量落盘（漏拷 = 用户归好的类在下次打开方案时静默回到「未分组」）
                GroupId = nv.GroupId
            };

            // Area/DbNumber 在泛型派生类上（ModbusAddress/S7Address），Core 无法直接转型，反射读取
            if (addr != null)
            {
                var addrType = addr.GetType();
                var areaProp = addrType.GetProperty("Area");
                dto.Area = areaProp?.GetValue(addr)?.ToString();
                var dbProp = addrType.GetProperty("DbNumber");
                if (dbProp != null && dbProp.PropertyType == typeof(int))
                    dto.DbNumber = (int)dbProp.GetValue(addr)!;

                // 5 位地址写法开关只在 ModbusAddress 上（S7 没有该属性）
                var legacyProp = addrType.GetProperty("UseLegacyAddress");
                if (legacyProp != null && legacyProp.PropertyType == typeof(bool))
                    dto.UseLegacyAddress = (bool)legacyProp.GetValue(addr)!;
            }
            return dto;
        }
    }
}
