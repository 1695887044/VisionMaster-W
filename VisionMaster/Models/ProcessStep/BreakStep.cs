using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.Models
{
    public class BreakStep : StepModel
    {
        public BreakStep(string icon, string pluginName, string typeName, string stepName = null) : base(icon, pluginName, typeName, stepName) { }
    }
    public class ContinueStep : StepModel
    {
        public ContinueStep(string icon, string pluginName, string typeName, string stepName = null) : base(icon, pluginName, typeName, stepName) { }
    }
    public class ReturnStep : StepModel
    {
        public ReturnStep(string icon, string pluginName, string typeName, string stepName = null) : base(icon, pluginName, typeName, stepName) { }
    }
}
