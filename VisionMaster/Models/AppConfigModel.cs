using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.Models
{
    /// <summary>
    /// 软件级方案清单条目（AppConfig.json，独立于任何 .vms 文件）
    /// </summary>
    public class AppSolutionEntry
    {
        /// <summary>序号（UI 展示用，不持久化，由清单顺序决定）</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public int Index { get; set; }

        /// <summary>方案名称（默认取文件名）</summary>
        public string Name { get; set; }

        /// <summary>注释</summary>
        public string Comment { get; set; } = "";

        /// <summary>方案文件完整路径</summary>
        public string Path { get; set; }
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
    }
}
