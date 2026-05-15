﻿﻿﻿﻿﻿﻿﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.Models
{
    /// <summary>
    /// 变量节点模型
    /// 用于在变量绑定面板中展示变量的树形结构
    /// </summary>
    public class VariableNode : BindableBase
    {
        /// <summary>
        /// 原始变量模型引用
        /// </summary>
        public GlobalVariableModel OriginalModel { get; set; }

        /// <summary>
        /// 变量名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 数据类型
        /// </summary>
        public Type DataType { get; set; }

        /// <summary>
        /// 类型名称（字符串表示）
        /// </summary>
        public string TypeName { get; set; }

        /// <summary>
        /// 变量描述
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 原始值
        /// </summary>
        public object RawValue { get; set; }

        /// <summary>
        /// 显示值（格式化后）
        /// </summary>
        public string DisplayValue
        {
            get
            {
                if (RawValue is Array arr) return $"Array [{arr.Length}]";
                return RawValue?.ToString() ?? "Null";
            }
        }

        /// <summary>
        /// 数组子节点默认值
        /// </summary>
        public object ChildDefaultValue { get; set; }

        /// <summary>
        /// 数组子节点值
        /// </summary>
        public object ChildValue { get; set; }

        /// <summary>
        /// 子节点集合（用于数组类型展开）
        /// </summary>
        public ObservableCollection<VariableNode> Children { get; } = new();

        /// <summary>
        /// 是否为容器节点（有子节点）
        /// </summary>
        public bool IsContainer => Children.Count > 0;

        /// <summary>
        /// 层级（0为根节点，1为子节点）
        /// </summary>
        public int Level { get; set; }

        /// <summary>
        /// 是否为根节点
        /// </summary>
        public bool IsRootNode => Level == 0;

        /// <summary>
        /// 是否为子节点
        /// </summary>
        public bool IsChildNode => Level == 1;

        /// <summary>
        /// 是否为可编辑节点（根节点且非数组）
        /// </summary>
        public bool IsEditableNode => Level == 0 && DataType != null && !DataType.IsArray;

        /// <summary>
        /// 是否为数组根节点
        /// </summary>
        public bool IsArrayRootNode => Level == 0 && DataType != null && DataType.IsArray;

        private bool _isExpanded;
        /// <summary>
        /// 是否展开（用于树形控件）
        /// </summary>
        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }
    }
}
