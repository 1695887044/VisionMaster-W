using Core.Interfaces.Result;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VisionMaster.Services;

namespace VisionMaster.Models
{
    public class CompilationResult:Result<CompiledFlow>
    {
        public List<string> Errors { get; } = new();
    }
}
