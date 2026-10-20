namespace Core.Interfaces
{
    /// <summary>
    /// 通讯变量快照（供「立刻读 / 立刻写」在发起 I/O 之前做本地校验与展示）。
    /// 只带寻址与展示需要的字段，不暴露宿主的 <c>NetworkVariableModel</c> 活对象。
    /// </summary>
    /// <param name="Name">变量名（写进流程的就是它）</param>
    /// <param name="ConnectionName">所属连接名</param>
    /// <param name="TypeName">声明类型短名（Int32 / Boolean / String…）</param>
    /// <param name="IsReadOnly">是否位于只读存储区（Modbus 离散输入 / S7 输入映像 I）</param>
    /// <param name="IsConnected">所属连接此刻是否已连接（快照时刻）</param>
    public readonly record struct CommVariableInfo(
        string Name,
        string ConnectionName,
        string TypeName,
        bool IsReadOnly,
        bool IsConnected);

    /// <summary>
    /// 通讯变量的运行期访问入口（「立刻读 / 立刻写」算子的能力契约）。
    ///
    /// 【为什么需要它】插件工程只引用 <c>Core.Interfaces</c>，物理上看不到
    /// <c>VisionMaster.Communications.ICommunicationManager</c>（它在 VM.Core），
    /// 更拿不到宿主的变量管理与连接对象。与 <see cref="IGlobalVariableWriter"/> /
    /// <see cref="ICameraProvider"/> 同一范式：把"按变量名立刻读写"收敛成一个能力对象，
    /// 由宿主实现并挂到 <see cref="IExecutionContext"/> 上。
    ///
    /// 【语义】三个方法都只做"本地查表 + 一次插队设备往返"：
    ///   · 变量不存在 / 不是通讯变量 / 缺连接或地址 / 连接离线 → 返回 false + 中文原因；
    ///   · 设备读写失败 / 插队队列满 → 返回 false + 中文原因；
    ///   · 成功时 <c>value</c> 是**工程值**（已按地址配置做 Scale/Offset 换算）。
    /// 禁止静默失败：调用方要能把 <c>error</c> 原样展示给操作员。
    /// </summary>
    public interface ICommVariableAccess
    {
        /// <summary>
        /// 查询变量快照（纯本地查表，不发起 I/O）。用于发起前的快速校验与展示。
        /// </summary>
        bool TryDescribe(string variableName, out CommVariableInfo info, out string? error);

        /// <summary>
        /// 插队读：优先于常规读写队列与轮询，立即从设备读取当前值。
        /// </summary>
        bool TryReadNow(string variableName, out object? value, out string? error);

        /// <summary>
        /// 插队写：优先于常规读写队列与轮询，立即把值写入设备。
        /// <paramref name="writtenValue"/> 是实际写入的工程值（文本已按声明类型解析）。
        /// </summary>
        bool TryWriteNow(string variableName, object? value, out object? writtenValue, out string? error);

        /// <summary>
        /// 只入队、不等待的插队写：成功返回 true（已进入插队写队列），调用方立即返回。
        /// <para>返回 true 只代表"已受理"，**不代表设备已写入**；设备晚到的失败走日志与
        /// <c>OnCommunicationError</c>，调用方无法再观测。要确认写入成功请用 <see cref="TryWriteNow"/>。</para>
        /// <para>失败返回 false + 中文原因（变量/连接/队列问题），此时命令未入队。</para>
        /// </summary>
        bool TryQueueWriteNow(string variableName, object? value, out object? writtenValue, out string? error);
    }

    /// <summary>
    /// <see cref="ICommVariableAccess"/> 的空实现：没有接入通讯层时使用。
    /// 与 <c>NullCameraProvider</c> / <c>NullMotionProvider</c> 同一口径——
    /// 调用方只判返回值，不必为"能力可能不存在"再写一层判空。
    /// </summary>
    public sealed class NullCommVariableAccess : ICommVariableAccess
    {
        public static readonly NullCommVariableAccess Instance = new();

        private const string NotAvailable = "当前执行环境没有接入通讯层，无法执行立刻读/写";

        public bool TryDescribe(string variableName, out CommVariableInfo info, out string? error)
        {
            info = default;
            error = NotAvailable;
            return false;
        }

        public bool TryReadNow(string variableName, out object? value, out string? error)
        {
            value = null;
            error = NotAvailable;
            return false;
        }

        public bool TryWriteNow(string variableName, object? value, out object? writtenValue, out string? error)
        {
            writtenValue = value;
            error = NotAvailable;
            return false;
        }

        public bool TryQueueWriteNow(string variableName, object? value, out object? writtenValue, out string? error)
        {
            writtenValue = value;
            error = NotAvailable;
            return false;
        }
    }
}
