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
        /// 节点查找表（步骤ID -> 编译节点）
        /// </summary>
        public Dictionary<Guid, CompiledNode> NodeLookup { get; private set; }

        /// <summary>
        /// 节点依赖表（步骤ID -> 直接上游步骤ID列表）
        /// 由 FlowCompiler.LinkPorts 在接线成功时记录，用于试运行时按依赖顺序先执行上游链
        /// </summary>
        public Dictionary<Guid, List<Guid>> DependencyMap { get; private set; }

        /// <summary>
        /// 创建编译流程
        /// </summary>
        public CompiledFlow(
            List<CompiledNode> rootNodes,
            Dictionary<Guid, IVisionPlugin> lookup,
            Dictionary<Guid, CompiledNode> nodeLookup,
            Dictionary<Guid, List<Guid>> dependencyMap)
        {
            RootNodes = rootNodes;
            PluginLookup = lookup;
            NodeLookup = nodeLookup;
            DependencyMap = dependencyMap;
        }

        /// <summary>
        /// 执行流程
        /// 顶层序列直接复用 CompiledNode.RunSequence（全引擎唯一的序列执行器），
        /// 这样"顶层怎么跑"与"循环体/分支怎么跑"是同一套语义，If 换层级不再有行为差异（A1）
        /// </summary>
        public void Run(IExecutionContext context)
        {
            CompiledNode.RunSequence(RootNodes, context, yieldToControlFlow: false);
        }
    }
}
