using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HalconDotNet;
using VisionMaster.Helpers;
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

        /// <inheritdoc />
        public bool TryResolve(ScadaVariableRef reference, out IScadaValueHandle? handle, out string? error)
        {
            handle = null;
            error = null;

            var variable = _registry.Resolve(reference.VariableId, reference.VariableName);

            if (variable == null)
            {
                error = $"找不到变量「{reference.DisplayText}」";
                return false;
            }

            // 标量引用：历史行为原样保留
            if (!reference.IsArrayElement)
            {
                handle = new VariableHandle(variable);
                return true;
            }

            if (!variable.DataType.IsArray)
            {
                error = $"变量「{variable.Name}」不是数组，不能按元素绑定";
                return false;
            }

            int length = GetArrayLength(variable);

            if (length < 0)
            {
                error = $"变量「{variable.Name}」的数组长度未知，无法按元素绑定";
                return false;
            }

            if (reference.ArrayIndex >= length)
            {
                error = $"下标 {reference.ArrayIndex} 超出数组「{variable.Name}」的长度 {length}";
                return false;
            }

            handle = new ArrayElementHandle(variable, reference.ArrayIndex);
            return true;
        }

        /// <summary>
        /// 取数组长度：本地变量看当前值/默认值里的数组，网络变量退回地址配置的 Length。
        /// 拿不到就返回 -1（"不知道"），由调用方给出可行动的原因，不猜一个 0。
        /// </summary>
        private static int GetArrayLength(IVariable variable)
        {
            if (variable.Value is Array current)
                return current.Length;

            if (variable.DefaultValue is Array fallback)
                return fallback.Length;

            if (variable.AddressConfig is { Length: > 0 } address)
                return address.Length;

            return -1;
        }

        /// <summary>
        /// 数组元素句柄：把"根变量 + 下标"包装成与标量变量同形的值通道。
        ///
        /// <b>身份为什么是确定性 Guid 而不是根 Id</b>：数据泵按句柄的 <c>VariableId</c> 建订阅键
        /// （<c>ScadaRuntimeBinder.KeyOf</c>），若所有元素共用根 Id，100 个元素会被合并成一份订阅，
        /// 每个控件拿到的都是同一个值。元素身份只用于运行态订阅键，不落盘——
        /// 落盘的是 <see cref="ScadaBinding.VariableId"/>（根 Id）+ <see cref="ScadaBinding.ArrayIndex"/>，
    /// 所以哈希算法将来要换也不会破坏已存的 .vms。
        /// </summary>
        private sealed class ArrayElementHandle : IScadaValueHandle
        {
            private readonly IVariable _variable;
            private readonly int _index;
            private readonly object _gate = new();

            /// <summary>上一次该元素的原始值；根变量事件是"整组变了"，只有本元素变了才转发</summary>
            private object? _lastRawValue;

            private EventHandler? _valueChanged;

            public ArrayElementHandle(IVariable variable, int index)
            {
                _variable = variable ?? throw new ArgumentNullException(nameof(variable));
                _index = index;
                _lastRawValue = ReadRawValue();
            }

            public Guid VariableId => ElementId(_variable.VariableId, _index);

            public string Name => $"{_variable.Name}[{_index.ToString(CultureInfo.InvariantCulture)}]";

            /// <summary>
            /// 元素的数据类型：有生效逐元素换算时是 <c>double</c>（工程域），
            /// 否则是声明元素类型。SCADA 文本写入按这个类型解析——写死声明类型（int）
            /// 会让 "105.1" 在到达 TryWriteElement 之前就被拒。
            /// </summary>
            public Type DataType => _variable is NetworkVariableModel network && network.HasConversionAt(_index)
                ? typeof(double)
                : _variable.DataType.GetElementType() ?? typeof(object);

            public object? Value
            {
                get
                {
                    var value = ReadRawValue();
                    return value is HImage image ? HalconImageHelper.ToBitmapSource(image) : value;
                }
            }

            /// <summary>
            /// 只在本元素的值真的变化时触发。根变量的 ValueChanged 是"整组变了"，
            /// 直接转发会让"元素 0 变化"给元素 1 的订阅续命——断线报警（Stale）永远等不到超时。
            /// </summary>
            public event EventHandler? ValueChanged
            {
                add
                {
                    lock (_gate)
                    {
                        if (_valueChanged == null)
                        {
                            // 订阅建立时以"此刻的值"为基线：句柄可能先于订阅被创建，
                            // 中间元素已经被写过；不刷新基线会把第一次无关的根事件当成变化。
                            _lastRawValue = ReadRawValue();
                            _variable.ValueChanged += OnRootValueChanged;
                        }

                        _valueChanged += value;
                    }
                }
                remove
                {
                    lock (_gate)
                    {
                        _valueChanged -= value;

                        if (_valueChanged == null)
                            _variable.ValueChanged -= OnRootValueChanged;
                    }
                }
            }

            private void OnRootValueChanged(object? sender, EventArgs e)
            {
                EventHandler? handler;

                lock (_gate)
                {
                    var raw = ReadRawValue();

                    if (Equals(raw, _lastRawValue))
                        return;

                    _lastRawValue = raw;
                    handler = _valueChanged;
                }

                handler?.Invoke(this, EventArgs.Empty);
            }

            private object? ReadRawValue()
            {
                if (_variable.Value is not Array array || _index < 0 || _index >= array.Length)
                    return null;

                return array.GetValue(_index);
            }

            public bool TryWrite(object? value, out string? error)
            {
                // 本地数组就地写；网络数组按元素地址写（TryWriteElement 不读改写、不碰邻居）。
                // 真正不能写的变量源由设计期的 SupportsElementWrite / IsReadOnlyArea 拦住，
                // 这里只按接口能力分派。
                if (_variable is IArrayElementWritable writable)
                    return writable.TryWriteElement(_index, value, out error);

                error = $"变量「{_variable.Name}」的数组元素只读（该变量源不支持单元素写入）";
                return false;
            }

            /// <summary>
            /// 元素订阅身份：同根同下标必得同 Guid，不同下标必得不同 Guid。
            /// 根 Id 为空（旧数据）时返回 Empty，让数据泵退回按名字建键。
            /// </summary>
            private static Guid ElementId(Guid rootId, int index)
            {
                if (rootId == Guid.Empty)
                    return Guid.Empty;

                string key = $"scada-array-v1|{rootId:N}|{index.ToString(CultureInfo.InvariantCulture)}";
                byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(key));

                Span<byte> bytes = stackalloc byte[16];
                hash.AsSpan(0, 16).CopyTo(bytes);
                return new Guid(bytes);
            }
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

            /// <summary>
            /// 取值出口，也是"Halcon 像素 → WPF 位图"唯一的转换落点。
            ///
            /// 图像变量（HImage）在这里就换成 <see cref="System.Windows.Media.ImageSource"/> 再往下走，
            /// 好处有三：图元库不必认识 HalconDotNet（保持画面层的干净边界）；数据泵的脏值暂存表
            /// 存的是已冻结的位图（可跨线程）；绑定到图元的换算链在
            /// ScadaValueConverter 的"值已是目标类型则直通"那一档就结束了，零额外开销。
            /// </summary>
            public object? Value
            {
                get
                {
                    var value = _variable.Value;
                    return value is HImage image ? HalconImageHelper.ToBitmapSource(image) : value;
                }
            }

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
