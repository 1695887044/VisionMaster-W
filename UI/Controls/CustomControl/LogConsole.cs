using System;
using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using UI.Models;

namespace UI.CustomControl
{
    /// <summary>
    /// 日志控制台：在 ListBox 之上加了「等级/关键字过滤」与「智能自动滚动」。
    ///
    /// 两条使用约束写在前面：
    /// ① 过滤挂在<b>集合的默认视图</b>上（WPF 的 ItemsControl 就是从这个视图取数据的）。
    ///    默认视图按集合共享且被框架缓存，所以<b>同一个集合不要同时交给第二个带过滤的控件</b>；
    ///    换源时本控件会主动摘掉旧视图上的过滤委托，不会留下"幽灵过滤"。
    /// ② 自动滚动是"粘底"策略：视图当前在底部时才跟着新日志走，用户往上翻即暂停，
    ///    翻回底部自动恢复。
    /// </summary>
    [TemplatePart(Name = "PART_ScrollViewer", Type = typeof(ScrollViewer))]
    public class LogConsole : ListBox
    {
        /// <summary>搜索框防抖时长：连续输入只在停顿这么久之后刷新一次视图</summary>
        private static readonly TimeSpan FilterDebounce = TimeSpan.FromMilliseconds(200);

        /// <summary>
        /// 判定"贴底"的容差。逻辑滚动（CanContentScroll=True，本控件主题即如此）下单位为"项"，
        /// 物理滚动下单位为像素——取 1，两种情况都够用。
        /// </summary>
        private const double BottomTolerance = 1;

        private ScrollViewer? _scrollViewer;
        private ICollectionView? _collectionView;
        private DispatcherTimer? _filterTimer;
        private bool _isUserScrolling;   // 用户主动往上翻过 → 暂停自动滚动
        private bool _scrollPending;     // 已排队一次"滚到底"，把成批日志合并成一次滚动
        private bool _trimPending;       // 已排队一次容量裁剪
        private int _refreshRetries;     // Refresh 撞上"视图正忙"时的重试计数

        static LogConsole()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(LogConsole), new FrameworkPropertyMetadata(typeof(LogConsole)));
        }

        #region 依赖属性：供外部 UI 直接绑定控制

        /// <summary>是否自动跟随最新日志（用户上翻时自动让位）</summary>
        public bool AutoScroll
        {
            get { return (bool)GetValue(AutoScrollProperty); }
            set { SetValue(AutoScrollProperty, value); }
        }
        public static readonly DependencyProperty AutoScrollProperty =
            DependencyProperty.Register("AutoScroll", typeof(bool), typeof(LogConsole),
                new PropertyMetadata(true, OnAutoScrollChanged));

        /// <summary>关键字过滤：同时匹配消息与来源，忽略大小写；空串表示不过滤</summary>
        public string SearchText
        {
            get { return (string)GetValue(SearchTextProperty); }
            set { SetValue(SearchTextProperty, value); }
        }
        public static readonly DependencyProperty SearchTextProperty =
            DependencyProperty.Register("SearchText", typeof(string), typeof(LogConsole),
                new PropertyMetadata(string.Empty, OnFilterPropertyChanged));

        /// <summary>等级过滤：null 表示不限等级</summary>
        public LogLevel? FilterLevel
        {
            get { return (LogLevel?)GetValue(FilterLevelProperty); }
            set { SetValue(FilterLevelProperty, value); }
        }
        public static readonly DependencyProperty FilterLevelProperty =
            DependencyProperty.Register("FilterLevel", typeof(LogLevel?), typeof(LogConsole),
                new PropertyMetadata(null, OnFilterPropertyChanged));

        /// <summary>分组属性名（LogItem 的属性名，如 Source）；空串表示不分组</summary>
        public string GroupBy
        {
            get { return (string)GetValue(GroupByProperty); }
            set { SetValue(GroupByProperty, value); }
        }
        public static readonly DependencyProperty GroupByProperty =
            DependencyProperty.Register("GroupBy", typeof(string), typeof(LogConsole),
                new PropertyMetadata(string.Empty, OnGroupByChanged));

        /// <summary>
        /// 视图保留的最大条数（0 = 不限，默认不限，行为与旧版一致）。超出后从头部裁剪最旧的记录。
        /// 裁剪的是<b>绑定源集合本身</b>（日志只是展示数据，不含业务状态），
        /// 因此源集合必须可编辑（如 ObservableCollection）；只读/固定大小/非 IList 时本项静默不生效。
        /// 注意：裁剪会让面板里看不到更早的历史，完整历史以文件通道（LogService）为准。
        /// </summary>
        public int MaxItems
        {
            get { return (int)GetValue(MaxItemsProperty); }
            set { SetValue(MaxItemsProperty, value); }
        }
        public static readonly DependencyProperty MaxItemsProperty =
            DependencyProperty.Register("MaxItems", typeof(int), typeof(LogConsole),
                new PropertyMetadata(0, OnMaxItemsChanged));

        #endregion

        // 手动打开自动滚动：视为"回到贴底"，立刻生效（否则要等下一条日志才看出开关有用）
        private static void OnAutoScrollChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is LogConsole console && (bool)e.NewValue)
            {
                console._isUserScrolling = false;
                console.RequestScrollToBottom();
            }
        }

        // 改容量：立刻按新上限裁剪一次
        private static void OnMaxItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is LogConsole console)
                console.RequestTrim();
        }

        // 外部改搜索词或等级 → 刷新过滤。
        // 等级是"点一下"的选择，立刻生效；搜索词是连续输入，做防抖（否则每敲一个字全量重算一遍日志）。
        private static void OnFilterPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not LogConsole console) return;

            if (e.Property == SearchTextProperty)
                console.ScheduleFilterRefresh();
            else
                console.RefreshFilterNow();
        }

        // 外部改分组条件 → 重设分组描述
        private static void OnGroupByChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            (d as LogConsole)?.ApplyGrouping();
        }

        // 🚨 拦截数据源：拿到视图用于过滤 / 分组 / 裁剪
        protected override void OnItemsSourceChanged(IEnumerable oldValue, IEnumerable newValue)
        {
            base.OnItemsSourceChanged(oldValue, newValue);

            // 先摘掉挂在旧集合默认视图上的过滤：默认视图按集合共享并被框架缓存，
            // 换源后它仍留着指向本控件的委托——旧集合会被"幽灵过滤"，本控件也回收不掉。
            if (_collectionView != null && !ReferenceEquals(_collectionView.SourceCollection, newValue))
                _collectionView.Filter = null;

            _collectionView = newValue == null ? null : CollectionViewSource.GetDefaultView(newValue);
            if (_collectionView == null)
                return;

            // 设置 Filter 会让视图自己刷新一次，FilterLevel/SearchText 的现值随之生效
            _collectionView.Filter = FilterLogic;
            ApplyGrouping();   // GroupBy 常在 ItemsSource 之前赋值（XAML 属性顺序），这里必须补一次

            // 换源：滚动状态复位，新日志默认贴底（AutoScroll 关掉时保持不动）
            _isUserScrolling = false;
            _refreshRetries = 0;
            _filterTimer?.Stop();   // 针对旧源排队的防抖刷新已无意义
            RequestScrollToBottom();
        }

        // 过滤核心逻辑
        private bool FilterLogic(object item)
        {
            // 集合里混进非日志项时不过滤掉：静默隐藏别的类型只会让人以为数据丢了
            if (item is not LogItem log) return true;

            // 1. 查等级
            var level = FilterLevel;
            if (level.HasValue && log.Level != level.Value) return false;

            // 2. 查文本（支持消息和来源）
            var keyword = SearchText;
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                keyword = keyword.Trim();
                bool matchMsg = log.Message?.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
                bool matchSrc = log.Source?.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!matchMsg && !matchSrc) return false;
            }
            return true;
        }

        // 分组描述重设（ItemsSource 就绪前调用只会记住 GroupBy，待数据源到达后统一应用）
        private void ApplyGrouping()
        {
            if (_collectionView == null) return;

            _collectionView.GroupDescriptions.Clear();
            var propName = GroupBy;
            if (!string.IsNullOrWhiteSpace(propName))
                _collectionView.GroupDescriptions.Add(new PropertyGroupDescription(propName));
        }

        #region 过滤刷新（防抖 + 视图忙时重试）

        private void ScheduleFilterRefresh()
        {
            if (_collectionView == null) return;   // 还没数据源：等 OnItemsSourceChanged 里统一应用

            if (_filterTimer == null)
            {
                var timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
                {
                    Interval = FilterDebounce
                };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    RefreshView();
                };
                _filterTimer = timer;
            }

            _filterTimer.Stop();   // 重新计时：连续输入只在停顿后刷新一次
            _filterTimer.Start();
        }

        private void RefreshFilterNow()
        {
            _filterTimer?.Stop();
            RefreshView();
        }

        private void RefreshView()
        {
            if (_collectionView == null) return;

            try
            {
                _collectionView.Refresh();
                _refreshRetries = 0;
            }
            catch (InvalidOperationException)
            {
                // 视图正在处理集合变更时 Refresh 会被拒绝（WPF 的"延迟刷新"机制）。
                // 稍后重试而不是把异常抛给 UI 线程；连续失败就放弃，等下一次条件变化。
                if (_refreshRetries++ < 3)
                    ScheduleFilterRefresh();
            }
        }

        #endregion

        // 获取模板中的滚动条
        public override void OnApplyTemplate()
        {
            if (_scrollViewer != null)
                _scrollViewer.ScrollChanged -= OnScrollChanged;

            base.OnApplyTemplate();

            _scrollViewer = GetTemplateChild("PART_ScrollViewer") as ScrollViewer;
            if (_scrollViewer != null)
            {
                _scrollViewer.ScrollChanged += OnScrollChanged;

                // 模板就绪前到达的日志不会再触发滚动，这里补一次（排队到布局之后执行）
                RequestScrollToBottom();
            }
        }

        // 监听用户滚动行为
        private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // 只看"垂直位置真的动了"的事件：内容变高会把 ScrollableHeight 撑大，那不是用户在滚。
            // （旧写法用 ExtentHeightChange == 0 来排除内容变化，会漏判"用户滚动与日志新增
            //   落在同一次布局里"的情况，于是把用户正在看的位置拽回底部。）
            if (e.VerticalChange == 0 || _scrollViewer == null) return;

            _isUserScrolling = _scrollViewer.VerticalOffset < _scrollViewer.ScrollableHeight - BottomTolerance;
        }

        // 新数据到达时，判断是否需要滚动
        protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e)
        {
            base.OnItemsChanged(e);

            // 只有"新增"和"整体重置（换源 / 清空重填）"需要跟到底部
            if (e.Action is not (NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Reset))
                return;

            RequestTrim();
            RequestScrollToBottom();
        }

        /// <summary>排队一次"滚到底"；同一批日志只排一次，执行时再复核一次条件</summary>
        private void RequestScrollToBottom()
        {
            if (!AutoScroll || _isUserScrolling || _scrollPending) return;

            _scrollPending = true;
            Dispatcher.InvokeAsync(() =>
            {
                _scrollPending = false;
                // 排队期间用户可能往上翻了、或关掉了自动滚动
                if (AutoScroll && !_isUserScrolling)
                    _scrollViewer?.ScrollToBottom();
            }, DispatcherPriority.Background);
        }

        /// <summary>排队一次容量裁剪（同一批日志只排一次）</summary>
        private void RequestTrim()
        {
            if (MaxItems <= 0 || _trimPending) return;

            _trimPending = true;
            Dispatcher.InvokeAsync(() =>
            {
                _trimPending = false;
                TrimToCapacity();
            }, DispatcherPriority.Background);
        }

        /// <summary>
        /// 按 MaxItems 裁剪最旧的记录。
        /// 必须排在 Dispatcher 上执行：ObservableCollection 在派发 CollectionChanged 的过程中
        /// 禁止再次改动自己（会抛"Cannot change ObservableCollection during a CollectionChanged event"），
        /// 而本方法正是在那次通知的处理链里被请求的。
        /// </summary>
        private void TrimToCapacity()
        {
            int max = MaxItems;
            if (max <= 0) return;

            if (_collectionView?.SourceCollection is not IList list || list.IsReadOnly || list.IsFixedSize)
                return;   // 源集合不可编辑：本项不生效，静默跳过

            int overflow = list.Count - max;
            for (int i = 0; i < overflow; i++)
                list.RemoveAt(0);
        }
    }
}
