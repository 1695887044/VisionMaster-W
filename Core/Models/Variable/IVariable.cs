using Core.Interfaces;
using VisionMaster.Communications;

namespace VisionMaster.Models
{
    public interface IVariable : IOutputPort
    {
        VariableType VariableType { get; }
        string? ConnectionName { get; }
        DeviceAddressBase? AddressConfig { get; }
        int PollIntervalMs { get; }
        /// <summary>
        /// 初始值。可写：变量管理弹窗"初始值"列经 VariableNode.DefaultValueText
        /// 按类型校验后写入（A1 修复链路）
        /// </summary>
        object? DefaultValue { get; set; }

        public string Description { get; set; }
        void ResetToDefault();
    }
}
