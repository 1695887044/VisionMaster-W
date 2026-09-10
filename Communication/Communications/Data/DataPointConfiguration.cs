using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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

}
