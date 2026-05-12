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
    /// 解决方案
    /// </summary>
    public class SolutionModel : BindableBase
    {
        private string solutionName = $"解决方案-{DateTime.Now.ToString("yyyyMMddHHmmss")}";

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

        public ObservableCollection<FlowModel> Flows { get; set; } =
            new()
            {
                new FlowModel() { FlowName = "GoHome",Description="程序回原点流程-默认设置" },
                new FlowModel() { FlowName = "MainTask" ,Description="程序主任务流程-默认设置" }
            };
    }
}
