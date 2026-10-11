using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.Models
{
    /// <summary>
    /// 分支匹配（Case）容器步骤模型。
    ///
    /// 与 If 容器的关系：**复用整条 If 机器**——编译成同一个 CompiledIfNode（按序求值、首个命中即选中、
    /// 命中分支的步骤清单交给 RunSequence），只是每个分支的条件不是"用户手写的布尔表达式"，
    /// 而是由编译器用「判据 == 匹配值」合成出来的：
    ///   · <see cref="JudgeExpression"/>（判据）：写在容器上的**一个变量别名**（LocalVariables 别名
    ///     或 RuntimeVariableRefs 变量名），只写一次；
    ///   · 每条 Case 分支的 <see cref="StepCollection.Expression"/> 存的是**匹配值文本**（不是布尔表达式），
    ///     由编译器按判据声明类型归一后与判据比较；
    ///   · 兜底分支用 BranchType.Else（编译器恒真语义现成、灰身份色现成），必须排在最后。
    ///
    /// 为什么不另建编译节点：Case 的运行语义（首个命中即执行、不穿透、无命中则整容器空过）
    /// 与 CompiledIfNode 逐条同构，另起一套只会把 If 已经修过的坑（求值异常上抛、状态上报、
    /// 断桥注入、容器失败上浮）重踩一遍。
    ///
    /// 历史：Switch/Case 半成品曾于 2026-09-24 下线（Case 被当普通布尔表达式解析、没有判据值来源），
    /// 只保留 BranchType.Case / ModuleCommandAction.AddCase 两个枚举值占位；本类是"用判据+匹配值补全语义"
    /// 的正式实现。
    /// </summary>
    public class CaseStep : ConditionStep
    {
        /// <summary>
        /// 判据表达式：必须是本容器已声明的一个输入变量别名（LocalVariables 的 Name）
        /// 或运行时变量名（RuntimeVariableRefs 的 Name）。空 = 编译期硬错误。
        ///
        /// 语义属性：必须走 SetProperty —— FlowModel 靠属性名查 [RuntimeState] 排除名单，
        /// 不在名单 → Version++ → 触发重编译。用自动属性会静默不发通知，
        /// 改了判据跑的还是旧编译产物（与 ParallelStep.ExecutionMode 同一坑）。
        /// </summary>
        public string JudgeExpression
        {
            get => field;
            set => SetProperty(ref field, value);
        }

        /// <summary>
        /// 创建分支匹配容器。基类构造器会把 PluginTypeName 不含 "If" 的容器预建成单个"默认分支"，
        /// 这里像 WhileStep 一样清空重建：一条 Case 分支 + 一条 Else 兜底（兜底放最后）。
        /// </summary>
        public CaseStep(string icon, string pluginName, string pluginTypeName, string stepName = null)
            : base(icon, pluginName, pluginTypeName, stepName)
        {
            Children.Clear();
            Children.Add(new StepCollection { BranchType = BranchType.Case, StepName = "Case 1" });
            Children.Add(new StepCollection { BranchType = BranchType.Else, StepName = "默认" });
        }

        /// <summary>
        /// 在"Case N"里挑第一个未被占用的 N（用户可能改过分支名，按"当前条数 + 1"会撞名）。
        /// 与 ProcessViewModel.NextBranchName（并行分支）同一手法。
        /// </summary>
        public string NextCaseBranchName()
        {
            for (int n = 1; ; n++)
            {
                string candidate = $"Case {n}";
                if (!Children.Any(c => c.StepName == candidate))
                    return candidate;
            }
        }
    }
}
