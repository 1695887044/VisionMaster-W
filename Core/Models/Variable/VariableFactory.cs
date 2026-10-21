
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
                // 数组必须给基准一份独立副本：DefaultValue 与 Value 指向同一个数组时，
                // 元素就地编辑（变量管理的 [i] 行）会同时改掉基准——"恢复初始值"再也回不到原值，
                // 而且逐元素比较会因 ReferenceEquals 直接判"未偏离"（2026-10-21 审查抓出）。
                DefaultValue = defaultValue is Array array ? array.Clone() : defaultValue,
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
