using Core.Interfaces;
using System;
using System.Globalization;
using VisionMaster.Helpers;
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
    ///    唯一放宽处是**文本**：配置界面只能产出文本，故按声明类型解析一次（见 TryCoerceText）；
    /// 3. 交给 <see cref="IWritableVariable.TryWrite"/> 真正落值——同值也要下发、失败必须带原因，
    ///    这正是该契约相对"直接写 Value setter"的价值所在。
    ///
    /// 为什么不做"变量不存在就自动创建"：插件在运行期悄悄改变量表，会让方案结构随运行结果漂移
    /// （跑一次多一个变量），而组态画面绑定的又是设计期那批名字，问题极难复现。
    /// 变量是设计期资产，运行期只能写，不能增。
    /// </summary>
    public sealed class GlobalVariableWriter : IGlobalVariableWriter, ISharedVariableAccess
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
                // 重名与"不存在"必须分开说：名字在多台设备上重复时按名寻址本身无解，
                // 报"找不到变量"会把用户引向"是不是被删了"的错误方向。
                if (VariableNameAmbiguity.TryExplain(_workspace.VariableRegistry, key, out var ambiguity))
                {
                    error = ambiguity;
                    return false;
                }

                error = $"找不到全局变量「{key}」，请先在变量管理里新建同名变量";
                return false;
            }

            return WriteResolved(variable, value, out error);
        }

        /// <inheritdoc />
        public bool TryRead(Guid variableId, string fallbackName, out object? value, out string? error)
        {
            value = null;

            // 写后读：会话里若有未提交的写，先冲刷再读——否则读到的是旧镜像（静默错值）。
            // 放在 Resolve 之前：变量不存在时也先把账上的写落地，避免"读失败把写一起吞掉"。
            WriteBatchSession.FlushCurrent();

            var variable = Resolve(variableId, fallbackName, out error);
            if (variable == null)
                return false;

            // IVariable 继承自 IOutputPort：当前值就是 Value（网络变量是通信线程镜像的那份）。
            value = variable.Value;
            return true;
        }

        /// <inheritdoc />
        public bool TryWrite(Guid variableId, string fallbackName, object? value, out string? error)
        {
            var variable = Resolve(variableId, fallbackName, out error);
            return variable != null && WriteResolved(variable, value, out error);
        }

        /// <summary>
        /// 按"Id 优先、名字兜底"解析变量（与 <c>VariableRegistry.ResolveGlobalLink</c> 同一口径）。
        /// Id 是权威寻址：变量改名后声明表仍能命中；名字兜底服务旧方案与手输。
        /// </summary>
        private IVariable? Resolve(Guid variableId, string fallbackName, out string? error)
        {
            error = null;

            if (_workspace == null)
            {
                error = "当前执行环境没有绑定工作区，无法访问共享变量";
                return null;
            }

            var registry = _workspace.VariableRegistry;
            IVariable? variable = variableId != Guid.Empty ? registry?.FindById(variableId) : null;
            if (variable == null && !string.IsNullOrWhiteSpace(fallbackName))
                variable = registry?.FindByName(fallbackName.Trim());

            if (variable == null)
            {
                // Id 没命中且名字重名：这条引用已经无法自愈（自愈要求"按名唯一命中"），
                // 必须把歧义讲清楚，否则用户只看到"找不到共享变量"而不知道同名有两个。
                if (VariableNameAmbiguity.TryExplain(registry, fallbackName, out var ambiguity))
                {
                    error = ambiguity;
                    return null;
                }

                var name = string.IsNullOrWhiteSpace(fallbackName) ? "（未记录名字）" : $"「{fallbackName.Trim()}」";
                error = $"找不到共享变量 {name}（Id={variableId}），请检查变量管理，或重新选择该声明";
            }

            return variable;
        }

        /// <summary>
        /// 类型守门 + 落值（按名写与按 Id 写共用，避免两套口径）。
        /// </summary>
        private static bool WriteResolved(IVariable variable, object? value, out string? error)
        {
            error = null;

            var declared = variable.DataType;
            if (value != null && declared != null && !declared.IsInstanceOfType(value))
            {
                // ② a 文本按声明类型解析一次。
                //
                // 为什么必须放宽这一步：配置界面上的「值」只能产出**文本**（端口是 object，
                // 手填的常量永远是 string），于是"给 Int32 变量填 21"走到这里必然是 string ——
                // 硬守门会让这类最普通的用法永远失败（真机 2026-10-10 反馈：
                // 「全局变量「OKCount」的类型是 Int32，无法写入 String」，界面上无解）。
                // 口径与运行时变量那条路（VariableAssignmentPlugin 的 Convert.ChangeType）一致：
                // 能转就转，转不了明确报错。只放宽文本 —— 类型化的值（double 塞进 int 变量）
                // 仍按原样报错，那多半是上游接线错了，静默截断会把错因推迟到很远的地方。
                if (!ValueTextCoercion.TryCoerce(value, declared, out var coerced))
                {
                    error = $"全局变量「{variable.Name}」的类型是 {declared.Name}，无法写入 {value.GetType().Name}（值 [{value}]）";
                    return false;
                }

                value = coerced;
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

            // 系统变量是运动轴状态的投影，刻意不实现 IWritableVariable（只读）。
            // 这里给一句能直接展示的专门说明，别让用户以为"这个变量坏了"。
            error = variable.VariableType == VariableType.System
                ? $"「{variable.Name}」是系统变量（只读），不能写入"
                : $"全局变量「{variable.Name}」不支持写入（该变量类型未实现可写契约）";
            return false;
        }

    }
}
