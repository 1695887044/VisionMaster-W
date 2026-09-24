using System;
using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using UI.Attributes;

namespace VisionMaster.Communications
{
    [Serializable]
    public class CommunicationConfig : BindableBase
    {
        [SuperDisplay(Name = "连接名称", GroupPath = "1. 基本设置", Order = 1, ColSpan = 12)]
        [Required(ErrorMessage = "名称不能为空")]
        public string ConnectionName
        {
            get => field;
            set => SetProperty(ref field, value);
        } = $"Conn_{DateTime.Now:HHmmss}";

        [SuperDisplay(
            Name = "协议类型",
            GroupPath = "1. 基本设置",
            Order = 2,
            ColSpan = 12,
            RequireRefresh = true
        )]
        public CommunicationType Protocol
        {
            get { return field; }
            set
            {
                OnChanged(value);
                field = value;
                RaisePropertyChanged();
            }
        }

        private void OnChanged(CommunicationType communication)
        {
            // B3：本方法与下面的 TryCreateConfig 曾经各持一份"支持哪些协议"的清单，且互不一致——
            // 这里认 6 个（把 OmronFins / MitsubishiMc / OpcUa 也算了进来），而工厂只注册了 3 个。
            // 于是属性面板选中那三个时：配置类真实存在、参数能填、Validate 也通过，
            // 一路走到 ConnectionFactoryManager.CreateConnection 才抛 NotSupportedException。
            // 现在两份清单合一，未实现的协议在这里就拿到 false。
            //
            // 拿到 false 时**保持原 Config 不动**、更不抛异常，理由有两条：
            // ① 这里是属性面板双向绑定的写入路径，setter 抛异常会穿透成未处理异常；
            //    而该交互发生在"创建新通信"弹窗内部，调用方的 try/catch 根本拦不到
            //    （改造前下拉选 FreeProtocol 就是这样把程序干掉的）。
            // ② 置 null 会让属性面板的"底层链路参数"嵌套分组无处展开（框架对 null 嵌套对象的行为未验证），
            //    保留原配置更稳——反正 Validate() 会拦住它。
            if (TryCreateConfig(communication, out var config))
                Config = config;
        }
        [SuperDisplay(Name = "底层链路参数", GroupPath = "2. 链路配置", Order = 1, ColSpan = 12)]
        [PropertyItem(Type = typeof(System.Windows.Controls.Control))] // 告诉框架这是一个嵌套对象，向下解析
        public ConnectionConfigBase Config
        {
            get => field;
            set { SetProperty(ref field, value); }
        } = new ModbusTcpConfig();

        /// <summary>
        /// <para>默认扫描组的周期（ms）——也是改造前的"连接级轮询周期"。</para>
        /// <para>默认组永远存在、不可删除改名；未指定扫描组（或指向已删除的组）的变量一律进默认组。
        /// 故本字段是"默认组周期"的唯一真相源，组表里不重复存一份（见 <see cref="ScanGroups"/>）。</para>
        /// </summary>
        [SuperDisplay(Name = "默认组周期(ms)", GroupPath = "3. 运行调度", Order = 1, ColSpan = 6)]
        public int ReadCycleMs { get; set; } = 1000;

        /// <summary>
        /// <para>自定义扫描组表（**不含默认组**，见 <see cref="ScanGroupConfig"/>）。</para>
        /// <para>为什么默认组不落盘：① 老 JSON 反序列化后本表为空 → 全部变量走默认组 → 行为与改造前一致，零回归；
        /// ② 周期只有一份真相源（<see cref="ReadCycleMs"/>），不会出现"属性面板改了周期、组表没跟着改"的双写不一致。</para>
        /// <para>不加 [SuperDisplay]：属性网格只渲染带该特性的成员，组表改由专用编辑器（连接设置 → 操作列"扫描组"）管理。</para>
        /// </summary>
        public List<ScanGroupConfig> ScanGroups { get; set; } = new();

        /// <summary>自定义扫描组数量上限（不含默认组）。每多一组，组内变量变稀疏、段合并率略降，8 组是经验平衡点</summary>
        public const int MaxScanGroups = 8;

        [SuperDisplay(Name = "开机自启", GroupPath = "3. 运行调度", Order = 2, ColSpan = 6)]
        public bool AutoStart { get; set; } = true;

        [SuperDisplay(Name = "自动重连", GroupPath = "3. 运行调度", Order = 3, ColSpan = 6)]
        public bool AutoReconnect { get; set; } = true;

        [SuperDisplay(Name = "启用该连接", GroupPath = "3. 运行调度", Order = 4, ColSpan = 6)]
        public bool IsEnabled { get; set; } = true;

        [SuperDisplay(Name = "备注说明", GroupPath = "1. 基本设置", Order = 3, ColSpan = 12)]
        public string Description { get; set; } = string.Empty;

        [JsonIgnore]
        public DateTime CreatedTime { get; set; } = DateTime.Now;

        [JsonIgnore]
        public DateTime LastModifiedTime { get; set; } = DateTime.Now;

        [JsonIgnore]
        public DateTime LastConnectedTime { get; set; }

        private ConnectionState _state = ConnectionState.Disconnected;

        /// <summary>连接状态（带通知：连接/断开后 UI 状态徽标自动联动）</summary>
        [JsonIgnore]
        public ConnectionState State
        {
            get => _state;
            set => SetProperty(ref _state, value);
        }

        /// <summary>
        /// 「轮询」列的展示文案：默认组周期，有自定义组时追加「+N 组」，
        /// 让用户在连接列表上就能看出"这条连接不止一个节拍"。
        /// <para>为什么做成模型上的只读属性，而不是 XAML 里的 Converter：文案要同时看
        /// <see cref="ReadCycleMs"/> 和 <see cref="ScanGroups"/> 两个字段，而组表是整体替换的
        /// <see cref="List{T}"/>（不逐项通知），只能在"组表被换掉"的地方集中通知一次——
        /// 见 <see cref="NotifyScanGroupsChanged"/>。做成 Converter 就得用 MultiBinding，反而更绕。</para>
        /// </summary>
        [JsonIgnore]
        public string PollHint => ScanGroups is { Count: > 0 }
            ? $"默认 {ReadCycleMs} ms  +{ScanGroups.Count} 组"
            : $"默认 {ReadCycleMs} ms";

        /// <summary>组表或默认组周期被整体替换后，通知 UI 重算 <see cref="PollHint"/></summary>
        public void NotifyScanGroupsChanged() => RaisePropertyChanged(nameof(PollHint));

        private string? _lastError;

        /// <summary>
        /// 该连接最后一次通信故障的文案（连接管理「状态」列的胶囊 ToolTip 显示它）。
        /// <para>为什么不落盘：这是"此刻的事实"而不是配置——重启后进程内没有任何连接，
        /// 把上次运行残留的报错恢复出来只会误导用户去查一个已经不存在的问题。故与
        /// <see cref="State"/> 同属运行时态，标 <see cref="JsonIgnoreAttribute"/>，
        /// 也不参与 <see cref="Clone"/>/<see cref="CopyFrom"/>（编辑弹窗不该把实时故障清掉或带过来）。</para>
        /// <para>为什么必须发通知：胶囊上的 ⚠ 图标与 ToolTip 都绑在它身上，且它是"状态之外的第二条信息"
        /// （连接可能已经离线但还没报过错），不发通知就永远停在初始值。</para>
        /// </summary>
        [JsonIgnore]
        public string? LastError
        {
            get => _lastError;
            set
            {
                if (SetProperty(ref _lastError, value))
                    RaisePropertyChanged(nameof(HasError));
            }
        }

        /// <summary>是否有未清除的通信故障（XAML 里给 ⚠ 图标和 ToolTip 做显隐判断）</summary>
        [JsonIgnore]
        public bool HasError => !string.IsNullOrEmpty(_lastError);

        /// <summary>连上即视为故障已过去：清掉最后错误，让 ⚠ 消失</summary>
        public void ClearLastError()
        {
            if (_lastError != null)
                LastError = null;
        }

        public CommunicationConfig() { }

        public CommunicationConfig(CommunicationType protocol)
        {
            // B3：这里原本还有一行 `Config = CreateConfig(protocol);`，属于重复且有害——
            // Protocol 的 setter 内部 OnChanged() 已经按协议建好一份 Config 了；
            // 而 CreateConfig 造出来的配置**不赋 Type**，Type 落到默认值 0（= ModbusTcp），
            // 于是 CommunicationConfig(ModbusRtu) / (SiemensS7) 造出来的对象自相矛盾（Protocol 与 Config.Type 不符），
            // 一进 Validate 就被判"协议类型不匹配"。删掉后由 OnChanged 统一负责。
            Protocol = protocol;
            ConnectionName = $"Conn_{protocol}_{DateTime.Now:HHmmss}";
        }

        public CommunicationConfig(string connectionName, ConnectionConfigBase config)
        {
            ConnectionName = connectionName;
            Config = config ?? throw new ArgumentNullException(nameof(config));
            Protocol = config.Type;
        }

        /// <summary>
        /// 按协议造一份默认链路配置（"支持哪些协议"的**唯一真相源**）。
        /// <para>返回 <c>false</c> = 该协议尚未实现（没有对应的连接实现类），<paramref name="config"/> 为 null，
        /// **不抛异常**——调用方（属性面板写入路径）要的是"安静地拒绝"，把话说清楚的任务交给
        /// <see cref="Validate"/>（见 <see cref="CommunicationProtocols"/>）。</para>
        /// <para>⚠ 必须显式赋 <see cref="ConnectionConfigBase.Type"/>：各 Config 子类都没有覆写它，
        /// 而它的默认值是 0（= <see cref="CommunicationType.ModbusTcp"/>）——
        /// 漏赋会让串口 / S7 的配置自报为 ModbusTcp，被 <see cref="Validate"/> 判为"协议类型不匹配"。</para>
        /// </summary>
        private static bool TryCreateConfig(CommunicationType protocol, [NotNullWhen(true)] out ConnectionConfigBase? config)
        {
            config = protocol switch
            {
                CommunicationType.ModbusTcp => new ModbusTcpConfig { Type = protocol },
                CommunicationType.ModbusRtu => new SerialConfig { Type = protocol },
                CommunicationType.SiemensS7 => new SiemensS7Config { Type = protocol },
                _ => null,
            };
            return config != null;
        }

        public bool Validate(out string errorMessage)
        {
            errorMessage = string.Empty;
            if (string.IsNullOrWhiteSpace(ConnectionName))
            {
                errorMessage = "名称不能为空";
                return false;
            }
            // B3：协议未实现必须**先**判，且要说人话。
            // 放在 Config 判空之前：未实现协议时 OnChanged 刻意保留原 Config（不为 null），
            // 若不先判这一条，用户会一路走到"协议类型不匹配"——那句话根本没告诉他真正的问题是什么。
            if (!CommunicationProtocols.IsImplemented(Protocol))
            {
                errorMessage = $"协议 {Protocol} 尚未实现，当前可用：{CommunicationProtocols.ImplementedText}";
                return false;
            }
            if (Config == null)
            {
                errorMessage = "配置不能为空";
                return false;
            }
            if (!Config.Validate(out string configError))
            {
                errorMessage = configError;
                return false;
            }
            if (Protocol != Config.Type)
            {
                errorMessage = "协议类型不匹配";
                return false;
            }
            return true;
        }

        public CommunicationConfig Clone() =>
            new CommunicationConfig
            {
                ConnectionName = ConnectionName + "_Copy",
                Protocol = Protocol,
                Config = Config?.Clone(),
                ReadCycleMs = ReadCycleMs,
                ScanGroups = ScanGroups?.Select(g => g.Clone()).ToList() ?? new(),
                IsEnabled = IsEnabled,
                AutoReconnect = AutoReconnect,
                AutoStart = AutoStart,
                Description = Description,
            };

        /// <summary>
        /// 把 <paramref name="other"/> 的**可编辑字段**复制到本实例（"副本编辑、确定才回写" 的落地手段）。
        /// 只覆盖用户在属性面板能改的项，<see cref="State"/>/<c>CreatedTime</c>/<c>LastConnectedTime</c>
        /// 等运行时状态一律保留本实例的值——它们反映真实连接实况，不能被编辑弹窗清掉。
        /// </summary>
        public void CopyFrom(CommunicationConfig other)
        {
            if (other == null)
                throw new ArgumentNullException(nameof(other));

            // 顺序不可颠倒：Protocol 的 setter 内部会 OnChanged() 重建一份默认 Config，
            // 先赋 Protocol 再赋 Config，最终落到 Config 上的才是副本里的那份配置
            Protocol = other.Protocol;
            Config = other.Config?.Clone(); // 再深拷一份：避免活对象与副本共享同一条链路配置子对象
            ConnectionName = other.ConnectionName;
            ReadCycleMs = other.ReadCycleMs;
            // 组表必须跟着一起拷：漏拷的后果是"在扫描组编辑器里改完点确定，活对象组表还是旧的"（改了不生效）
            ScanGroups = other.ScanGroups?.Select(g => g.Clone()).ToList() ?? new();
            IsEnabled = other.IsEnabled;
            AutoReconnect = other.AutoReconnect;
            AutoStart = other.AutoStart;
            Description = other.Description;
            UpdateModifiedTime();

            // ReadCycleMs 是普通自动属性、ScanGroups 是整体替换的 List，两者都不发通知；
            // 上面刚把它们都换掉了，这里补一次通知，否则连接列表的「轮询」列还显示旧文案
            NotifyScanGroupsChanged();
        }

        public void UpdateLastConnectedTime() => LastConnectedTime = DateTime.Now;

        public void UpdateModifiedTime() => LastModifiedTime = DateTime.Now;

        public override string ToString() =>
            $"{ConnectionName} [{Protocol}] ({Config}) - {(IsEnabled ? "启用" : "禁用")}";
    }

    /// <summary>
    /// 已实现的通讯协议清单（**单一真相源**）。
    ///
    /// <para>B3 背景：改造前"哪些协议可用"有两份不一致的清单——
    /// <see cref="CommunicationConfig"/> 的 <c>OnChanged</c> 认 6 个
    /// （含三个只有配置类、没有连接实现的 OmronFins / MitsubishiMc / OpcUa），
    /// 而 <c>CreateConfig</c> 与 <c>ConnectionFactoryManager</c> 只认 3 个。
    /// 于是用户在属性面板里选中那三个：Config 类真实存在、参数能填、<c>Validate</c> 也通过，
    /// 一路走到 <c>ConnectionFactoryManager.CreateConnection</c> 才抛
    /// <c>NotSupportedException</c>；选中 FreeProtocol 更糟——属性面板内部直接抛
    /// <c>ArgumentOutOfRangeException</c>，弹窗外的 try/catch 拦不到。</para>
    ///
    /// <para>⚠ 本清单必须与 <c>ConnectionFactoryManager.RegisterDefaults()</c> 注册的工厂**保持一致**。
    /// 为什么不在代码里直接取工厂清单：Core 层不引用 Communication 层（<c>Core.csproj</c> 的
    /// ProjectReference 里没有 <c>VM.Communication</c>，方向也不允许反过来），取不到。
    /// 故这一致性由 CommChecks 的断言钉住（见测试方法 <c>CommunicationProtocolsAndAddRollback</c>），
    /// 谁改了一边忘了另一边，回归会当场失败。</para>
    ///
    /// <para>为什么不做成"下拉框只列已实现协议"：属性下拉由公共组件
    /// <c>EnumGenerator</c> 生成，它对 <c>Enum.GetValues</c> 的每个值**无条件**全列，
    /// 且按类型做了 static 缓存；要收窄就得给 <c>SuperDisplayAttribute</c> 加"允许值"字段、
    /// 再改 EnumGenerator 的缓存键——动的是全项目共用的组件，回归面远大于收益。
    /// 故选择"下拉照旧全列，但选中未实现协议时给一句人话错误"。</para>
    /// </summary>
    public static class CommunicationProtocols
    {
        /// <summary>已实现的协议（顺序即错误提示里的展示顺序）。⚠ 与工厂注册表手工保持同步</summary>
        public static readonly CommunicationType[] Implemented =
        {
            CommunicationType.ModbusTcp,
            CommunicationType.ModbusRtu,
            CommunicationType.SiemensS7,
        };

        /// <summary>该协议是否已有可用的连接实现</summary>
        public static bool IsImplemented(CommunicationType type) => Array.IndexOf(Implemented, type) >= 0;

        /// <summary>给用户看的可用协议文案（Validate 的错误信息里要用它）</summary>
        public static string ImplementedText => string.Join(" / ", Implemented);
    }
}
