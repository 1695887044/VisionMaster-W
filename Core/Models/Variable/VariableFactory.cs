
using System;
using VisionMaster.Communications;

namespace VisionMaster.Models
{
    public static class VariableFactory
    {
        /// <summary>
        /// 创建本地变量。
        /// variableId 仅在"按方案快照还原变量"时传入（保持身份不变）；
        /// 常规新建留空即自动生成新身份。
        /// </summary>
        public static IVariable CreateLocal(string name, Type dataType, string description, object? defaultValue = null, Guid? variableId = null)
        {
            return new LocalVariableModel
            {
                Name = name,
                VariableId = variableId ?? Guid.NewGuid(),
                DataType = dataType,
                Description = description,
                DefaultValue = defaultValue,
                Value = defaultValue
            };
        }

        public static IVariable CreateNetwork(
            string name, 
            Type dataType, 
            string connectionName,
            DeviceAddressBase addressConfig,
            string description,
            object? defaultValue = null,
            Guid? variableId = null)
        {
            return new NetworkVariableModel
            {
                Name = name,
                VariableId = variableId ?? Guid.NewGuid(),
                DataType = dataType,
                ConnectionName = connectionName,
                AddressConfig = addressConfig,
                Description = description,
                DefaultValue = defaultValue,
                Value = defaultValue
            };
        }
    }
}
