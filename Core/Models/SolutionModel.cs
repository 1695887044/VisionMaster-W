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
        ///
        /// <b>不落盘</b>（S13-f）：它是运行期 UI 提示，不是方案内容。
        /// ① 打开与保存的路径都由宿主显式赋值（ShellViewModel / SolutionListViewModel），
        ///    从来没有"从文件里读回来"这条需求；
        /// ② 它必须在落盘<b>之后</b>才能知道（保存成功才有路径），若参与序列化，
        ///    磁盘上那份永远比内存里少一个字段——退出时的"内存 vs 磁盘逐字比较"
        ///    会把一次没改过的方案判成"有未保存改动"而多存一份草稿。
        /// </summary>
        [JsonIgnore]
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

        private Scada.ScadaDocument scada = new();

        /// <summary>
        /// SCADA 组态文档（画面 / 图元 / 绑定），随 .vms 一起落盘。
        ///
        /// 只挂"根对象"而不是直接挂画面集合：报表、报警、权限、画面跳转这些
        /// 组态内容将来都往 <see cref="Scada.ScadaDocument"/> 上加字段，
        /// 不必回来改 <see cref="SolutionModel"/> 这个被全工程引用的类型。
        ///
        /// ObjectCreationHandling.Replace：反序列化时整体替换成文件里的实例，
        /// 而不是往默认实例上"灌"（避免字段初始化器与文件内容叠加）。
        /// 属性永不为 null——setter 兜底，因为 .vms 是可被人工编辑的文件，
        /// 一句 "Scada": null 不该让整个方案打不开（后续 EnsureIdentity 会直接解引用）。
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public Scada.ScadaDocument Scada
        {
            get { return scada; }
            set
            {
                scada = value ?? new Scada.ScadaDocument();
                RaisePropertyChanged();
            }
        }
    }
}
