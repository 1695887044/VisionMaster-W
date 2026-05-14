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
    }
}
