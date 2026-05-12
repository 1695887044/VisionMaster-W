using GongSolutions.Wpf.DragDrop;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows.Media;

namespace VisionMaster.Models
{
    public abstract class StepModel:BindableBase
    {
        public string StepID { get; set; } = Guid.NewGuid().ToString("N");

        public string Icon { get; init; }
        public string PluginName { get; set; }

        public string PluginTypeName { get; init; }


        public string StepName
        {
            get => field;
            set => SetProperty(ref field, value);
        }
        public string Description
        {
            get => field;
            set => SetProperty(ref field, value);
        }

        public int SortId { get; set; }

        public bool IsEnabled { get; set; } = true;

        public StepState State
        {
            get => field;
            set => SetProperty(ref field, value);
        }
        [JsonIgnore]
        public double ExecutionTimeMs
        {
            get => field;
            set => SetProperty(ref field, value);
        }
        public Dictionary<string, object> InputValues { get; set; } = new Dictionary<string, object>();
        public Dictionary<string, string> LinkedSources { get; set; } = new Dictionary<string, string>();

        public StepModel(string icon,string pluginName, string pluginTypeName, string stepName =null)
        {
            Icon=icon;
            PluginName=pluginName;
            this.PluginTypeName = pluginTypeName;
            StepName = stepName == null ? pluginName: stepName;
            StepID = StepName;
            Description = pluginName;
        }

    }
    public class ActionStep : StepModel
    {
        public ActionStep(string icon, string pluginName, string pluginTypeName, string stepName = null) : base(icon, pluginName, pluginTypeName, stepName)
        {

        }
    }

    public class StepCollection : BindableBase
    {
        // 1. 核心集合：存放拖入该分支下的所有子算子 (初始化，防空指针)
        public ObservableCollection<StepModel> Steps { get; } = new ObservableCollection<StepModel>();

        public BranchType BranchType
        {
            get => field;
            set => SetProperty(ref field, value);
        }
        public string StepName
        {
            get => field;
            set => SetProperty(ref field, value);
        }
        public string Expression
        {
            get => field;
            set => SetProperty(ref field, value);
        }

        public string DisplayName
        {
            get
            {
                // 如果是 Else 或 Default，它们没有条件表达式，直接原样返回
                if (BranchType == BranchType.Else || BranchType == BranchType.Default)
                {
                    return StepName;
                }

                // 如果用户写了表达式，就把表达式拼接上去，例如：ElseIf [ Score > 80 ]
                if (!string.IsNullOrWhiteSpace(Expression))
                {
                    return $"{StepName} [ {Expression} ]";
                }

                // 如果还没写表达式，就提示一下
                return $"{StepName} [ 未绑定条件 ]";
            }
        }
    }
    public class ConditionStep : StepModel
    {
        public ObservableCollection<StepCollection> Children { get; } = new();

        public ConditionStep(string icon, string pluginName, string pluginTypeName, string stepName = null)
            : base(icon, pluginName, pluginTypeName, stepName)
        {

            if (pluginTypeName.Contains("If")) // 假设你的 If 算子标识名包含 "If"
            {
                // 对于 If 算子，默认给它分配 "If" 和 "Else" 两个分支
                Children.Add(new StepCollection
                {
                    BranchType = BranchType.If,
                    StepName = "If 分支",
                    Expression = "" // 预留给用户填写的条件，比如 "Score > 80"
                });

                Children.Add(new StepCollection
                {
                    BranchType = BranchType.Else,
                    StepName = "Else 分支"
                });
            }
            else if (pluginTypeName.Contains("Switch")) // 预留给 Switch 算子
            {
                // 对于 Switch 算子，默认给一个 Case 分支
                Children.Add(new StepCollection
                {
                    BranchType = BranchType.Case,
                    StepName = "Case 1",
                    Expression = ""
                });

                // 可以选择性地添加 Default 分支
                // Children.Add(new StepCollection { BranchType = BranchType.Default, StepName = "Default" });
            }
            else
            {
                // 兜底逻辑
                Children.Add(new StepCollection
                {
                    BranchType = BranchType.If,
                    StepName = "默认分支"
                });
            }
        }
    }
}
