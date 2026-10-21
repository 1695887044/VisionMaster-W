using Core.Interfaces;
using VisionMaster.Communications;
using VisionMaster.Services;
using Prism.Mvvm;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Windows;
using VisionMaster.Helpers;

using Newtonsoft.Json;

namespace VisionMaster.Models
{
    public class NetworkVariableModel : BindableBase, IVariable, IVariableDisplaySource, IRenameableVariable, IWritableVariable, IArrayElementWritable
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
        /// 显示分组 Id（方案级 <see cref="SolutionModel.VariableGroups"/> 的引用；Guid.Empty = 未分组）。
        /// <para>只用于变量管理的归类与筛选，不参与采集、轮询与写入；改名不级联（按 Id 引用）。
        /// 与 <see cref="ScanGroup"/> 是两条正交的轴：一个管"多久读一次"，一个管"给谁看"。</para>
        /// </summary>
        public Guid GroupId { get; set; }

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

        private ElementConversion?[] _elementConversions = Array.Empty<ElementConversion?>();

        /// <summary>
        /// 数组逐元素工程量换算档案（下标与数组元素一一对应；null 项 = 该元素不换算）。
        ///
        /// <para><b>为什么挂在变量上而不是地址对象上</b>：<see cref="DeviceAddressBase"/> 上的
        /// Scale/Offset 是"整个变量共用一份"，表达不了"第 0 项温度、第 1 项压力"或逐路标定；
        /// 地址对象回答"怎么寻址"，元素档案回答"每个元素怎么解释"。</para>
        ///
        /// <para><b>为什么用数组 + Volatile</b>：轮询线程每次发布都会读它，UI 在弹窗里编辑后整体替换；
        /// 引用读写是原子的，轮询不会读到"一半新一半旧"的档案。长度允许短于数组长度
        /// （缺省项按不换算处理），但 UI 与持久化都会补齐到元素个数。</para>
        ///
        /// <para>地址对象上的 <see cref="DeviceAddressBase.EnableConversion"/> 对数组仍然禁用
        /// （那是标量的"整组统一换算"语义，与逐元素档案并存会双重换算）。</para>
        /// </summary>
        public ElementConversion?[] ElementConversions
        {
            get => Volatile.Read(ref _elementConversions);
            set => Volatile.Write(ref _elementConversions, value ?? Array.Empty<ElementConversion?>());
        }

        /// <summary>是否存在真正生效的逐元素换算（Scale/Offset 非 identity；只看数值元素）</summary>
        [JsonIgnore]
        public bool HasElementConversion
        {
            get
            {
                if (!DataType.IsArray || DataType == typeof(byte[])) return false;

                var conversions = ElementConversions;
                for (int i = 0; i < conversions.Length; i++)
                {
                    var conversion = conversions[i];
                    if (conversion != null && !conversion.IsIdentity) return true;
                }
                return false;
            }
        }

        /// <summary>
        /// 读路径的换算入口（由 <c>NetworkVariableBridge</c> 注入 <c>CommunicationVariable.EngineeringConverter</c>）。
        /// <para>标量走地址对象上的换算（改造前行为）；数组逐元素换算，数值元素返回
        /// <c>double[]</c>（工程值本来就是小数，与标量换算返回 double 同一口径）。</para>
        /// </summary>
        public object? ToEngineeringValue(object? rawValue)
        {
            if (rawValue is not Array array)
                return AddressConfig?.ConvertToEngineering(rawValue) ?? rawValue;

            if (!HasElementConversion) return rawValue;

            Type? elementType = DataType.GetElementType();
            if (elementType == null || !ArrayConversion.IsNumericElement(elementType)) return rawValue;

            var conversions = ElementConversions;
            var result = new double[array.Length];
            for (int i = 0; i < array.Length; i++)
            {
                var conversion = i < conversions.Length ? conversions[i] : null;
                if (!ArrayConversion.TryToEngineering(conversion, array.GetValue(i), elementType, out var value, out _)
                    || value is not double engineering)
                {
                    // 换算失败：该元素退回原始数值（读链路不能因为一条坏档案丢整组数据）
                    result[i] = TryToDouble(array.GetValue(i));
                    continue;
                }

                result[i] = engineering;
            }

            return result;
        }

        /// <summary>界面显示一个元素（带单位/小数位）；无档案时按不变文化原样显示</summary>
        public string FormatElement(int index, object? value)
        {
            var conversions = ElementConversions;
            var conversion = index >= 0 && index < conversions.Length ? conversions[index] : null;
            return ArrayConversion.Format(conversion, value);
        }

        private static double TryToDouble(object? value)
        {
            try
            {
                return value == null ? 0d : Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return 0d;
            }
        }

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
        /// 是否支持"只写修改项"（按元素地址逐项下发）。
        /// <para>判据直接问地址对象能不能造出元素地址：Modbus/S7 支持；MC/FINS 未实现；
        /// Modbus 的 byte[]、奇数宽度的 string[] 等会在这里返回 false——
        /// 不要另写一份"协议 → 支持"的静态表，那必然与 <c>TryCreateElementAddress</c> 漂移。</para>
        /// </summary>
        [JsonIgnore]
        public bool SupportsElementWrite
        {
            get
            {
                if (!DataType.IsArray || AddressConfig == null) return false;
                return AddressConfig.TryCreateElementAddress(0, out _, out _);
            }
        }

        /// <summary>
        /// 整组写入的超限预警（null = 未超限/未知协议）。
        /// <para>只做提示，不在这里拦截：Modbus 120 寄存器 / S7 约 110 字节是单请求上限，
        /// 超限时整组写会拿到一个协议错误；弹窗据此建议改用「只写修改项」。</para>
        /// </summary>
        public string? GetWholeGroupWriteWarning()
        {
            if (!DataType.IsArray || AddressConfig == null) return null;

            if (AddressConfig is DeviceAddressBase<ModbusArea> modbus)
            {
                if (modbus.Area is ModbusArea.Coils or ModbusArea.DiscreteInputs)
                {
                    return modbus.Length > 2000
                        ? $"整组 {modbus.Length} 位超过 Modbus 单次 2000 位上限，整组写入会失败；请改用「只写修改项」。"
                        : null;
                }

                int registers = (modbus.TotalBytes + 1) / 2;
                return registers > 120
                    ? $"整组 {registers} 个寄存器超过 Modbus 单次 120 个上限，整组写入会失败；请改用「只写修改项」。"
                    : null;
            }

            if (AddressConfig is DeviceAddressBase<S7Area>)
            {
                int bytes = AddressConfig.TotalBytes;
                return bytes > 110
                    ? $"整组 {bytes} 字节超过 S7 单次约 110 字节上限，整组写入可能失败；请改用「只写修改项」。"
                    : null;
            }

            return null;
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
        /// 只写数组的一个元素：按元素地址直接下发，不做"读整组→改一项→写回"。
        ///
        /// 为什么不能走整组写：整组写要求调用方先拿到最新镜像，镜像过期或并发写就会
        /// 覆盖别的元素；而模板/画面写 `Control[3]` 的语义本来就是"只动第 3 个点"。
        /// Modbus/S7 的地址类实现了 <see cref="DeviceAddressBase.TryCreateElementAddress"/>；
        /// 其它协议不支持时明确拒绝，不静默退回整组写。
        /// </summary>
        public bool TryWriteElement(int index, object? value, out string? error)
        {
            error = null;

            if (!DataType.IsArray)
            {
                error = $"变量 [{Name}] 不是数组，不能按元素写入";
                return false;
            }

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

            if (AddressConfig.EnableConversion)
            {
                error = $"变量 [{Name}] 是数组类型，暂不支持工程量换算（请关闭地址上的「启用转换」）";
                return false;
            }

            if (index < 0 || index >= AddressConfig.Length)
            {
                error = $"下标 {index} 超出数组 [{Name}] 的长度 {AddressConfig.Length}";
                return false;
            }

            if (!AddressConfig.TryCreateElementAddress(index, out var elementAddress, out var addressError)
                || elementAddress == null)
            {
                error = addressError ?? $"变量 [{Name}] 的地址不支持单元素写入";
                return false;
            }

            Type elementType = DataType.GetElementType() ?? typeof(object);

            if (!TryCoerceElement(value, elementType, out object? converted, out error))
                return false;

            // 逐元素换算：界面/SCADA 给的是工程值，设备要的是原始值。
            // 地址对象上的整组换算对数组是禁用的，所以这里只走元素档案；
            // byte[] 是原始块，不参与换算（即使有人手工塞了档案）。
            object? deviceElement = converted;
            var conversions = ElementConversions;
            var conversion = DataType != typeof(byte[]) && index < conversions.Length
                ? conversions[index]
                : null;
            if (!ArrayConversion.TryToRaw(conversion, converted, elementType, out deviceElement, out var conversionError))
            {
                error = $"变量 [{Name}] 的第 {index} 项换算失败：{conversionError}";
                return false;
            }

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
                var rawValue = elementAddress.ConvertToRaw(deviceElement);
                if (rawValue == null)
                {
                    error = $"变量 [{Name}] 的第 {index} 项无法转换为设备原始值（检查数据类型与地址配置）";
                    return false;
                }

                // string[] 的单个元素也要按"每元素固定字节数"编码成定长块，
                // 与整组写 TryEncodeStringArray 的分块规则一致；否则设备侧会把短串尾部旧字节留下。
                object? wireValue = rawValue;
                if (DataType == typeof(string[]) && wireValue is string text)
                {
                    if (!TryEncodeFixedString(text, AddressConfig.ElementByteLength, out var encoded, out var encodeError))
                    {
                        error = $"变量 [{Name}] 的第 {index} 项写入失败：{encodeError}";
                        return false;
                    }

                    wireValue = encoded;
                }

                CommunicationManager.WriteVariable(ConnectionName, elementAddress.Address, wireValue);

                // 设备已确认 → 更新镜像。克隆一份数组再改元素：引用变化才能触发 ValueChanged，
                // 直接改原数组会被 SetProperty 的同引用短路掉（画面不会刷新）。
                // 换算生效时镜像是 double[]（工程域），写入的 converted 是声明元素类型——按镜像元素类型转一次。
                if (Value is Array mirror)
                {
                    try
                    {
                        var copy = (Array)mirror.Clone();
                        copy.SetValue(ToMirrorElement(copy, converted), index);
                        UpdateMirrorValue(copy);
                    }
                    catch (Exception mirrorEx)
                    {
                        // 设备已经写成功：镜像更新失败不能反判成"写入失败"，下一拍轮询会刷新。
                        System.Diagnostics.Debug.WriteLine(
                            $"[NetworkVariable] 变量 {Name} 第 {index} 项写入成功但镜像更新失败：{mirrorEx.Message}");
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                error = $"变量 [{Name}] 的第 {index} 项写入失败：{ex.Message}";
                return false;
            }
        }

        /// <summary>元素值归一：文本按元素类型解析，已是目标类型直通，其余不变文化转换。</summary>
        private static bool TryCoerceElement(
            object? value, Type elementType, out object? converted, out string? error)
        {
            converted = null;
            error = null;

            if (value == null)
            {
                if (elementType.IsValueType && Nullable.GetUnderlyingType(elementType) == null)
                {
                    error = $"数组元素类型 {elementType.Name} 不接受空值";
                    return false;
                }

                return true;
            }

            if (elementType.IsInstanceOfType(value))
            {
                converted = value;
                return true;
            }

            if (value is string text)
                return VariableValueConverter.TryConvert(text, elementType, out converted, out error);

            try
            {
                converted = Convert.ChangeType(value, elementType, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception ex)
            {
                error = $"「{value}」无法转换为数组元素类型 {elementType.Name}：{ex.Message}";
                return false;
            }
        }

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

            // 镜像值（工程域）与设备值（原始域）从这一步开始分家：
            // 数组逐元素换算时，镜像是 double[]（与读路径同一表示），设备侧是反算后的声明元素类型。
            object? mirrorValue = value;
            object? deviceValue = value;

            // 数组写入：整组替换。长度必须与地址配置一致，元素逐个归一到声明类型；
            // 换算走变量上的逐元素档案（地址对象上的 EnableConversion 对数组仍然禁用）。
            if (DataType.IsArray)
            {
                if (AddressConfig.EnableConversion)
                {
                    error = $"变量 [{Name}] 是数组类型，不支持地址上的整组换算（请关闭「启用转换」，改用数组的逐元素换算）";
                    return false;
                }
                if (value is not Array inputArray)
                {
                    error = $"变量 [{Name}] 是数组类型，写入值必须是数组（收到 {value?.GetType().Name ?? "null"}）";
                    return false;
                }
                if (!TryNormalizeArray(inputArray, out var normalized, out var normalizeError))
                {
                    error = $"变量 [{Name}] 数组写入失败：{normalizeError}";
                    return false;
                }

                value = normalized;
                mirrorValue = ToMirrorArray(normalized);
                writtenValue = mirrorValue;

                if (!TryToRawArray(normalized, out var rawArray, out var conversionError))
                {
                    error = $"变量 [{Name}] 数组换算失败：{conversionError}";
                    return false;
                }
                deviceValue = rawArray;
            }
            // 字符串变量：非文本值按不变文化转成文本（避免把 int 当数值写到字符串区，
            // 那样 HslHelper 会按 4 字节整数下发，字符串区直接错位）。
            else if (DataType == typeof(string) && value is not null and not string)
            {
                value = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
                writtenValue = value;
                mirrorValue = value;
                deviceValue = value;
            }
            // 文本按声明类型解析一次（R34 宽进严出；只放宽文本）。
            // 界面能产出的只有文本，硬守门会让"给 Int32 变量填 21"永远失败。
            else if (value is string text)
            {
                if (!ValueTextCoercion.TryCoerce(text, DataType, out var coerced))
                {
                    error = $"变量 [{Name}] 的类型是 {DataType.Name}，值 [{text}] 无法按该类型解析";
                    return false;
                }
                value = coerced;
                writtenValue = coerced;
                mirrorValue = coerced;
                deviceValue = coerced;
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
                var rawValue = AddressConfig.ConvertToRaw(deviceValue);
                if (rawValue == null)
                {
                    error = $"变量 [{Name}] 的值无法转换为设备原始值（检查数据类型与地址配置）";
                    return false;
                }

                // 文本类按"定长字节块"下发：设备侧是固定宽度的字符串区，写短了尾部会保留旧字节。
                // 字符串标量用 Length（总字节数）；字符串数组用 ElementByteLength（每元素宽度）。
                object? wireValue = rawValue;
                if (DataType == typeof(string) && wireValue is string scalarText)
                {
                    if (!TryEncodeFixedString(scalarText, AddressConfig.Length, out var encoded, out var encodeError))
                    {
                        error = $"变量 [{Name}] 写入失败：{encodeError}";
                        return false;
                    }
                    wireValue = encoded;
                }
                else if (DataType == typeof(string[]) && wireValue is string[] stringArray)
                {
                    if (!TryEncodeStringArray(stringArray, AddressConfig.ElementByteLength, out var encoded, out var encodeError))
                    {
                        error = $"变量 [{Name}] 写入失败：{encodeError}";
                        return false;
                    }
                    wireValue = encoded;
                }

                if (urgent)
                {
                    if (waitForCompletion)
                    {
                        // 插队写（等待）：最高优先级车道，优先于插队读、常规读写与轮询
                        CommunicationManager.WriteVariableNow(ConnectionName, Name, wireValue);
                        UpdateMirrorValue(mirrorValue); // 设备已确认 → 镜像同步并通知 UI
                    }
                    else
                    {
                        // 插队写（只入队）：成功只代表"已受理"，不更新镜像——
                        // 命令还没执行，更新镜像等于向界面撒谎；等下一拍轮询读到设备新值再刷新。
                        if (!CommunicationManager.TryTriggerWriteNow(
                                ConnectionName, Name, wireValue, out var queueError))
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
                    CommunicationManager.WriteVariable(ConnectionName, AddressConfig.Address, wireValue);
                    UpdateMirrorValue(mirrorValue); // 写确认成功 → 镜像同步并通知 UI
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
        /// 把外部数组归一到变量声明的元素类型，并校验长度。
        /// <para>长度必须与地址配置一致：设备点位跨度是固定的，短写/长写都会覆盖邻居或留下旧值。</para>
        /// <para>元素转换走 <see cref="VariableValueConverter"/>（布尔认 1/0、数值不变文化），
        /// 与界面单值写入同一份规则，避免"界面能填、数组写不进"的口径漂移。</para>
        /// </summary>
        private bool TryNormalizeArray(Array input, out Array normalized, out string? error)
        {
            normalized = input;
            error = null;

            var elementType = DataType.GetElementType();
            if (elementType == null)
            {
                error = "数组元素类型无法确定";
                return false;
            }

            int expected = AddressConfig?.Length ?? 0;
            if (input.Length != expected)
            {
                error = $"长度 {input.Length} 与配置的{(DataType == typeof(byte[]) ? "字节数" : "元素个数")} {expected} 不一致";
                return false;
            }

            var result = Array.CreateInstance(elementType, input.Length);
            for (int i = 0; i < input.Length; i++)
            {
                object? item = input.GetValue(i);
                try
                {
                    object? converted;
                    if (elementType == typeof(string))
                    {
                        converted = Convert.ToString(item, CultureInfo.InvariantCulture) ?? string.Empty;
                    }
                    else if (item is null)
                    {
                        converted = elementType.IsValueType ? Activator.CreateInstance(elementType) : null;
                    }
                    else if (item is string text)
                    {
                        if (!VariableValueConverter.TryConvert(text, elementType, out converted, out var convertError))
                        {
                            error = $"第 {i} 项 [{text}] 无法转换为 {elementType.Name}：{convertError}";
                            return false;
                        }
                    }
                    else
                    {
                        converted = Convert.ChangeType(item, elementType, CultureInfo.InvariantCulture);
                    }

                    result.SetValue(converted, i);
                }
                catch (Exception ex)
                {
                    error = $"第 {i} 项 [{item}] 无法转换为 {elementType.Name}：{ex.Message}";
                    return false;
                }
            }

            normalized = result;
            return true;
        }

        /// <summary>
        /// 把"声明元素类型"的工程值数组转成镜像表示。
        /// <para>换算生效时读路径发布的是 <c>double[]</c>（工程值），写成功后镜像必须落到同一表示，
        /// 否则会出现"写一次镜像变 int[]、下一拍轮询又变 double[]"的类型翻转；
        /// 更重要的是 <see cref="Array.SetValue(object, int)"/> 不允许把 int 塞进 double[]
        /// （按元素写成功后更新镜像时会直接抛 InvalidCastException）。</para>
        /// </summary>
        private Array ToMirrorArray(Array normalized)
        {
            if (!HasElementConversion) return normalized;

            Type? elementType = DataType.GetElementType();
            if (elementType == null || !ArrayConversion.IsNumericElement(elementType)) return normalized;

            var result = new double[normalized.Length];
            for (int i = 0; i < normalized.Length; i++)
                result[i] = TryToDouble(normalized.GetValue(i));
            return result;
        }

        /// <summary>把声明元素类型的工程值转成镜像数组的元素类型（int → double[] 镜像时用）</summary>
        private static object? ToMirrorElement(Array mirror, object? value)
        {
            Type? mirrorElement = mirror.GetType().GetElementType();
            if (mirrorElement == null || value == null) return value;

            Type target = Nullable.GetUnderlyingType(mirrorElement) ?? mirrorElement;
            if (target.IsInstanceOfType(value)) return value;

            try
            {
                return Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
            }
            catch
            {
                return value;
            }
        }

        /// <summary>
        /// 工程值数组 → 设备原始值数组（逐元素反算）。
        /// <para>没有生效档案时原样返回（引用不变，不额外分配）；失败时给出"第几项 + 原因"，
        /// 由调用方在弹窗里标红对应行。</para>
        /// </summary>
        private bool TryToRawArray(Array engineering, out Array raw, out string? error)
        {
            raw = engineering;
            error = null;

            if (!HasElementConversion) return true;

            Type? elementType = DataType.GetElementType();
            if (elementType == null)
            {
                error = "数组元素类型无法确定";
                return false;
            }

            var conversions = ElementConversions;
            var result = Array.CreateInstance(elementType, engineering.Length);
            for (int i = 0; i < engineering.Length; i++)
            {
                var conversion = i < conversions.Length ? conversions[i] : null;
                if (!ArrayConversion.TryToRaw(conversion, engineering.GetValue(i), elementType, out var value, out var itemError))
                {
                    error = $"第 {i} 项：{itemError}";
                    return false;
                }

                result.SetValue(value, i);
            }

            raw = result;
            return true;
        }

        /// <summary>
        /// 把字符串编码成定长字节块（ASCII + 尾部补 0）。
        /// <para>设备侧字符串区是固定宽度：写短了尾部保留旧字节，读回来会粘上上次的残留；
        /// 写长了会覆盖相邻字段，所以超长必须明确失败。</para>
        /// </summary>
        private static bool TryEncodeFixedString(string text, int byteLength, out byte[] encoded, out string? error)
        {
            encoded = Array.Empty<byte>();
            error = null;

            if (byteLength < 1)
            {
                error = "字符串字节长度未配置（必须 >= 1）";
                return false;
            }

            byte[] bytes = Encoding.ASCII.GetBytes(text ?? string.Empty);
            if (bytes.Length > byteLength)
            {
                error = $"字符串长度 {bytes.Length} 字节超过配置的 {byteLength} 字节";
                return false;
            }

            encoded = new byte[byteLength];
            Array.Copy(bytes, encoded, bytes.Length);
            return true;
        }

        /// <summary>把字符串数组编码成"每元素固定宽度"的连续字节块（ASCII + 尾部补 0）。</summary>
        private static bool TryEncodeStringArray(string[] values, int elementByteLength, out byte[] encoded, out string? error)
        {
            encoded = Array.Empty<byte>();
            error = null;

            if (elementByteLength < 1)
            {
                error = "字符串数组未配置每元素字节数（必须 >= 1）";
                return false;
            }

            var buffer = new byte[elementByteLength * values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                byte[] bytes = Encoding.ASCII.GetBytes(values[i] ?? string.Empty);
                if (bytes.Length > elementByteLength)
                {
                    error = $"第 {i} 项长度 {bytes.Length} 字节超过每元素宽度 {elementByteLength} 字节";
                    return false;
                }
                Array.Copy(bytes, 0, buffer, i * elementByteLength, bytes.Length);
            }

            encoded = buffer;
            return true;
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

            // 数组只走显式整组写（TryWriteToValue / 立刻写）：Value setter 的隐式写不做整块覆盖，
            // 避免绑定/镜像赋值在不知情时把整段设备数据重写。
            if (DataType.IsArray) return;

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
            // 数组只读：恢复初始值既不写设备，也不该把镜像值改成基准（那会让界面显示一个设备上并不存在的值）
            if (DataType.IsArray) return;

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
