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
        object? DefaultValue { get; }

        public string Description { get; set; }
        void ResetToDefault();
    }
}