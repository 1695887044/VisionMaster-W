
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

        public Dictionary<Guid, Type> VarTypes { get; set; } = new();
        public List<Guid> LocalVarIds { get; set; } = new();

        public List<CompiledNode> ExecutionSteps { get; set; } = new();
    }
}
