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
        private bool _isRunning;

        /// <summary>
        /// 运行期标志：本分支（并行分支）是否正在执行。
        ///
        /// 由 <c>CompiledParallelNode.ExecuteBranch</c> 在扇出开始置 true、收尾 finally 复位——
        /// 只有真并发路径打标（顺序退化是平铺逐个跑，分支胶囊不打标，步骤自己会高亮；
        /// If/While/For 的分支胶囊同样不打标，本批只覆盖并行分支——用户反馈的是"并行分组
        /// 下面的分支/组头没有焦点显示"）。
        /// [JsonIgnore]：纯运行期，不落盘；SetProperty 发通知，流程栏分支胶囊的触发器
        /// 绑它点亮（分支线程跨线程发标量属性通知，WPF 自动编组，与 StepModel.State 同款）。
        /// </summary>
        [JsonIgnore]
        public bool IsRunning
        {
            get => _isRunning;
            set => SetProperty(ref _isRunning, value);
        }

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
        ///
        /// 【双语义契约】本字段的内容按所属 StepCollection 的 BranchType 分两种读法，
        /// 任何新消费方必须先确认拿到的是哪个分支类型的语义，不许想当然按表达式解析：
        ///  · If / ElseIf / WhileLoop 分支 = 布尔条件表达式——编译期由 FlowCompiler
        ///    CreateInterpreter().Parse 解析（约 FlowCompiler.cs:492-592 一带），空值/解析失败是硬编译错误；
        ///  · Case 分支 = 匹配值文本——不是表达式！CaseValueHelper.TryConvert 按判据类型转常量后
        ///    合成「判据 == 常量」（2026-10-11 Case 分支线加入此列）；
        ///  · Else / Default（含 For 循环体）= 恒真，编译器不消费（RequiresExpression=false，可留空）。
        /// 消费方名单：FlowCompiler.cs（解析/转换）、ConditionEditorViewModel（编辑草稿）、
        /// ProcessView.xaml 分支胶囊文案（经 DisplayName 拼接，R24——胶囊只认 DisplayName 一个来源）、
        /// FlowCanvasChecks 的 B0 参考解释器（LogicBruteforceChecks.EvalCond）。
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
