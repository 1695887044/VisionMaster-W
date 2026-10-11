using Core.Interfaces;
using DynamicExpresso;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.Models
{
    /// <summary>
    /// 编译后的分支节点
    /// 包含条件表达式和执行步骤列表
    /// </summary>
    public class CompiledBranch
    {
        /// <summary>
        /// 编译后的条件 Lambda 表达式
        /// </summary>
        public Lambda ConditionLambda { get; set; }

        /// <summary>
        /// 变量类型映射（变量ID -> 类型）
        /// </summary>
        public Dictionary<Guid, Type> VarTypes { get; set; } = new();

        /// <summary>
        /// 本地变量 ID 列表
        /// </summary>
        public List<Guid> LocalVarIds { get; set; } = new();

        /// <summary>
        /// 运行时变量名称列表（按 delegateParams 顺序）
        /// 与 RuntimeVarTypes 一一对应
        /// 运行时从 context.LocalVariables 按名取值
        /// </summary>
        public List<string> RuntimeVarNames { get; set; } = new();

        /// <summary>
        /// 运行时变量类型映射（变量名 -> 类型）
        /// </summary>
        public Dictionary<string, Type> RuntimeVarTypes { get; set; } = new();

        /// <summary>
        /// 该分支的执行步骤列表
        /// </summary>
        public List<CompiledNode> ExecutionSteps { get; set; } = new();

        /// <summary>
        /// 编译期注入的常量实参（Case 分支的匹配值，已按判据声明类型归一）。
        ///
        /// 非 null 时 BuildBranchArgs 把它追加在实参末尾——顺序必须与 FlowCompiler 合成
        /// 「判据 == 常量参数」表达式时追加参数的顺序严格一致（同 delegateParams 的既有约定）。
        /// 匹配值在编译期已做非空校验，所以这里的 null 只表示"本分支不是 Case 分支"。
        /// </summary>
        public object ConstantArgument { get; set; }
    }
}
