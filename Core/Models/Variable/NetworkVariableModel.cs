using Core.Interfaces;
using VisionMaster.Communications;
using VisionMaster.Services;
using Prism.Mvvm;
using System.Windows;
using VisionMaster.Helpers;

using Newtonsoft.Json;

namespace VisionMaster.Models
{
    public class NetworkVariableModel : BindableBase, IVariable, IVariableDisplaySource, IRenameableVariable, IWritableVariable
    {
        private string _dataTypeString;
        private Type _dataType;
        private object? _value;

        [JsonIgnore]
        private ICommunicationManager? CommunicationManager;

        /// <summary>
        /// 绑定通信管理器（建网络变量后必须调用，否则 Value 读写不会触达设备）。
        /// 创建方负责在方案加载/变量新建时注入，保证镜像值与设备值联动
        /// </summary>
        public void Bind(ICommunicationManager manager) => CommunicationManager = manager;

        /// <summary>
        /// 轮询镜像更新：由通信管理器的轮询链路调用，仅更新本地镜像值并触发通知，
        /// 不走 Value setter（避免回写设备造成读写自激）
        /// </summary>
        public void UpdateMirrorValue(object? newValue)
        {
            if (SetProperty(ref _value, newValue))
                _valueChanged?.Invoke(this, EventArgs.Empty);
        }

        private VariableQuality _quality = VariableQuality.Bad;

        /// <summary>
        /// 质量戳镜像（仅供 UI 展示采集健康度，不参与业务逻辑）：
        /// Good=最近读成功 / Uncertain=最近读失败（当前显示的是旧值）/ Bad=断线或从未读到。
        /// 由通信层经 CommunicationVariable.QualityCallback 推送，仅在质量切换时触发（幂等）
        /// </summary>
        [JsonIgnore]
        public VariableQuality Quality
        {
            get => _quality;
            private set => SetProperty(ref _quality, value);
        }

        /// <summary>通信层质量推送入口（NetworkVariableBridge 接线）</summary>
        public void UpdateQuality(VariableQuality quality) => Quality = quality;

        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 变量稳定身份（GUID）：不随改名/换连接变化，供连线/监视项/SCADA 绑定寻址。
        /// 默认为新 GUID；可写仅用于反序列化与旧方案迁移（历史数据无此字段时由持久化层补发）
        /// </summary>
        public Guid VariableId { get; set; } = Guid.NewGuid();
        public VariableType VariableType => VariableType.Communication;
        public string? ConnectionName { get; set; }
        public DeviceAddressBase? AddressConfig { get; set; }

        /// <summary>
        /// 所属扫描组名（对标 KEPServerEX 的 Scan Class）。空 = 默认组，周期取连接的 ReadCycleMs。
        /// 组表定义在连接配置（CommunicationConfig.ScanGroups）上，此处只存组名引用——
        /// 改周期只需改一处（组表），不必逐变量改。
        /// </summary>
        public string ScanGroup { get; set; } = string.Empty;

        /// <summary>
        /// <para>是否允许与相邻地址合并成一次区域读（默认 true，与改造前行为一致）。</para>
        /// <para><b>什么时候该关</b>：按"只读这一小块、别读邻居"的语义配点时（相邻地址属于别的系统、
        /// 读它会触发对方设备的副作用、或对方对越界敏感），关掉就只读本变量自己的跨度。</para>
        /// <para><b>关掉不等于退回单点反射读</b>：仍走段读（自己的跨度一次读完、按结构化解码），
        /// 只是不跟邻居合并——字节序、类型解码、坏段健康统计全都照旧。</para>
        /// <para>改它之后需要重注册轮询（<c>NetworkVariableBridge.ReRegister</c>），
        /// 因为该开关在编译轮询计划时被抄进段划分。</para>
        /// </summary>
        public bool MergeAdjacentRead { get; set; } = true;

        public string DataTypeString
        {
            get => _dataTypeString ?? TypeCache.GetTypeKey(DataType);
            set
            {
                _dataTypeString = value;
                _dataType = TypeCache.GetType(value);
            }
        }

        [JsonIgnore]
        public Type DataType
        {
            get
            {
                if (_dataType == null && !string.IsNullOrEmpty(_dataTypeString))
                {
                    _dataType = TypeCache.GetType(_dataTypeString);
                }
                return _dataType ?? typeof(string);
            }
            set
            {
                _dataType = value;
                _dataTypeString = TypeCache.GetTypeKey(value);
            }
        }

        public string Description { get; set; } = string.Empty;
        public object? DefaultValue { get; set; }

        /// <summary>
        /// <para>是否允许被多个写入者共享（默认 false = 严格单写者）。</para>
        /// <para>为什么需要它：并行流程的两个分支同时写同一个输出是现实场景（一个伸出、一个缩回），
        /// 没有租约就是"最后写入者赢"，表现为设备偶尔动作错、且无法复现。
        /// 真正"本来就该共享"的点（如公共复位位）显式勾上即可。</para>
        /// </summary>
        public bool AllowSharedWrite { get; set; }

        /// <summary>
        /// 写租约超时（毫秒）；0 = 用默认 30 秒。
        /// <para>超时回收防止分支异常退出后永久占用。</para>
        /// </summary>
        public int LeaseTimeoutMs { get; set; }

        /// <summary>
        /// 写租约注册表（由宿主在方案加载/变量重建时注入；null = 不做互斥，行为与改造前一致）。
        /// </summary>
        [JsonIgnore]
        public WriteLeaseRegistry? LeaseRegistry { get; set; }

        /// <summary>
        /// 本变量的写租约键：<c>{连接名}.{变量名}</c>。
        /// <para>用字符串而不是对象引用：改名后键跟着变（改名本身已走注销+重注册），
        /// 且不同连接的同名变量天然分开。</para>
        /// </summary>
        [JsonIgnore]
        public string LeaseKey => $"{ConnectionName}.{Name}";

        public object? Value
        {
            get => _value; // 轮询镜像值：设备同步由通信管理器轮询链路负责，getter 不再直读设备（避免绑定高频同步 IO）
            set
            {
                if (SetProperty(ref _value, value))
                {
                    WriteToDevice(value);
                }
            }
        }

        /// <summary>
        /// 只读存储区（Modbus 离散输入 / S7 输入映像 I）。
        /// <para>写链路必须在发往设备之前拦住：这两类区在协议层就是只读的，
        /// 发下去只会换回一个协议错误，现场看到的是"写了但设备没动"。</para>
        /// </summary>
        public bool IsReadOnlyArea
        {
            get
            {
                if (AddressConfig is DeviceAddressBase<ModbusArea> ma)
                    return ma.Area == ModbusArea.DiscreteInputs;
                if (AddressConfig is DeviceAddressBase<S7Area> sa)
                    return sa.Area == S7Area.I;
                return false;
            }
        }

        /// <summary>
        /// 显式写值到设备（工业写操作正门）：
        /// ① 无条件下发——不经过 Value setter，规避"镜像与目标值相同被 SetProperty 短路而实际没写"的陷阱
        ///   （设备侧可能被外部改回同值，"再写一次"是命令不是状态设置）；
        /// ② 返回真实结果——离线/未配置地址/写异常都给出明确原因，UI 必须按结果反馈，禁止无条件报成功。
        /// 写成功后同步镜像值（走 UpdateMirrorValue，不再二次下发）。
        /// </summary>
        public bool TryWriteToValue(object? value, out string? error)
            => TryWriteCore(value, urgent: false, waitForCompletion: true, out _, out error);

        /// <summary>
        /// 插队写（「立刻写」的等待模式）：与 <see cref="TryWriteToValue"/> 同一套校验/换算/租约，
        /// 只把传输换成最高优先级的插队写车道，并等设备返回。
        /// <para><paramref name="writtenValue"/> 是实际写入的工程值：文本会先按声明类型解析
        /// （如 Int32 变量的 "21" → 21），下游看到的就是真正下发的那个值。</para>
        /// </summary>
        public bool TryWriteNowToValue(object? value, out object? writtenValue, out string? error)
            => TryWriteCore(value, urgent: true, waitForCompletion: true, out writtenValue, out error);

        /// <summary>
        /// 插队写（「立刻写」的不等待模式）：只保证命令进入插队写队列，调用方立即返回。
        /// <para>成功时 <paramref name="writtenValue"/> 是已入队的工程值，但**设备尚未写入**；
        /// 晚到的设备失败只走日志/通讯错误事件，本方法无法再观测。
        /// 不更新镜像值：命令还没执行，更新镜像等于向界面撒谎，等下一拍轮询刷新。</para>
        /// </summary>
        public bool TryQueueWriteNowToValue(object? value, out object? writtenValue, out string? error)
            => TryWriteCore(value, urgent: true, waitForCompletion: false, out writtenValue, out error);

        /// <summary>
        /// 普通写与插队写的共同核心：校验 → 只读区 → 文本解析 → 写租约 → 工程值转原始值 → 下发 → 同步镜像。
        /// <para>两条路径必须共用这一份：分开写两套必然出现"普通写能过、插队写过不了"的口径漂移。</para>
        /// </summary>
        private bool TryWriteCore(object? value, bool urgent, bool waitForCompletion,
                                  out object? writtenValue, out string? error)
        {
            writtenValue = value;
            error = null;

            if (AddressConfig == null)
            {
                error = $"变量 [{Name}] 未配置设备地址，无法写入";
                return false;
            }
            if (CommunicationManager == null)
            {
                error = $"变量 [{Name}] 未绑定通信管理器（连接可能已删除，请在变量管理中重新挂接）";
                return false;
            }
            if (string.IsNullOrWhiteSpace(ConnectionName))
            {
                error = $"变量 [{Name}] 缺少连接名，无法定位设备";
                return false;
            }

            // 离线预检：只读连接状态（拿裸连接读状态是允许的，做 I/O 不允许——I/O 必须走 Manager）
            var connection = CommunicationManager.GetConnection(ConnectionName);
            if (connection == null || !connection.IsConnected)
            {
                error = $"连接 [{ConnectionName}] 离线，变量 [{Name}] 未写入设备";
                return false;
            }

            if (IsReadOnlyArea)
            {
                error = $"变量 [{Name}] 位于只读存储区（离散输入 / S7 输入映像 I），不能写入";
                return false;
            }

            // 文本按声明类型解析一次（R34 宽进严出；只放宽文本）。
            // 界面能产出的只有文本，硬守门会让"给 Int32 变量填 21"永远失败。
            if (value is string text)
            {
                if (!ValueTextCoercion.TryCoerce(text, DataType, out var coerced))
                {
                    error = $"变量 [{Name}] 的类型是 {DataType.Name}，值 [{text}] 无法按该类型解析";
                    return false;
                }
                value = coerced;
                writtenValue = coerced;
            }

            // 写租约：并行分支抢同一个输出时明确拒绝，而不是"最后写入者赢"。
            // owner 取当前流程/步骤（WriteScope），流程外的手动写用独立的 manual owner。
            string owner = WriteScope.CurrentOwner?.Trim() is { Length: > 0 } flowOwner
                ? flowOwner
                : WriteLeaseRegistry.ManualOwner;

            var leases = LeaseRegistry;
            if (leases != null && !AllowSharedWrite)
            {
                int leaseTimeout = LeaseTimeoutMs > 0 ? LeaseTimeoutMs : 30000;
                if (!leases.TryAcquire(LeaseKey, owner, leaseTimeout, allowShared: false, out var leaseError))
                {
                    error = $"写入被拒绝：{leaseError}";
                    return false;
                }
            }

            try
            {
                var rawValue = AddressConfig.ConvertToRaw(value);
                if (rawValue == null)
                {
                    error = $"变量 [{Name}] 的值无法转换为设备原始值（检查数据类型与地址配置）";
                    return false;
                }

                if (urgent)
                {
                    if (waitForCompletion)
                    {
                        // 插队写（等待）：最高优先级车道，优先于插队读、常规读写与轮询
                        CommunicationManager.WriteVariableNow(ConnectionName, Name, rawValue);
                        UpdateMirrorValue(value); // 设备已确认 → 镜像同步并通知 UI
                    }
                    else
                    {
                        // 插队写（只入队）：成功只代表"已受理"，不更新镜像——
                        // 命令还没执行，更新镜像等于向界面撒谎；等下一拍轮询读到设备新值再刷新。
                        if (!CommunicationManager.TryTriggerWriteNow(
                                ConnectionName, Name, rawValue, out var queueError))
                        {
                            error = string.IsNullOrWhiteSpace(queueError)
                                ? $"变量 [{Name}] 插队写入队失败"
                                : queueError!;
                            return false;
                        }
                    }
                }
                else
                {
                    // 经 Manager → 连接专属线程下发：与轮询、其他写命令在同一条线程上串行执行
                    CommunicationManager.WriteVariable(ConnectionName, AddressConfig.Address, rawValue);
                    UpdateMirrorValue(value); // 写确认成功 → 镜像同步并通知 UI
                }

                return true;
            }
            catch (Exception ex)
            {
                error = $"变量 [{Name}] 写入失败：{ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// 插队读（「立刻读」步骤用）：优先于常规读写队列与轮询，返回工程值。
        /// <para>读成功后由 Manager 的 <c>UpdateValue</c> 推回镜像（本方法不再重复写），
        /// 因此变量管理 / 组态画面看到的就是这次读到的值。</para>
        /// </summary>
        public bool TryReadNow(out object? value, out string? error)
        {
            value = null;
            error = null;

            if (AddressConfig == null)
            {
                error = $"变量 [{Name}] 未配置设备地址，无法读取";
                return false;
            }
            if (CommunicationManager == null)
            {
                error = $"变量 [{Name}] 未绑定通信管理器（连接可能已删除，请在变量管理中重新挂接）";
                return false;
            }
            if (string.IsNullOrWhiteSpace(ConnectionName))
            {
                error = $"变量 [{Name}] 缺少连接名，无法定位设备";
                return false;
            }

            var connection = CommunicationManager.GetConnection(ConnectionName);
            if (connection == null || !connection.IsConnected)
            {
                error = $"连接 [{ConnectionName}] 离线，变量 [{Name}] 未读取";
                return false;
            }

            try
            {
                value = CommunicationManager.ReadVariableNow(ConnectionName, Name);
                return true;
            }
            catch (Exception ex)
            {
                error = $"变量 [{Name}] 立刻读失败：{ex.Message}";
                return false;
            }
        }

        [JsonIgnore]
        private EventHandler? _valueChanged;
        
        public event EventHandler ValueChanged
        {
            add => _valueChanged += value;
            remove => _valueChanged -= value;
        }

        private void WriteToDevice(object? value)
        {
            if (AddressConfig == null || CommunicationManager == null) return;

            var connection = CommunicationManager.GetConnection(ConnectionName);
            if (connection != null && connection.IsConnected)
            {
                try
                {
                    var rawValue = AddressConfig.ConvertToRaw(value);
                    if (rawValue != null)
                        CommunicationManager.WriteVariable(ConnectionName, AddressConfig.Address, rawValue);
                }
                catch (Exception ex)
                {
                    // setter 不是用户的显式命令，不弹窗打扰；但也不能像旧实现那样 catch {} 一吞了之——
                    // 写失败必须留痕，否则"赋值了却没进设备"永远查不出来
                    System.Diagnostics.Debug.WriteLine($"[NetworkVariable] 变量 {Name} 隐式写失败：{ex.Message}");
                }
            }
        }

        /// <summary>
        /// <see cref="IWritableVariable"/> 认领：直接转发到 <see cref="TryWriteToValue"/>。
        /// 不把 TryWriteToValue 直接提到接口上，是因为它是本模型的特有实现（要处理地址/连接/离线预检），
        /// 而接口只承诺"能显式写并给出结果"这一件事，具体变量源（将来的 SCADA 外部标签）各写各的。
        /// </summary>
        public bool TryWrite(object? value, out string? error) => TryWriteToValue(value, out error);

        /// <summary>
        /// 恢复初始值 = 一次"把默认值写进设备"的命令：
        /// 走 TryWriteToValue 无条件下发（镜像恰与默认值相同时旧实现会被 SetProperty 短路、设备根本没写）；
        /// 离线时退化为仅更新镜像，让 UI 至少反映用户意图
        /// </summary>
        public void ResetToDefault()
        {
            if (!TryWriteToValue(DefaultValue, out _))
                Value = DefaultValue;
        }

        #region IVariableDisplaySource（HMI 画布组态预留，显式实现避免与模型接口冲突）

        string IVariableDisplaySource.BindingKey => Name;

        string IVariableDisplaySource.DisplayLabel => string.IsNullOrEmpty(Description) ? Name : Description;

        Type IVariableDisplaySource.DataType => DataType;

        object? IVariableDisplaySource.Value => Value;

        string IVariableDisplaySource.SourceLabel => ConnectionName ?? "网络";

        /// <summary>只读存储区来源不可写（Modbus 离散输入 / S7 输入映像 I）</summary>
        bool IVariableDisplaySource.IsReadOnly => IsReadOnlyArea;

        void IVariableDisplaySource.WriteValue(object? newValue) => Value = newValue;

        #endregion
    }
}
