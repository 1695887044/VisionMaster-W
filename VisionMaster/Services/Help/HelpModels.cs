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

        /// <summary>搜索命中的起始位置（-1 = 无命中）与长度；只在搜索过滤生成的副本上写过</summary>
        private int _matchIndex = -1;
        private int _matchLength;

        /// <summary>节点标题（目录树显示文字）</summary>
        public string Title { get; init; } = string.Empty;

        /// <summary>Font Awesome 图标码点（空 = 不显示图标）</summary>
        public string Icon { get; init; } = string.Empty;

        /// <summary>角标文字（如"自动生成"；空 = 不显示）</summary>
        public string? Badge { get; init; }

        /// <summary>是否有角标（界面绑它控制显隐，省一个转换器）</summary>
        public bool HasBadge => !string.IsNullOrEmpty(Badge);

        /// <summary>
        /// 本页是不是<b>开发章节</b>（讲原理/契约/移植清单的那类页）。
        /// 判据来自文件名里的 <c>dev-</c> 标记，由 <see cref="HelpCatalogService"/> 在收集时打标。
        ///
        /// 为什么标在节点而不是 <see cref="HelpPage"/> 上：显隐发生在目录树上（"这一条目给不给看"），
        /// 正文本身不需要知道自己给谁看；<c>HelpPage</c> 是跨搜索副本共用的引用，
        /// 挂它上面会让"复制节点"多一个必须同步的字段（挂节点上同样要同步，见 HelpViewModel 的两处克隆）。
        /// </summary>
        public bool IsDevDoc { get; init; }

        /// <summary>子节点（空 = 叶子节点，此时 <see cref="Page"/> 非空）</summary>
        public List<HelpNode> Children { get; init; } = new();

        /// <summary>本节点对应的帮助页（仅叶子节点非空）</summary>
        public HelpPage? Page { get; init; }

        /// <summary>
        /// 子项数量文字（分组/插件行右侧那个计数）。
        /// 叶子节点返回空串——界面上用 <see cref="HasChildCount"/> 控显隐，同样是为了不引转换器。
        /// </summary>
        public string ChildCountText => Children.Count > 0 ? Children.Count.ToString() : string.Empty;

        public bool HasChildCount => Children.Count > 0;

        // ==================================================================
        //  搜索命中高亮
        //
        //  为什么切成三段而不是写个"关键字加底色"的转换器
        //  ---------
        //  高亮要在 TextBlock 的**行内**换底色，转换器只能整段换色、换不了局部底色；
        //  要么上 attached property 造 Inlines，要么由数据侧把"命中前/命中段/命中后"算好。
        //  后者零转换器、XAML 里就是三个 TextBlock，且过滤树本来就是一次性构建的，
        //   substring 的代价花不掉什么（与"分支换色用 RelativeSource 不用转换器"同一口径）。
        // ==================================================================

        /// <summary>命中前的那截标题（无命中时 = 整条标题）</summary>
        public string TitleHead => _matchIndex < 0 ? Title : Title.Substring(0, _matchIndex);

        /// <summary>命中的那截标题（带底色显示；无命中时空串）</summary>
        public string TitleMatch => _matchIndex < 0 ? string.Empty : Title.Substring(_matchIndex, _matchLength);

        /// <summary>命中后的那截标题</summary>
        public string TitleTail => _matchIndex < 0
            ? string.Empty
            : Title.Substring(Math.Min(_matchIndex + _matchLength, Title.Length));

        public bool HasMatch => _matchIndex >= 0;

        /// <summary>标记命中位置（搜索过滤在副本上调用）。</summary>
        public void MarkMatch(int index, int length)
        {
            if (index < 0 || index > Title.Length)
                return;

            _matchIndex = index;
            _matchLength = Math.Min(Math.Max(0, length), Title.Length - index);
            foreach (var name in new[] { nameof(TitleHead), nameof(TitleMatch), nameof(TitleTail), nameof(HasMatch) })
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

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
    /// 一页帮助正文的来源档位——决定页眉那枚徽章的**样式**（三档三种画法）。
    ///
    /// 为什么不直接看 <see cref="HelpPage.Source"/> 的文字判断：
    /// 那个字段装的是"软件内置"或插件**显示名**，显示名改一次、判据就错一档（与
    /// "引用一律用 Id 不用显示名"同一条口径）；自动生成页更是与插件自带页共用插件名做 Source。
    /// </summary>
    public enum HelpSourceKind
    {
        /// <summary>宿主自带手册（主色淡底徽章）</summary>
        Host,

        /// <summary>插件自带手册（灰底徽章）</summary>
        Plugin,

        /// <summary>还没写手册、由元数据拼出来的兜底页（虚线边框徽章）</summary>
        Auto,
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

        /// <summary>
        /// 来源档位（决定页眉徽章画法）。默认按插件算：宿主页与自动生成页都在
        /// <see cref="HelpCatalogService"/> 里显式覆盖，新增来路时不至于静默掉进某一档。
        /// </summary>
        public HelpSourceKind Kind { get; init; } = HelpSourceKind.Plugin;
    }
}
