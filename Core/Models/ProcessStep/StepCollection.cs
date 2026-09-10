using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.Models
{
    /// <summary>
    /// 步骤集合
    /// 用于管理条件分支或循环体中的步骤列表
    /// </summary>
    public class StepCollection : BindableBase
    {
        /// <summary>
        /// 步骤列表
        /// </summary>
        public ObservableCollection<StepModel> Steps { get; } = new ObservableCollection<StepModel>();

        /// <summary>
        /// 分支类型
        /// </summary>
        public BranchType BranchType
        {
            get => field;
            set => SetProperty(ref field, value);
        }

        /// <summary>
        /// 步骤名称
        /// </summary>
        public string StepName
        {
            get => field;
            set => SetProperty(ref field, value);
        }

        /// <summary>
        /// 条件表达式字符串
        /// 用户在界面上输入，后端解析执行
        /// 例如 "Score > 80"
        /// 对于 Else 和 Default 分支，此属性可以留空
        /// </summary>
        public string Expression
        {
            get => field;
            set
            {
                if (SetProperty(ref field, value))
                    RaisePropertyChanged(nameof(DisplayName)); // 表达式变化时同步更新显示名
            }
        }

        /// <summary>
        /// 显示名称（包含条件表达式）
        /// </summary>
        public string DisplayName
        {
            get
            {
                // 如果是 Else 或 Default，直接返回步骤名
                if (BranchType == BranchType.Else || BranchType == BranchType.Default)
                {
                    return StepName;
                }

                // 如果有表达式，拼接显示
                if (!string.IsNullOrWhiteSpace(Expression))
                {
                    return $"{StepName} [ {Expression} ]";
                }

                // 提示用户未绑定条件
                return $"{StepName} [ 未绑定条件 ]";
            }
        }
    }
}
