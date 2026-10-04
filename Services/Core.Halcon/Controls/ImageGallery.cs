using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Core.Halcon.Models;

namespace Core.Halcon.Controls
{
    /// <summary>图集导出请求：控件只发起请求，落盘交给宿主（好让宿主弹目录选择、统一提示结果）</summary>
    public sealed class GalleryExportEventArgs : EventArgs
    {
        /// <summary>要导出的帧（"导出选中"为选中项；"导出全部"为当前筛选后的全部）</summary>
        public IReadOnlyList<ImageFrame> Frames { get; init; } = Array.Empty<ImageFrame>();
    }

    /// <summary>
    /// 图像集（画廊）控件：左侧"按流程筛选 + 缩略图列表"，右侧大图预览，可选右侧再挂一个对比大图。
    ///
    /// 设计取舍
    /// ---------
    /// · **缩略图用位图、大图才用 HALCON 窗口**：每个缩略图都开一个 <c>HSmartWindowControlWPF</c>
    ///   会创建同等数量的窗口句柄与 D3D 合成面，几十张图就把界面拖垮。位图列表可虚拟化，成本与图量无关。
    /// · **大图复用 <see cref="HalconBase"/> 家族**（默认 <see cref="ImageDisplay"/>）：缩放/平移/十字线/
    ///   图像信息/保存原图/右键"适应"这些能力一处实现、处处一致，不在画廊里另写一套。
    /// · **控件只做"显示 + 筛选 + 交互"**：数据从哪来、留多少张、什么时候清空、导出到哪，都是宿主的事。
    ///   控件不认识流程引擎；导出请求以事件抛给宿主。
    /// · 列表用 <see cref="ListBox"/> 默认虚拟化面板，配合宿主端张数上限（默认几百张）保证滚动流畅。
    /// </summary>
    public class ImageGallery : Control
    {
        /// <summary>"全部流程"这一个筛选项的显示文本（<see cref="SelectedFlow"/> 取此值 = 不筛选）</summary>
        public const string AllFlowsLabel = "（全部流程）";

        private const string PartList = "PART_List";
        private const string PartBig = "PART_Big";
        private const string PartBig2 = "PART_Big2";
        private const string PartClear = "PART_Clear";
        private const string PartFlowFilter = "PART_FlowFilter";
        private const string PartLock = "PART_Lock";
        private const string PartPin = "PART_Pin";
        private const string PartCompare = "PART_Compare";
        private const string PartExportSelected = "PART_ExportSelected";
        private const string PartExportAll = "PART_ExportAll";

        private ListBox? _list;
        private ImageDisplay? _big;
        private Button? _clearButton;
        private ComboBox? _flowFilter;
        private ToggleButton? _lockToggle;
        private Button? _pinButton;
        private ToggleButton? _compareToggle;
        private Button? _exportSelectedButton;
        private Button? _exportAllButton;

        private bool _recomputeQueued;
        private INotifyCollectionChanged? _observedSource;

        static ImageGallery()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(ImageGallery), new FrameworkPropertyMetadata(typeof(ImageGallery)));
        }

        /// <summary>宿主挂"清空图集"的动作；控件本身不删数据</summary>
        public event EventHandler? ClearRequested;

        /// <summary>宿主挂"导出"的动作（弹目录、落盘、提示结果）</summary>
        public event EventHandler<GalleryExportEventArgs>? ExportRequested;

        /// <summary>筛选条件变化（可选联动）</summary>
        public event EventHandler? SelectedFlowChanged;

        #region 依赖属性

        /// <summary>数据源：<see cref="ImageFrame"/> 集合（宿主传入，通常是采集服务的只读集合）</summary>
        public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
            nameof(ItemsSource), typeof(IEnumerable), typeof(ImageGallery),
            new PropertyMetadata(null, OnItemsSourceChanged));

        public IEnumerable? ItemsSource
        {
            get => (IEnumerable?)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        /// <summary>当前选中帧（大图预览的对象）；双向</summary>
        public static readonly DependencyProperty SelectedFrameProperty = DependencyProperty.Register(
            nameof(SelectedFrame), typeof(ImageFrame), typeof(ImageGallery),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public ImageFrame? SelectedFrame
        {
            get => (ImageFrame?)GetValue(SelectedFrameProperty);
            set => SetValue(SelectedFrameProperty, value);
        }

        /// <summary>对比基准帧（"钉住当前"写入；对比模式下显示在右侧第二张大图）</summary>
        public static readonly DependencyProperty PinnedFrameProperty = DependencyProperty.Register(
            nameof(PinnedFrame), typeof(ImageFrame), typeof(ImageGallery),
            new FrameworkPropertyMetadata(null));

        public ImageFrame? PinnedFrame
        {
            get => (ImageFrame?)GetValue(PinnedFrameProperty);
            set => SetValue(PinnedFrameProperty, value);
        }

        /// <summary>是否展开对比大图（模板里的触发器据此显示第二张大图与分隔条）</summary>
        public static readonly DependencyProperty IsCompareModeProperty = DependencyProperty.Register(
            nameof(IsCompareMode), typeof(bool), typeof(ImageGallery),
            new PropertyMetadata(false));

        public bool IsCompareMode
        {
            get => (bool)GetValue(IsCompareModeProperty);
            set => SetValue(IsCompareModeProperty, value);
        }

        /// <summary>流程筛选：<see cref="AllFlowsLabel"/> = 全部</summary>
        public static readonly DependencyProperty SelectedFlowProperty = DependencyProperty.Register(
            nameof(SelectedFlow), typeof(string), typeof(ImageGallery),
            new FrameworkPropertyMetadata(AllFlowsLabel,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedFlowChanged));

        public string SelectedFlow
        {
            get => (string)GetValue(SelectedFlowProperty);
            set => SetValue(SelectedFlowProperty, value);
        }

        /// <summary>新增帧且当前无选中/选中被淘汰时，是否自动选中最新一张（"锁定"按钮控制它）</summary>
        public static readonly DependencyProperty AutoSelectNewestProperty = DependencyProperty.Register(
            nameof(AutoSelectNewest), typeof(bool), typeof(ImageGallery),
            new PropertyMetadata(true));

        public bool AutoSelectNewest
        {
            get => (bool)GetValue(AutoSelectNewestProperty);
            set => SetValue(AutoSelectNewestProperty, value);
        }

        /// <summary>
        /// 只看"归属窗口号 = 本值"的图。<b>0 或负数 = 不过滤</b>（"全部输出"那张列表就用 0）。
        /// 多格布局下每一格各绑一个值（第 N 格绑 N），于是每格是自己的一条列表；
        /// 换布局时按窗口号复用控件，切回来还是原来那些图。
        /// </summary>
        public static readonly DependencyProperty ViewIndexFilterProperty = DependencyProperty.Register(
            nameof(ViewIndexFilter), typeof(int), typeof(ImageGallery),
            new PropertyMetadata(0, OnViewIndexFilterChanged));

        public int ViewIndexFilter
        {
            get => (int)GetValue(ViewIndexFilterProperty);
            set => SetValue(ViewIndexFilterProperty, value);
        }

        private static void OnViewIndexFilterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ImageGallery gallery) gallery.QueueRecompute();
        }

        /// <summary>
        /// 列表为空时画面上的提示文案（宿主按"为什么没图"来设置它：
        /// 采集总开关关了 / 插件没注入 / 兜底没开，三种原因的处置方式完全不同）。
        /// </summary>
        public static readonly DependencyProperty HintTextProperty = DependencyProperty.Register(
            nameof(HintText), typeof(string), typeof(ImageGallery),
            new PropertyMetadata("本次运行还没有图像。"));

        public string HintText
        {
            get => (string)GetValue(HintTextProperty);
            set => SetValue(HintTextProperty, value);
        }

        #endregion

        /// <summary>筛选后的帧列表（与 <see cref="ItemsSource"/> 同步维护，供模板直接绑定）</summary>
        public ObservableCollection<ImageFrame> FilteredFrames { get; } = new();

        /// <summary>可选流程列表（含"（全部流程）"，首项）</summary>
        public ObservableCollection<string> AvailableFlows { get; } = new() { AllFlowsLabel };

        /// <summary>筛选后可见帧数（供宿主显示计数，可选）</summary>
        public int VisibleCount => FilteredFrames.Count;

        /// <summary>当前多选的帧（未多选时为空）</summary>
        public IReadOnlyList<ImageFrame> SelectedFrames
            => _list?.SelectedItems?.OfType<ImageFrame>().ToList() ?? (IReadOnlyList<ImageFrame>)Array.Empty<ImageFrame>();

        public override void OnApplyTemplate()
        {
            UnhookTemplateParts();
            base.OnApplyTemplate();
            HookTemplateParts();
            EnsureContextMenu();

            if (_flowFilter != null)
                _flowFilter.SelectedItem = AvailableFlows.Contains(SelectedFlow) ? SelectedFlow : AllFlowsLabel;

            if (_lockToggle != null)
                _lockToggle.IsChecked = !AutoSelectNewest;      // 勾选 = 锁定（不自动跟随最新）
            if (_compareToggle != null)
                _compareToggle.IsChecked = IsCompareMode;

            QueueRecompute();
        }

        private void HookTemplateParts()
        {
            _list = GetTemplateChild(PartList) as ListBox;
            _big = GetTemplateChild(PartBig) as ImageDisplay;
            _clearButton = GetTemplateChild(PartClear) as Button;
            _flowFilter = GetTemplateChild(PartFlowFilter) as ComboBox;
            _lockToggle = GetTemplateChild(PartLock) as ToggleButton;
            _pinButton = GetTemplateChild(PartPin) as Button;
            _compareToggle = GetTemplateChild(PartCompare) as ToggleButton;
            _exportSelectedButton = GetTemplateChild(PartExportSelected) as Button;
            _exportAllButton = GetTemplateChild(PartExportAll) as Button;

            if (_list != null) _list.MouseDoubleClick += OnListDoubleClick;
            if (_list != null) _list.PreviewMouseWheel += OnListMouseWheel;
            if (_flowFilter != null) _flowFilter.SelectionChanged += OnFlowFilterSelectionChanged;
            if (_clearButton != null) _clearButton.Click += OnClearClick;
            if (_lockToggle != null) _lockToggle.Checked += OnLockChanged;
            if (_lockToggle != null) _lockToggle.Unchecked += OnLockChanged;
            if (_pinButton != null) _pinButton.Click += OnPinClick;
            if (_compareToggle != null) _compareToggle.Checked += OnCompareChanged;
            if (_compareToggle != null) _compareToggle.Unchecked += OnCompareChanged;
            if (_exportSelectedButton != null) _exportSelectedButton.Click += OnExportSelectedClick;
            if (_exportAllButton != null) _exportAllButton.Click += OnExportAllClick;
        }

        private void UnhookTemplateParts()
        {
            if (_list != null) _list.MouseDoubleClick -= OnListDoubleClick;
            if (_list != null) _list.PreviewMouseWheel -= OnListMouseWheel;
            if (_flowFilter != null) _flowFilter.SelectionChanged -= OnFlowFilterSelectionChanged;
            if (_clearButton != null) _clearButton.Click -= OnClearClick;
            if (_lockToggle != null) _lockToggle.Checked -= OnLockChanged;
            if (_lockToggle != null) _lockToggle.Unchecked -= OnLockChanged;
            if (_pinButton != null) _pinButton.Click -= OnPinClick;
            if (_compareToggle != null) _compareToggle.Checked -= OnCompareChanged;
            if (_compareToggle != null) _compareToggle.Unchecked -= OnCompareChanged;
            if (_exportSelectedButton != null) _exportSelectedButton.Click -= OnExportSelectedClick;
            if (_exportAllButton != null) _exportAllButton.Click -= OnExportAllClick;
        }

        /// <summary>
        /// 画布空白处的右键菜单：**清空 / 导出**是去掉工具条后仅剩的入口，所以在代码里建，
        /// 不写进 ControlTemplate —— ContextMenu 属于弹出树，模板里的元素取不到模板名字域，
        /// 靠 GetTemplateChild 挂接会静默失效（菜单能弹、点了没反应）。
        /// </summary>
        private void EnsureContextMenu()
        {
            if (ContextMenu != null) return;

            var clear = new MenuItem { Header = "清空画布" };
            clear.Click += OnClearClick;

            var exportSelected = new MenuItem { Header = "导出选中" };
            exportSelected.Click += OnExportSelectedClick;

            var exportAll = new MenuItem { Header = "导出全部" };
            exportAll.Click += OnExportAllClick;

            ContextMenu = new ContextMenu { Items = { clear, new Separator(), exportSelected, exportAll } };
        }

        #region 交互

        private void OnFlowFilterSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_flowFilter?.SelectedItem is string s && s != SelectedFlow)
                SelectedFlow = s;
        }

        private void OnClearClick(object sender, RoutedEventArgs e) => ClearRequested?.Invoke(this, EventArgs.Empty);

        /// <summary>双击缩略图 = 大图按原始像素 1:1 显示（复用 HalconBase 的"适应图片/窗口"）</summary>
        private void OnListDoubleClick(object sender, MouseButtonEventArgs e) => _big?.FitToImage();

        /// <summary>
        /// 缩略图条是单行横向滚动的，而鼠标滚轮默认只认纵向：不映射的话用户会觉得"滑不动"
        /// （只有底部那条细滚动条能拖，现场很难发现）。
        ///
        /// 【为什么用 LineLeft/LineRight 而不是 ScrollToHorizontalOffset(±像素)】
        /// 内容面板是 VirtualizingStackPanel —— 逻辑滚动，偏移量的单位是"项"不是像素。
        /// 这里按 ±120 反而会一次跳 120 张图。Line* 与单位无关，一格滚轮走 2 张，手感刚好。
        /// </summary>
        private void OnListMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_list == null || e.Delta == 0) return;

            var scroll = FindDescendant<ScrollViewer>(_list);
            if (scroll == null || scroll.ScrollableWidth <= 0) return;   // 没排满就别吃事件

            for (int i = 0; i < 2; i++)
            {
                if (e.Delta > 0) scroll.LineLeft();
                else scroll.LineRight();
            }
            e.Handled = true;
        }

        /// <summary>按类型找可视树里的第一个后代（模板内部元素用 FindName 不稳定）</summary>
        private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T typed) return typed;

                var found = FindDescendant<T>(child);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>锁定 = 关掉"自动跟随最新"：运行中图集持续刷新时，正在看的这张不会被顶掉</summary>
        private void OnLockChanged(object sender, RoutedEventArgs e)
            => SetCurrentValue(AutoSelectNewestProperty, _lockToggle?.IsChecked != true);

        /// <summary>钉住当前帧作为对比基准</summary>
        private void OnPinClick(object sender, RoutedEventArgs e)
        {
            PinnedFrame = SelectedFrame;
            if (PinnedFrame != null && _compareToggle?.IsChecked != true)
            {
                // 钉住即隐含"我要对比"：顺手把对比模式打开，少一步操作
                if (_compareToggle != null) _compareToggle.IsChecked = true;
                else SetCurrentValue(IsCompareModeProperty, true);
            }
        }

        private void OnCompareChanged(object sender, RoutedEventArgs e)
            => SetCurrentValue(IsCompareModeProperty, _compareToggle?.IsChecked == true);

        private void OnExportSelectedClick(object sender, RoutedEventArgs e)
        {
            var frames = SelectedFrames;
            if (frames.Count == 0 && SelectedFrame != null)
                frames = new List<ImageFrame> { SelectedFrame };

            if (frames.Count == 0) return;
            ExportRequested?.Invoke(this, new GalleryExportEventArgs { Frames = frames });
        }

        private void OnExportAllClick(object sender, RoutedEventArgs e)
        {
            if (FilteredFrames.Count == 0) return;
            ExportRequested?.Invoke(this, new GalleryExportEventArgs { Frames = FilteredFrames.ToList() });
        }

        #endregion

        private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ImageGallery gallery) return;

            if (gallery._observedSource != null)
                gallery._observedSource.CollectionChanged -= gallery.OnSourceCollectionChanged;

            gallery._observedSource = e.NewValue as INotifyCollectionChanged;
            if (gallery._observedSource != null)
                gallery._observedSource.CollectionChanged += gallery.OnSourceCollectionChanged;

            gallery.QueueRecompute();
        }

        private void OnSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => QueueRecompute();

        private static void OnSelectedFlowChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ImageGallery gallery) return;
            gallery.SelectedFlowChanged?.Invoke(gallery, EventArgs.Empty);
            gallery.QueueRecompute();
        }

        /// <summary>
        /// 合并多次变更，避免一次采集插入 N 帧就重算 N 次（O(N²)）。
        ///
        /// 为什么必须是 <see cref="DispatcherPriority.Normal"/> 而不是 Background：
        /// 触发重算的正是"采集往集合里插帧"这件事，而插入本身走 Normal 优先级
        /// （ImageCollectionService.RunOnUi → BeginInvoke 默认优先级）。循环运行时插帧是**连续洪水**，
        /// 挂在 Background 的重算会被永久饿死——故障表现极具迷惑性：
        /// 帧明明在集合里，界面却一直停在"本格暂无图像"，且单次运行（插完就没流量了）完全正常。
        /// 同优先级才能按 FIFO 与插入交替执行：不被饿死，最坏只是跟着排队。
        /// </summary>
        private void QueueRecompute()
        {
            if (_recomputeQueued) return;
            _recomputeQueued = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _recomputeQueued = false;
                Recompute();
            }), DispatcherPriority.Normal);
        }

        /// <summary>按当前筛选条件重建可见列表与流程下拉项，并尽力保持选中项</summary>
        public void Recompute()
        {
            var all = ItemsSource?.OfType<ImageFrame>().ToList() ?? new List<ImageFrame>();

            // 0) 归属过滤：多格布局下每格只看"显示窗口号 = 自己"的图（0/负 = 不过滤，"全部输出"用）
            if (ViewIndexFilter > 0)
                all = all.Where(f => f.ViewIndex == ViewIndexFilter).ToList();

            // 1) 流程下拉项：稳定排序，保证"全部"始终在首
            var flows = all.Select(f => f.FlowName)
                           .Where(s => !string.IsNullOrEmpty(s))
                           .Distinct(StringComparer.Ordinal)
                           .OrderBy(s => s, StringComparer.Ordinal)
                           .ToList();

            if (!AvailableFlows.SequenceEqual(new[] { AllFlowsLabel }.Concat(flows)))
            {
                AvailableFlows.Clear();
                AvailableFlows.Add(AllFlowsLabel);
                foreach (var f in flows) AvailableFlows.Add(f);

                // 当前筛选项的流程已被清空（比如换了方案）→ 回落"全部"
                if (!AvailableFlows.Contains(SelectedFlow))
                    SetCurrentValue(SelectedFlowProperty, AllFlowsLabel);
            }

            // 2) 可见帧
            bool allFlows = string.IsNullOrEmpty(SelectedFlow) || SelectedFlow == AllFlowsLabel;
            var visible = allFlows
                ? all
                : all.Where(f => string.Equals(f.FlowName, SelectedFlow, StringComparison.Ordinal)).ToList();

            visible = visible.OrderBy(f => f.FlowName, StringComparer.Ordinal)
                             .ThenBy(f => f.Sequence)
                             .ToList();

            // 3) 原地同步（抑制无谓的整表重建，保持 ListBox 选中/滚动状态）
            if (!FilteredFrames.SequenceEqual(visible))
            {
                var previous = SelectedFrame;
                FilteredFrames.Clear();
                foreach (var f in visible) FilteredFrames.Add(f);

                if (previous != null && visible.Contains(previous))
                    SetCurrentValue(SelectedFrameProperty, previous);
                else if (AutoSelectNewest && visible.Count > 0)
                    SetCurrentValue(SelectedFrameProperty, visible[visible.Count - 1]);
                else if (visible.Count == 0)
                    SetCurrentValue(SelectedFrameProperty, null);
            }
            else if (SelectedFrame != null && !visible.Contains(SelectedFrame))
            {
                SetCurrentValue(SelectedFrameProperty, AutoSelectNewest && visible.Count > 0
                    ? visible[visible.Count - 1]
                    : null);
            }
        }
    }
}
