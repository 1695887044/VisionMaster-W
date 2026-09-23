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
    }
}
