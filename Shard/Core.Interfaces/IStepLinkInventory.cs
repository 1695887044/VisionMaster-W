using System.Collections.Generic;

namespace Core.Interfaces
{
    /// <summary>
    /// 步骤连线清单（能力接口，可选实现）。
    ///
    /// 为什么单独开一个接口、而不是往 <see cref="IStepConfigData"/> 加成员：
    /// 那个接口有 8 个实现者（StepModel + 7 个断言/冒烟夹具），加一个非默认成员会把
    /// 所有夹具一起拽进来改；而"能枚举自己身上挂了哪些连线键"目前只有需要清理
    /// 孤儿连线的插件用得上。与 ICameraProvider / IMotionProvider / IGlobalVariableWriter
    /// 同一范式：调用方用 <c>is IStepLinkInventory</c> 判能力，没有实现就跳过。
    /// </summary>
    public interface IStepLinkInventory
    {
        /// <summary>
        /// 当前挂在本步骤上的连线键快照（输入端口名 / 动态变量名）。
        /// 调用方只读遍历，增删仍走 <see cref="IStepConfigData.SetLink"/> / <see cref="IStepConfigData.RemoveLink"/>。
        /// </summary>
        IEnumerable<string> GetLinkedPortNames();
    }
}
