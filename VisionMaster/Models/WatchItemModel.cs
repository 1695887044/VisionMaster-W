using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace VisionMaster.Models
{
    public class WatchItemModel
    {
        public Guid StepId { get; set; }
        public string StepName { get; set; } // 冗余字段，方便脱机查看

        /// <summary>
        /// 如果为空，代表监控该算子【全部】的输入输出。
        /// 如果有值，代表精确监控单个引脚。
        /// </summary>
        public string PortName { get; set; }
        public bool IsInput { get; set; }    // 仅在 PortName 不为空时生效
    }

    public enum WatchMode
    {
        [Description("监控所有输入和输出")]
        AllInputsAndOutputs, 
        [Description("仅监控所有输出 (通常输入是不变的，大家只关心输出结果)")]
        AllOutputsOnly,      
        [Description("监控指定引脚")]
        SpecificPorts       
    }
}
