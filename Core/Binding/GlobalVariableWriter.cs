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
                // ② a 文本按声明类型解析一次。
                //
                // 为什么必须放宽这一步：配置界面上的「值」只能产出**文本**（端口是 object，
                // 手填的常量永远是 string），于是"给 Int32 变量填 21"走到这里必然是 string ——
                // 硬守门会让这类最普通的用法永远失败（真机 2026-10-10 反馈：
                // 「全局变量「OKCount」的类型是 Int32，无法写入 String」，界面上无解）。
                // 口径与运行时变量那条路（VariableAssignmentPlugin 的 Convert.ChangeType）一致：
                // 能转就转，转不了明确报错。只放宽文本 —— 类型化的值（double 塞进 int 变量）
                // 仍按原样报错，那多半是上游接线错了，静默截断会把错因推迟到很远的地方。
                if (!TryCoerceText(value, declared, out var coerced))
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

            error = $"全局变量「{variable.Name}」不支持写入（该变量类型未实现可写契约）";
            return false;
        }

        /// <summary>
        /// 把**文本**值按变量的声明类型解析一次。
        ///
        /// 只管文本：界面能产出的只有文本，而"文本→数值/布尔"正是用户写字面量的方式。
        /// 数组 / 枚举 / HImage 这类目标一律解析不了 → 返回 false，由调用方报"类型不匹配"。
        /// Nullable 先剥掉，否则 <c>Nullable&lt;int&gt;</c> 会让 ChangeType 直接抛。
        ///
        /// 两条口径：
        ///   · **不变区域性**（InvariantCulture）：本机的 zh-CN 与不变区域性在小数点上一致（`.`），
        ///     但显式写死才能保证换机器/换区域设置时同一个方案解析出同一个数；
        ///   · **数值不许带千分位分隔符**：默认的 Convert 会把 `2,5` 读成 `25` ——
        ///     用户想写 2.5 而系统静默写成 25，属于"应当报错却改了值"的一类，宁可让他改写法
        ///     （`1500` 写错成 `1,500` 会被拒，重打一遍就好）。
        /// </summary>
        private static bool TryCoerceText(object value, Type declared, out object? coerced)
        {
            coerced = value;
            if (value is not string raw)
                return false;

            var text = raw.Trim();
            var target = Nullable.GetUnderlyingType(declared) ?? declared;

            if (text.Length == 0)
                return false;

            if (TypeHelper.IsNumericType(target) && text.IndexOf(',') >= 0)
                return false;

            try
            {
                coerced = Convert.ChangeType(text, target, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                // 解析不了就交给调用方报错（错误信息里带上原值，用户才知道是哪个值被拒了）
                return false;
            }
        }
    }
}
