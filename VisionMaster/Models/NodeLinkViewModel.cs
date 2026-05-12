using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VisionMaster.Services;

namespace VisionMaster.Models
{
    public class NodeLinkViewModel
    {
        public string StepID { get; set; }
        public string DisplayName { get; set; }
        public string IconCode { get; set; }
        public IReadOnlyList<PortSchema> InputSchemas { get; set; }
        public IReadOnlyList<PortSchema> OutputSchemas { get; set; }
    }
}
