﻿﻿﻿﻿﻿﻿using System;
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
        public IVariable OriginalModel { get; set; }

        private bool _isNetwork;
        /// <summary>
        /// 是否为网络变量（VariableType.Communication）
        /// </summary>
        public bool IsNetwork
        {
            get => _isNetwork;
            set => SetProperty(ref _isNetwork, value);
        }

        private string? _sourceLabel;
        /// <summary>
        /// 来源标签：本地变量="本地"，网络变量=所属连接名（UI 徽章显示用）
        /// </summary>
        public string? SourceLabel
        {
            get => _sourceLabel;
            set => SetProperty(ref _sourceLabel, value);
        }

        private string? _address;
        /// <summary>
        /// 设备地址（网络变量才有，如 40100 / MW10 / DB1.DBW0）
        /// </summary>
        public string? Address
        {
            get => _address;
            set => SetProperty(ref _address, value);
        }

        private bool _isConnected;
        /// <summary>
        /// 连接在线状态（网络变量；本地变量恒 true）
        /// </summary>
        public bool IsConnected
        {
            get => _isConnected;
            set => SetProperty(ref _isConnected, value);
        }

        private string _scanGroupText = string.Empty;
        /// <summary>
        /// 扫描组显示文案（仅网络变量根节点用）：
        /// 模型里空组名表示"默认组"，界面上要显示成"默认组"而不是空白，故由 VM 抄写时归一化。
        /// </summary>
        public string ScanGroupText
        {
            get => _scanGroupText;
            set
            {
                if (SetProperty(ref _scanGroupText, value))
                    ScanGroupChanged?.Invoke(this);
            }
        }

        /// <summary>
        /// 该变量所属连接的可选扫描组名（含"默认组"，恒在首位）。
        /// 本地变量为空集合 → 列上不渲染下拉框。
        /// </summary>
        public ObservableCollection<string> ScanGroupOptions { get; } = new();

        /// <summary>
        /// 扫描组变更回调：由 VM 注入。
        /// 节点在 Core 工程、不认识通信管理器，"回写模型 + 重注册轮询"只能由上层做。
        /// </summary>
        public Action<VariableNode>? ScanGroupChanged { get; set; }

        /// <summary>
        /// 是否显示「扫描组」列内容（仅网络变量的根节点；数组子节点不显示）
        /// </summary>
        public bool ShowScanGroup => IsRootNode && IsNetwork;

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

        /// <summary>
        /// "初始值"列的类型安全代理（A1）：
        /// 旧绑定直写 IVariable.DefaultValue(object)，任何字符串都被原样塞进模型——
        /// 给 int 变量填 "abc" 也"保存成功"，恢复初始值时污染值流入算子端口才爆炸。
        /// 现按节点 DataType 转换：合法才写模型，非法抛异常交由绑定引擎标记校验失败（模型保持干净）。
        /// 数组类型不走此入口（走"编辑集合"对话框）。
        /// </summary>
        public string DefaultValueText
        {
            get => OriginalModel?.DefaultValue?.ToString() ?? "";
            set
            {
                var model = OriginalModel;
                if (model == null || DataType == null) return;

                Type t = Nullable.GetUnderlyingType(DataType) ?? DataType;
                if (t.IsArray) return; // 数组初始值只走"编辑集合"

                object? converted;
                if (string.IsNullOrWhiteSpace(value))
                    converted = t.IsValueType ? Activator.CreateInstance(t) : null;
                else
                {
                    try { converted = Convert.ChangeType(value, t); }
                    catch (Exception ex)
                    {
                        throw new ArgumentException($"'{value}' 不是有效的 {t.Name} 值：{ex.Message}");
                    }
                }

                model.DefaultValue = converted;
                ChildDefaultValue = converted;
                RaisePropertyChanged(nameof(ChildDefaultValue));
            }
        }

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
