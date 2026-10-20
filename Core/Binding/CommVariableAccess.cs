using Core.Interfaces;
using VisionMaster.Communications;
using VisionMaster.Models;
using VisionMaster.Services;

namespace VisionMaster.Binding
{
    /// <summary>
    /// <see cref="ICommVariableAccess"/> 的主程序侧实现：
    /// 按变量名解析到 <see cref="NetworkVariableModel"/>，再把读写委托给它。
    ///
    /// 【为什么变量级校验放在 Model 而不是这里】
    /// 地址换算、文本按声明类型解析、写租约、只读区判断、镜像同步——这些语义都属于"一个通讯变量"，
    /// 普通写（<c>TryWriteToValue</c>）与插队写（<c>TryWriteNowToValue</c>）必须共用同一份实现，
    /// 否则迟早出现"普通写能过、插队写过不了"的口径漂移。本类只做"按名查表 + 类型守门"。
    /// </summary>
    public sealed class CommVariableAccess : ICommVariableAccess
    {
        private readonly IWorkspaceManager _workspace;
        private readonly ICommunicationManager _manager;

        public CommVariableAccess(IWorkspaceManager workspace, ICommunicationManager manager)
        {
            _workspace = workspace;
            _manager = manager;
        }

        /// <inheritdoc />
        public bool TryDescribe(string variableName, out CommVariableInfo info, out string? error)
        {
            info = default;

            if (!TryResolve(variableName, out var variable, out error))
                return false;

            // 连接状态只读属性，不做 I/O；快照时刻的状态，运行期 Manager 还会再校验一次
            var connection = _manager.GetConnection(variable.ConnectionName!);
            info = new CommVariableInfo(
                variable.Name,
                variable.ConnectionName!,
                variable.DataType.Name,
                variable.IsReadOnlyArea,
                connection?.IsConnected == true);
            return true;
        }

        /// <inheritdoc />
        public bool TryReadNow(string variableName, out object? value, out string? error)
        {
            value = null;
            if (!TryResolve(variableName, out var variable, out error))
                return false;

            return variable.TryReadNow(out value, out error);
        }

        /// <inheritdoc />
        public bool TryWriteNow(string variableName, object? value,
                                out object? writtenValue, out string? error)
        {
            writtenValue = value;
            if (!TryResolve(variableName, out var variable, out error))
                return false;

            return variable.TryWriteNowToValue(value, out writtenValue, out error);
        }

        /// <inheritdoc />
        public bool TryQueueWriteNow(string variableName, object? value,
                                     out object? writtenValue, out string? error)
        {
            writtenValue = value;
            if (!TryResolve(variableName, out var variable, out error))
                return false;

            return variable.TryQueueWriteNowToValue(value, out writtenValue, out error);
        }

        /// <summary>
        /// 按名解析到通讯变量。四道门：名字非空 → 变量存在 → 是网络变量（不是运行时/系统变量）
        /// → 有连接名与地址。任何一道不过都给出可直接展示的中文原因。
        /// </summary>
        private bool TryResolve(string variableName, out NetworkVariableModel variable, out string? error)
        {
            variable = null!;
            error = null;

            var key = variableName?.Trim();
            if (string.IsNullOrEmpty(key))
            {
                error = "通讯变量名不能为空";
                return false;
            }

            if (_workspace == null)
            {
                error = "当前执行环境没有绑定工作区，无法访问通讯变量";
                return false;
            }

            var resolved = _workspace.VariableRegistry?.FindByName(key);
            if (resolved == null)
            {
                error = $"找不到变量「{key}」，请先在变量管理里确认";
                return false;
            }

            if (resolved is not NetworkVariableModel networkVariable)
            {
                error = $"「{key}」不是通讯变量（当前类型：{resolved.VariableType}）";
                return false;
            }

            if (string.IsNullOrWhiteSpace(networkVariable.ConnectionName))
            {
                error = $"变量「{key}」缺少连接名，无法定位设备";
                return false;
            }

            if (networkVariable.AddressConfig == null)
            {
                error = $"变量「{key}」未配置设备地址，无法读写";
                return false;
            }

            variable = networkVariable;
            return true;
        }
    }
}
