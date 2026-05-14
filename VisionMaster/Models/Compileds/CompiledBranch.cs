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
        /// 该分支的执行步骤列表
        /// </summary>
        public List<CompiledNode> ExecutionSteps { get; set; } = new();
    }
}
