using System;
using System.Collections.Generic;
using System.Linq;
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

        // 二级分组（卡片式的 Expander）。同一页签里要么都有第二段、要么都没有——
        // 混着写会让没写第二段的那几个落进一个叫"默认分组"的折叠头里。
        private const string SubSession = "会话";
        private const string SubRetention = "历史留档";

        private bool _independentWindow;
        private string _monitorLabel = string.Empty;
        private string _idleTimeoutText = string.Empty;
        private string _auditRetentionText = string.Empty;
        private string _alarmRetentionText = string.Empty;
        private bool _galleryEnabled = true;
        private bool _galleryClearOnRun;
        private bool _galleryKeepHistory;
        private bool _galleryIncludeRealtime = true;
        private bool _galleryCollectAllPorts;
        private bool _gallerySaveOnAlarm;
        private string _galleryTotalLimitText = string.Empty;
        private string _galleryPerFlowLimitText = string.Empty;

        /// <param name="monitors">
        /// 目标屏候选（第一项固定是「跟随主屏」）。由调用方在**打开弹窗这一刻**从
        /// <c>ScadaMonitors.All()</c> 取一次——它是"这台机器现在接着什么"，不是配置；
        /// 为它做实时刷新只会引入一条没人测过的路径。
        /// </param>
        public SystemSettingsDraft(IEnumerable<ScadaMonitorOption>? monitors)
        {
            Monitors = (monitors ?? Enumerable.Empty<ScadaMonitorOption>()).ToList();
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
    }
}
