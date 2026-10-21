using System;
using System.Collections.Generic;
using System.ComponentModel;
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
    /// 画布在"每轮流程开始"时对上一轮图像的处理口径（见 <see cref="ImageGallerySettings.RunStartMode"/>）。
    /// 两个成员对应两种现场用法，不是偏好：调机时"清空"只看本轮，多流程并跑时"覆盖本流程"不互相清空。
    /// </summary>
    public enum ImageGalleryRunStartMode
    {
        /// <summary>清空整张画布：本轮开始前把列表里所有图（含其它流程的）全部释放</summary>
        ClearAll,

        /// <summary>覆盖本流程：只清掉"即将运行的这条流程"上一轮产生的图，其它流程的图保留</summary>
        ReplaceFlow
    }

    /// <summary>
    /// 图像集（视觉图像页"图集模式"）的配置节（见 <see cref="AppConfigModel.ImageGallery"/>）。
    ///
    /// 为什么单独成节
    /// ---------
    /// 这几个值共同描述"这台机器怎么留存流程输出图"：每轮开始怎么清（口径）、留多少（两个上限）、
    /// 同一位置是否保留历史帧、要不要把没主动发布但挂在输出端口上的图也收进来。
    /// 平铺进 <see cref="AppConfigModel"/> 会让人以为它们彼此独立，实际调其中一个必然牵动其余。
    ///
    /// 上限口径与"永不清理"约定
    /// ---------
    /// <b>0 或负数 = 不限制</b>（与审计/报警保留天数的口径一致）。默认给有限值：
    /// 一个 500 万像素的彩图副本约 15MB，不限量等于让长时间循环运行把内存吃干。
    /// </summary>
    public class ImageGallerySettings
    {
        /// <summary>总张数上限默认值（跨全部流程）</summary>
        public const int DefaultTotalLimit = 300;

        /// <summary>单流程张数上限默认值</summary>
        public const int DefaultPerFlowLimit = 50;

        /// <summary>缩略图最长边默认像素（只影响网格渲染，不影响大图清晰度）</summary>
        public const int DefaultThumbnailMaxSize = 160;

        /// <summary>是否启用图集采集。关掉后流程照跑，只是不再收集图像（省内存）</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// 每轮流程开始时的处理口径：清空整张画布 / 覆盖本流程上一轮的图。
        /// 默认 <see cref="ImageGalleryRunStartMode.ReplaceFlow"/>——多流程并跑时不互相清空。
        /// </summary>
        public ImageGalleryRunStartMode RunStartMode { get; set; } = ImageGalleryRunStartMode.ReplaceFlow;

        /// <summary>总张数上限（跨全部流程）。<b>0 或负数 = 不限制</b></summary>
        public int TotalLimit { get; set; } = DefaultTotalLimit;

        /// <summary>单流程张数上限。<b>0 或负数 = 不限制</b></summary>
        public int PerFlowLimit { get; set; } = DefaultPerFlowLimit;

        /// <summary>
        /// 同一"流程 + 步骤 + 端口"是否保留历史帧（每轮追加）。
        /// 默认 <c>false</c> = 覆盖保留最新：图集张数天然被"输出端口数"封顶，
        /// 循环运行也不会越跑越多。需要看"这一轮和上一轮的差异"时再打开。
        /// </summary>
        public bool KeepFrameHistory { get; set; } = false;

        /// <summary>
        /// 是否把"挂在输出端口上、但插件没有主动发布预览"的 HImage 也收进来（兜底）。
        /// 默认 <c>false</c>：画布列表以**插件主动注入**为准——注入的图带着插件给的标题与信息，
        /// 列表才有内容可显示；打开它会把所有输出端口的图也收进来，但那些图没有说明信息，
        /// 且中间过程图会把列表刷满。需要"什么都不改也能看到图"时再打开。
        /// </summary>
        public bool CollectAllOutputPorts { get; set; } = false;

        /// <summary>是否把插件主动发布的实时预览（<c>ImageDisplayEvent</c>）也收进图集</summary>
        public bool IncludeRealtimePreviews { get; set; } = true;

        /// <summary>缩略图最长边像素（越小内存越省；建议 96~200）</summary>
        public int ThumbnailMaxSize { get; set; } = DefaultThumbnailMaxSize;

        /// <summary>
        /// 报警触发时是否把当前图集整批落盘到程序目录的 <c>AlarmFrames</c>（按天 + 报警名分目录）。
        /// 默认 <c>false</c>：留存要写磁盘、且报警可能密集触发，默认开等于给现场埋一个磁盘增长点；
        /// 需要复盘"报警当时画面是什么样"的现场，在「系统参数设置」里自行打开。
        /// </summary>
        public bool SaveFramesOnAlarm { get; set; } = false;
    }

    /// <summary>
    /// SCADA 运行态 Web 客户端的点击策略（见 <see cref="ScadaWebSettings.ClickPolicy"/>）。
    ///
    /// 两个成员不是"好看不好看"的偏好，而是两种现场安全口径：
    ///   <see cref="AllOperators"/>     = 所有已登录账号都能点（含操作员）；适合只给操作工看/操作的屏；
    ///   <see cref="HighestRoleOnly"/>   = 只有当前登录账号里权限最高的那一档能点；默认值。
    /// </summary>
    public enum ScadaWebClickPolicy
    {
        /// <summary>所有已登录账号都可点击（不按角色再收窄）</summary>
        AllOperators = 0,

        /// <summary>只有最高权限档（管理员/工程师）可点击，其余账号只读——默认</summary>
        HighestRoleOnly = 1
    }

    /// <summary>
    /// SCADA 运行态 Web 客户端（可选模块）的配置节（见 <see cref="AppConfigModel.ScadaWeb"/>）。
    ///
    /// <b>默认口径（四条，缺一条都不是"安全默认"）</b>
    /// ---------
    ///   1. <b>默认不启用</b>（<see cref="Enabled"/> = false）：Web 是一个可选模块，
    ///      程序目录里没有 <c>Web\VisionMaster.ScadaWeb.dll</c> 时启动链也不该报错；
    ///   2. <b>默认只读</b>（<see cref="RequireLogin"/> = true、<see cref="AllowAnonymousView"/> = false）：
    ///      打开网页先登录，未登录看不到任何画面内容；
    ///   3. <b>默认只有最高权限可点</b>（<see cref="ClickPolicy"/> = <see cref="ScadaWebClickPolicy.HighestRoleOnly"/>）：
    ///      普通操作员在浏览器里看得见画面、点不动图元；
    ///   4. <b>默认禁用运动命令</b>（<see cref="AllowMotionCommands"/> = false）：
    ///      浏览器里点一下就让轴动起来，是这套功能里唯一"点错会撞机"的入口，
    ///      必须由现场显式打开（还叠加了 <see cref="OperationLeaseEnabled"/> 的操作权租约）。
    ///
    /// <b>改配置需重启</b>（M1 口径）
    /// ---------
    /// 本节的读取发生在 <c>App.RegisterTypes</c>（见 <c>ScadaWebModuleLoader</c>），
    /// 那一刻容器还没构建，也没有任何"改完即时生效"的通道。所以改了
    /// <see cref="Enabled"/> / <see cref="Port"/> / <see cref="BindAddress"/> 之后
    /// <b>必须重启 VisionMaster</b> 才会生效——不做成热重载是刻意的：换监听地址要停服务、
    /// 断掉所有已连会话，而"点保存就断线"在产线上比"重启生效"更难接受。
    /// 其余字段（策略类）由 Web 模块每轮现读，改完重启一次同样生效。
    /// </summary>
    public class ScadaWebSettings
    {
        /// <summary>默认监听端口（与 HttpImageServer 的 19000、NetworkCameraServer 的 19100 错开）</summary>
        public const int DefaultPort = 8080;

        /// <summary>默认监听地址：<c>0.0.0.0</c> = 所有网卡（现场用平板/另一台机器打开网页）</summary>
        public const string DefaultBindAddress = "0.0.0.0";

        /// <summary>默认最大并发会话数</summary>
        public const int DefaultMaxSessions = 16;

        /// <summary>默认每会话入站队列容量</summary>
        public const int DefaultSessionInboundQueueCapacity = 32;

        /// <summary>入站队列容量的下界（小于它等于每帧都可能丢）</summary>
        public const int MinSessionInboundQueueCapacity = 16;

        /// <summary>入站队列容量的上界（再大就只是把延迟堆在内存里）</summary>
        public const int MaxSessionInboundQueueCapacity = 64;

        /// <summary>默认每会话命令速率上限（条/秒）</summary>
        public const int DefaultCommandRatePerSecond = 5;

        /// <summary>确认令牌默认有效期（秒）</summary>
        public const int DefaultConfirmTokenSeconds = 30;

        /// <summary>确认令牌有效期下界（再短就连"看一眼提示再点确认"都不够）</summary>
        public const int MinConfirmTokenSeconds = 5;

        /// <summary>确认令牌有效期上界</summary>
        public const int MaxConfirmTokenSeconds = 300;

        /// <summary>取票接口的默认每分钟上限（每个来源 IP）</summary>
        public const int DefaultTicketRatePerMinute = 10;

        /// <summary>取票限速的下界（0 或负数按 1 处理，不许关掉限流）</summary>
        public const int MinTicketRatePerMinute = 1;

        /// <summary>取票限速的上界</summary>
        public const int MaxTicketRatePerMinute = 600;

        /// <summary>默认单帧快照上限（字节）。2MB ≈ 一张 1080p JPEG 的 4 倍余量</summary>
        public const int DefaultSnapshotMaxBytes = 2 * 1024 * 1024;

        /// <summary>默认单帧增量预算（字节）。120KB 是"窄带现场也要能刷出来"的量级</summary>
        public const int DefaultFrameBudgetBytes = 120 * 1024;

        /// <summary>
        /// 是否启用 Web 运行态模块。<b>默认 false</b>——不启用时加载器直接返回 null，
        /// 启动链既不加载程序集、也不监听任何端口（正常状态，不写 Warn）。
        /// 改完需重启（见类注释）。
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// 监听端口。默认 <see cref="DefaultPort"/>。改完需重启；
        /// 与 HTTP 收图 / 网络相机两个服务的端口必须错开（否则其中一个绑不上）。
        /// </summary>
        public int Port { get; set; } = DefaultPort;

        /// <summary>
        /// 监听地址。<c>0.0.0.0</c> = 所有网卡；<c>127.0.0.1</c> = 只允许本机（调试/反向代理前置时用）。
        /// 现场只有视觉网该被访问时，填该网卡的实际 IP。改完需重启。
        /// </summary>
        public string BindAddress { get; set; } = DefaultBindAddress;

        /// <summary>
        /// 是否要求登录后才能查看画面。<b>默认 true</b>。
        /// 关掉它等于"网页一打开就是运行画面"，只适合完全隔离的调试网络。
        /// </summary>
        public bool RequireLogin { get; set; } = true;

        /// <summary>
        /// 未登录时是否允许<b>只读</b>查看（画面 + 变量值，不含任何写入/动作）。<b>默认 false</b>。
        /// 与 <see cref="RequireLogin"/> 的区别：后者管"能不能进"，本项管"进来之前能看到什么"。
        /// </summary>
        public bool AllowAnonymousView { get; set; } = false;

        /// <summary>
        /// 是否允许浏览器下发命令（写变量 / 执行图元事件 / 确认报警）。<b>默认 false = 只读镜像</b>。
        ///
        /// 这是 M2 的总闸，与 <see cref="ClickPolicy"/> 是两道独立的门：
        ///   · 本项为 false → 所有 <c>cmd.*</c> 一律回 <c>E_POLICY</c>（"当前为只读模式"），
        ///     <c>caps</c> 全 false——<b>即使角色与策略都允许</b>；
        ///   · 本项为 true 才轮到 <see cref="ClickPolicy"/> 与角色判定。
        /// 默认关的理由与 <see cref="AllowAnonymousView"/> 同一条：浏览器是远程入口，
        /// 一次升级不该让现场网页忽然获得写能力。运行期可改（改后推 caps + policyVersion++），
        /// 不需要重启。
        /// </summary>
        public bool AllowCommands { get; set; } = false;

        /// <summary>
        /// 危险动作（停流程、运动类动作）的一次性确认令牌有效期（秒）。<b>默认 30</b>，
        /// 取值夹在 <see cref="MinConfirmTokenSeconds"/>~<see cref="MaxConfirmTokenSeconds"/>。
        ///
        /// 令牌绑定 <c>(sessionId, elementId, actionKind)</c>、单次使用、先删后执行；
        /// 有效期从签发时刻算（服务端时钟）。太长等于把"确认"变成一张长期通行证，
        /// 太短则用户还没看清提示就已经过期。
        /// </summary>
        public int ConfirmTokenSeconds { get; set; } = DefaultConfirmTokenSeconds;

        /// <summary>
        /// <c>POST /api/ticket</c> 的每来源 IP 每分钟请求上限（匿名取票 / 登录 / 换票三种请求合并计数）。
        /// <b>默认 10</b>，取值夹在 <see cref="MinTicketRatePerMinute"/>~<see cref="MaxTicketRatePerMinute"/>。
        ///
        /// 计数按 <c>(remoteIp, 60s 滑动窗口)</c>，<b>不因用户名不同而分开</b>——否则换一个用户名
        /// 就能绕过限速去刷密码。超出上限回 429 + <c>E_RATE_LIMITED</c> + <c>retryAfterMs</c>，
        /// 不签发 ticket、不做 PBKDF2。
        /// </summary>
        public int TicketRatePerMinute { get; set; } = DefaultTicketRatePerMinute;

        /// <summary>
        /// 最大并发会话数。<b>默认 16</b>。超过后新连接被明确拒绝（不是排队），
        /// 免得一台机器开几十个标签页把运行态拖慢。
        /// </summary>
        public int MaxSessions { get; set; } = DefaultMaxSessions;

        /// <summary>
        /// 会话空闲判死阈值（秒）。<b>默认 45</b>；0 = 关闭判死。
        ///
        /// 判据是"当前 socket 45 秒既没有入站帧、也没有一次成功出站"（双条件）：
        /// 只看入站会误杀被浏览器节流的后台标签页；只看出站会杀掉所有空闲页
        /// （未运行时本来就没有值帧）。命令在飞（pending/executing）的会话豁免。
        /// </summary>
        public int SessionIdleTimeoutSeconds { get; set; } = 45;

        /// <summary>
        /// 每会话入站命令队列容量，取值范围 <see cref="MinSessionInboundQueueCapacity"/>~<see cref="MaxSessionInboundQueueCapacity"/>
        /// （16~64）。<b>默认 32</b>。超出范围时按边界夹取并记一条 Warn（不阻断启动）。
        /// </summary>
        public int SessionInboundQueueCapacity { get; set; } = DefaultSessionInboundQueueCapacity;

        /// <summary>
        /// 点击策略。<b>默认 <see cref="ScadaWebClickPolicy.HighestRoleOnly"/></b>——
        /// 只有最高权限档可点，其余账号只读。需要"操作员也能点"时改成 <see cref="ScadaWebClickPolicy.AllOperators"/>。
        /// </summary>
        public ScadaWebClickPolicy ClickPolicy { get; set; } = ScadaWebClickPolicy.HighestRoleOnly;

        /// <summary>
        /// 是否允许从 Web 下发运动命令（点动/定位/回零）。<b>默认 false</b>。
        /// 这是"点错会撞机"的唯一入口，打开前请同时确认 <see cref="OperationLeaseEnabled"/>
        /// 与现场的安全回路（急停/限位）确实在位。
        /// </summary>
        public bool AllowMotionCommands { get; set; } = false;

        /// <summary>
        /// 是否启用"操作权租约"（同一时刻只有一份会话能下发写命令，其余只读）。<b>默认 false</b>。
        /// 打开后，多个浏览器同时开着也不会出现"两个人抢着写同一个变量"。
        /// </summary>
        public bool OperationLeaseEnabled { get; set; } = false;

        /// <summary>
        /// 每会话命令速率上限（条/秒）。<b>默认 5</b>。0 或负数按 1 处理（不许关掉限流）。
        /// </summary>
        public int CommandRatePerSecond { get; set; } = DefaultCommandRatePerSecond;

        /// <summary>
        /// 单帧快照上限（字节）。<b>默认 2MB</b>。超限时按缩帧处理并记诊断，不抛给客户端。
        /// </summary>
        public int SnapshotMaxBytes { get; set; } = DefaultSnapshotMaxBytes;

        /// <summary>
        /// 单帧增量预算（字节）。<b>默认 120KB</b>。决定一帧里能塞多少"变化的图元"，
        /// 超出部分留到下一帧（宁可慢半拍，也不把带宽打满）。
        /// </summary>
        public int FrameBudgetBytes { get; set; } = DefaultFrameBudgetBytes;

        // ===== 全操作员开放（AllOperators）时的监管闸门 =====
        //
        // 背景：<see cref="ClickPolicy"/> = <see cref="ScadaWebClickPolicy.HighestRoleOnly"/> 时，
        // 浏览器里的写入口本来就只对最高权限档开放；一旦改成 <see cref="ScadaWebClickPolicy.AllOperators"/>，
        // 普通操作员也能在网页上按按钮、写变量、下运动命令——现场失去的正是"只有工程师能改"这层保护。
        // 下面五项就是补回这层保护的替代手段（身份、留痕、限速、危险操作二次确认、操作权租约）。
        //
        // 口径（M2 实现）：<see cref="ClickPolicy"/> 切到 <see cref="ScadaWebClickPolicy.AllOperators"/> 时，
        // 下面五项是**监管闸门**——缺项拒绝启用 AllOperators（配置校验失败、Web 模块按不可用处理并记 Warn），
        // 不做"缺哪项就悄悄跳过哪项"。理由是这五项各自对应一种现场事故，
        // 少一项就等于给操作员开一条没有护栏的通道，而这种"看起来开了、其实少一层"最难在事后发现。
        // <see cref="AllOperatorsRequiresLease"/> 是其中"租约是否也计入必开清单"的那一项：
        // 默认 false 保持历史行为，M2 校验时按它决定要不要把 <see cref="OperationLeaseEnabled"/> 一起要求上。

        /// <summary>
        /// 是否要求登录用户在线（有活跃会话）才允许写操作。<b>默认 true</b>。
        /// AllOperators 下的第一道闸门：写动作必须能对应到一个仍在线的账号，而不是一个已登出的残留令牌。
        /// </summary>
        public bool RequireOnlineUsers { get; set; } = true;

        /// <summary>
        /// 是否要求每一次写操作都落审计（<c>ScadaAuditWriter</c>）。<b>默认 true</b>。
        /// AllOperators 下"谁按的"是事后唯一能查的东西，关掉等于放弃追责。
        /// </summary>
        public bool RequireAudit { get; set; } = true;

        /// <summary>
        /// 是否要求每会话命令限流（<see cref="CommandRatePerSecond"/>）。<b>默认 true</b>。
        /// AllOperators 下用户数变多、误触变多，限流是防止"连点把变量刷爆"的唯一手段。
        /// </summary>
        public bool RequireRateLimit { get; set; } = true;

        /// <summary>
        /// 危险操作（运动命令、急停复位等）是否要求二次确认。<b>默认 true</b>。
        /// AllOperators 下按错按钮的概率随人数上升，二次确认是最后一道人机防线。
        /// </summary>
        public bool RequireConfirmForDangerous { get; set; } = true;

        /// <summary>
        /// AllOperators 时是否强制启用操作权租约（同一时刻只有一份会话能写）。<b>默认 false</b>。
        /// 与 <see cref="OperationLeaseEnabled"/> 的关系：后者是"要不要租约"，本项是"放开 AllOperators 时
        /// 是否<b>强制</b>要求租约"；两者都开才是"放开写权限 + 只允许一个人写"。
        /// 默认 false 是历史行为（M1 口径），M2 会把它纳入上面那组闸门的校验。
        /// </summary>
        public bool AllOperatorsRequiresLease { get; set; } = false;

        /// <summary>
        /// 单图元每秒变更上限（条/秒/图元）。<b>默认 2</b>。
        /// AllOperators 下的细粒度限流：按图元而不是按会话算，防止一个会话把某一个图元刷成"每秒几十帧"。
        /// </summary>
        public int ElementRatePerSecond { get; set; } = 2;

        /// <summary>
        /// 图像令牌有效期（秒）。<b>默认 45</b>。
        /// AllOperators 下图像请求走一次性令牌而不是裸 URL：令牌过期即失效，
        /// 避免"把浏览器里那张图的地址发出去"成为长期可用的旁路。
        /// </summary>
        public int ImageTokenSeconds { get; set; } = 45;
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

        /// <summary>
        /// 图像集（视觉图像页"图集模式"）配置，见 <see cref="ImageGallerySettings"/>。
        ///
        /// <b>不能为 null</b>：与上面两节同一理由——运行时会被直接点着用
        /// （<c>Current.ImageGallery.TotalLimit</c>），而老版本 AppConfig.json 里没有这个节，
        /// Newtonsoft 遇到缺失的引用类型只留 null 不会自动 new。初始化器让"文件里没有"
        /// 与"文件里写了默认值"走同一条路。
        /// </summary>
        public ImageGallerySettings ImageGallery { get; set; } = new();

        /// <summary>
        /// 并行执行全局设置（流程引擎真并行二期），见 <see cref="ParallelExecutionSettings"/>。
        ///
        /// <b>不能为 null</b>：与上面三节同一理由——运行时会被直接点着用
        /// （<c>Current.ParallelExecution.FailFastByDefault</c>），而老版本的 AppConfig.json
        /// 里没有这个节，Newtonsoft 遇到缺失的引用类型只留 null 不会自动 new。
        /// 初始化器让"文件里没有"与"文件里写了默认值"走同一条路（AppConfigModel.cs 三次踩坑史）。
        /// </summary>
        public ParallelExecutionSettings ParallelExecution { get; set; } = new();

        /// <summary>
        /// 流程画布的「随用户」视图态（评审清单 B 包），见 <see cref="FlowCanvasSettings"/>。
        ///
        /// <b>不能为 null</b>：与上面四节同一理由——FlowCanvasViewModel 构造时会直接点着用
        /// （<c>Current.FlowCanvasSettings.ShowOrderLinks</c>），而老版本的 AppConfig.json
        /// 里没有这个节，Newtonsoft 遇到缺失的引用类型只留 null 不会自动 new。
        /// 初始化器让"文件里没有"与"文件里写了默认值"走同一条路（AppConfigModel.cs 三次踩坑史）。
        /// </summary>
        public FlowCanvasSettings FlowCanvasSettings { get; set; } = new();

        /// <summary>
        /// 工具箱的「随用户」态（收藏的算子清单），见 <see cref="ToolboxSettings"/>。
        ///
        /// <b>不能为 null</b>：与上面几节同一理由——ToolViewModel 构造时会直接点着用
        /// （<c>Current.Toolbox.Favorites</c>），而老版本的 AppConfig.json 里没有这个节，
        /// Newtonsoft 遇到缺失的引用类型只留 null 不会自动 new。
        /// 初始化器让"文件里没有"与"文件里写了默认值"走同一条路（AppConfigModel.cs 三次踩坑史）。
        /// </summary>
        public ToolboxSettings Toolbox { get; set; } = new();

        /// <summary>
        /// 帮助手册的阅读态（开发章节显隐），见 <see cref="HelpSettings"/>。
        ///
        /// <b>不能为 null</b>：与上面几节同一理由——HelpViewModel 打开时会直接点着用
        /// （<c>Current.Help.ShowDeveloperDocs</c>），而老版本的 AppConfig.json 里没有这个节，
        /// Newtonsoft 遇到缺失的引用类型只留 null 不会自动 new。
        /// 初始化器让"文件里没有"与"文件里写了默认值"走同一条路。
        /// </summary>
        public HelpSettings Help { get; set; } = new();

        /// <summary>
        /// SCADA 运行态 Web 客户端（可选模块）配置，见 <see cref="ScadaWebSettings"/>。
        ///
        /// <b>不能为 null</b>：与上面几节同一理由——加载器与 Web 模块都会直接点着用
        /// （<c>Current.ScadaWeb.Enabled</c> / <c>Current.ScadaWeb.Port</c>），
        /// 而老版本的 AppConfig.json 里没有这个节，Newtonsoft 遇到缺失的引用类型
        /// 只留 null 不会自动 new。初始化器让"文件里没有"与"文件里写了默认值"走同一条路。
        ///
        /// 默认 <see cref="ScadaWebSettings.Enabled"/> = false：不启用时启动链不加载 Web 程序集、
        /// 不监听端口，升级到本版本对现场的行为改变为零（详见该类的"默认口径"四条）。
        /// </summary>
        public ScadaWebSettings ScadaWeb { get; set; } = new();

        // ==================================================================
        //  日志（2026-10-21）：原先全是 LogService 里的硬编码
        // ==================================================================

        /// <summary>日志保留天数默认值（含今天）。与 LogService 的构造兜底同源</summary>
        public const int DefaultLogRetentionDays = 30;

        /// <summary>日志面板保留条数默认值（只在内存里，不是落盘）</summary>
        public const int DefaultLogPanelMaxRows = 5000;

        /// <summary>报警图集保留天数默认值（含今天）</summary>
        public const int DefaultAlarmFrameRetentionDays = 90;

        /// <summary>密码最小长度默认值（= 历史行为，升级不改现场节奏）</summary>
        public const int DefaultMinPasswordLength = 4;

        /// <summary>
        /// 日志文件保留天数（含今天）。<b>0 或负数 = 永不清理</b>。
        /// 与审计 / 报警保留同一口径——它们都是"磁盘上的历史证据"，规则不该两样。
        /// </summary>
        public int LogRetentionDays { get; set; } = DefaultLogRetentionDays;

        /// <summary>
        /// 日志目录。<b>空串 = 程序目录下的 Logs</b>（= 历史行为）。
        ///
        /// 为什么允许改：装在 Program Files 或以受限用户跑时，程序目录可能不可写；
        /// 也有现场要求把日志放到独立数据盘。留空时行为与升级前逐字一致。
        /// </summary>
        public string LogDirectory { get; set; } = "";

        /// <summary>
        /// 落盘的最小级别（面板不受影响——面板永远收全部级别）。
        /// 磁盘紧张的机台可以只落警告与错误。
        /// </summary>
        public LogFileLevel LogFileLevel { get; set; } = LogFileLevel.Info;

        /// <summary>
        /// 日志面板保留条数（只在内存里，超过按最旧丢弃）。
        /// 0 或负数 = 不限制（不推荐：连续跑几天会把内存吃干）。
        /// </summary>
        public int LogPanelMaxRows { get; set; } = DefaultLogPanelMaxRows;

        /// <summary>
        /// 报警图集（程序目录 <c>AlarmFrames</c> 下的 PNG）保留天数（含今天）。
        /// <b>0 或负数 = 永不清理</b>。
        ///
        /// 为什么必须有这一项：每次报警都把整个图集落盘，每张 ~15MB 量级，而这一目录
        /// 在加这一项之前<b>只写不删</b>——它是唯一一个没有上限的磁盘增长点。
        /// </summary>
        public int AlarmFrameRetentionDays { get; set; } = DefaultAlarmFrameRetentionDays;

        /// <summary>
        /// 账号密码最小长度。<b>0 或负数按 <see cref="DefaultMinPasswordLength"/> 处理</b>
        /// （"允许空密码"不是这条配置的语义，别让它成为关掉校验的后门）。
        /// </summary>
        public int MinPasswordLength { get; set; } = DefaultMinPasswordLength;
    }

    /// <summary>
    /// 日志落盘的最小级别（软件级配置，见 <see cref="AppConfigModel.LogFileLevel"/>）。
    ///
    /// 顺序就是严重度（Info &lt; Warning &lt; Error）：过滤按"级别 &gt;= 配置值"。
    /// 显示名走 <see cref="DescriptionAttribute"/>——属性面板的 EnumGenerator 读的是它
    /// （不是 <c>[Display]</c>，见 R84 的三条上屏路径）。
    /// </summary>
    public enum LogFileLevel
    {
        /// <summary>信息及以上（历史行为：所有日志都落盘）</summary>
        [Description("全部（信息及以上）")]
        Info = 0,

        /// <summary>只落警告与错误</summary>
        [Description("仅警告与错误")]
        Warning = 1,

        /// <summary>只落错误</summary>
        [Description("仅错误")]
        Error = 2
    }

    /// <summary>
    /// 流程画布的「随用户」视图态（AppConfig.json 的 <c>FlowCanvas</c> 节）。
    ///
    /// 画布视图态的三栏口径（完整表见 Core\Models\FlowLayoutStore.cs 头注释）里，
    /// 这一节承担"随用户"栏：与流程无关、与"这台机器的操作者习惯"绑定——
    /// 换个方案、换个流程都不该丢，随软件配置落盘，不进 .vms。
    /// </summary>
    public class FlowCanvasSettings
    {
        /// <summary>
        /// 是否显示端口数据线。默认 <b>false</b>（历史行为，用户口径：
        /// "端口之间的连线默认不显示，只显示执行顺序"；非法连线不受此开关影响）。
        /// </summary>
        public bool ShowDataLinks { get; set; } = false;

        /// <summary>
        /// 是否显示执行顺序链。默认 <b>true</b>（历史行为：顺序链是画布的主视觉）。
        /// </summary>
        public bool ShowOrderLinks { get; set; } = true;
    }

    /// <summary>
    /// 工具箱的「随用户」态（AppConfig.json 的 <c>Toolbox</c> 节）。
    ///
    /// 为什么落在这里：收藏是"这台机器的操作者习惯"——换方案、换流程、重启软件都不该丢，
    /// 但它又不属于任何一份 .vms（图纸是给别的机器也能跑的），所以随软件配置落盘。
    /// 键取 <c>ToolItemModel.ModuleTypeName</c>（程序集限定类型名）：显示名可以改、
    /// 分类可以调，只有类型名是插件的稳定身份。
    /// </summary>
    public class ToolboxSettings
    {
        /// <summary>
        /// 收藏的算子 <c>ModuleTypeName</c> 清单。
        /// 读取侧（ToolViewModel）会拿当前已注册插件做一次存在性过滤——
        /// 插件升级/卸载后老配置里的类型名会失效，留着只会在界面上多出一个拖不动的死条目。
        /// </summary>
        public List<string> Favorites { get; set; } = new();
    }

    /// <summary>
    /// 帮助手册的阅读态（AppConfig.json 的 <c>Help</c> 节）。
    ///
    /// 为什么开发章节要留开关（而不是"干脆不打进 DLL"）：插件手册随插件程序集内嵌分发，
    /// 软件在启动时把所有插件的手册合并成一本书（见 <c>HelpCatalogService</c>）。
    /// "方便以后全局合并"要求<b>加载永远是全量的</b>——同一个 DLL 在不同配置下交出两份不同的目录，
    /// 接手合并的人就无法假设"这本书是完整的"。所以裁剪发生在<b>显示</b>层而不是加载层：
    /// 开发章节照常收集、照常进目录，只是默认不在目录里显示。
    ///
    /// 默认 <c>false</c>（= 现场操作员看到的那本书与改版之前逐字一致）：
    /// 一次升级不该把内部实现细节忽然摊给产线上查手册的人。
    /// </summary>
    public class HelpSettings
    {
        /// <summary>
        /// 是否在帮助目录里显示<b>开发章节</b>（文件名带 <c>dev-</c> 标记的那些页，
        /// 讲实现原理、宿主契约与移植清单，见 <c>HelpCatalogService.DeveloperPageMark</c>）。
        /// </summary>
        public bool ShowDeveloperDocs { get; set; } = false;
    }

    /// <summary>
    /// 并行执行全局设置（二期）。装配链：AppSettingsService.Load()/Save() 末尾把
    /// FailFastByDefault / ForceSequential 灌进静态 <c>GlobalParallelConfig</c>（运行期解析，改配置下一轮生效）。
    /// </summary>
    public class ParallelExecutionSettings
    {
        /// <summary>
        /// 并行分支业务失败（步骤 Success=false）时默认是否取消其余分支
        /// （FailFastMode=Inherit 的容器取此值）。默认 false = 一期行为。
        /// FailFast 语义（评审高危 3 定死）：只做提前取消 + 容器终态标 Failed，
        /// 不产生异常、不上抛、不置会话 Faulted。
        /// </summary>
        public bool FailFastByDefault { get; set; } = false;

        /// <summary>
        /// 进程级总闸：true = 所有并行组退化顺序执行（现场排障一键回退）。
        /// 默认 false = 真并发；写入 AppConfig.json 的 <c>ParallelExecution.ForceSequential</c>，
        /// 启动/保存时经 <c>AppSettingsService.SyncParallelConfig</c> 灌进 <c>GlobalParallelConfig</c>，
        /// 改完重启（或触发一次配置保存）生效、无需重编译。
        /// </summary>
        public bool ForceSequential { get; set; } = false;
    }
}
