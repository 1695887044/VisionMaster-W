using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
        [Icon(SvgIcons.Icon_Solution)]
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

        /// <summary>
        /// 流程集合
        /// </summary>
        public ObservableCollection<FlowModel> Flows { get; set; } =
            new()
            {
                new FlowModel() { FlowName = "GoHome", Description="回原" },
                new FlowModel() { FlowName = "MainTask" , Description="主任务" }
            };

        /// <summary>
        /// 通讯配置集合（跟着解决方案走）
        /// </summary>
        public ObservableCollection<Communications.CommunicationConfig> CommunicationConfigs { get; set; } = new();

        /// <summary>
        /// 全局变量集合（跟着解决方案走）
        /// </summary>
        public ObservableCollection<GlobalVariableModel> GlobalVariables { get; set; } = new();

        /// <summary>
        /// 动态监视项列表（用于调试时查看变量值）
        /// </summary>
        public ObservableCollection<WatchItemModel> WatchItems { get; set; } = new();
    }
}
