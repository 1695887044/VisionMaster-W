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
        /// 逐页渲染缓存。存的是"文档 + 该文档对应的 markdown"这一对：
        /// 打印要重新渲染一份，用的必须是 <b>当前页</b> 的正文——只缓存文档、正文另存一个字段的话，
        /// "看 A → 看 B → 回退到 A（命中缓存）→ 打印"就会打出 B 的正文而标题写 A。
        /// </summary>
        private readonly Dictionary<HelpPage, (FlowDocument Document, string Markdown)> _renderCache = new();

        private HelpNode? _selectedNode;
        private HelpPage? _currentPage;
        private string _currentMarkdown = string.Empty;
        private FlowDocument? _document;
        private double _tocWidth = 300;
        private double _lastVisibleTocWidth = 300;
        private double _bodyFontSize = 14;
        private string _fontSizeLabel = "标准";
        private bool _navigating;

        public HelpViewModel(HelpCatalogService catalog, ILogService log)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _log = log ?? throw new ArgumentNullException(nameof(log));

            ToggleTocCommand = new DelegateCommand(ToggleToc);
            BackCommand = new DelegateCommand(GoBack, () => _history.Count > 1);
            PrintCommand = new DelegateCommand(Print);
            CloseCommand = new DelegateCommand(() => RequestClose.Invoke(ButtonResult.Cancel));
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

        /// <summary>底部状态栏文字</summary>
        public string StatusText => $"{_catalog.Summary}　·　Ctrl+P 打印当前页，Esc 关闭";

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

            Nodes.Clear();
            foreach (var group in _catalog.Groups)
                Nodes.Add(group);

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
