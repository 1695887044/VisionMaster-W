using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UI.Attributes;
using UI.Icons;

namespace VisionMaster.Models
{
    /// <summary>
    /// 解决方案模型
    /// 表示一个完整的视觉检测方案，包含多个流程
    /// </summary>
    public class SolutionModel : BindableBase
    {
        private string solutionName = $"解决方案-{DateTime.Now.ToString("yyyyMMddHHmmss")}";

        /// <summary>
        /// 解决方案名称
        /// </summary>
        [Required]
        [Icon(IconCode = SvgIcons.Icon_Solution)]
        [SuperDisplay(Name = "解决方案名称")]
        public string SolutionName
        {
            get { return solutionName; }
            set
            {
                solutionName = value;
                RaisePropertyChanged();
            }
        }

        private Double version = 1.0;

        /// <summary>
        /// 版本号
        /// </summary>
        [SuperDisplay(Name = "版本号")]
        public Double Version
        {
            get { return version; }
            set
            {
                version = value;
                RaisePropertyChanged();
            }
        }

        private string solutionFilePath;

        /// <summary>
        /// 方案文件完整路径（打开/保存成功后记录，供状态栏与标题展示）
        /// </summary>
        public string SolutionFilePath
        {
            get { return solutionFilePath; }
            set
            {
                solutionFilePath = value;
                RaisePropertyChanged();
            }
        }

        private SolutionConfig config = new SolutionConfig();

        /// <summary>
        /// 解决方案级系统配置（面板布局、图像视图布局等，随 .vms 持久化）
        /// </summary>
        public SolutionConfig Config
        {
            get { return config; }
            set
            {
                config = value;
                RaisePropertyChanged();
            }
        }

        /// <summary>
        /// 流程集合
        /// Newtonsoft.ObjectCreationHandling.Replace：反序列化时整体替换集合（而非向默认集合追加），
        /// 否则字段初始化器预置的 GoHome/MainTask 会与 JSON 里的流程叠加导致重复。
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public ObservableCollection<FlowModel> Flows { get; set; } =
            new()
            {
                new FlowModel() { FlowName = "GoHome", Description="回原" },
                new FlowModel() { FlowName = "MainTask" , Description="主任务" }
            };

        /// <summary>
        /// 通讯配置集合（跟着解决方案走）
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public ObservableCollection<Communications.CommunicationConfig> CommunicationConfigs { get; set; } = new();

        /// <summary>
        /// 全局变量集合（跟着解决方案走）
        /// E3：变量持久化已统一走 VariableSnapshots（DTO 多态可控），本属性全库无人写入、
        /// 恒为空却仍参与序列化（且 IVariable 多态 $type 正是数组白名单雷区），
        /// 保留属性兼容旧文件读取，但不再落盘
        /// </summary>
        [JsonIgnore]
        public ObservableCollection<IVariable> GlobalVariables { get; set; } = new();

        /// <summary>
        /// 变量持久化快照（本地+网络变量统一经此随方案落盘；
        /// 加载时由 VariablePersistenceService 重建到 Workspace.GlobalVariables）
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<VariableDto> VariableSnapshots { get; set; } = new();

        /// <summary>
        /// 动态监视项列表（用于调试时查看变量值）
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public ObservableCollection<WatchItemModel> WatchItems { get; set; } = new();
    }
}
