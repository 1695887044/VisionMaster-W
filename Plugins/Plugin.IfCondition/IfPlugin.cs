using Core.Interfaces;
using System.ComponentModel.DataAnnotations;

namespace Plugin.IfCondition
{
    [Display(Name = "If", GroupName = "逻辑控制", ShortName = "\uf059")]
    public class IfPlugin : BranchPluginBase
    {

       public  List<KeyValuePair<string, Func<IExecutionContext, bool>>> BranchEvaluators { get; } = new();
        public string ElseBranchKey { get; set; }

        public override void Dispose()
        {
           
        }

        public override void Initialize()
        {

        }
        public override string SelectBranchKey(IExecutionContext context)
        {
            foreach (var evaluator in BranchEvaluators)
            {
                if (evaluator.Value.Invoke(context))
                {
                    return evaluator.Key;
                }
            }

            if (!string.IsNullOrEmpty(ElseBranchKey))
            {
                return ElseBranchKey;
            }
            return null;
        }
    }
}
