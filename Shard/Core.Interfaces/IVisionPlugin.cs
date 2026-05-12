using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Interfaces
{
    public enum PluginRunStatus
    {
        Idle,       // 未执行/空闲
        Running,    // 正在运行
        Success,    // 执行成功
        Error       // 执行失败
    }
    public interface IBranchPlugin : IVisionPlugin
    {
        Dictionary<string, List<IVisionPlugin>> Branches { get; }

        string SelectBranchKey(IExecutionContext context);
    }
    public abstract class BranchPluginBase : VisionPluginBase, IBranchPlugin
    {

        public Dictionary<string, List<IVisionPlugin>> Branches { get; } = new();

        public abstract string SelectBranchKey(IExecutionContext context);

        public override void RunAlgorithm(IExecutionContext context)
        {
            string targetBranchKey = SelectBranchKey(context);

            if (string.IsNullOrEmpty(targetBranchKey) || !Branches.TryGetValue(targetBranchKey, out var stepsToExecute))
            {
                return;
            }

            foreach (var step in stepsToExecute)
            {
                if (context.CancellationToken.IsCancellationRequested)
                {
                    break;
                }
                step.Execute(context);
            }
        }
    }
    public interface IVisionPlugin
    {
        
        static int InstanceCount { get; }
        string PluginID { get; set; }

        string InstanceName { get; set; }
        IReadOnlyDictionary<string, IInputPort> Inputs { get; }
        IReadOnlyDictionary<string, IOutputPort> Outputs { get; }

        bool Execute(IExecutionContext context);


        void Initialize();

        void Dispose();
    }
}
