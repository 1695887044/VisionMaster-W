using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Commands;
using Prism.Mvvm;
using UI.Attributes;

namespace VisionMaster.Models
{
    /// <summary>
    /// 「运行窗口落在哪块屏」下拉里的一项。
    ///
    /// 为什么不直接把 <see cref="Services.ScadaMonitor"/> 摆进下拉：那是服务层的"一块屏"
    /// （物理矩形 + 设备名），不含任何展示口径。界面要的是"叫什么、显示成什么、鼠标停上去提示什么"——
    /// 把 Label 拼装写在弹窗那边，服务层就不必认识中文，也不必为了换个说法而改。
    /// </summary>
    public sealed class ScadaMonitorOption
    {
        public ScadaMonitorOption(string deviceName, string label, string detail)
        {
            DeviceName = deviceName;
            Label = label;
            Detail = detail;
        }

        /// <summary>设备名（如 <c>\\.\DISPLAY2</c>）；<b>空串 = 跟随主屏</b>。这就是落进配置的那个值</summary>
        public string DeviceName { get; }

        /// <summary>下拉里显示的文字</summary>
        public string Label { get; }

        /// <summary>鼠标停上去的提示（放设备名，方便对得上系统显示设置）</summary>
        public string Detail { get; }
    }

    /// <summary>
    /// 「系统设置」弹窗（<c>CardPropertyGrid</c> 反射生成正文）的**编辑草稿**。
    ///
    /// 【为什么两个旧弹窗合并成一个】「运行窗口设置」与「系统参数设置」写的是同一份东西
    /// （软件级偏好 → 程序目录 <c>AppConfig.json</c>），口径也逐条相同：草稿 + 确定才落盘、
    /// 只把真变了的项写进审计、写盘失败留在窗上让人重试。分成两个弹窗等于把
    /// "读配置 → 编辑 → 算变更 → Save → 失败留窗 → 落审计"这整条链抄了两份。
    ///
    /// 【为什么用草稿，而不是把 <see cref="AppConfigModel"/> 直接交给属性面板】
    ///   ① 属性面板是**就地编辑**：点"取消"不会回滚。草稿 + 确认才写回，取消 = 整份丢弃。
    ///   ② 配置模型是**序列化契约**，不该为了"弹窗长什么样"往它身上贴 <c>[SuperDisplay]</c>；
    ///      显示名 / 分组 / 顺序属于界面知识。
    ///   ③ 数值项在草稿里是**字符串**：属性面板的 NumericBox 遇到清空会写 NaN、绑定失败后
    ///      属性保持旧值——界面上空着、存下去的却是上一次的数。收字符串 + 确认时统一解析，
    ///      才能做到"宁可不让他确定，也不能让他以为改成了"（口径与旧 ScadaSystemParametersViewModel 一致）。
    ///
    /// 【新增一项要改哪里】只改这个类：加一个带 <see cref="SuperDisplayAttribute"/> 的属性。
    /// 视图里没有字段清单，XAML 一行都不用动；数值项另需在 VM 的确认链里多一行解析。
    /// </summary>
    public sealed class SystemSettingsDraft : BindableBase
    {
        // ------------------------------------------------------------------
        //  页签名（也是弹窗入口分页用的键：ShellViewModel 按它指定落在哪一页）
        // ------------------------------------------------------------------

        public const string GroupRunWindow = "运行窗口";
        public const string GroupSafety = "安全与留档";
        public const string GroupGallery = "画布";
        public const string GroupLog = "日志";
        public const string GroupServices = "服务与启动";
        public const string GroupParallel = "并行执行";
        public const string GroupSpeech = "语音播报";

        // 二级分组（卡片式的 Expander）。同一页签里要么都有第二段、要么都没有——
        // 混着写会让没写第二段的那几个落进一个叫"默认分组"的折叠头里。
        private const string SubSession = "会话";
        private const string SubRetention = "历史留档";
        private const string SubAccount = "账号";
        private const string SubHttpServer = "HTTP 收图服务";
        private const string SubCameraServer = "网络相机收图服务";
        private const string SubStartup = "启动";

        private bool _independentWindow;
        private string _monitorLabel = string.Empty;
        private string _idleTimeoutText = string.Empty;
        private string _auditRetentionText = string.Empty;
        private string _alarmRetentionText = string.Empty;
        private bool _galleryEnabled = true;
        private bool _galleryClearOnRun;
        private bool _galleryKeepHistory;
        private bool _galleryKeepInjectedHistory;
        private bool _galleryShowThumbnailStrip = true;
        private bool _galleryIncludeRealtime = true;
        private bool _galleryCollectAllPorts;
        private bool _gallerySaveOnAlarm;
        private string _galleryTotalLimitText = string.Empty;
        private string _galleryPerFlowLimitText = string.Empty;
        private string _alarmFrameRetentionText = string.Empty;
        private string _minPasswordLengthText = string.Empty;

        private string _logRetentionText = string.Empty;
        private string _logDirectory = string.Empty;
        private LogFileLevel _logFileLevel = LogFileLevel.Info;
        private string _logPanelMaxRowsText = string.Empty;
        private bool _openLogFolder;

        private bool _httpEnabled = true;
        private string _httpHost = string.Empty;
        private string _httpPortText = string.Empty;
        private string _httpToken = string.Empty;
        private string _httpTimeoutText = string.Empty;

        private bool _cameraEnabled = true;
        private string _cameraHost = string.Empty;
        private string _cameraPortText = string.Empty;
        private string _cameraToken = string.Empty;

        private bool _communicationStartupCheck = true;

        private bool _parallelFailFastByDefault;
        private bool _parallelForceSequential;

        /// <param name="monitors">
        /// 目标屏候选（第一项固定是「跟随主屏」）。由调用方在**打开弹窗这一刻**从
        /// <c>ScadaMonitors.All()</c> 取一次——它是"这台机器现在接着什么"，不是配置；
        /// 为它做实时刷新只会引入一条没人测过的路径。
        /// </param>
        /// <param name="openLogFolder">
        /// 「打开日志目录」按钮的动作。属性面板的 <c>[Command]</c> 绑的是**绑定对象自己**上的命令
        /// （<c>CommandProcessor</c> 用 <c>context.BindingSource</c>），所以命令挂在草稿上、
        /// 真正的动作由宿主注入——草稿不认识 <c>Process.Start</c>，断言里也好替换。
        /// </param>
        public SystemSettingsDraft(
            IEnumerable<ScadaMonitorOption>? monitors,
            Action? openLogFolder = null)
        {
            Monitors = (monitors ?? Enumerable.Empty<ScadaMonitorOption>()).ToList();
            OpenLogFolderCommand = openLogFolder == null ? null : new DelegateCommand(openLogFolder);
        }

        /// <summary>
        /// 目标屏候选。<b>刻意不贴 [SuperDisplay]</b>：它是 <see cref="ComboSourceAttribute"/>
        /// 的候选来源，不是可编辑字段（贴了会渲染成一行只读文本）。
        /// </summary>
        public IReadOnlyList<ScadaMonitorOption> Monitors { get; }

        /// <summary>下拉候选文案（与 <see cref="MonitorLabel"/> 同一个字符串，反查设备名走 Monitors）</summary>
        public IReadOnlyList<string> MonitorLabels => Monitors.Select(m => m.Label).ToList();

        // ------------------------------------------------------------------
        //  1. 运行窗口
        // ------------------------------------------------------------------

        /// <summary>
        /// 独立运行窗口。勾选 = 带标题栏、可与主界面（含视觉图像）并排，开发调试用；
        /// 不勾选 = 无边框铺满主屏，现场生产用。
        ///
        /// 草稿里是 bool 而不是 <see cref="ScadaRunWindowMode"/>：界面要回答的只有一个二选一，
        /// 属性面板对 bool 给的是开关（EnumGenerator 给的是下拉，两行字还得挤在项里）。
        /// **写回时仍然写枚举**——落盘形状（整数 0/1）不能变，见 AppConfigModel.RunWindowMode。
        /// </summary>
        [SuperDisplay(
            Name = "独立运行窗口",
            Group = new[] { GroupRunWindow },
            GroupOrder = "1",
            Order = 0,
            Description = "勾选 = 带标题栏、可自由拖动缩放，与主界面（含视觉图像）并排显示，开发调试用；"
                        + "不勾选 = 无边框铺满整个主界面，运行画面就是唯一界面，现场触摸屏 / 一体机跑生产用")]
        public bool IndependentWindow
        {
            get => _independentWindow;
            set => SetProperty(ref _independentWindow, value);
        }

        /// <summary>
        /// 目标屏：存的是候选**文案**（下拉的 SelectedItem），确认时由 VM 反查回设备名。
        ///
        /// 为什么不是设备名直接上下拉：<see cref="ComboSourceAttribute"/> 的候选是纯字符串，
        /// 值就是显示名本身（它的注释写明了"不需要显示名 ↔ 值两套"）。要保住"落盘仍是设备名"，
        /// 就得在这一层做一次反查——映射只在一处，比把两套值散到界面上好。
        /// </summary>
        [SuperDisplay(
            Name = "运行窗口落在哪块屏",
            Group = new[] { GroupRunWindow },
            GroupOrder = "1",
            Order = 1,
            Description = "多显示器现场可把运行画面单独放到操作员正对的那块屏；单屏机器保持「跟随主屏」即可。"
                        + "指定的显示器不在线时运行期自动回落主屏")]
        [ComboSource(nameof(MonitorLabels))]
        public string MonitorLabel
        {
            get => _monitorLabel;
            set => SetProperty(ref _monitorLabel, value);
        }

        // ------------------------------------------------------------------
        //  2. 安全与留档
        // ------------------------------------------------------------------

        /// <summary>空闲多久自动登出（分钟）。0 或负数 = 不自动登出</summary>
        [SuperDisplay(
            Name = "空闲自动登出",
            Group = new[] { GroupSafety, SubSession },
            GroupOrder = "2",
            Order = 0,
            Description = "多久没人操作就自动退出登录，权限回到「操作员」。填 0 或负数 = 不自动登出"
                        + "（无人值守的机台用；默认 10 分钟）")]
        [RegexValidation(@"^-?\d+$", "请填整数（0 或负数 = 不自动登出）")]
        public string IdleTimeoutText
        {
            get => _idleTimeoutText;
            set => SetProperty(ref _idleTimeoutText, value);
        }

        /// <summary>操作审计保留天数。0 或负数 = 永不清理</summary>
        [SuperDisplay(
            Name = "操作审计保留",
            Group = new[] { GroupSafety, SubRetention },
            GroupOrder = "2",
            Order = 0,
            Description = "程序目录 Audit 下的操作凭证流水，超过保留期的在下次写入时自动删除。"
                        + "填 0 或负数 = 永不清理（默认 90 天）")]
        [RegexValidation(@"^-?\d+$", "请填整数（0 或负数 = 永不清理）")]
        public string AuditRetentionText
        {
            get => _auditRetentionText;
            set => SetProperty(ref _auditRetentionText, value);
        }

        /// <summary>报警历史保留天数。0 或负数 = 永不清理</summary>
        [SuperDisplay(
            Name = "报警历史保留",
            Group = new[] { GroupSafety, SubRetention },
            GroupOrder = "2",
            Order = 1,
            Description = "程序目录 Alarms 下的报警流水（含面板导出的快照），超过保留期的在下次写入时自动删除。"
                        + "填 0 或负数 = 永不清理（默认 365 天）")]
        [RegexValidation(@"^-?\d+$", "请填整数（0 或负数 = 永不清理）")]
        public string AlarmRetentionText
        {
            get => _alarmRetentionText;
            set => SetProperty(ref _alarmRetentionText, value);
        }

        /// <summary>账号密码最小长度</summary>
        [SuperDisplay(
            Name = "密码最小长度",
            Group = new[] { GroupSafety, SubAccount },
            GroupOrder = "2",
            Order = 0,
            Description = "新建账号 / 改密码时要求的最少字符数。填 0 或负数按默认值 4 处理"
                        + "（这条配置的语义是「至少几位」，不是「允许空密码」）")]
        [RegexValidation(@"^-?\d+$", "请填整数（0 或负数 = 用默认值 4）")]
        public string MinPasswordLengthText
        {
            get => _minPasswordLengthText;
            set => SetProperty(ref _minPasswordLengthText, value);
        }

        // ------------------------------------------------------------------
        //  3. 画布（视觉图像页 · 图像列表）
        // ------------------------------------------------------------------

        /// <summary>是否启用图集采集</summary>
        [SuperDisplay(
            Name = "启用画布采集",
            Group = new[] { GroupGallery },
            GroupOrder = "3",
            Order = 0,
            Description = "关掉后流程照跑，只是不再收集图像")]
        public bool GalleryEnabled
        {
            get => _galleryEnabled;
            set => SetProperty(ref _galleryEnabled, value);
        }

        /// <summary>每轮流程开始时是否清空整张画布</summary>
        [SuperDisplay(
            Name = "每轮开始清空整张画布",
            Group = new[] { GroupGallery },
            GroupOrder = "3",
            Order = 1,
            Description = "勾选 = 清空全部（含其它流程的图）；不勾选 = 只覆盖「即将运行的这条流程」上一轮的图")]
        public bool GalleryClearOnRun
        {
            get => _galleryClearOnRun;
            set => SetProperty(ref _galleryClearOnRun, value);
        }

        /// <summary>同一位置是否保留历史帧</summary>
        [SuperDisplay(
            Name = "同一位置保留历史帧",
            Group = new[] { GroupGallery },
            GroupOrder = "3",
            Order = 2,
            Description = "不勾选 = 每个「步骤.端口」只留最新一张；插件注入的图不受此影响")]
        public bool GalleryKeepHistory
        {
            get => _galleryKeepHistory;
            set => SetProperty(ref _galleryKeepHistory, value);
        }

        /// <summary>插件注入的图是否保留历史帧（"收容"）</summary>
        [SuperDisplay(
            Name = "保留插件注入图的历史帧",
            Group = new[] { GroupGallery },
            GroupOrder = "3",
            Order = 3,
            Description = "不勾选（默认）= 每个窗口只显示该流程最新一张，内存 = 窗口数 × 1 张；"
                        + "勾选 = 每张都留下，可回看历史，但循环运行时内存与列表会持续增长")]
        public bool GalleryKeepInjectedHistory
        {
            get => _galleryKeepInjectedHistory;
            set => SetProperty(ref _galleryKeepInjectedHistory, value);
        }

        /// <summary>是否显示画布下方缩略图条</summary>
        [SuperDisplay(
            Name = "显示缩略图条",
            Group = new[] { GroupGallery },
            GroupOrder = "3",
            Order = 4,
            Description = "仅在「保留插件注入图的历史帧」打开时生效：关掉可把下方条子占的高度全让给大图"
                        + "（多格布局下差别明显）")]
        public bool GalleryShowThumbnailStrip
        {
            get => _galleryShowThumbnailStrip;
            set => SetProperty(ref _galleryShowThumbnailStrip, value);
        }

        /// <summary>是否收录插件实时预览发布的图</summary>
        [SuperDisplay(
            Name = "收录插件注入的图像",
            Group = new[] { GroupGallery },
            GroupOrder = "3",
            Order = 3,
            Description = "插件 PublishPreview 出来的图进入画布列表")]
        public bool GalleryIncludeRealtime
        {
            get => _galleryIncludeRealtime;
            set => SetProperty(ref _galleryIncludeRealtime, value);
        }

        /// <summary>是否兜底采集所有输出端口</summary>
        [SuperDisplay(
            Name = "兜底采集所有输出端口",
            Group = new[] { GroupGallery },
            GroupOrder = "3",
            Order = 4,
            Description = "插件没主动注入的图也收；这些图没有说明信息")]
        public bool GalleryCollectAllPorts
        {
            get => _galleryCollectAllPorts;
            set => SetProperty(ref _galleryCollectAllPorts, value);
        }

        /// <summary>报警触发时是否把当前图集整批落盘</summary>
        [SuperDisplay(
            Name = "报警时留存当前图集",
            Group = new[] { GroupGallery },
            GroupOrder = "3",
            Order = 5,
            Description = "落盘到程序目录 AlarmFrames，用于事后复盘")]
        public bool GallerySaveOnAlarm
        {
            get => _gallerySaveOnAlarm;
            set => SetProperty(ref _gallerySaveOnAlarm, value);
        }

        /// <summary>图集总张数上限（跨全部流程）。0 或负数 = 不限制</summary>
        [SuperDisplay(
            Name = "总张数上限",
            Group = new[] { GroupGallery },
            GroupOrder = "3",
            Order = 6,
            Description = "全部流程加起来最多留多少张，超出按最旧优先覆盖（每张约 15MB 量级，别配太大）。"
                        + "填 0 或负数 = 不限制（默认 300）")]
        [RegexValidation(@"^-?\d+$", "请填整数（0 或负数 = 不限制）")]
        public string GalleryTotalLimitText
        {
            get => _galleryTotalLimitText;
            set => SetProperty(ref _galleryTotalLimitText, value);
        }

        /// <summary>图集单流程张数上限。0 或负数 = 不限制</summary>
        [SuperDisplay(
            Name = "单流程张数上限",
            Group = new[] { GroupGallery },
            GroupOrder = "3",
            Order = 7,
            Description = "图集按流程分组，每个流程一组最多留多少张。填 0 或负数 = 不限制（默认 50）")]
        [RegexValidation(@"^-?\d+$", "请填整数（0 或负数 = 不限制）")]
        public string GalleryPerFlowLimitText
        {
            get => _galleryPerFlowLimitText;
            set => SetProperty(ref _galleryPerFlowLimitText, value);
        }

        /// <summary>报警图集（AlarmFrames）保留天数。0 或负数 = 永不清理</summary>
        [SuperDisplay(
            Name = "报警图集保留天数",
            Group = new[] { GroupGallery },
            GroupOrder = "3",
            Order = 8,
            Description = "程序目录 AlarmFrames/yyyy-MM-dd 下的报警留存图（每张 ~15MB 量级），"
                        + "超过保留期的在启动时与保存设置时自动删除。填 0 或负数 = 永不清理（默认 90 天）")]
        [RegexValidation(@"^-?\d+$", "请填整数（0 或负数 = 永不清理）")]
        public string AlarmFrameRetentionText
        {
            get => _alarmFrameRetentionText;
            set => SetProperty(ref _alarmFrameRetentionText, value);
        }

        // ------------------------------------------------------------------
        //  4. 日志
        // ------------------------------------------------------------------

        /// <summary>日志文件保留天数。0 或负数 = 永不清理</summary>
        [SuperDisplay(
            Name = "日志保留天数",
            Group = new[] { GroupLog },
            GroupOrder = "4",
            Order = 0,
            Description = "程序目录 Logs 下的 yyyy-MM-dd.log，超过保留期的在启动时与保存设置时自动删除。"
                        + "填 0 或负数 = 永不清理（默认 30 天）")]
        [RegexValidation(@"^-?\d+$", "请填整数（0 或负数 = 永不清理）")]
        public string LogRetentionText
        {
            get => _logRetentionText;
            set => SetProperty(ref _logRetentionText, value);
        }

        /// <summary>日志目录。空串 = 程序目录下的 Logs</summary>
        [SuperDisplay(
            Name = "日志目录",
            Group = new[] { GroupLog },
            GroupOrder = "4",
            Order = 1,
            Description = "留空 = 程序目录下的 Logs（默认）。保存时会先试建目录并写一个探针文件，"
                        + "不可写则拒绝保存——否则文件日志会静默消失，只剩日志面板")]
        public string LogDirectory
        {
            get => _logDirectory;
            set => SetProperty(ref _logDirectory, value);
        }

        /// <summary>落盘的最小级别（面板不受影响）</summary>
        [SuperDisplay(
            Name = "落盘最小级别",
            Group = new[] { GroupLog },
            GroupOrder = "4",
            Order = 2,
            Description = "磁盘紧张时只落警告与错误。日志面板仍然收全部级别，这一项只影响写进 .log 文件的内容")]
        public LogFileLevel LogFileLevel
        {
            get => _logFileLevel;
            set => SetProperty(ref _logFileLevel, value);
        }

        /// <summary>日志面板保留条数。0 或负数 = 不限制</summary>
        [SuperDisplay(
            Name = "面板保留条数",
            Group = new[] { GroupLog },
            GroupOrder = "4",
            Order = 3,
            Description = "日志面板只在内存里留最近多少条，超出按最旧丢弃（文件里的完整历史不受影响）。"
                        + "填 0 或负数 = 不限制（不推荐：连续跑几天会吃内存；默认 5000）")]
        [RegexValidation(@"^-?\d+$", "请填整数（0 或负数 = 不限制）")]
        public string LogPanelMaxRowsText
        {
            get => _logPanelMaxRowsText;
            set => SetProperty(ref _logPanelMaxRowsText, value);
        }

        /// <summary>
        /// 「打开日志目录」按钮。值本身没有意义——<see cref="BoolStateGenerator"/> 见到
        /// <see cref="CommandAttribute"/> 就把这一行渲染成按钮，命令绑到
        /// <see cref="OpenLogFolderCommand"/>（挂在草稿自己身上，动作由宿主注入）。
        /// </summary>
        [SuperDisplay(
            Name = "打开日志目录",
            Group = new[] { GroupLog },
            GroupOrder = "4",
            Order = 4,
            Description = "用资源管理器打开当前生效的日志目录（按上方的「日志目录」计算）")]
        [Command(Command = nameof(OpenLogFolderCommand))]
        public bool OpenLogFolder
        {
            get => _openLogFolder;
            set => SetProperty(ref _openLogFolder, value);
        }

        /// <summary>「打开日志目录」的动作（由宿主注入；断言里可为 null）</summary>
        public DelegateCommand? OpenLogFolderCommand { get; }

        // ------------------------------------------------------------------
        //  5. 服务与启动
        // ------------------------------------------------------------------

        /// <summary>是否启用 HTTP 收图服务</summary>
        [SuperDisplay(
            Name = "启用",
            Group = new[] { GroupServices, SubHttpServer },
            GroupOrder = "5",
            Order = 0,
            Description = "网络推送图像 → 跑流程 → 回包。关掉就没有任何入口，等于功能不存在")]
        public bool HttpServerEnabled
        {
            get => _httpEnabled;
            set => SetProperty(ref _httpEnabled, value);
        }

        /// <summary>HTTP 收图服务监听地址</summary>
        [SuperDisplay(
            Name = "监听地址",
            Group = new[] { GroupServices, SubHttpServer },
            GroupOrder = "5",
            Order = 1,
            Description = "0.0.0.0 = 所有网卡；127.0.0.1 = 只收本机（调试用，不暴露到局域网）；"
                        + "双网卡只想让视觉网那侧推图时填该网卡的实际 IP")]
        public string HttpServerHost
        {
            get => _httpHost;
            set => SetProperty(ref _httpHost, value);
        }

        /// <summary>HTTP 收图服务监听端口</summary>
        [SuperDisplay(
            Name = "监听端口",
            Group = new[] { GroupServices, SubHttpServer },
            GroupOrder = "5",
            Order = 2,
            Description = "改这个值时客户端（推图的相机 / PLC / 上位机）的 URL 也要跟着改。1~65535")]
        [RegexValidation(@"^\d+$", "请填 1~65535 的端口号")]
        public string HttpServerPortText
        {
            get => _httpPortText;
            set => SetProperty(ref _httpPortText, value);
        }

        /// <summary>HTTP 收图服务访问令牌（Bearer）</summary>
        [SuperDisplay(
            Name = "访问令牌",
            Group = new[] { GroupServices, SubHttpServer },
            GroupOrder = "5",
            Order = 3,
            Description = "客户端必须在 Authorization: Bearer {令牌} 头里带上它。默认值是公开的占位值，"
                        + "只防误连、不是安全防线；现场投用前请改掉")]
        public string HttpServerToken
        {
            get => _httpToken;
            set => SetProperty(ref _httpToken, value);
        }

        /// <summary>单次请求等待流程结束的超时（毫秒）</summary>
        [SuperDisplay(
            Name = "请求超时（毫秒）",
            Group = new[] { GroupServices, SubHttpServer },
            GroupOrder = "5",
            Order = 4,
            Description = "收图后同步等流程跑完再回包，这个值决定客户端最多等多久。"
                        + "太快会把正常慢流程判成失败；一般取现场最长流程耗时的 2~3 倍（默认 30000）")]
        [RegexValidation(@"^\d+$", "请填整数毫秒数")]
        public string HttpServerTimeoutText
        {
            get => _httpTimeoutText;
            set => SetProperty(ref _httpTimeoutText, value);
        }

        /// <summary>是否启用网络相机收图服务</summary>
        [SuperDisplay(
            Name = "启用",
            Group = new[] { GroupServices, SubCameraServer },
            GroupOrder = "5",
            Order = 5,
            Description = "客户端持续推帧 + 心跳 → 相机帧队列 → 流程消费")]
        public bool CameraServerEnabled
        {
            get => _cameraEnabled;
            set => SetProperty(ref _cameraEnabled, value);
        }

        /// <summary>网络相机收图服务监听地址</summary>
        [SuperDisplay(
            Name = "监听地址",
            Group = new[] { GroupServices, SubCameraServer },
            GroupOrder = "5",
            Order = 6,
            Description = "0.0.0.0 = 所有网卡；127.0.0.1 = 只收本机")]
        public string CameraServerHost
        {
            get => _cameraHost;
            set => SetProperty(ref _cameraHost, value);
        }

        /// <summary>网络相机收图服务监听端口</summary>
        [SuperDisplay(
            Name = "监听端口",
            Group = new[] { GroupServices, SubCameraServer },
            GroupOrder = "5",
            Order = 7,
            Description = "客户端推帧的 URL 要跟着改。1~65535")]
        [RegexValidation(@"^\d+$", "请填 1~65535 的端口号")]
        public string CameraServerPortText
        {
            get => _cameraPortText;
            set => SetProperty(ref _cameraPortText, value);
        }

        /// <summary>网络相机收图服务访问令牌</summary>
        [SuperDisplay(
            Name = "访问令牌",
            Group = new[] { GroupServices, SubCameraServer },
            GroupOrder = "5",
            Order = 8,
            Description = "客户端在 Authorization: Bearer {令牌} 头里带上它。默认值是公开的占位值，投用前请改掉")]
        public string CameraServerToken
        {
            get => _cameraToken;
            set => SetProperty(ref _cameraToken, value);
        }

        /// <summary>启动时是否执行通讯连通性自检</summary>
        [SuperDisplay(
            Name = "启动时通讯连通性自检",
            Group = new[] { GroupServices, SubStartup },
            GroupOrder = "5",
            Order = 9,
            Description = "产线现场可关闭以加快启动。这一项无论开关都只报警告、不拦启动；"
                        + "关闭后主界面出现时不再自动探测设备在线状态")]
        public bool CommunicationStartupCheck
        {
            get => _communicationStartupCheck;
            set => SetProperty(ref _communicationStartupCheck, value);
        }

        // ------------------------------------------------------------------
        //  6. 并行执行（全局默认）
        // ------------------------------------------------------------------

        /// <summary>并行组的「继承全局」默认值</summary>
        [SuperDisplay(
            Name = "失败聚合默认开启",
            Group = new[] { GroupParallel },
            GroupOrder = "6",
            Order = 0,
            Description = "并行组选「继承全局」时取这个值：分支业务失败时默认是否取消其余兄弟分支。"
                        + "默认关（= 历史行为）")]
        public bool ParallelFailFastByDefault
        {
            get => _parallelFailFastByDefault;
            set => SetProperty(ref _parallelFailFastByDefault, value);
        }

        /// <summary>进程级总闸：全部并行组退化顺序执行</summary>
        [SuperDisplay(
            Name = "强制顺序执行（总闸）",
            Group = new[] { GroupParallel },
            GroupOrder = "6",
            Order = 1,
            Description = "现场排障用：打开后所有并行组都退化顺序执行，用来判断问题是不是并发引起的。"
                        + "保存即生效，排障完记得关掉")]
        public bool ParallelForceSequential
        {
            get => _parallelForceSequential;
            set => SetProperty(ref _parallelForceSequential, value);
        }

        // ------------------------------------------------------------------
        //  7. 语音播报（TTS）
        // ------------------------------------------------------------------

        private bool _speechEnabled = true;
        private string _speechRateText = "0";
        private string _speechVoice = string.Empty;
        private string _speechMinSeverityLabel = "警告";

        /// <summary>级别下拉的候选文案（与 <see cref="SpeechMinSeverityLabel"/> 同一个字符串，写回时反查）</summary>
        public IReadOnlyList<string> SpeechSeverityLabels { get; } = new[] { "提示", "警告", "严重" };

        /// <summary>语音播报总开关（顶栏静音仍然独立生效）</summary>
        [SuperDisplay(
            Name = "启用语音播报",
            Group = new[] { GroupSpeech },
            GroupOrder = "7",
            Order = 0,
            Description = "报警与组态动作里配置的「语音播报」是否念出来。关掉后提示音照常；"
                        + "顶栏「静音 N 分钟」期间播报也会一起停（紧急级报警不受静音约束）")]
        public bool SpeechEnabled
        {
            get => _speechEnabled;
            set => SetProperty(ref _speechEnabled, value);
        }

        /// <summary>语速（-10~10，0 = 系统默认）</summary>
        [SuperDisplay(
            Name = "语速",
            Group = new[] { GroupSpeech },
            GroupOrder = "7",
            Order = 1,
            Description = "范围 -10 ~ 10，0 = 系统默认。负数更慢、正数更快；现场听不清报警名时先往负数调")]
        [RegexValidation(@"^-?\d+$", "请填 -10 ~ 10 的整数（0 = 系统默认）")]
        public string SpeechRateText
        {
            get => _speechRateText;
            set => SetProperty(ref _speechRateText, value);
        }

        /// <summary>音色（系统语音名）。空 = 系统默认语音</summary>
        [SuperDisplay(
            Name = "音色",
            Group = new[] { GroupSpeech },
            GroupOrder = "7",
            Order = 2,
            Description = "系统里已安装的语音名（如 Microsoft Huihui Desktop）。留空 = 系统默认语音。"
                        + "填了本机没有的名字会被忽略并落一条 Warn，不会静默变成别的音色")]
        public string SpeechVoice
        {
            get => _speechVoice;
            set => SetProperty(ref _speechVoice, value);
        }

        /// <summary>报警播报的最低级别（组态动作触发的播报不受此限）</summary>
        [SuperDisplay(
            Name = "报警播报最低级别",
            Group = new[] { GroupSpeech },
            GroupOrder = "7",
            Order = 3,
            Description = "只影响「报警自动播报」：低于这一级的不念，避免提示级刷屏。"
                        + "组态动作里的「语音播报」不受此门槛限制（那是操作员主动触发的）")]
        [ComboSource(nameof(SpeechSeverityLabels))]
        public string SpeechMinSeverityLabel
        {
            get => _speechMinSeverityLabel;
            set => SetProperty(ref _speechMinSeverityLabel, value);
        }
    }
}
