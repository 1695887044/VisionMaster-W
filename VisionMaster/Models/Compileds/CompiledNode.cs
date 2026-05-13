using Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.Models
{
    /// <summary>
    /// 虚拟机底层指令基类 (对外隐藏)
    /// </summary>
    public abstract class CompiledNode
    {
        // 虚拟机的核心问答机制：
        public abstract List<CompiledNode> RunAndGetNext(IExecutionContext context);
    }
    /// <summary>
    /// 包装外部第三方视觉算法的“执行指令”
    /// </summary>
    public class CompiledPluginNode : CompiledNode
    {
        public IVisionPlugin ExternalPlugin { get; set; }

        public override List<CompiledNode> RunAndGetNext(IExecutionContext context)
        {
            // 跑真正的图像算法
            ExternalPlugin.Execute(context);

            // 普通算子没有肚子里嵌套的子节点，直接返回 null，告诉虚拟机继续往下走
            return null;
        }
    }
}
