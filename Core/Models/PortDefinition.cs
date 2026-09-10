﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.Models
{
    /// <summary>
    /// 端口定义
    /// 描述插件的输入/输出端口信息
    /// </summary>
    public class PortDefinition
    {
        /// <summary>
        /// 端口名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 端口描述
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 数据类型名称（如 "System.Double"）
        /// </summary>
        public string DataTypeName { get; set; }

        /// <summary>
        /// 是否为功能性枚举端口
        /// 标记为 true 时，该端口会在变量绑定界面显示预设选项，不需要链接上游变量
        /// </summary>
        public bool IsFunctionalEnum { get; set; }

        /// <summary>
        /// 预设选项列表（当 IsFunctionalEnum 为 true 时使用）
        /// </summary>
        public List<string> PresetOptions { get; set; } = new List<string>();
    }
}
