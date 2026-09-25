using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace VisionMaster.Models
{
    /// <summary>
    /// 软件级方案清单条目（AppConfig.json，独立于任何 .vms 文件）
    /// </summary>
    public class AppSolutionEntry
    {
        /// <summary>序号（UI 展示用，不持久化，由清单顺序决定）</summary>
        [JsonIgnore]
        public int Index { get; set; }

        /// <summary>方案名称（默认取文件名）</summary>
        public string Name { get; set; }

        /// <summary>注释</summary>
        public string Comment { get; set; } = "";

        /// <summary>方案文件完整路径</summary>
        public string Path { get; set; }
    }

    /// <summary>
    /// 组态运行窗口的显示形态（软件级配置，见 <see cref="AppConfigModel.RunWindowMode"/>）。
    /// 两个成员各自对应一个真实场景，不是"好看不好看"的偏好：
    /// 现场跑生产要的是"运行画面即唯一界面"，开发调试要的是"画面与图像/日志同屏"。
    /// </summary>
    public enum ScadaRunWindowMode
    {
        /// <summary>
        /// 依附主窗口：无边框 + 最大化，盖住整个主界面。
        /// 现场触摸屏/一体机跑生产时的形态。
        /// </summary>
        AttachedToMainWindow,

        /// <summary>
        /// 独立窗口：带系统标题栏、可缩放，与主界面（含视觉图像面板）并排显示。
        /// 开发调试时的形态。
        /// </summary>
        IndependentWindow
    }

    /// <summary>
    /// HTTP 收图服务的配置节（见 <see cref="AppConfigModel.HttpImageServer"/>）。
    ///
    /// 为什么单独成类而不是把几个字段平铺进 <see cref="AppConfigModel"/>
    /// ---------
    /// 这几个字段是"一整件事"——启用开关、监听地址、端口、令牌缺一不可，
    /// 平铺进去后读配置的人（和写代码的人）会以为它们彼此独立。
    /// 独立成节还有一个实际好处：将来"网络收图"之外的第二个 HTTP 入口（比如远程调试口）
    /// 可以直接照抄一个节，不会与这几个字段在名字上打架。
    /// </summary>
    public class HttpImageServerSettings
    {
        /// <summary>默认监听端口。19000 段是"现场自留"的常见取值，避开 80/8080/5000 等易冲突端口</summary>
        public const int DefaultPort = 19000;

        /// <summary>默认令牌（占位值，见 <see cref="Token"/> 的说明）</summary>
        public const string DefaultToken = "visionmaster";

        /// <summary>默认监听地址：<c>0.0.0.0</c> = 所有网卡（现场相机/上位机从别的机器推图）</summary>
        public const string DefaultHost = "0.0.0.0";

        /// <summary>
        /// 是否启用 HTTP 收图服务。默认<b>开启</b>——这是本功能的全部意义所在：
        /// 关掉就没有任何入口，等于功能不存在。现场不需要时手动改 false（也省一个监听端口）。
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// 监听地址。<c>0.0.0.0</c> = 监听所有网卡；<c>127.0.0.1</c> = 只收本机（调试用，不暴露到局域网）。
        /// 本机有双网卡时（视觉网 + 办公网）若只想让视觉网那侧能推图，填该网卡的实际 IP。
        /// </summary>
        public string Host { get; set; } = DefaultHost;

        /// <summary>
        /// 监听端口。改这个值时客户端（推图的相机/PLC/上位机）的 URL 也要跟着改，
        /// 所以一旦现场联调通过就不要再动。
        /// </summary>
        public int Port { get; set; } = DefaultPort;

        /// <summary>
        /// 访问令牌（Bearer）。客户端必须在 <c>Authorization: Bearer {Token}</c> 头里带上它。
        ///
        /// 默认值是 <see cref="DefaultToken"/> 这个"明摆着的占位值"：它写在源码和文档里，
        /// 谁都猜得到，<b>只用来防止误连（别的程序恰好往这个端口发东西），不是安全防线</b>。
        /// 服务启动时若发现令牌仍是默认值，会在日志里记一条 Warn 提醒现场改掉——
        /// 不做成"启动失败"：调试阶段本来就要先跑通再收紧，卡在启动上会让人先把整个功能关掉。
        /// </summary>
        public string Token { get; set; } = DefaultToken;

        /// <summary>
        /// 单次请求等待流程结束的超时（毫秒）。默认 30s。
        ///
        /// 收图后是"同步等流程跑完再回包"，所以这个值决定客户端最多等多久。
        /// 太快（如 2s）会把正常的慢流程判成失败；太慢则客户端那边的超时先到、白等。
        /// 一般取现场流程最长耗时的 2~3 倍。
        /// </summary>
        public int RequestTimeoutMs { get; set; } = 30000;

        /// <summary>
        /// 令牌是否仍是默认占位值（服务启动时据此决定要不要 Warn）。
        /// 派生量，不进 JSON——写进去只会多一个可能与 <see cref="Token"/> 矛盾的真值。
        /// </summary>
        [JsonIgnore]
        public bool IsUsingDefaultToken =>
            string.IsNullOrWhiteSpace(Token) ||
            string.Equals(Token, DefaultToken, StringComparison.Ordinal);
    }

    /// <summary>
    /// 网络相机收图服务的配置节（见 <see cref="AppConfigModel.NetworkCameraServer"/>）。
    ///
    /// 为什么与 <see cref="HttpImageServerSettings"/> 分开成两节、且用独立端口
    /// ---------
    /// 两者都是"HTTP 收图"，但语义完全不同：
    ///   HttpImageServer  = 外部推一张图 → 立刻跑一次流程 → 同步回结果（一请求一帧一次执行，只留最新帧）；
    ///   NetworkCameraServer = 客户端按相机该有的样子持续送帧（消费语义 + 环形缓冲 + 溢出计数 + 心跳判活）。
    /// 挤在同一个服务上，每次改相机逻辑都要重测已上线的"网络推送"功能，回归面白白翻倍；
    /// 端口分开后两条链路互不影响，现场排障也能一眼看出是哪条通道在报错。
    /// </summary>
    public class NetworkCameraServerSettings
    {
        /// <summary>默认监听端口。刻意与 HttpImageServer 的 19000 错开，避免两个服务抢同一端口</summary>
        public const int DefaultPort = 19100;

        /// <summary>默认令牌（与 HttpImageServer 同值的占位值，只防误连、不是安全防线）</summary>
        public const string DefaultToken = "visionmaster";

        /// <summary>默认监听地址：<c>0.0.0.0</c> = 所有网卡（相机客户端可能来自另一台机器）</summary>
        public const string DefaultHost = "0.0.0.0";

        /// <summary>
        /// 是否启用网络相机收图服务。默认<b>开启</b>——不启用则"网络相机"这一类相机完全无法工作。
        /// 现场只用真机时改 false 即可省一个监听端口。
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>监听地址。<c>0.0.0.0</c> = 所有网卡；<c>127.0.0.1</c> = 只收本机</summary>
        public string Host { get; set; } = DefaultHost;

        /// <summary>监听端口。改它时客户端的推图地址要同步改，联调通过后不要再动</summary>
        public int Port { get; set; } = DefaultPort;

        /// <summary>
        /// 访问令牌（Bearer）。客户端须在 <c>Authorization: Bearer {Token}</c> 头里带上。
        /// 默认值是写在源码与文档里的占位值，只用于防止"别的程序恰好往这个端口发东西"。
        /// </summary>
        public string Token { get; set; } = DefaultToken;

        /// <summary>
        /// 令牌是否仍是默认占位值（服务启动时据此 Warn 提醒现场修改）
        /// </summary>
        [JsonIgnore]
        public bool IsUsingDefaultToken =>
            string.IsNullOrWhiteSpace(Token) ||
            string.Equals(Token, DefaultToken, StringComparison.Ordinal);
    }

    /// <summary>
    /// 软件级配置（AppConfig.json）：方案清单 + 默认启动方案
    /// 注意：这是软件全局配置，不随任何解决方案持久化
    /// </summary>
    public class AppConfigModel
    {
        /// <summary>
        /// 空闲自动登出的默认分钟数。<b>与历史行为逐字一致</b>——升级不能让现场
        /// 已经在跑的机器忽然"不踢人了"（那等于把一台无人看管的设备一直留在管理员权限上）。
        /// </summary>
        public const int DefaultIdleTimeoutMinutes = 10;

        /// <summary>操作审计的默认保留天数（含今天）。与 <c>ScadaAuditWriter.DefaultRetentionDays</c> 同源</summary>
        public const int DefaultAuditRetentionDays = 90;

        /// <summary>报警历史的默认保留天数（含今天）。与 <c>ScadaAlarmHistoryWriter.DefaultRetentionDays</c> 同源</summary>
        public const int DefaultAlarmHistoryRetentionDays = 365;

        /// <summary>
        /// 默认启动方案路径（软件启动时自动加载；空 = 不自动加载）
        /// </summary>
        public string StartupSolutionPath { get; set; } = "";

        /// <summary>
        /// 方案清单（有序）
        /// </summary>
        public List<AppSolutionEntry> Solutions { get; set; } = new();

        /// <summary>
        /// 启动时是否执行通讯连通性自检（产线现场可关闭以加快启动）
        /// </summary>
        public bool EnableCommunicationStartupCheck { get; set; } = true;

        /// <summary>
        /// 组态运行窗口的显示形态（「系统 → 运行窗口设置」可改，改完立即写盘、下次运行生效）。
        ///
        /// 默认取 <see cref="ScadaRunWindowMode.AttachedToMainWindow"/>（= 历史行为，无边框最大化）：
        /// 这是现场部署形态，不能让一次升级把已在产线上跑的设备改成"窗口化运行"。
        /// 开发调试嫌运行画面盖住视觉图像时，再手动切到独立窗口。
        ///
        /// 刻意不加 <c>StringEnumConverter</c>：Newtonsoft 默认把枚举写成整数，
        /// 而字符串形式一旦拼错会让整份 AppConfig.json 反序列化失败——连带把方案清单一起丢掉。
        /// 稳定性优先于可读性，含义由这里的注释与本文件顶部枚举的注释负责。
        /// </summary>
        public ScadaRunWindowMode RunWindowMode { get; set; } = ScadaRunWindowMode.AttachedToMainWindow;

        /// <summary>
        /// 组态运行窗口落在哪块显示器上。值是**显示器设备名**（如 <c>\\.\DISPLAY2</c>）；
        /// <b>空串 = 跟随主屏</b>（= 历史行为）。
        ///
        /// 为什么存设备名而不是"第几块屏"
        /// ---------
        /// 序号是"枚举出来的次序"，现场插拔一次显示器、换一个视频口，同一个序号就指到另一块屏上——
        /// 表现为"昨天还好好的，今天运行画面跑错屏了"，而且改配置的人看不出哪里错了。
        /// 设备名由系统按"适配器 + 输出口"分配，只要线还插在同一个口上就不变（拔了再插回来也认得出）。
        /// 代价是换口会认不出——那时按"名字找不到就回落主屏"的规则处理（见 <c>ScadaMonitors.Resolve</c>），
        /// 运行日志里会记一条，现场照着改一下即可，不会把画面摆到屏幕外面去。
        ///
        /// 空串是默认值这件事很重要：升级后所有已经在跑的机器读到的都是空串，
        /// 落屏行为与 S13-e 之前逐字一致。多屏现场要"副屏只给操作员看画面"时才手动指定。
        /// </summary>
        public string RunWindowMonitor { get; set; } = "";

        /// <summary>
        /// 空闲多久自动登出（分钟）。<b>0 或负数 = 不自动登出</b>。
        ///
        /// 为什么可配：10 分钟是工业 HMI 的常见取值，但现场真的有"走开半小时"的工位
        /// （去仓库取料、去看另一台设备），一律按 10 分钟踢人只会让人反复重登、
        /// 最后干脆用管理员账号不登出——那比放宽超时更不安全。反过来说，
        /// 无人值守的机台也可以配 0（永不自动登出）。
        ///
        /// 默认 <see cref="DefaultIdleTimeoutMinutes"/> = 历史行为，升级不改现场节奏。
        /// 落盘写的是<b>分钟数这个原始值</b>（见 <see cref="IdleTimeout"/> 的换算口）。
        /// </summary>
        public int IdleTimeoutMinutes { get; set; } = DefaultIdleTimeoutMinutes;

        /// <summary>
        /// 操作审计（程序目录 <c>Audit</c> 下的 <c>Audit-yyyy-MM-dd.csv</c>）保留天数（含今天）。
        /// <b>0 或负数 = 永不清理</b>——留给"法规要求留档、磁盘也够"的现场。
        /// 默认 <see cref="DefaultAuditRetentionDays"/>。
        /// </summary>
        public int AuditRetentionDays { get; set; } = DefaultAuditRetentionDays;

        /// <summary>
        /// 报警历史（程序目录 <c>Alarms</c> 下的 <c>AlarmHistory-yyyy-MM-dd.csv</c>）保留天数（含今天）。
        /// <b>0 或负数 = 永不清理</b>。默认 <see cref="DefaultAlarmHistoryRetentionDays"/>——
        /// 比审计留得久：现场要翻很久以前的同类故障作对比。
        /// </summary>
        public int AlarmHistoryRetentionDays { get; set; } = DefaultAlarmHistoryRetentionDays;

        /// <summary>
        /// 把 <see cref="IdleTimeoutMinutes"/> 翻成会话超时口径
        /// （<see cref="TimeSpan.Zero"/> 或负值 = 不自动登出，这正是 <c>ScadaAccessPolicy</c> 认的语义）。
        ///
        /// 为什么要有这个换算口、而不是各调用点自己 <c>TimeSpan.FromMinutes</c>
        /// ---------
        /// "分钟 → TimeSpan"与"0 表示永不"这两条规则如果分散在宿主、弹窗、断言三处，
        /// 早晚出现"界面显示 0 分钟、实际还是按 10 分钟踢人"——这种不一致只在现场
        /// 长时间无人操作时才暴露，最难查。规则只写这一处。
        ///
        /// 不进 JSON（<c>[JsonIgnore]</c>）：落盘的只有分钟数那个原始值，
        /// 它是派生量，写进去只会多一个可能与真值矛盾的数字。
        /// </summary>
        [JsonIgnore]
        public TimeSpan IdleTimeout => IdleTimeoutMinutes <= 0
            ? TimeSpan.Zero
            : TimeSpan.FromMinutes(IdleTimeoutMinutes);

        /// <summary>
        /// HTTP 收图服务配置（网络推送图像 → 跑流程 → 回包，见 <see cref="HttpImageServerSettings"/>）。
        ///
        /// <b>不能为 null</b>：这个属性在运行时会被直接点着用（<c>Current.HttpImageServer.Enabled</c>），
        /// 而老版本的 AppConfig.json 里根本没有这个节——反序列化时 Newtonsoft 遇到缺失的引用类型
        /// 不会自动 new，只会留 null。初始化器保证"文件里没有"与"文件里写了默认值"走同一条路。
        /// </summary>
        public HttpImageServerSettings HttpImageServer { get; set; } = new();

        /// <summary>
        /// 网络相机收图服务配置（客户端持续推帧 + 心跳 → 相机帧队列 → 流程消费，
        /// 见 <see cref="NetworkCameraServerSettings"/>）。
        ///
        /// <b>不能为 null</b>：与 <see cref="HttpImageServer"/> 同一理由——运行时会被直接点着用
        /// （<c>Current.NetworkCameraServer.Enabled</c>），而老版本的 AppConfig.json 里没有这个节，
        /// Newtonsoft 遇到缺失的引用类型只留 null 不会自动 new。初始化器让"文件里没有"
        /// 与"文件里写了默认值"走同一条路。
        /// </summary>
        public NetworkCameraServerSettings NetworkCameraServer { get; set; } = new();
    }
}
