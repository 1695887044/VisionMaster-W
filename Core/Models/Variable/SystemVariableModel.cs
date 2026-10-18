using System;
using Core.Interfaces;
using Prism.Mvvm;
using VisionMaster.Communications;

namespace VisionMaster.Models
{
    /// <summary>
    /// 系统变量模型：**派生出来的只读量**（当前只有运动轴状态投影，见 <c>SystemVariableCatalog</c>）。
    ///
    /// 三条刻意的取舍
    /// ---------
    /// ① <b>不实现 <see cref="IWritableVariable"/></b>：写路径（<c>GlobalVariableWriter</c> /
    ///    <c>ScadaValueSource</c>）都按"实现该接口才允许写"判定，不实现它就会如实报
    ///    "不支持写入"——这正是系统变量要的语义，不必在写入器里加任何类型特判。
    /// ② <b>不实现 <see cref="IRenameableVariable"/></b>：名字由"轴名 + 字段"派生，
    ///    用户改名只会得到一条与轴脱钩的僵尸变量；轴改名走 <see cref="UpdateName"/> 由目录统一刷新，
    ///    <c>IVariableRegistry.TryRename</c> 因此会拒绝它（"变量类型 X 不允许改名"）。
    /// ③ <b>不落盘</b>：不进 <c>SolutionModel</c> / <c>VariableSnapshots</c>，
    ///    因此也不需要 <c>DataTypeString</c> 那套序列化往返；<see cref="DataType"/> 直接由构造注入。
    ///
    /// 身份（<see cref="VariableId"/>）是**确定性 Guid**（见 SystemVariableCatalog 的推导规则）：
    /// 同一根轴的同一个字段在任何机器、任何次启动都得到同一个 Id，
    /// 于是按 Id 寻址的连线/画面绑定在重启后仍然命中，不需要任何"登记"动作。
    /// </summary>
    public class SystemVariableModel : BindableBase, IVariable, IVariableDisplaySource
    {
        private string _name;
        private object? _value;

        /// <summary>
        /// 构造系统变量。
        /// </summary>
        /// <param name="variableId">确定性身份（由轴 Id + 字段名推导，调用方保证）</param>
        /// <param name="name">派生名字，形如 <c>Motion.X.Position</c></param>
        /// <param name="dataType">值的声明类型（决定下游绑定的编辑器与转换规则）</param>
        /// <param name="description">给操作员看的说明（含单位与"只读"提示）</param>
        public SystemVariableModel(Guid variableId, string name, Type dataType, string description)
        {
            VariableId = variableId;
            _name = name ?? string.Empty;
            DataType = dataType ?? typeof(object);
            Description = description ?? string.Empty;
        }

        /// <inheritdoc />
        public Guid VariableId { get; }

        /// <summary>
        /// 变量名（派生量，接口层只读）。
        /// 轴改名由目录调 <see cref="UpdateName"/> 统一刷新，用户不能直接改它 —— 理由见类注释 ②。
        /// </summary>
        public string Name => _name;

        /// <inheritdoc />
        public Type DataType { get; }

        /// <inheritdoc />
        public VariableType VariableType => VariableType.System;

        /// <summary>系统变量没有通信连接（接口要求，恒 null）</summary>
        public string? ConnectionName => null;

        /// <summary>系统变量没有设备地址（接口要求，恒 null）</summary>
        public DeviceAddressBase? AddressConfig => null;

        /// <summary>
        /// 初始值：接口要求可读写，但系统变量**不参与任何写回**——
        /// "恢复初始值"对派生量没有意义（它的值只由运动卡状态决定）。
        /// 这里仍做成可写属性，是为了让变量管理/属性网格这类按接口反射的界面不至于把它当只读异常处理；
        /// 写入不会影响 <see cref="Value"/>。
        /// </summary>
        public object? DefaultValue { get; set; }

        /// <inheritdoc />
        public string Description { get; set; }

        /// <summary>
        /// 当前值：**只读镜像**，只有 <see cref="UpdateMirrorValue"/> 能改它。
        /// 刻意不给 setter —— 只读语义必须在类型上就成立，而不是靠调用方自觉
        /// （<c>IOutputPort</c> 的 <c>Value</c> 要求可写，这里显式实现成抛 <see cref="NotSupportedException"/>，
        /// 与 <c>ConstantOutputPort</c> / <c>ArrayIndexProxyPort</c> 同一手法）。
        /// </summary>
        public object? Value => _value;

        /// <summary><see cref="IPort.Value"/> 的显式实现：读镜像，写一律拒绝（见 <see cref="Value"/>）</summary>
        object IPort.Value
        {
            get => _value!;
            set => throw new NotSupportedException($"系统变量「{_name}」是只读派生量，不支持写入");
        }

        private EventHandler? _valueChanged;

        /// <summary>值发生实质变化时触发（由 <see cref="UpdateMirrorValue"/> 统一发出）</summary>
        public event EventHandler ValueChanged
        {
            add => _valueChanged += value;
            remove => _valueChanged -= value;
        }

        /// <summary>
        /// 更新镜像值（发布器每拍调用）。
        ///
        /// 只有值真的变了才通知：100ms 一拍、一屏几十个绑定，按"每拍必通知"推会把 UI 线程刷满。
        /// <b>比较必须用 <see cref="object.Equals(object?, object?)"/>，不能用 <c>SetProperty</c></b>：
        /// 镜像字段声明成 <c>object?</c>，值类型会被装箱，而 <c>SetProperty</c> 走的是
        /// <c>EqualityComparer&lt;object&gt;.Default</c>（引用比较）——同一个位置每拍都是新箱子，
        /// 判定永远"变了"，等于每拍给所有订阅方推一次事件（正是要避免的事）。
        /// <c>object.Equals</c> 走值语义（Double.Equals / Boolean.Equals），同值即短路。
        /// </summary>
        internal void UpdateMirrorValue(object? value)
        {
            if (Equals(_value, value))
                return;

            _value = value;
            RaisePropertyChanged(nameof(Value));
            _valueChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 刷新派生名字（轴改名时由 <c>SystemVariableCatalog</c> 调用）。
        /// 必须是 <c>internal</c>：外部直接改名字会绕过目录的按名索引，留下指向旧名的死键。
        /// </summary>
        internal void UpdateName(string name)
        {
            var next = name ?? string.Empty;
            if (string.Equals(_name, next, StringComparison.Ordinal))
                return;

            _name = next;
            RaisePropertyChanged(nameof(Name));
        }

        /// <summary>
        /// 刷新说明文案（轴改名时与名字一起更新，否则清单里会写着"运动轴 X 的位置"而名字是 Y）。
        /// 与 <see cref="UpdateName"/> 同一理由：只许目录改，改完必须同步索引。
        /// </summary>
        internal void UpdateDescription(string description)
        {
            var next = description ?? string.Empty;
            if (string.Equals(Description, next, StringComparison.Ordinal))
                return;

            Description = next;
            RaisePropertyChanged(nameof(Description));
        }

        /// <summary>
        /// 恢复初始值：**空实现**。
        /// 系统变量是派生只读量（值只由运动卡状态决定），把它重置成 DefaultValue
        /// 只会造出一个"看起来被改了、下一拍又被发布器覆盖"的假动作，所以这里什么也不做。
        /// </summary>
        public void ResetToDefault()
        {
            // 有意为空：见方法注释（不做任何写回，也不改镜像值）
        }

        #region IVariableDisplaySource（HMI 画布组态用，显式实现避免与模型接口冲突）

        string IVariableDisplaySource.BindingKey => Name;

        string IVariableDisplaySource.DisplayLabel => string.IsNullOrEmpty(Description) ? Name : Description;

        Type IVariableDisplaySource.DataType => DataType;

        object? IVariableDisplaySource.Value => Value;

        /// <summary>来源徽章：与用户变量（"本地"/连接名）区分开，一眼看得出这是运行态派生量</summary>
        string IVariableDisplaySource.SourceLabel => "系统";

        /// <summary>恒只读：图元上的输入框据此禁用编辑（见 IVariableDisplaySource 契约）</summary>
        bool IVariableDisplaySource.IsReadOnly => true;

        /// <summary>
        /// 控件写回入口：**空实现**，与 <see cref="IVariableDisplaySource.IsReadOnly"/> = true 配套。
        /// 为什么不抛：这条路径可能被界面的双向绑定顺手调到（用户敲了一下又回弹），
        /// 抛异常会把一次"无害的无效输入"升级成未捕获异常（宿主没有全局兜底，UI 线程异常必退进程）；
        /// 真正的写操作走 <see cref="IWritableVariable"/>，那里根本不含本类型，会如实报"不支持写入"。
        /// </summary>
        void IVariableDisplaySource.WriteValue(object? newValue)
        {
            // 有意为空：只读来源，写请求丢弃（理由见方法注释）
        }

        #endregion
    }
}
