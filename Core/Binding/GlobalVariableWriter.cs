using Core.Interfaces;
using VisionMaster.Models;
using VisionMaster.Services;

namespace VisionMaster.Binding
{
    /// <summary>
    /// 全局变量写入器：<see cref="IGlobalVariableWriter"/> 在主程序侧的实现。
    ///
    /// 职责只有三步，顺序不能调换：
    /// 1. 按名查变量（走 <see cref="IVariableRegistry.FindByName"/>，与连线/监视项同一套索引，
    ///    不自行遍历变量集合，避免出现"一个索引知道新变量、另一个不知道"的分裂）；
    /// 2. 类型守门：值的类型必须能赋给变量的声明类型。
    ///    为什么要挡：变量池的类型是下游所有消费方的契约，double 变量被塞进 HImage 之后，
    ///    出错点会漂移到很远的地方（取值方强转失败），最难查；
    /// 3. 交给 <see cref="IWritableVariable.TryWrite"/> 真正落值——同值也要下发、失败必须带原因，
    ///    这正是该契约相对"直接写 Value setter"的价值所在。
    ///
    /// 为什么不做"变量不存在就自动创建"：插件在运行期悄悄改变量表，会让方案结构随运行结果漂移
    /// （跑一次多一个变量），而组态画面绑定的又是设计期那批名字，问题极难复现。
    /// 变量是设计期资产，运行期只能写，不能增。
    /// </summary>
    public sealed class GlobalVariableWriter : IGlobalVariableWriter
    {
        private readonly IWorkspaceManager _workspace;

        /// <summary>
        /// 构造函数。
        /// </summary>
        /// <param name="workspace">工作区管理器（提供全局变量集合与变量索引）。
        /// 允许为 null：容器注册用的简化执行上下文没有工作区，此时写入一律失败并给出中文原因。</param>
        public GlobalVariableWriter(IWorkspaceManager workspace)
        {
            _workspace = workspace;
        }

        /// <inheritdoc />
        public bool TryWrite(string name, object? value, out string? error)
        {
            error = null;

            var key = name?.Trim();
            if (string.IsNullOrEmpty(key))
            {
                error = "目标全局变量名不能为空";
                return false;
            }

            if (_workspace == null)
            {
                error = "当前执行环境没有绑定工作区，无法写入全局变量";
                return false;
            }

            var variable = _workspace.VariableRegistry?.FindByName(key);
            if (variable == null)
            {
                error = $"找不到全局变量「{key}」，请先在变量管理里新建同名变量";
                return false;
            }

            var declared = variable.DataType;
            if (value != null && declared != null && !declared.IsInstanceOfType(value))
            {
                error = $"全局变量「{variable.Name}」的类型是 {declared.Name}，无法写入 {value.GetType().Name}";
                return false;
            }

            if (variable is IWritableVariable writable)
            {
                if (writable.TryWrite(value, out var writeError))
                    return true;

                error = string.IsNullOrEmpty(writeError)
                    ? $"全局变量「{variable.Name}」写入失败"
                    : writeError;
                return false;
            }

            error = $"全局变量「{variable.Name}」不支持写入（该变量类型未实现可写契约）";
            return false;
        }
    }
}
