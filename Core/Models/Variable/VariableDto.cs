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

        /// <summary>DataValueType 枚举名（Boolean/Int16/Int32/Float/...）</summary>
        public string? DataValueType { get; set; }

        /// <summary>S7 专用：DB 块编号</summary>
        public int DbNumber { get; set; } = 1;

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
                CommunicationType.ModbusTcp => "ModbusAddress",
                CommunicationType.SiemensS7 => "S7Address",
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
            address.DataType = dataType;
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
                DataValueType = addr?.DataType.ToString()
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
            }
            return dto;
        }
    }
}
