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
    }
}
