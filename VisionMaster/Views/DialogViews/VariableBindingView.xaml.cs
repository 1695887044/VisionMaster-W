using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Core.Interfaces;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using UI.Converters;
using VisionMaster.Helpers;
using VisionMaster.Models;
using VisionMaster.ViewModels;

namespace VisionMaster.Views
{
    /// <summary>
    /// 变量绑定弹窗（Web 设计基线 v1）的交互逻辑。
    ///
    /// 视图侧只做三件事：搜索 / 筛选 / 分组的 <see cref="ICollectionView"/> 编排、
    /// 计数与空态文案、以及把 VM 暴露的绑定状态翻译成右栏英雄区/页脚文案。
    /// 绑定写入仍全部走 VariableBindingViewModel 的既有命令，视图不碰模型。
    /// </summary>
    public partial class VariableBindingView : UserControl
    {
        private VariableBindingViewModel? _vm;
        private VariableBindingViewModel? _hookedVm;

        private ICollectionView? _targetView;
        private ICollectionView? _sourceView;
        private ICollectionView? _candidateView;

        private ObservableCollection<InputPortUIModel>? _hookedPorts;
        private ObservableCollection<ToolItemModel>? _hookedNodes;
        private ObservableCollection<PortDefinition>? _hookedCandidates;
        private readonly List<InputPortUIModel> _attachedPorts = new();

        private string _targetFilter = "all";
        private string _sourceTab = "all";

        public VariableBindingView()
        {
            InitializeComponent();

            // 与 GlobalVariableView 同一时序：Prism 的 AutoWireViewModel 在
            // InitializeComponent 解析期间就把 DataContext 设好了，DataContextChanged
            // 之后不会再补发，所以构造器里必须直接 Hook 一次。
            DataContextChanged += (_, __) => HookViewModel();
            Loaded += (_, __) =>
            {
                HookViewModel();
                ClampToWorkArea();
            };
            Unloaded += (_, __) => UnhookViewModel();
            HookViewModel();
        }

        /// <summary>
        /// 鼠标滚轮就近滚动：不依赖键盘焦点，也不让嵌套列表吞掉滚轮。
        ///
        /// 现象（2026-10-20 真机）：鼠标悬停在右栏/候选列表上滚轮没反应，必须先点一下。
        /// 原因有两层：
        /// ① 候选 ListBox 的 ScrollViewer.VerticalScrollBarVisibility=Disabled 后仍会
        ///    处理 MouseWheel 并标记 Handled，外层 ScrollViewer 收不到；
        /// ② WPF 的 ListBox 在未获得键盘焦点时对滚轮的响应不稳定。
        /// 这里在 Preview 阶段找"指针下最近一个可滚动的 ScrollViewer"，直接 LineUp/LineDown，
        /// 谁可滚就滚谁（左/中列表、右栏整体），并标记 Handled 防止二次滚动。
        /// </summary>
        private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Handled)
                return;

            var scrollViewer = FindScrollableAncestor(e.OriginalSource as DependencyObject);
            if (scrollViewer == null)
                return;

            // 与 WPF 默认一致：一格滚轮 3 行（ListBox 的逻辑滚动下就是 3 项）。
            for (int i = 0; i < 3; i++)
            {
                if (e.Delta > 0)
                    scrollViewer.LineUp();
                else
                    scrollViewer.LineDown();
            }
            e.Handled = true;
        }

        private static ScrollViewer? FindScrollableAncestor(DependencyObject? source)
        {
            var current = source;
            while (current != null)
            {
                if (current is ScrollViewer sv && sv.ScrollableHeight > 0)
                    return sv;

                // TextBlock 里的 Run 是 ContentElement（不是 Visual），
                // 对它调 VisualTreeHelper.GetParent 会抛 InvalidOperationException；
                // 非 Visual 走逻辑树，其余走视觉树。
                current = current is Visual
                    ? VisualTreeHelper.GetParent(current)
                    : LogicalTreeHelper.GetParent(current);
            }
            return null;
        }

        /// <summary>
        /// 视图绑定 VM 后的统一钩子（幂等）：订阅 VM / 集合 / 端口项，重建三个默认视图的 Filter。
        /// </summary>
        private void HookViewModel()
        {
            var next = DataContext as VariableBindingViewModel;
            if (!ReferenceEquals(_hookedVm, next))
            {
                UnhookViewModel();
                _hookedVm = next;
                _vm = next;
                if (_vm != null)
                    _vm.PropertyChanged += OnViewModelPropertyChanged;
            }

            _vm = next;
            if (_vm == null)
                return;

            AttachTargetView();
            AttachSourceView();
            AttachCandidateView();
            AttachCollectionHandlers();
            UpdateEverything();
        }

        private void UnhookViewModel()
        {
            if (_hookedVm != null)
                _hookedVm.PropertyChanged -= OnViewModelPropertyChanged;

            DetachPortHandlers();

            if (_hookedNodes != null)
                _hookedNodes.CollectionChanged -= OnNodesChanged;
            _hookedNodes = null;

            if (_hookedCandidates != null)
                _hookedCandidates.CollectionChanged -= OnCandidatesChanged;
            _hookedCandidates = null;

            _hookedVm = null;
            _vm = null;
        }

        private void AttachCollectionHandlers()
        {
            if (_vm == null)
                return;

            if (!ReferenceEquals(_hookedPorts, _vm.DisplayDataPort))
            {
                DetachPortHandlers();
                _hookedPorts = _vm.DisplayDataPort;
                _hookedPorts.CollectionChanged += OnPortsChanged;
                foreach (var port in _hookedPorts)
                    AttachPort(port);
            }

            if (!ReferenceEquals(_hookedNodes, _vm.TreeNodes))
            {
                if (_hookedNodes != null)
                    _hookedNodes.CollectionChanged -= OnNodesChanged;
                _hookedNodes = _vm.TreeNodes;
                _hookedNodes.CollectionChanged += OnNodesChanged;
            }

            if (!ReferenceEquals(_hookedCandidates, _vm.DisplayPorts))
            {
                if (_hookedCandidates != null)
                    _hookedCandidates.CollectionChanged -= OnCandidatesChanged;
                _hookedCandidates = _vm.DisplayPorts;
                _hookedCandidates.CollectionChanged += OnCandidatesChanged;
            }
        }

        private void AttachPort(InputPortUIModel port)
        {
            if (port == null || _attachedPorts.Contains(port))
                return;
            port.PropertyChanged += OnPortItemPropertyChanged;
            _attachedPorts.Add(port);
        }

        private void DetachPort(InputPortUIModel port)
        {
            if (port == null)
                return;
            port.PropertyChanged -= OnPortItemPropertyChanged;
            _attachedPorts.Remove(port);
        }

        private void DetachPortHandlers()
        {
            if (_hookedPorts != null)
                _hookedPorts.CollectionChanged -= OnPortsChanged;

            foreach (var port in _attachedPorts.ToList())
                DetachPort(port);
            _hookedPorts = null;
        }

        private void AttachTargetView()
        {
            if (_vm == null)
                return;

            var view = CollectionViewSource.GetDefaultView(_vm.DisplayDataPort);
            if (!ReferenceEquals(_targetView, view) && _targetView != null)
                _targetView.Filter = null;

            _targetView = view;
            _targetView.Filter = FilterTarget;
        }

        private void AttachSourceView()
        {
            if (_vm == null)
                return;

            var view = CollectionViewSource.GetDefaultView(_vm.TreeNodes);
            if (!ReferenceEquals(_sourceView, view) && _sourceView != null)
                _sourceView.Filter = null;

            _sourceView = view;

            bool grouped = _sourceView.GroupDescriptions.Count == 1
                && _sourceView.GroupDescriptions[0] is SourceCategoryGroupDescription;
            if (!grouped)
            {
                _sourceView.GroupDescriptions.Clear();
                _sourceView.GroupDescriptions.Add(new SourceCategoryGroupDescription());
            }

            _sourceView.Filter = FilterSource;
        }

        private void AttachCandidateView()
        {
            if (_vm == null)
                return;

            var view = CollectionViewSource.GetDefaultView(_vm.DisplayPorts);
            if (!ReferenceEquals(_candidateView, view) && _candidateView != null)
                _candidateView.Filter = null;

            _candidateView = view;
            _candidateView.Filter = FilterCandidate;

            if (!ReferenceEquals(_hookedCandidates, _vm.DisplayPorts))
            {
                if (_hookedCandidates != null)
                    _hookedCandidates.CollectionChanged -= OnCandidatesChanged;
                _hookedCandidates = _vm.DisplayPorts;
                _hookedCandidates.CollectionChanged += OnCandidatesChanged;
            }
        }

        private bool FilterTarget(object item)
        {
            if (item is not InputPortUIModel port)
                return false;

            if (_targetFilter == "bound" && !port.IsBound)
                return false;
            if (_targetFilter == "unbound" && port.IsBound)
                return false;

            string query = TargetSearchBox?.Text?.Trim() ?? string.Empty;
            if (query.Length == 0)
                return true;

            string haystack = string.Join(
                " ",
                port.Definition?.Name,
                port.Definition?.DataTypeName,
                port.Definition?.Description);
            return haystack.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool FilterSource(object item)
        {
            if (item is not ToolItemModel node)
                return false;

            string category = GetSourceCategory(node);
            if (_sourceTab == "variable"
                && !string.Equals(category, "Variable", StringComparison.OrdinalIgnoreCase))
                return false;
            if (_sourceTab == "step"
                && !string.Equals(category, "Step", StringComparison.OrdinalIgnoreCase))
                return false;

            string query = SourceSearchBox?.Text?.Trim() ?? string.Empty;
            if (query.Length == 0)
                return true;

            string haystack = string.Join(" ", node.ModuleGroup, node.Name, node.Description);
            return haystack.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool FilterCandidate(object item)
        {
            if (item is not PortDefinition port)
                return false;

            string query = CandidateSearchBox?.Text?.Trim() ?? string.Empty;
            if (query.Length == 0)
                return true;

            string haystack = string.Join(" ", port.Name, port.DataTypeName, port.Description);
            return haystack.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetSourceCategory(ToolItemModel node)
            => node == null || string.IsNullOrWhiteSpace(node.SourceCategory)
                ? "Step"
                : node.SourceCategory;

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.PropertyName))
            {
                UpdateEverything();
                return;
            }

            switch (e.PropertyName)
            {
                case nameof(VariableBindingViewModel.DisplayPorts):
                    AttachCandidateView();
                    UpdateCandidateCount();
                    UpdateCandidateEmptyState();
                    break;
                case nameof(VariableBindingViewModel.SelectedNode):
                case nameof(VariableBindingViewModel.SelectedInputPort):
                    _candidateView?.Refresh();
                    UpdateCandidateCount();
                    UpdateCandidateEmptyState();
                    UpdateCounts();
                    break;
                case nameof(VariableBindingViewModel.ShowAllPorts):
                    UpdateCandidateCount();
                    UpdateCandidateEmptyState();
                    break;
                case nameof(VariableBindingViewModel.BoundPortCount):
                case nameof(VariableBindingViewModel.TotalPortCount):
                case nameof(VariableBindingViewModel.ConstantValue):
                case nameof(VariableBindingViewModel.HasFunctionalEnumPort):
                case nameof(VariableBindingViewModel.IsConstantMode):
                case nameof(VariableBindingViewModel.IsUpstreamMode):
                case nameof(VariableBindingViewModel.IsSingleBindMode):
                case nameof(VariableBindingViewModel.TargetStepName):
                case nameof(VariableBindingViewModel.SelectedPresetDisplay):
                    UpdateCounts();
                    break;
            }
        }

        private void OnPortsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                // ObservableCollection.Clear() 的 OldItems 为 null；不靠它退订，
                // 否则旧端口会一直挂在视图事件上（VM 重建端口表时最明显）。
                foreach (var port in _attachedPorts.ToList())
                    DetachPort(port);
            }
            else
            {
                if (e.OldItems != null)
                {
                    foreach (InputPortUIModel port in e.OldItems)
                        DetachPort(port);
                }

                if (e.NewItems != null)
                {
                    foreach (InputPortUIModel port in e.NewItems)
                        AttachPort(port);
                }
            }

            _targetView?.Refresh();
            UpdateCounts();
            UpdateTargetEmptyState();
            _candidateView?.Refresh();
            UpdateCandidateCount();
            UpdateCandidateEmptyState();
        }

        private void OnPortItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(InputPortUIModel.LinkedAddress)
                && e.PropertyName != nameof(InputPortUIModel.IsBound))
                return;

            _targetView?.Refresh();
            UpdateCounts();
            UpdateTargetEmptyState();
            _candidateView?.Refresh();
            UpdateCandidateCount();
            UpdateCandidateEmptyState();
        }

        private void OnNodesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            _sourceView?.Refresh();
            UpdateSourceCounts();
            UpdateSourceEmptyState();
        }

        private void OnCandidatesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            _candidateView?.Refresh();
            UpdateCandidateCount();
            UpdateCandidateEmptyState();
        }

        private void TargetSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _targetView?.Refresh();
            UpdateCounts();
            UpdateTargetEmptyState();
        }

        private void SourceSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _sourceView?.Refresh();
            UpdateSourceCounts();
            UpdateSourceEmptyState();
        }

        private void CandidateSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _candidateView?.Refresh();
            UpdateCandidateCount();
            UpdateCandidateEmptyState();
        }

        private void TargetFilterList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TargetFilterList?.SelectedItem is ListBoxItem item && item.Tag is string filter)
            {
                _targetFilter = filter;
                _targetView?.Refresh();
                UpdateCounts();
                UpdateTargetEmptyState();
            }
        }

        private void SourceTabList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SourceTabList?.SelectedItem is ListBoxItem item && item.Tag is string tab)
            {
                _sourceTab = tab;
                _sourceView?.Refresh();
                UpdateSourceCounts();
                UpdateSourceEmptyState();
            }
        }

        private void OnShowAllClick(object sender, RoutedEventArgs e)
        {
            if (_vm != null)
                _vm.ShowAllPorts = true;
        }

        private void UpdateEverything()
        {
            UpdateCounts();
            UpdateTargetEmptyState();
            UpdateSourceCounts();
            UpdateSourceEmptyState();
            UpdateCandidateCount();
            UpdateCandidateEmptyState();
        }

        private void UpdateCounts()
        {
            if (_vm == null)
                return;

            // 统一走 VM 的计数（VM 在 BuildTree 与端口 LinkedAddress 变化时通知），
            // 避免视图与 VM 各算一套口径。
            int total = _vm.TotalPortCount;
            int bound = _vm.BoundPortCount;
            int unbound = total - bound;

            if (TargetFilterAllCount != null)
                TargetFilterAllCount.Text = total.ToString(CultureInfo.InvariantCulture);
            if (TargetFilterUnboundCount != null)
                TargetFilterUnboundCount.Text = unbound.ToString(CultureInfo.InvariantCulture);
            if (TargetFilterBoundCount != null)
                TargetFilterBoundCount.Text = bound.ToString(CultureInfo.InvariantCulture);
            if (TargetHeaderCountText != null)
                TargetHeaderCountText.Text = (_targetView?.Cast<object>().Count() ?? total)
                    .ToString(CultureInfo.InvariantCulture);

            if (TargetFootText != null)
                TargetFootText.Text = _vm.IsSingleBindMode
                    ? "由插件视图发起 · 只能选择一个目标"
                    : $"已绑定 {bound} / {total}";

            if (ContextTypeText != null)
                ContextTypeText.Text = _vm.IsSingleBindMode ? "单绑定" : "当前步骤";

            if (ContextMetaText != null)
                ContextMetaText.Text = _vm.IsSingleBindMode
                    ? "目标端口 1 · 等待回传"
                    : $"输入端口 {total} · 已绑定 {bound}";

            if (TitleSubtitleText != null)
                TitleSubtitleText.Text = _vm.IsSingleBindMode
                    ? "插件视图请求绑定 · 选定后立即返回"
                    : $"{_vm.TargetStepName} · {total} 个输入端口 · 已绑定 {bound} 个";

            if (BindingProgressBar != null)
            {
                BindingProgressBar.Maximum = Math.Max(total, 1);
                BindingProgressBar.Value = bound;
            }

            UpdateFooter();
        }

        private void UpdateFooter()
        {
            if (FooterSummaryText == null)
                return;

            if (_vm?.SelectedInputPort == null)
            {
                FooterSummaryText.Text = "未选择输入端口";
                return;
            }

            string bindingText;
            if (_vm.HasFunctionalEnumPort)
            {
                bindingText = string.IsNullOrWhiteSpace(_vm.SelectedPresetDisplay)
                    ? "未选择预设"
                    : $"预设: {_vm.SelectedPresetDisplay}";
            }
            else if (_vm.IsConstantMode && !string.IsNullOrWhiteSpace(_vm.ConstantValue))
            {
                bindingText = $"常量值: {_vm.ConstantValue}";
            }
            else if (!string.IsNullOrWhiteSpace(_vm.SelectedInputPort.LinkedAddress))
            {
                bindingText = _vm.SelectedInputPort.LinkedAddress;
            }
            else
            {
                bindingText = "未绑定";
            }

            FooterSummaryText.Text = $"已选：{_vm.SelectedInputPort.Definition?.Name} · {bindingText}";
        }

        private void UpdateTargetEmptyState()
        {
            if (TargetEmptyOverlay == null)
                return;

            bool empty = _targetView == null || _targetView.IsEmpty;
            TargetEmptyOverlay.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            if (!empty)
                return;

            bool noData = _vm == null || _vm.DisplayDataPort.Count == 0;
            TargetEmptyTitle.Text = noData ? "没有输入端口" : "没有匹配的输入端口";
            TargetEmptyHint.Text = noData
                ? "当前步骤没有可绑定的输入端口。"
                : "清空搜索关键词或切换筛选条件后重试。";
        }

        private void UpdateSourceCounts()
        {
            int visible = _sourceView?.Cast<object>().Count() ?? 0;

            if (SourceHeaderCountText != null)
                SourceHeaderCountText.Text = visible.ToString(CultureInfo.InvariantCulture);
            if (SourceFootText != null)
                SourceFootText.Text = $"共 {visible} 个";
        }

        private void UpdateSourceEmptyState()
        {
            if (SourceEmptyOverlay == null)
                return;

            bool empty = _sourceView == null || _sourceView.IsEmpty;
            SourceEmptyOverlay.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            if (!empty)
                return;

            bool noData = _vm == null || _vm.TreeNodes.Count == 0;
            SourceEmptyTitle.Text = noData ? "没有可用的数据来源" : "没有匹配的数据来源";
            SourceEmptyHint.Text = noData
                ? "当前步骤之前还没有可绑定的全局变量或上游算子。请先在流程中添加算子。"
                : "清空搜索关键词或切换页签后重试。";
        }

        private void UpdateCandidateCount()
        {
            int visible = _candidateView?.Cast<object>().Count() ?? 0;
            if (CandidateCountText != null)
                CandidateCountText.Text = visible.ToString(CultureInfo.InvariantCulture);
        }

        private void UpdateCandidateEmptyState()
        {
            if (CandidateEmptyOverlay == null)
                return;

            bool empty = _candidateView == null || _candidateView.IsEmpty;
            CandidateEmptyOverlay.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            if (!empty)
                return;

            if (_vm?.SelectedNode == null)
            {
                CandidateEmptyTitle.Text = "先选择一个数据来源";
                CandidateEmptyHint.Text = "从中间的「数据来源」列表中选择全局变量、运行时变量或上游算子。";
                return;
            }

            bool sourceHasOutputs = _vm.SelectedNode.OutputDefinitions?.Count > 0;
            if (!sourceHasOutputs)
            {
                CandidateEmptyTitle.Text = "该来源没有输出端口";
                CandidateEmptyHint.Text = "换一个数据来源，或先检查上游算子是否已配置输出。";
                return;
            }

            CandidateEmptyTitle.Text = "没有匹配的候选端口";
            CandidateEmptyHint.Text = !string.IsNullOrWhiteSpace(CandidateSearchBox?.Text)
                ? "清空搜索关键词后重试。"
                : "当前来源没有类型兼容的输出端口；可勾选「显示全部」查看不兼容项。";
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            ApplyResponsiveColumns(sizeInfo.NewSize.Width);
        }

        /// <summary>
        /// 1000 窄档按设计基线收成 264 / 280，把宽度让给右栏（约 400），
        /// 避免英雄区来源文本被截成「全局变量 (Global).I...」。
        /// </summary>
        private void ApplyResponsiveColumns(double width)
        {
            bool narrow = width > 0 && width < 1100;
            if (TargetColumn != null)
                TargetColumn.Width = new GridLength(narrow ? 264 : 280);
            if (SourceColumn != null)
                SourceColumn.Width = new GridLength(narrow ? 280 : 300);
        }

        /// <summary>
        /// 默认 1180 在 1920 屏 @150% 的工作区（约 1280）能放下；更小的逻辑桌面
        /// 打开时收敛到工作区，三栏仍由 MinWidth 守住可用宽度。
        /// </summary>
        private void ClampToWorkArea()
        {
            var window = Window.GetWindow(this);
            if (window == null)
                return;

            var workArea = SystemParameters.WorkArea;
            if (window.Width > workArea.Width)
                window.Width = workArea.Width;
            if (window.Height > workArea.Height)
                window.Height = workArea.Height;
        }

        /// <summary>
        /// 中栏按 SourceCategory 分组。ToolItemModel 是 record（SourceCategory 为 init），
        /// 若属性尚未落地则按 ModuleGroup 回退分组，避免分组头整体消失。
        /// </summary>
        private sealed class SourceCategoryGroupDescription : PropertyGroupDescription
        {
            public SourceCategoryGroupDescription()
                : base("SourceCategory")
            {
            }

            public override object GroupNameFromItem(object item, int level, CultureInfo culture)
            {
                object value;
                try
                {
                    value = base.GroupNameFromItem(item, level, culture);
                }
                catch
                {
                    value = null;
                }

                if (value != null
                    && !ReferenceEquals(value, DependencyProperty.UnsetValue)
                    && !string.IsNullOrWhiteSpace(value.ToString()))
                {
                    return value;
                }

                return item is ToolItemModel node ? GetSourceCategory(node) : "Step";
            }
        }
    }

    /// <summary>
    /// 端口类型名的数组判断：DataTypeName 是 AssemblyQualifiedName（如
    /// "System.Double[], System.Private.CoreLib, Version=..."），不能用 EndsWith("[]")。
    /// 先按 Type 解析，解析不出再退回字符串包含，避免未知类型名把数组徽标/按钮藏掉。
    /// </summary>
    internal static class BindingTypeText
    {
        public static bool IsArray(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return false;
            if (TypeHelper.ResolveType(typeName)?.IsArray == true)
                return true;
            return typeName.IndexOf("[]", StringComparison.Ordinal) >= 0;
        }
    }

    /// <summary>右栏英雄区「数据来源」主文案。</summary>
    public sealed class BindingHeroNameConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            bool isEnum = values.Length > 0 && values[0] is bool b0 && b0;
            bool isConstant = values.Length > 1 && values[1] is bool b1 && b1;
            string constant = values.Length > 2 ? Text(values[2]) : string.Empty;
            string preset = values.Length > 3 ? Text(values[3]) : string.Empty;
            string linked = values.Length > 4 ? Text(values[4]) : string.Empty;
            bool isBound = values.Length > 5 && values[5] is bool b5 && b5;

            if (isEnum)
                return string.IsNullOrWhiteSpace(preset) ? "尚未选择" : preset;
            if (isConstant)
                return string.IsNullOrWhiteSpace(constant) ? "常量值未填写" : constant;
            if (isBound && !string.IsNullOrWhiteSpace(linked))
                return linked;
            return "尚未绑定";
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();

        private static string Text(object value)
            => value == null || ReferenceEquals(value, DependencyProperty.UnsetValue)
                ? string.Empty
                : value.ToString() ?? string.Empty;
    }

    /// <summary>右栏英雄区「数据来源」副文案。</summary>
    public sealed class BindingHeroHintConverter : IMultiValueConverter
    {
        private static readonly TypeNameToFriendlyNameConverter Friendly = new();

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            bool isEnum = values.Length > 0 && values[0] is bool b0 && b0;
            bool isConstant = values.Length > 1 && values[1] is bool b1 && b1;
            string constant = values.Length > 2 ? Text(values[2]) : string.Empty;
            string linked = values.Length > 3 ? Text(values[3]) : string.Empty;
            bool isBound = values.Length > 4 && values[4] is bool b4 && b4;
            string typeName = values.Length > 5 ? Text(values[5]) : string.Empty;

            if (isEnum)
                return string.IsNullOrWhiteSpace(constant) ? "从下方选择一个功能预设" : $"枚举成员：{constant}";
            if (isConstant)
                return string.IsNullOrWhiteSpace(constant)
                    ? (isBound ? $"当前上游：{linked} · 填写后将替换" : $"目标类型：{FriendlyType(typeName)}")
                    : $"目标类型：{FriendlyType(typeName)}";
            if (isBound)
                return FriendlyType(typeName);
            return "从下方选择候选输出端口";
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();

        private static string FriendlyType(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return "未知类型";
            return Friendly.Convert(typeName, typeof(string), null, CultureInfo.CurrentCulture) as string
                ?? typeName;
        }

        private static string Text(object value)
            => value == null || ReferenceEquals(value, DependencyProperty.UnsetValue)
                ? string.Empty
                : value.ToString() ?? string.Empty;
    }

    /// <summary>右栏英雄区下方的连线类型说明。</summary>
    public sealed class BindingHeroMetaConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            bool isEnum = values.Length > 0 && values[0] is bool b0 && b0;
            bool isConstant = values.Length > 1 && values[1] is bool b1 && b1;
            string constant = values.Length > 2 ? Text(values[2]) : string.Empty;
            bool isBound = values.Length > 3 && values[3] is bool b3 && b3;
            LinkKind kind = values.Length > 4 && values[4] is LinkKind k ? k : LinkKind.StepPort;

            if (isEnum)
                return "连线类型：功能预设 · 写入枚举成员名，不连接上游";
            if (isConstant)
                return string.IsNullOrWhiteSpace(constant)
                    ? (isBound
                        ? "连线类型：常量 · 填写后确定写入（当前为上游连线）"
                        : "连线类型：常量 · 填写后确定写入")
                    : "连线类型：常量 · 确定后写入固定值";
            if (isBound)
            {
                return kind switch
                {
                    LinkKind.GlobalVariable => "连线类型：全局变量 · 运行期读取变量值",
                    LinkKind.RuntimeVariable => "连线类型：运行时变量 · 由变量定义节点写入",
                    _ => "连线类型：步骤输出 · 运行期读取上游结果",
                };
            }
            return "尚未建立连线";
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();

        private static string Text(object value)
            => value == null || ReferenceEquals(value, DependencyProperty.UnsetValue)
                ? string.Empty
                : value.ToString() ?? string.Empty;
    }

    /// <summary>常量模式写入预览。</summary>
    public sealed class ConstantPreviewConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            string target = values.Length > 0 ? Text(values[0]) : string.Empty;
            string constant = values.Length > 1 ? Text(values[1]).Trim() : string.Empty;
            return constant.Length == 0
                ? $"{target} ← （保持现有绑定）"
                : $"{target} ← 常量值: {constant}";
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();

        private static string Text(object value)
            => value == null || ReferenceEquals(value, DependencyProperty.UnsetValue)
                ? string.Empty
                : value.ToString() ?? string.Empty;
    }

    /// <summary>功能预设写入预览（显示名 + 写入值分离）。</summary>
    public sealed class PresetPreviewConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            string target = values.Length > 0 ? Text(values[0]) : string.Empty;
            string option = values.Length > 1 ? Text(values[1]) : string.Empty;
            string display = values.Length > 2 ? Text(values[2]) : string.Empty;

            if (string.IsNullOrWhiteSpace(option))
                return $"{target} ← 尚未选择";
            return $"{target} ← {option}（{display}）";
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();

        private static string Text(object value)
            => value == null || ReferenceEquals(value, DependencyProperty.UnsetValue)
                ? string.Empty
                : value.ToString() ?? string.Empty;
    }

    /// <summary>候选行「绑定 / 选择索引」按钮文案。</summary>
    public sealed class CandidateActionLabelConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => BindingTypeText.IsArray(Text(value)) ? "选择索引" : "绑定";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();

        private static string Text(object value)
            => value == null || ReferenceEquals(value, DependencyProperty.UnsetValue)
                ? string.Empty
                : value.ToString() ?? string.Empty;
    }

    /// <summary>候选行是否就是当前已绑定的那个输出端口。</summary>
    public sealed class CandidateBoundVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            string linked = values.Length > 0 ? Text(values[0]) : string.Empty;
            string nodeName = values.Length > 1 ? Text(values[1]) : string.Empty;
            string portName = values.Length > 2 ? Text(values[2]) : string.Empty;
            return IsBound(linked, nodeName, portName) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();

        internal static bool IsBound(string linked, string nodeName, string portName)
        {
            if (string.IsNullOrWhiteSpace(linked) || string.IsNullOrWhiteSpace(portName))
                return false;

            if (!string.IsNullOrWhiteSpace(nodeName))
            {
                if (string.Equals(linked, $"{nodeName}.{portName}", StringComparison.Ordinal))
                    return true;
                if (linked.StartsWith($"{nodeName}.{portName}[", StringComparison.Ordinal))
                    return true;
            }

            return string.Equals(linked, $"Runtime.{portName}", StringComparison.Ordinal);
        }

        private static string Text(object value)
            => value == null || ReferenceEquals(value, DependencyProperty.UnsetValue)
                ? string.Empty
                : value.ToString() ?? string.Empty;
    }

    /// <summary>候选行「绑定」按钮可见性：可绑且尚未绑定。</summary>
    public sealed class CandidateBindButtonVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            bool bindable = values.Length > 0 && values[0] is bool b0 && b0;
            string linked = values.Length > 1 ? Text(values[1]) : string.Empty;
            string nodeName = values.Length > 2 ? Text(values[2]) : string.Empty;
            string portName = values.Length > 3 ? Text(values[3]) : string.Empty;

            if (!bindable || CandidateBoundVisibilityConverter.IsBound(linked, nodeName, portName))
                return Visibility.Collapsed;
            return Visibility.Visible;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();

        private static string Text(object value)
            => value == null || ReferenceEquals(value, DependencyProperty.UnsetValue)
                ? string.Empty
                : value.ToString() ?? string.Empty;
    }

    /// <summary>数组类型角标可见性。</summary>
    public sealed class ArrayTypeVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => BindingTypeText.IsArray(Text(value))
                ? Visibility.Visible
                : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();

        private static string Text(object value)
            => value == null || ReferenceEquals(value, DependencyProperty.UnsetValue)
                ? string.Empty
                : value.ToString() ?? string.Empty;
    }

    /// <summary>「还有 N 个端口被隐藏」提示条可见性。</summary>
    public sealed class HiddenNoteVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            int hidden = values.Length > 0 && values[0] is int i ? i : 0;
            bool showAll = values.Length > 1 && values[1] is bool b && b;
            return hidden > 0 && !showAll ? Visibility.Visible : Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>按端口数据类型挑一个 Font Awesome 字形（视觉近似设计稿的类型图标）。</summary>
    public sealed class PortGlyphConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length > 1 && values[1] is bool isEnum && isEnum)
                return "\uf022"; // list

            string type = values.Length > 0 ? Text(values[0]) : string.Empty;
            if (BindingTypeText.IsArray(type)) return "\uf0ca";
            if (type.IndexOf("HImage", StringComparison.OrdinalIgnoreCase) >= 0) return "\uf03e";
            if (type.IndexOf("HRegion", StringComparison.OrdinalIgnoreCase) >= 0) return "\uf125";
            if (type.IndexOf("Boolean", StringComparison.OrdinalIgnoreCase) >= 0) return "\uf205";
            if (type.IndexOf("String", StringComparison.OrdinalIgnoreCase) >= 0) return "\uf031";
            if (type.IndexOf("DateTime", StringComparison.OrdinalIgnoreCase) >= 0) return "\uf017";
            if (type.IndexOf("Matrix", StringComparison.OrdinalIgnoreCase) >= 0) return "\uf0ce";
            if (type.IndexOf("Object", StringComparison.OrdinalIgnoreCase) >= 0) return "\uf1b2";
            if (type.IndexOf("Double", StringComparison.OrdinalIgnoreCase) >= 0
                || type.IndexOf("Int", StringComparison.OrdinalIgnoreCase) >= 0
                || type.IndexOf("Single", StringComparison.OrdinalIgnoreCase) >= 0)
                return "\uf292";
            return "\uf0c1";
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();

        private static string Text(object value)
            => value == null || ReferenceEquals(value, DependencyProperty.UnsetValue)
                ? string.Empty
                : value.ToString() ?? string.Empty;
    }
}
