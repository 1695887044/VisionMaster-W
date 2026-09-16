using System;

namespace VisionMaster.Communications
{
    /// <summary>
    /// <para>Modbus 地址配置。</para>
    /// <para>生成的地址串为 HSL 富地址格式 "x=功能码;0基址"（如 "x=3;100"），原因：</para>
    /// <para>1) 旧 "40001" 约定会被 HSL 当作寄存器号 40001 字面解析——静默读错位置；</para>
    /// <para>2) 旧 "40001.1" 位格式 HSL 无法解析——读失败。</para>
    /// <para>读写两条链路都吃富地址（已核对 HSL：写时 x=3 自动转 16、x=1 自动转 5）。</para>
    /// </summary>
    public class ModbusAddress : DeviceAddressBase<ModbusArea>
    {
        /// <summary>区域对应的读功能码（1 线圈 / 2 离散 / 3 保持 / 4 输入）</summary>
        public int FunctionCode => Area switch
        {
            ModbusArea.Coils => 1,
            ModbusArea.DiscreteInputs => 2,
            ModbusArea.HoldingRegisters => 3,
            ModbusArea.InputRegisters => 4,
            _ => 3
        };

        /// <summary>0 基原始地址（用户填写的偏移量，直接就是 HSL 认识的地址）</summary>
        public int RawAddress => int.TryParse(Offset, out int v) && v >= 0 ? v : 0;

        protected override string BuildAddress()
        {
            // 位偏移不参与：线圈/离散本身就是位粒度；寄存器区已禁止 Boolean 位访问（见 Validate）
            return $"x={FunctionCode};{RawAddress}";
        }

        public override bool TryCreatePollAddress(out PollAddress? poll)
        {
            poll = null;
            if (!int.TryParse(Offset, out int start) || start < 0) return false;

            int fc = FunctionCode;
            bool bitArea = fc is 1 or 2;
            // 位区 1 点=1 单元；寄存器区按字折算（Boolean 已被限制在位区，这里都是数值类型）
            int units = bitArea ? Math.Max(1, Length) : Math.Max(1, (TotalBytes + 1) / 2);

            poll = new PollAddress
            {
                Protocol = PollProtocol.Modbus,
                GroupKey = $"FC{fc}",
                SegmentPrefix = $"x={fc};",
                Start = start,
                SpanUnits = units,
                IsBitArea = bitArea,
                IsBitAccess = bitArea,
                BitOffset = -1
            };
            return true;
        }

        protected override bool IsAreaCompatibleWithDataType(ModbusArea area, DataValueType dataType)
        {
            // 线圈和离散输入只支持布尔类型
            if (area is ModbusArea.Coils or ModbusArea.DiscreteInputs)
            {
                return dataType == DataValueType.Boolean;
            }

            // 寄存器区不支持布尔：旧方案允许 Boolean 读 1 个寄存器（2 字节错位），
            // 位访问 "40001.1" 又无法解析——布尔一律走线圈/离散区
            if (dataType == DataValueType.Boolean)
            {
                return false;
            }

            return true;
        }
    }
}
