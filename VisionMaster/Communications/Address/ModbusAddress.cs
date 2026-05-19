using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.Communications
{
    public class ModbusAddress : DeviceAddressBase<ModbusArea>
    {
        protected override string BuildAddress()
        {
            if (!int.TryParse(Offset, out int offsetVal)) offsetVal = 0;

            int prefix = Area switch
            {
                ModbusArea.HoldingRegisters => 40000,
                ModbusArea.InputRegisters => 30000,
                ModbusArea.DiscreteInputs => 10000,
                ModbusArea.Coils => 0,
                _ => 0
            };

            string address = (prefix + offsetVal + 1).ToString();

            // 对于位类型，添加位偏移
            if (IsBitType)
            {
                address += $".{BitOffset}";
            }

            return address;
        }

        protected override bool IsAreaCompatibleWithDataType(ModbusArea area, DataValueType dataType)
        {
            // 线圈和离散输入只支持布尔类型
            if (area is ModbusArea.Coils or ModbusArea.DiscreteInputs)
            {
                return dataType == DataValueType.Boolean;
            }

            // 寄存器不支持位类型布尔（应该使用线圈）
            if (dataType == DataValueType.Boolean && IsBitType)
            {
                return false;
            }

            return true;
        }
    }
}
