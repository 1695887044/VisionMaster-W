﻿﻿﻿﻿﻿﻿﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.Models
{
    /// <summary>
    /// 工具项模型
    /// 表示一个可用于流程设计的算子/工具
    /// </summary>
    public record class ToolItemModel
    {
        /// <summary>
        /// 工具唯一标识
        /// </summary>
        public Guid Id { get; init; }

        /// <summary>
        /// 图标编码（Font Awesome 图标）
        /// </summary>
        public string Icon { get; init; }

        /// <summary>
        /// 分类名称（如 "视觉工具"、"逻辑控制"）
        /// </summary>
        public string Category { get; init; }

        /// <summary>
        /// 工具名称（显示名称）
        /// </summary>
        public string Name { get; init; }

        /// <summary>
        /// 工具描述
        /// </summary>
        public string Description { get; init; }

        /// <summary>
        /// 是否为容器类型（如 If、While、For）
        /// </summary>
        public bool IsContainer { get; init; } = false;

        /// <summary>
        /// 模块分组名称
        /// </summary>
        public string ModuleGroup { get; init; } = "Tool";

        /// <summary>
        /// 模块类型名称（完整类型名，用于反射创建实例）
        /// </summary>
        public string ModuleTypeName { get; init; }

        /// <summary>
        /// 输入端口定义列表
        /// </summary>
        public List<PortDefinition>? InputDefinitions { get; set; }

        /// <summary>
        /// 输出端口定义列表
        /// </summary>
        public List<PortDefinition>? OutputDefinitions { get; set; }
    }

    /// <summary>
    /// 工具分组模型
    /// 用于在工具面板中按分类组织工具
    /// </summary>
    public class ToolGroupModel
    {
        /// <summary>
        /// 分组名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 该分组下的所有工具
        /// </summary>
        public ObservableCollection<ToolItemModel> Children { get; set; } = new();
    }
}
