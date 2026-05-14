using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace VisionMaster.Models
{
    public class WatchItemModel : BindableBase
    {
        // 标记当前这个监视项的类型
        public WatchItemType ItemType { get; set; }

        // ================= 算子相关配置 =================
        public Guid StepId { get; set; }
        public string StepName { get; set; }
        public string PortName { get; set; }
        public bool IsInput { get; set; }

        // ================= 全局变量配置 =================
        public string GlobalVariableName { get; set; }
    }

    public enum WatchItemType
    {
        PluginAll,       // 监视整个算子的所有引脚
        PluginPort,      // 监视算子的某一个特定引脚
        GlobalVariable   // 监视全局变量
    }
}
