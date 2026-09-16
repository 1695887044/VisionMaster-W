using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

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
            set
            {
                if (SetProperty(ref field, value))
                {
                    RaisePropertyChanged(nameof(RequiresExpression));
                    RaisePropertyChanged(nameof(IsConditionMissing));
                    RaisePropertyChanged(nameof(DisplayName));
                }
            }
        }

        /// <summary>
        /// 步骤名称
        /// </summary>
        public string StepName
        {
            get => field;
            set
            {
                if (SetProperty(ref field, value))
                {
                    // DisplayName 由 StepName 派生（分支卡片、画布泳道标题绑的都是 DisplayName），
                    // 只改分支名时若不补发通知，界面上会停在旧值——与 Expression/BranchType 的写法保持一致
                    RaisePropertyChanged(nameof(DisplayName));
                }
            }
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
                {
                    RaisePropertyChanged(nameof(IsConditionMissing)); // 表达式变化时同步刷新"缺条件"红灯判定
                    RaisePropertyChanged(nameof(DisplayName)); // 表达式变化时同步更新显示名
                }
            }
        }

        /// <summary>
        /// 该分支是否必须书写条件表达式。
        /// Else/Default（Else 分支与 For 循环体）由编译器强制视为 true，无需条件；
        /// If/ElseIf/Case/WhileLoop 都必须有条件，空条件在编译时是硬错误
        /// </summary>
        [JsonIgnore]
        public bool RequiresExpression =>
            BranchType != BranchType.Else && BranchType != BranchType.Default;

        /// <summary>
        /// 统一红灯判定标准：UI 红点、红字、弹窗灰条全部读这一个属性，
        /// 杜绝"文字说未绑定、灯却不红"或反过来的多头判定（P1-⑦ 病根）
        /// </summary>
        [JsonIgnore]
        public bool IsConditionMissing =>
            RequiresExpression && string.IsNullOrWhiteSpace(Expression);

        /// <summary>
        /// 显示名称（包含条件表达式）
        /// </summary>
        public string DisplayName
        {
            get
            {
                // 如果是 Else 或 For 循环体（无需条件），直接返回步骤名
                if (!RequiresExpression)
                {
                    return StepName;
                }

                // 如果有表达式，拼接显示
                if (!string.IsNullOrWhiteSpace(Expression))
                {
                    return $"{StepName} [ {Expression} ]";
                }

                // 提示用户未绑定条件（与 IsConditionMissing 红灯同一判定，Case/WhileLoop 同样生效）
                return $"{StepName} [ 未绑定条件 ]";
            }
        }
    }
}
