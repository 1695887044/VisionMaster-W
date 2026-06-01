using VisionMaster.Communications;

namespace VisionMaster.Models
{
    public static class VariableFactory
    {
        public static IVariable CreateLocal(string name, Type dataType,string description, object? defaultValue = null)
        {
            return new LocalVariableModel
            {
                Name = name,
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
            int pollIntervalMs = 500)
        {
            return new NetworkVariableModel
            {
                Name = name,
                DataType = dataType,
                ConnectionName = connectionName,
                AddressConfig = addressConfig,
                Description = description,
                DefaultValue = defaultValue,
                Value = defaultValue,
                PollIntervalMs = pollIntervalMs
            };
        }
    }
}