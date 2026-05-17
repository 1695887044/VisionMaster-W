using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace VisionMaster.Communications
{
    public class DataPointConfiguration
    {
        public string Name { get; set; } = "";
        public string ConnectionName { get; set; } = "";
        public string AddressType { get; set; } = "";
        public Dictionary<string, object> AddressProperties { get; set; } = new();

        public bool EnableConversion { get; set; }
        public double? Scale { get; set; }
        public double EngineeringOffset { get; set; }
        public string? Unit { get; set; }
        public int DecimalPlaces { get; set; }

        public bool EnableAlarm { get; set; }
        public Dictionary<string, object> AlarmProperties { get; set; } = new();

        public bool EnableHistory { get; set; }
        public int MaxHistorySize { get; set; } = 1000;
    }

    public class DataPointConfigurationManager
    {
        private readonly DataPointManager _dpManager;
        private readonly List<DataPointConfiguration> _configurations = new();

        public DataPointConfigurationManager(DataPointManager dpManager)
        {
            _dpManager = dpManager ?? throw new ArgumentNullException(nameof(dpManager));
        }

        public void AddConfiguration(DataPointConfiguration config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (string.IsNullOrWhiteSpace(config.Name)) throw new ArgumentException("Name cannot be empty");
            if (string.IsNullOrWhiteSpace(config.ConnectionName)) throw new ArgumentException("ConnectionName cannot be empty");

            _configurations.Add(config);
        }

        public void RemoveConfiguration(string name, string connectionName)
        {
            _configurations.RemoveAll(c => c.Name == name && c.ConnectionName == connectionName);
        }

        public async Task ExportToFileAsync(string filePath)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            var json = JsonSerializer.Serialize(_configurations, options);
            await File.WriteAllTextAsync(filePath, json);
        }

        public async Task ImportFromFileAsync(string filePath)
        {
            if (!File.Exists(filePath)) throw new FileNotFoundException("Configuration file not found", filePath);

            var json = await File.ReadAllTextAsync(filePath);
            var configs = JsonSerializer.Deserialize<List<DataPointConfiguration>>(json);

            if (configs != null)
            {
                _configurations.Clear();
                _configurations.AddRange(configs);
            }
        }

        public IReadOnlyList<DataPointConfiguration> GetAllConfigurations() => _configurations.AsReadOnly();

        public DataPointConfiguration? GetConfiguration(string name, string connectionName)
        {
            return _configurations.FirstOrDefault(c => c.Name == name && c.ConnectionName == connectionName);
        }

        public void Clear()
        {
            _configurations.Clear();
        }
    }
}
