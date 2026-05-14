﻿﻿﻿using Core.Interfaces;
using System;
using System.Collections.Generic;

namespace VisionMaster.Models
{
    /// <summary>
    /// 编译节点基类
    /// 所有编译后节点的抽象基类
    /// </summary>
    public abstract class CompiledNode
    {
        /// <summary>
        /// 节点唯一标识
        /// </summary>
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// 节点名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 执行节点并获取下一个要执行的节点列表
        /// </summary>
        public abstract List<CompiledNode> RunAndGetNext(IExecutionContext context);
    }

    /// <summary>
    /// 编译后的插件节点
    /// 封装外部视觉插件的执行
    /// </summary>
    public class CompiledPluginNode : CompiledNode
    {
        /// <summary>
        /// 外部插件实例
        /// </summary>
        public IVisionPlugin ExternalPlugin { get; set; }

        /// <summary>
        /// 执行插件节点
        /// </summary>
        public override List<CompiledNode> RunAndGetNext(IExecutionContext context)
        {
            context.CurrentNodeId = Id;

            if (context.CancellationToken.IsCancellationRequested)
                return null;

            ExternalPlugin.Execute(context);

            return null;
        }
    }
}
