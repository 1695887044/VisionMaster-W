using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.Models
{
    public class StepCollection : BindableBase
    {
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
        /// <summary>
        /// 表达式字符串，例如 "Score > 80"，用户在界面上输入，后端解析执行。对于 Else 和 Default 分支，这个属性可以留空，因为它们不需要条件表达式。
        /// </summary>
        public string Expression
        {
            get => field;
            set
            {
                if (SetProperty(ref field, value))
                    RaisePropertyChanged(nameof(DisplayName)); // 🌟 核心：用户敲代码时，树节点瞬间同步更新！
            }
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
}
