
using Core.Interfaces;
using DynamicExpresso;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.Models
{
    public class CompiledBranch
    {


        public Lambda ConditionLambda { get; set; }

        public new Dictionary<string, Type> VarTypes { get; set; } = new Dictionary<string, Type>();

        public List<string> LocalVarNames { get; set; } = new List<string>();

        public List<CompiledNode> ExecutionSteps { get; set; } = new List<CompiledNode>();
    }
}
