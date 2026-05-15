﻿﻿﻿﻿﻿using Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.Models
{
    /// <summary>
    /// 树节点模型
    /// 用于在变量绑定面板中显示可用变量树
    /// </summary>
    public class TreeNodeModel
    {
        /// <summary>
        /// 节点名称（如 "视觉工具" 或 "模板匹配_1"）
        /// </summary>
        public string NodeName { get; set; }

        /// <summary>
        /// 图标编码（Font Awesome 图标）
        /// </summary>
        public string IconCode { get; set; }

        /// <summary>
        /// 子节点集合
        /// </summary>
        public ObservableCollection<TreeNodeModel> Children { get; set; } = new ObservableCollection<TreeNodeModel>();

        /// <summary>
        /// 可用端口集合（该节点提供的输出端口）
        /// </summary>
        public ObservableCollection<IOutputPort> AvailablePorts { get; set; } = new ObservableCollection<IOutputPort>();
    }
}
