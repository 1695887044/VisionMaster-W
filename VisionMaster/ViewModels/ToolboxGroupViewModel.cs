using System;
using System.Collections.ObjectModel;
using System.Windows.Media;
using Prism.Mvvm;
using VisionMaster.Models;

namespace VisionMaster.ViewModels
{
    /// <summary>
    /// 工具箱面板里的一个分组（一个分类行，或置顶的「收藏」行）。
    ///
    /// 为什么要从 <c>ToolGroupModel</c>（Core\Models\ToolItemModel.cs）换过来：
    /// 那条模型只有 Name + Children，而新面板的分组头还要"专属图标 + 专属色"两样观感，
    /// 而这两样是**宿主侧的展示知识**（分类名→图标/颜色的映射在
    /// <see cref="VisionMaster.Helpers.ToolboxCategoryVisuals"/>），不该塞进 Core 的数据模型里。
    /// 所以分组在 VM 层重新包一层——注意**只包分组，不包条目**：
    /// 条目绑定的必须仍是原始 <c>ToolItemModel</c> 实例，否则 gong 的拖拽负载会变成 wrapper、
    /// 放置端（FlowCanvasView.FindDraggedTool 硬认 ToolItemModel）拿到 null，拖拽直接断链。
    /// </summary>
    public class ToolboxGroupViewModel : BindableBase
    {
        /// <summary>分组标题（分类名；收藏组固定为"收藏"）</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>分组图标（Font Awesome 6 Pro Solid 码点）</summary>
        public string Icon { get; init; } = string.Empty;

        /// <summary>分组专属色（已冻结的画刷；本仓先例 ShellViewModel.RunStateBrush）</summary>
        public Brush AccentBrush { get; init; } = Brushes.Gray;

        /// <summary>是不是置顶的「收藏」分组（面板据此决定分组头要不要显示成星形）</summary>
        public bool IsFavorites { get; init; }

        /// <summary>该分组下的算子（元素类型必须是原始 ToolItemModel——拖拽链路的硬约束）</summary>
        public ObservableCollection<ToolItemModel> Children { get; } = new();

        /// <summary>分组内算子数（分组头右侧的数量胶囊）</summary>
        public int Count => Children.Count;

        private bool _isExpanded;

        /// <summary>
        /// 分组是否展开（TwoWay 绑到 Expander.IsExpanded）。
        /// 默认折叠——分类下动辄十几个算子，全展开会把面板撑成一长条；
        /// 「收藏」组例外（默认展开，它是用户自己挑出来的少数几条）。
        /// </summary>
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (SetProperty(ref _isExpanded, value))
                    ExpandedChanged?.Invoke(this, value);
            }
        }

        /// <summary>
        /// 展开态变化回调：父 VM 据此把状态记进字典，**重建分组时不丢**。
        /// 注意父 VM 在"过滤重建期"会忽略这个回调（否则搜索时的强制展开会污染用户的折叠偏好）。
        /// </summary>
        public Action<ToolboxGroupViewModel, bool>? ExpandedChanged { get; init; }
    }
}
