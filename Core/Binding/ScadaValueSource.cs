using System;
using VisionMaster.Models;
using VisionMaster.Scada;

namespace VisionMaster.Binding
{
    /// <summary>
    /// <see cref="IScadaValueSource"/> 的默认实现：把变量注册表的双路解析接到组态运行态数据泵上。
    ///
    /// 为什么要有这一层薄适配（而不是让数据泵直接拿 IVariableRegistry）
    /// ---------
    /// 数据泵（ScadaRuntimeBinder）住在 WPF 侧、规则却属于运行态领域；
    /// 让它直接依赖 VM.Core 的注册表，等于把"变量怎么找"这件事钉死在数据泵里，
    /// 将来接 OPC 组订阅、历史回放就得改数据泵。这里把"怎么找"收进一个类，
    /// 数据泵只认 <see cref="IScadaValueHandle"/> 那六个成员。
    ///
    /// 寻址口径与流程连线、画面绑定完全一致：<b>Id 优先、名字兜底</b>（见 IVariableRegistry.Resolve）。
    /// </summary>
    public sealed class RegistryScadaValueSource : IScadaValueSource
    {
        private readonly IVariableRegistry _registry;

        public RegistryScadaValueSource(IVariableRegistry registry)
            => _registry = registry ?? throw new ArgumentNullException(nameof(registry));

        /// <inheritdoc />
        public bool TryResolve(Guid variableId, string? fallbackName, out IScadaValueHandle? handle)
        {
            var variable = _registry.Resolve(variableId, fallbackName);
            handle = variable == null ? null : new VariableHandle(variable);
            return handle != null;
        }

        /// <summary>
        /// 句柄实现：IVariable → IScadaValueHandle 的薄转发，自己不持有任何状态。
        /// 刻意不做句柄缓存——数据泵对每个变量只解析一次，多一层缓存只会多一处失效点。
        /// </summary>
        private sealed class VariableHandle : IScadaValueHandle
        {
            private readonly IVariable _variable;

            public VariableHandle(IVariable variable) => _variable = variable;

            public Guid VariableId => _variable.VariableId;

            public string Name => _variable.Name;

            public Type DataType => _variable.DataType;

            public object? Value => _variable.Value;

            /// <summary>
            /// 事件用 add/remove 直接转发到底层变量：句柄不自己维护订阅表，
            /// 于是"谁订阅了、订阅了几次"与底层 ValueChanged 完全一致，摘订阅不会留下幽灵回调。
            /// </summary>
            public event EventHandler? ValueChanged
            {
                add => _variable.ValueChanged += value;
                remove => _variable.ValueChanged -= value;
            }

            /// <inheritdoc />
            public bool TryWrite(object? value, out string? error)
            {
                // 显式写只认 IWritableVariable（命令语义：同值也下发、必须返回真实结果）。
                // 没实现它的变量（将来的只读变量、计算型变量）如实报错，不偷偷退回 Value setter——
                // 那会给出"写成功了"的假象，而设备侧什么都没收到。
                if (_variable is IWritableVariable writable)
                    return writable.TryWrite(value, out error);

                error = $"变量「{_variable.Name}」不支持写入（未实现可写契约）";
                return false;
            }
        }
    }
}
