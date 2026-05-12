using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using UI.Models;

namespace UI.CustomControl
{
    [TemplatePart(Name = "PART_ScrollViewer", Type = typeof(ScrollViewer))]
    public class LogConsole : ListBox
    {
        private ScrollViewer _scrollViewer;
        private ICollectionView _collectionView;
        private bool _isUserScrolling = false; // 智能滚动标记

        static LogConsole()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(LogConsole), new FrameworkPropertyMetadata(typeof(LogConsole)));
        }

        #region 💡 依赖属性：供外部 UI 直接绑定控制

        public bool AutoScroll
        {
            get { return (bool)GetValue(AutoScrollProperty); }
            set { SetValue(AutoScrollProperty, value); }
        }
        public static readonly DependencyProperty AutoScrollProperty =
            DependencyProperty.Register("AutoScroll", typeof(bool), typeof(LogConsole), new PropertyMetadata(true));

        public string SearchText
        {
            get { return (string)GetValue(SearchTextProperty); }
            set { SetValue(SearchTextProperty, value); }
        }
        public static readonly DependencyProperty SearchTextProperty =
            DependencyProperty.Register("SearchText", typeof(string), typeof(LogConsole), new PropertyMetadata(string.Empty, OnFilterPropertyChanged));

        public LogLevel? FilterLevel
        {
            get { return (LogLevel?)GetValue(FilterLevelProperty); }
            set { SetValue(FilterLevelProperty, value); }
        }
        public static readonly DependencyProperty FilterLevelProperty =
            DependencyProperty.Register("FilterLevel", typeof(LogLevel?), typeof(LogConsole), new PropertyMetadata(null, OnFilterPropertyChanged));

        public string GroupBy
        {
            get { return (string)GetValue(GroupByProperty); }
            set { SetValue(GroupByProperty, value); }
        }
        public static readonly DependencyProperty GroupByProperty =
            DependencyProperty.Register("GroupBy", typeof(string), typeof(LogConsole), new PropertyMetadata(string.Empty, OnGroupByChanged));

        #endregion

        // 当外部改变搜索词或等级时，触发内部过滤
        private static void OnFilterPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is LogConsole console && console._collectionView != null)
                console._collectionView.Refresh();
        }

        // 当外部改变分组条件时，触发内部分组
        private static void OnGroupByChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is LogConsole console && console._collectionView != null)
            {
                console._collectionView.GroupDescriptions.Clear();
                var propName = e.NewValue as string;
                if (!string.IsNullOrWhiteSpace(propName))
                    console._collectionView.GroupDescriptions.Add(new PropertyGroupDescription(propName));
            }
        }

        // 🚨 拦截数据源：获取 View 用于过滤
        protected override void OnItemsSourceChanged(IEnumerable oldValue, IEnumerable newValue)
        {
            base.OnItemsSourceChanged(oldValue, newValue);
            if (newValue != null)
            {
                _collectionView = CollectionViewSource.GetDefaultView(newValue);
                _collectionView.Filter = FilterLogic;
            }
        }

        // 过滤核心逻辑
        private bool FilterLogic(object item)
        {
            if (!(item is LogItem log)) return false;

            // 1. 查等级
            if (FilterLevel.HasValue && log.Level != FilterLevel.Value) return false;

            // 2. 查文本 (支持消息和来源)
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                bool matchMsg = log.Message?.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0;
                bool matchSrc = log.Source?.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!matchMsg && !matchSrc) return false;
            }
            return true;
        }

        // 获取模板中的滚动条
        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            _scrollViewer = GetTemplateChild("PART_ScrollViewer") as ScrollViewer;
            if (_scrollViewer != null)
            {
                // 监听用户滚动行为
                _scrollViewer.ScrollChanged += (s, e) =>
                {
                    if (e.ExtentHeightChange == 0) // 只有当用户主动拖拽时
                    {
                        // 判定：如果滚到底部了，恢复自动滚动；如果往上滚了，暂停自动滚动
                        _isUserScrolling = _scrollViewer.VerticalOffset < _scrollViewer.ScrollableHeight - 5;
                    }
                };
            }
        }

        // 新数据到达时，判断是否需要滚动
        protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e)
        {
            base.OnItemsChanged(e);

            if (AutoScroll && !_isUserScrolling && e.Action == NotifyCollectionChangedAction.Add)
            {
                Dispatcher.InvokeAsync(() =>
                {
                    _scrollViewer?.ScrollToBottom();
                }, System.Windows.Threading.DispatcherPriority.Background);
            }
        }
    }
}
