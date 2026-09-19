﻿﻿﻿﻿﻿﻿﻿﻿using System;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.Models
{
    /// <summary>
    /// 监视项模型
    /// 用于调试时实时查看变量或端口的值
    /// </summary>
    public class WatchItemModel : BindableBase
    {
        /// <summary>
        /// 监视项类型
        /// </summary>
        public WatchItemType ItemType { get; set; }

        /// <summary>
        /// 步骤ID（算子相关）
        /// </summary>
        public Guid StepId { get; set; }

        /// <summary>
        /// 步骤名称（算子相关）
        /// </summary>
        public string StepName { get; set; }

        /// <summary>
        /// 端口名称（算子相关）
        /// </summary>
        public string PortName { get; set; }

        /// <summary>
        /// 是否为输入端口（算子相关）
        /// </summary>
        public bool IsInput { get; set; }

        /// <summary>
        /// 全局变量名称（全局变量相关）。
        /// 仅作展示与旧数据兼容——<b>寻址请用 <see cref="VariableId"/></b>：
        /// 变量改名后本字段会被级联刷新，但任何"按名字找变量"的代码都会在改名瞬间失联。
        /// </summary>
        public string GlobalVariableName { get; set; }

        /// <summary>
        /// 全局变量的稳定身份（全局变量相关；算子/端口监视项恒为 <see cref="Guid.Empty"/>）。
        ///
        /// 为什么监视项也要存 Id：监视栏的解析逻辑过去是
        /// <c>GlobalVariables.FirstOrDefault(gv =&gt; gv.Name == GlobalVariableName)</c>，
        /// 变量一改名监视项立刻变成"(未知变量)"，而且不报错。改按 Id 解析后，
        /// 改名只影响展示文案，监视链路不再断。
        /// 旧方案文件里没有这个字段 → 反序列化后为 Guid.Empty，级联时按旧名兜底自愈。
        /// </summary>
        public Guid VariableId { get; set; }
    }

    /// <summary>
    /// 监视项类型枚举
    /// </summary>
    public enum WatchItemType
    {
        /// <summary>
        /// 监视整个算子的所有引脚
        /// </summary>
        PluginAll,

        /// <summary>
        /// 监视算子的某一个特定引脚
        /// </summary>
        PluginPort,

        /// <summary>
        /// 监视全局变量
        /// </summary>
        GlobalVariable
    }
}
