using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Media.Imaging;

namespace VisionMaster.Services.Help
{
    /// <summary>
    /// 帮助目录树节点（分组 → 插件/手册 → 页，三级；叶子节点带 <see cref="Page"/>）。
    ///
    /// 为什么用一棵通用递归树而不是"分组/插件/页"三个具体类型
    /// ---------
    /// 界面侧只需要一棵 <c>TreeView</c> + 一个递归 DataTemplate，宿主手册与插件手册
    /// 深度还不一样（宿主手册只有两级）。三个类型要么让模板写三份、要么各留一个可空子集合，
    /// 都不如"有没有 Children 决定是不是叶子"清楚。
    /// </summary>
    public sealed class HelpNode : INotifyPropertyChanged
    {
        private bool _isExpanded;

        /// <summary>节点标题（目录树显示文字）</summary>
        public string Title { get; init; } = string.Empty;

        /// <summary>Font Awesome 图标码点（空 = 不显示图标）</summary>
        public string Icon { get; init; } = string.Empty;

        /// <summary>角标文字（如"自动生成"；空 = 不显示）</summary>
        public string? Badge { get; init; }

        /// <summary>是否有角标（界面绑它控制显隐，省一个转换器）</summary>
        public bool HasBadge => !string.IsNullOrEmpty(Badge);

        /// <summary>子节点（空 = 叶子节点，此时 <see cref="Page"/> 非空）</summary>
        public List<HelpNode> Children { get; init; } = new();

        /// <summary>本节点对应的帮助页（仅叶子节点非空）</summary>
        public HelpPage? Page { get; init; }

        /// <summary>
        /// 是否展开。
        /// 可写且带通知：目录树的 <c>TreeViewItem.IsExpanded</c> 与它双向绑定——用户展开/收起的状态
        /// 因此存在节点上，滚动虚拟化回收行容器后再生成，展开状态不会丢。
        /// </summary>
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded == value)
                    return;
                _isExpanded = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public override string ToString() => Title;
    }

    /// <summary>
    /// 一页帮助正文。
    ///
    /// <see cref="LoadMarkdown"/> 是"现取"而不是"存字符串"：目录在启动自检链里合并，
    /// 那一刻只登记每页的标题与取数口，正文等到用户点开才读——插件多了以后，
    /// 启动阶段不必把所有手册正文都读进内存。
    /// </summary>
    public sealed class HelpPage
    {
        public string Title { get; init; } = string.Empty;

        /// <summary>正文 Markdown 取数口（内嵌资源 / 插件接口现取 / 自动生成）</summary>
        public Func<string> LoadMarkdown { get; init; } = () => string.Empty;

        /// <summary>
        /// 图片解析口：正文里的相对路径（如 <c>images/flow.png</c>）→ 位图。
        /// 拿不到返回 null，渲染器降级成文字（不抛、不空白）。
        /// </summary>
        public Func<string, BitmapSource?>? LoadImage { get; init; }

        /// <summary>来源说明（"软件内置" / 插件显示名），显示在页眉</summary>
        public string Source { get; init; } = string.Empty;
    }
}
