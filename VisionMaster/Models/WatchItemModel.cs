﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
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
        /// 全局变量名称（全局变量相关）
        /// </summary>
        public string GlobalVariableName { get; set; }
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
