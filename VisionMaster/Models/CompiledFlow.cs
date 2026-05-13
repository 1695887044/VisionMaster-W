using Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VisionMaster.Models;

namespace VisionMaster.Services
{
    public class CompiledFlow
    {

        /// <summary>
        /// 指令集
        /// </summary>
        public List<CompiledNode> RootNodes { get; private set; }
        public Dictionary<Guid, IVisionPlugin> PluginLookup { get; private set; } // 全局找人字典
        public CompiledFlow(List<CompiledNode> rootNodes, Dictionary<Guid, IVisionPlugin> lookup)
        {
            RootNodes = rootNodes;
            PluginLookup = lookup;
        }
        public void Run(IExecutionContext context)
        {
            if (RootNodes == null || RootNodes.Count == 0) return;

            // 🌟 工业级标配：执行栈 (Execution Stack)
            // 用来记录当前执行到哪一层、哪一个节点了
            var stack = new Stack<IEnumerator<CompiledNode>>();
            stack.Push(RootNodes.GetEnumerator());

            while (stack.Count > 0)
            {
                var currentEnumerator = stack.Peek();

                // 如果当前这一层还有下一个节点
                if (currentEnumerator.MoveNext())
                {
                    var currentNode = currentEnumerator.Current;

                    // 1. 发送指令让节点执行，并向它索要“子流程”
                    var nextBranchToRun = currentNode.RunAndGetNext(context);

                    // 2. 如果这是一个 IF 节点，且它命中了一个分支，它会返回那个分支的指令集
                    if (nextBranchToRun != null && nextBranchToRun.Count > 0)
                    {
                        // 把子分支压入栈顶！下一次 while 循环就会优先跑这个子分支
                        stack.Push(nextBranchToRun.GetEnumerator());
                    }
                }
                else
                {
                    // 当前这一层（比如某个 If 分支里的代码）跑完了，把这一层从栈里弹出去，回到上一层继续
                    stack.Pop();
                }
            }
        }
    }
}
