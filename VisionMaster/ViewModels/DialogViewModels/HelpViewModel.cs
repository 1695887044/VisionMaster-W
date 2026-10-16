using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Core.Interfaces;
using Prism.Commands;
using Prism.Dialogs;
using Prism.Mvvm;
using VisionMaster.Services;
using VisionMaster.Services.Help;

namespace VisionMaster.ViewModels.DialogViewModels
{
    /// <summary>
    /// 「帮助手册」弹窗：左目录、右正文、工具栏（隐藏目录 / 上一步 / 打印 / 字号）。
    ///
    /// 内容是<b>合并出来的一本</b>：宿主自带手册 + 各插件自带手册 + 未写手册插件的自动生成页
    /// （见 <see cref="HelpCatalogService"/>）。窗口本身不认识"插件"这个概念，
    /// 只认一棵目录树和一个正文加载器——将来加"全文搜索 / 最近浏览"也在这层做。
    ///
    /// 几个刻意的取舍
    /// ---------
    /// ① <b>正文按需渲染 + 逐页缓存</b>：切回已看过的页不重新解析 md。
    ///    改字号时清缓存重渲染（字号进了 FlowDocument 的字号属性，不清就新旧混排）。
    /// ② <b>分组/插件节点只展开、不换正文</b>：点目录里的"图像处理"不该把正文清空，
    ///    用户找的是下面那个算子。
    /// ③ <b>回退是"页"的历史，不是树的选中历史</b>：用户从 A 页跳到 B 页再回退，
    ///    应当回到 A 页正文，目录选中态跟着同步（两边不一致是最容易被吐槽的一类小毛病）。
    /// ④ <b>打印用"重新渲染一份"</b>，不打印屏幕上那份：打印要重设页宽页高，
    ///    直接改屏幕文档会把它排版弄乱（回来滚到哪儿都变了）。
    /// </summary>
    public class HelpViewModel : BindableBase, IDialogAware
    {
        /// <summary>字号档位：小 / 标准 / 大（打印沿用当前档位）</summary>
        private static readonly (string Label, double Size)[] FontPresets =
        {
            ("小", 12.5),
            ("标准", 14),
            ("大", 17),
        };

        private readonly HelpCatalogService _catalog;
        private readonly ILogService _log;
        private readonly List<HelpPage> _history = new();

        /// <summary>
        /// 软件配置（只为"开发章节显隐"这一项服务，改完即落盘）。
        /// <b>可以为 null</b>：断言宿主（HelpProbe）直接 <c>new HelpViewModel(catalog, log)</c>，
        /// 它不该在 <c>bin</c> 里写出 AppConfig.json（密闭性口径见 AppSettingsService 的注释）；
        /// 为 null 时开关照样能切，只是不持久化、下次打开回到默认档。
        /// </summary>
        private readonly AppSettingsService? _settings;

        /// <summary>
        /// 未经搜索过滤的完整目录（<see cref="HelpCatalogService"/> 合并出来的那棵树）。
        /// 搜索时 <see cref="Nodes"/> 换成过滤副本，清空搜索就整棵换回来——
        /// 原树上的展开状态因此不会被搜索污染（用户搜一遍出来，目录全展开是最烦的）。
        /// </summary>
        private readonly List<HelpNode> _fullTree = new();

        /// <summary>
        /// 按"开发章节显隐"裁过一遍的目录（搜索过滤作用于<b>这一棵</b>，不是 <see cref="_fullTree"/>）。
        ///
        /// 为什么要单独一层而不是在 <see cref="HelpCatalogService"/> 里就不收开发页：
        /// 加载永远是全量的（这是内嵌手册能支撑"全局合并"的前提），显隐只是视图态；
        /// 两层过滤（显隐 → 搜索）各自独立，切换开关时不会把用户的搜索词一起吃掉。
        /// </summary>
        private readonly List<HelpNode> _visibleTree = new();

        /// <summary>
        /// 逐页渲染缓存。存的是"文档 + 该文档对应的 markdown"这一对：
        /// 打印要重新渲染一份，用的必须是 <b>当前页</b> 的正文——只缓存文档、正文另存一个字段的话，
        /// "看 A → 看 B → 回退到 A（命中缓存）→ 打印"就会打出 B 的正文而标题写 A。
        /// </summary>
        private readonly Dictionary<HelpPage, (FlowDocument Document, string Markdown)> _renderCache = new();

        private HelpNode? _selectedNode;
        private HelpPage? _currentPage;
        private string _currentMarkdown = string.Empty;
        private string _query = string.Empty;
        private FlowDocument? _document;
        private double _tocWidth = 300;
        private double _lastVisibleTocWidth = 300;
        private double _bodyFontSize = 14;
        private string _fontSizeLabel = "标准";
        private bool _navigating;
        private bool _showDevDocs;

        public HelpViewModel(HelpCatalogService catalog, ILogService log, AppSettingsService? settings = null)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _settings = settings;
            _showDevDocs = settings?.Current?.Help?.ShowDeveloperDocs ?? false;

            ToggleTocCommand = new DelegateCommand(ToggleToc);
            BackCommand = new DelegateCommand(GoBack, () => _history.Count > 1);
            PrintCommand = new DelegateCommand(Print);
            CloseCommand = new DelegateCommand(OnEscape);
        }

        #region 绑定属性

        /// <summary>目录根（分组级集合）</summary>
        public ObservableCollection<HelpNode> Nodes { get; } = new();

        /// <summary>目录选中项。分组节点只展开；页节点换正文。</summary>
        public HelpNode? SelectedNode
        {
            get => _selectedNode;
            set
            {
                if (SetProperty(ref _selectedNode, value))
                    OnSelectedNodeChanged();
            }
        }

        /// <summary>右侧正文（渲染好的 FlowDocument）</summary>
        public FlowDocument? Document
        {
            get => _document;
            private set => SetProperty(ref _document, value);
        }

        /// <summary>正文页眉：当前页标题</summary>
        public string PageTitle { get; private set; } = "帮助手册";

        /// <summary>正文页眉：来源（软件内置 / 插件名）</summary>
        public string PageSource { get; private set; } = string.Empty;

        /// <summary>
        /// 正文页眉徽章的档位（Host / Plugin / Auto），视图按它给三种画法
        /// （主色淡底 / 灰底 / 虚线边框）。绑的是枚举的字符串形式而不是枚举本身：
        /// XAML 里 DataTrigger 直接写 <c>Value="Auto"</c> 就成，不必为一个枚举再引命名空间和转换器。
        /// </summary>
        public string PageSourceKind { get; private set; } = string.Empty;

        /// <summary>底部状态栏文字</summary>
        public string StatusText => $"{_catalog.Summary}（{(ShowDevDocs ? "已显示" : "已隐藏")}）　·　Ctrl+P 打印当前页，Esc 关闭";

        /// <summary>
        /// 是否在目录里显示<b>开发章节</b>（工具栏那个勾选框）。
        ///
        /// 口径：<b>只裁显示、不裁加载</b>。开发页始终由 <see cref="HelpCatalogService"/> 全量收进目录，
        /// 这里换的是一棵裁过的视图树——所以切开关不需要重新合并（不重读资源、不重跑 <c>Build</c>），
        /// 也保证"同一份 DLL 在任何配置下收纳到的手册是同一本"。
        /// </summary>
        public bool ShowDevDocs
        {
            get => _showDevDocs;
            set
            {
                if (!SetProperty(ref _showDevDocs, value))
                    return;

                _settings?.SyncHelpSettings(value);
                RebuildVisibleTree();
                ApplyFilter(_query);
                RaisePropertyChanged(nameof(StatusText));
            }
        }

        /// <summary>
        /// 目录列宽。与 <c>ColumnDefinition.Width</c> <b>双向绑定</b>：用户拖分隔条改的是这一列，
        /// 拖完必须写回 VM —— 否则「隐藏目录 → 再显示」会跳回一个和用户刚才拉的不一样的宽度。
        /// </summary>
        public GridLength TocColumnWidth
        {
            get => new GridLength(_tocWidth);
            set
            {
                var width = value.IsAbsolute ? value.Value : _tocWidth;
                if (Math.Abs(width - _tocWidth) < 0.5)
                    return;

                if (width > 1)
                    _lastVisibleTocWidth = width;
                _tocWidth = width;

                RaisePropertyChanged();
                RaisePropertyChanged(nameof(IsTocVisible));
                RaisePropertyChanged(nameof(ToggleTocLabel));
            }
        }

        /// <summary>目录列是否显示（CHM 的「隐藏」按钮）</summary>
        public bool IsTocVisible => _tocWidth > 1;

        public string ToggleTocLabel => IsTocVisible ? "隐藏目录" : "显示目录";

        /// <summary>字号下拉的选项文字</summary>
        public string[] FontSizeOptions { get; } = FontPresets.Select(p => p.Label).ToArray();

        /// <summary>
        /// 当前字号档位（下拉双向绑定，赋值即应用）。
        /// 为什么用"档位名"而不是直接绑字号数字：界面只给三档，绑数字反而多一个"用户手输 13.7 怎么办"的分支。
        ///
        /// <b>忽略空值写入</b>：ComboBox 在 ItemsSource 到位之前会把 SelectedItem 写回 null
        /// （控件自身的初始化行为，与 VM 无关）。不挡掉的话，开局这一下就会把 VM 里的档位清成 null，
        /// 界面上显示的是下拉的第一项、而实际生效的字号还是默认值——两边不一致且没人会往这上面想。
        /// 真正的"重新推一次选中态"放在 <see cref="OnDialogOpened"/> 末尾（那里 ItemsSource 已就位）。
        /// </summary>
        public string SelectedFontSize
        {
            get => _fontSizeLabel;
            set
            {
                if (string.IsNullOrEmpty(value) || !SetProperty(ref _fontSizeLabel, value))
                    return;
                ApplyFontSize(value);
            }
        }

        #endregion

        #region 命令

        public DelegateCommand ToggleTocCommand { get; }
        public DelegateCommand BackCommand { get; }
        public DelegateCommand PrintCommand { get; }
        public DelegateCommand CloseCommand { get; }

        /// <summary>隐藏 / 显示目录（宽度归零但不丢，再点回来是原来的宽度）</summary>
        private void ToggleToc()
        {
            if (IsTocVisible)
            {
                _lastVisibleTocWidth = _tocWidth;
                _tocWidth = 0;
            }
            else
            {
                _tocWidth = _lastVisibleTocWidth > 1 ? _lastVisibleTocWidth : 300;
            }

            RaisePropertyChanged(nameof(TocColumnWidth));
            RaisePropertyChanged(nameof(IsTocVisible));
            RaisePropertyChanged(nameof(ToggleTocLabel));
        }

        /// <summary>
        /// Esc 的去向：正在搜索时**先清空搜索**，再按一次才关窗。
        /// 不这么分的话，在搜索框里按 Esc 会把整个帮助窗关掉——刚打的字连带窗口一起没了。
        /// </summary>
        private void OnEscape()
        {
            if (IsSearching)
            {
                Query = string.Empty;
                return;
            }
            RequestClose.Invoke(ButtonResult.Cancel);
        }

        #endregion

        #region 目录搜索

        /// <summary>
        /// 目录搜索关键字（搜索框双向绑定，Ctrl+F 聚焦）。改一次就重算一遍过滤树：
        /// 目录总量是"分组 → 插件 → 页"约 300 个节点，逐次全量重算比留一份增量索引更省心，
        /// 也快得看不出延迟。
        ///
        /// <b>只搜标题、不搜正文</b>是刻意的：正文由 <see cref="HelpPage.LoadMarkdown"/> 现取
        /// （见 HelpCatalogService 的"启动只登记标题"），要搜正文就得启动时把上万行手册读进内存建索引。
        /// </summary>
        public string Query
        {
            get => _query;
            set
            {
                var query = value ?? string.Empty;
                if (!SetProperty(ref _query, query))
                    return;
                ApplyFilter(query);
            }
        }

        /// <summary>是否处于搜索状态（Esc 分支与"清空搜索"提示都看它）</summary>
        public bool IsSearching => _query.Trim().Length > 0;

        /// <summary>搜索状态下一颗命中都没有：目录区给空态，别留一片白让人以为程序卡住</summary>
        public bool HasNoMatch { get; private set; }

        /// <summary>命中了几页（搜索时显示在搜索框下方，给用户一个"就这些"的落点）</summary>
        public int MatchPageCount { get; private set; }

        private void ApplyFilter(string rawQuery)
        {
            var needle = rawQuery.Trim();
            Nodes.Clear();

            if (needle.Length == 0)
            {
                foreach (var group in _visibleTree)
                    Nodes.Add(group);
            }
            else
            {
                foreach (var group in _visibleTree)
                {
                    var hit = MatchNode(group, needle);
                    if (hit != null)
                        Nodes.Add(hit);
                }
            }

            // 选中态按"正文是哪一页"重新认领：过滤把树换了一遍，但 HelpPage 引用没换
            SelectCurrentPage();

            MatchPageCount = CountPages(Nodes);
            HasNoMatch = needle.Length > 0 && Nodes.Count == 0;
            RaisePropertyChanged(nameof(IsSearching));
            RaisePropertyChanged(nameof(HasNoMatch));
            RaisePropertyChanged(nameof(MatchPageCount));
        }

        /// <summary>
        /// 按 <see cref="ShowDevDocs"/> 重算一遍可见树。
        /// 开着开发章节时<b>直接用原节点</b>（不克隆）：展开状态存在节点上，来回切开关不该把
        /// 用户手工展开的那些分组重置掉；只有隐藏开发页时才克隆出裁过的副本。
        /// </summary>
        private void RebuildVisibleTree()
        {
            _visibleTree.Clear();
            foreach (var group in _fullTree)
            {
                var kept = ShowDevDocs ? group : PruneDevDocs(group);
                if (kept != null)
                    _visibleTree.Add(kept);
            }
        }

        /// <summary>
        /// 裁掉一棵子树里的开发章节页；整支裁空了就返回 null（目录里不留空分组，
        /// 也不留"点不开正文的插件节点"——只有开发页的插件在关闭态就是没内容可看）。
        /// 与 <c>MatchNode</c> 同一套"副本共用 <see cref="HelpPage"/> 引用"的口径。
        ///
        /// **一支子树里一个开发页都没有时返回原节点**，不克隆：展开状态写在节点上，
        /// 开关不该把用户手工展开的分组重置掉，也不该让"清空搜索后节点引用逐字相同"那条契约失效。
        /// </summary>
        private static HelpNode? PruneDevDocs(HelpNode node)
        {
            if (node.Page != null)
                return node.IsDevDoc ? null : node;

            List<HelpNode>? kept = null;
            var changed = false;
            foreach (var child in node.Children)
            {
                var hit = PruneDevDocs(child);
                if (!ReferenceEquals(hit, child)) changed = true;
                if (hit == null) continue;
                kept ??= new List<HelpNode>();
                kept.Add(hit);
            }

            if (kept == null || kept.Count == 0)
                return null;
            if (!changed)
                return node;

            return new HelpNode
            {
                Title = node.Title,
                Icon = node.Icon,
                Badge = node.Badge,
                Children = kept,
            };
        }

        /// <summary>
        /// 过滤出一个节点：本节点标题命中、或子树里有命中才保留，否则返回 null（整支剪掉）。
        ///
        /// 返回的是<b>副本</b>不是原节点：展开状态与高亮位置都要写在节点上，
        /// 直接改原树等于"搜一次就把用户的目录展开状态和上次的命中底色留下来"。
        /// 副本与原节点共用同一个 <see cref="HelpPage"/> 引用——逐页缓存与历史记录都按页引用比对，
        /// 换页对象会让"回退到看过的页"认不出来（HelpProbe 第 5 节钉的就是这条）。
        /// </summary>
        private static HelpNode? MatchNode(HelpNode node, string needle)
        {
            var matchIndex = node.Title.IndexOf(needle, StringComparison.OrdinalIgnoreCase);

            var kept = new List<HelpNode>();
            foreach (var child in node.Children)
            {
                var hit = MatchNode(child, needle);
                if (hit != null)
                    kept.Add(hit);
            }

            // 分组/插件名自己命中、但下面一个条目都没命中：整支原样留下。
            // 只留一个空分组等于把用户正要找的东西藏起来——搜"图像处理"就该看到这个分类里的全部算子。
            if (matchIndex >= 0 && kept.Count == 0 && node.Children.Count > 0)
                kept = CloneSubtree(node.Children, needle);

            if (matchIndex < 0 && kept.Count == 0)
                return null;

            var copy = new HelpNode
            {
                Title = node.Title,
                Icon = node.Icon,
                Badge = node.Badge,
                Page = node.Page,
                IsDevDoc = node.IsDevDoc,
                Children = kept,
            };
            if (kept.Count > 0)
                copy.IsExpanded = true;   // 搜索结果里的分支必须展开，否则命中的页看不见
            if (matchIndex >= 0)
                copy.MarkMatch(matchIndex, needle.Length);
            return copy;
        }

        /// <summary>整棵子树照抄（不过滤），顺带把命中的标题标出来、分支展开</summary>
        private static List<HelpNode> CloneSubtree(List<HelpNode> nodes, string needle)
        {
            var copies = new List<HelpNode>(nodes.Count);
            foreach (var node in nodes)
            {
                var copy = new HelpNode
                {
                    Title = node.Title,
                    Icon = node.Icon,
                    Badge = node.Badge,
                    Page = node.Page,
                    IsDevDoc = node.IsDevDoc,
                    Children = CloneSubtree(node.Children, needle),
                };
                if (copy.Children.Count > 0)
                    copy.IsExpanded = true;
                var index = node.Title.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                    copy.MarkMatch(index, needle.Length);
                copies.Add(copy);
            }
            return copies;
        }

        private static int CountPages(IEnumerable<HelpNode> nodes)
        {
            var count = 0;
            foreach (var node in nodes)
                count += (node.Page != null ? 1 : 0) + CountPages(node.Children);
            return count;
        }

        /// <summary>把目录选中态指回"当前正文那一页"（换树之后按页引用重新认领）。</summary>
        private void SelectCurrentPage()
        {
            var page = _currentPage;
            if (page == null)
                return;
            SelectedNode = FindNodeForPage(page) ?? SelectedNode;
        }

        #endregion

        #region 导航

        private void OnSelectedNodeChanged()
        {
            var page = SelectedNode?.Page;
            if (page == null)
                return;   // 分组/插件节点：交给 TreeView 自己展开

            if (!_navigating)
            {
                if (_history.Count == 0 || !ReferenceEquals(_history[^1], page))
                    _history.Add(page);
                BackCommand.RaiseCanExecuteChanged();
            }

            ShowPage(page);
        }

        private void GoBack()
        {
            if (_history.Count <= 1)
                return;

            _history.RemoveAt(_history.Count - 1);
            var page = _history[^1];

            // 目录选中态跟着回退；_navigating 抑制"再记一次历史"
            _navigating = true;
            try
            {
                SelectedNode = FindNodeForPage(page) ?? SelectedNode;
            }
            finally
            {
                _navigating = false;
            }

            // 兜底：即使树上没找到该页（理论上不会），正文也必须跟着回退
            ShowPage(page);
            BackCommand.RaiseCanExecuteChanged();
        }

        private void ShowPage(HelpPage page)
        {
            _currentPage = page;
            PageTitle = page.Title;
            PageSource = page.Source;
            PageSourceKind = page.Kind.ToString();

            if (!_renderCache.TryGetValue(page, out var entry))
            {
                var markdown = ReadMarkdownSafe(page);
                entry = (MarkdownToFlowDocument.Render(markdown, page.LoadImage, _bodyFontSize), markdown);
                _renderCache[page] = entry;
            }

            // 正文与文档一起从缓存取：打印源（_currentMarkdown）必须跟着当前页走，
            // 不能在"首次渲染"那一刻才赋值（否则回退到已看过的页再打印，打的是上一页的正文）。
            _currentMarkdown = entry.Markdown;
            Document = entry.Document;
            RaisePropertyChanged(nameof(PageTitle));
            RaisePropertyChanged(nameof(PageSource));
            RaisePropertyChanged(nameof(PageSourceKind));
        }

        private string ReadMarkdownSafe(HelpPage page)
        {
            try
            {
                return page.LoadMarkdown() ?? string.Empty;
            }
            catch (Exception ex)
            {
                // 单页读失败只影响这一页：把原因写在正文里（比空白页好排查得多）
                _log.Warn($"[帮助] 手册页「{page.Title}」读取失败：{ex.Message}");
                return $"# {page.Title}\n\n> 本页内容读取失败：{ex.Message}";
            }
        }

        private HelpNode? FindNodeForPage(HelpPage page)
        {
            foreach (var group in Nodes)
            {
                var hit = FindNodeForPage(group, page);
                if (hit != null)
                    return hit;
            }
            return null;
        }

        private static HelpNode? FindNodeForPage(HelpNode node, HelpPage page)
        {
            if (node.Page != null && ReferenceEquals(node.Page, page))
                return node;
            foreach (var child in node.Children)
            {
                var hit = FindNodeForPage(child, page);
                if (hit != null)
                    return hit;
            }
            return null;
        }

        /// <summary>按标题找一层主题节点（插件名 / 分组名），找到就选中它的第一页</summary>
        private HelpNode? FindTopicByTitle(string title)
        {
            foreach (var group in Nodes)
            {
                foreach (var topic in group.Children)
                {
                    if (string.Equals(topic.Title, title, StringComparison.Ordinal))
                        return topic.Children.Count > 0 ? topic.Children[0] : topic;
                }
            }
            return null;
        }

        private static HelpNode? FirstLeaf(IEnumerable<HelpNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (node.Page != null)
                    return node;
                var hit = FirstLeaf(node.Children);
                if (hit != null)
                    return hit;
            }
            return null;
        }

        #endregion

        #region 打印 / 字号

        private void Print()
        {
            if (_currentPage == null)
                return;

            var dialog = new PrintDialog();
            if (dialog.ShowDialog() != true)
                return;

            // 重新渲染一份专供打印：打印要重设页宽页高，改屏幕那份会把阅读位置与排版一起弄乱
            var document = MarkdownToFlowDocument.Render(_currentMarkdown, _currentPage.LoadImage, _bodyFontSize);
            document.PageWidth = dialog.PrintableAreaWidth > 0 ? dialog.PrintableAreaWidth : 793;
            document.PageHeight = dialog.PrintableAreaHeight > 0 ? dialog.PrintableAreaHeight : 1122;
            document.ColumnWidth = document.PageWidth;
            document.PagePadding = new Thickness(56, 56, 56, 64);

            IDocumentPaginatorSource paginatorSource = document;
            dialog.PrintDocument(paginatorSource.DocumentPaginator, $"帮助手册 - {_currentPage.Title}");
        }

        private void ApplyFontSize(string? presetLabel)
        {
            var preset = Array.Find(FontPresets, p => string.Equals(p.Label, presetLabel, StringComparison.Ordinal));
            if (preset.Label == null)
                return;

            _bodyFontSize = preset.Size;

            // 字号进了 FlowDocument 的字号属性，缓存里那些文档还是旧字号——不清就是新旧混排
            _renderCache.Clear();
            if (_currentPage != null)
                ShowPage(_currentPage);
        }

        #endregion

        #region IDialogAware

        public DialogCloseListener RequestClose { get; set; }

        public bool CanCloseDialog() => true;

        public void OnDialogClosed() { }

        /// <summary>
        /// 打开时装配目录。
        /// 正常路径下目录已由启动自检链合并好；这里的 <c>Build</c> 是兜底
        /// （自检项失败只剩警告时目录是空的，就地补一次，别让用户看到空窗口）。
        /// </summary>
        public void OnDialogOpened(IDialogParameters parameters)
        {
            if (!_catalog.IsBuilt)
            {
                try
                {
                    _catalog.Build();
                }
                catch (Exception ex)
                {
                    _log.Warn($"[帮助] 目录合并失败：{ex.Message}");
                }
            }

            // 完整目录留一份底：搜索与显隐换的都是副本，清空搜索要整棵换回来
            _fullTree.Clear();
            _fullTree.AddRange(_catalog.Groups);
            RebuildVisibleTree();

            // 搜索词直接清字段、不走 Query setter：此刻 _fullTree 还没就位，
            // 让 setter 里的 ApplyFilter 空跑一遍会把目录清成空树
            _query = string.Empty;
            RaisePropertyChanged(nameof(Query));
            RaisePropertyChanged(nameof(IsSearching));
            RaisePropertyChanged(nameof(ShowDevDocs));

            Nodes.Clear();
            foreach (var group in _visibleTree)
                Nodes.Add(group);

            MatchPageCount = CountPages(Nodes);
            HasNoMatch = false;
            RaisePropertyChanged(nameof(MatchPageCount));
            RaisePropertyChanged(nameof(HasNoMatch));

            RaisePropertyChanged(nameof(StatusText));
            // 字号下拉的选中态在这里重推一次：此刻 ItemsSource 已就位，绑定能把档位稳落到控件上
            //（ComboBox 初始化期会把 SelectedItem 写成 null，理由见 SelectedFontSize 的注释）
            RaisePropertyChanged(nameof(SelectedFontSize));

            // 支持"带着某个插件名打开"（流程步骤右键「帮助」、插件配置窗的「?」按钮走这条）
            HelpNode? start = null;
            if (parameters != null
                && parameters.ContainsKey("plugin")
                && parameters["plugin"] is string pluginName
                && !string.IsNullOrWhiteSpace(pluginName))
            {
                start = FindTopicByTitle(pluginName);
            }

            SelectedNode = start ?? FirstLeaf(Nodes);
        }

        #endregion
    }
}
