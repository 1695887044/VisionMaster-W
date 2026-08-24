using Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;

namespace VisionMaster.Models
{
    /// <summary>
    /// 编译后的流程
    /// 包含执行引擎和插件查找表
    /// </summary>
    public class CompiledFlow
    {
        /// <summary>
        /// 根节点列表
        /// </summary>
        public List<CompiledNode> RootNodes { get; private set; }

        /// <summary>
        /// 插件查找表（步骤ID -> 插件实例）
        /// </summary>
        public Dictionary<Guid, IVisionPlugin> PluginLookup { get; private set; }

        /// <summary>
        /// 创建编译流程
        /// </summary>
        public CompiledFlow(List<CompiledNode> rootNodes, Dictionary<Guid, IVisionPlugin> lookup)
        {
            RootNodes = rootNodes;
            PluginLookup = lookup;
        }

        /// <summary>
        /// 执行流程
        /// 使用栈式遍历实现深度优先执行
        /// </summary>
        public void Run(IExecutionContext context)
        {
            if (RootNodes == null || RootNodes.Count == 0) return;

            var stack = new Stack<IEnumerator<CompiledNode>>();
            stack.Push(RootNodes.GetEnumerator());

            while (stack.Count > 0)
            {
                if (context.CancellationToken.IsCancellationRequested)
                    return;

                var currentEnumerator = stack.Peek();

                if (currentEnumerator.MoveNext())
                {
                    var currentNode = currentEnumerator.Current;

                    context.CurrentNodeId = currentNode.Id;

                    if (context.CancellationToken.IsCancellationRequested)
                        return;

                    var nextBranchToRun = currentNode.RunAndGetNext(context);

                    if (nextBranchToRun != null && nextBranchToRun.Count > 0)
                    {
                        stack.Push(nextBranchToRun.GetEnumerator());
                    }

                    if (context.CurrentFlowState == FlowControlState.Return)
                    {
                        return;
                    }
                }
                else
                {
                    stack.Pop();
                }
            }
        }
    }
}
