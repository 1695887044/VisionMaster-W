using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Core.Interfaces;
using Prism.Ioc;
using VisionMaster.ViewModels.DialogViewModels;

namespace VisionMaster.Views.DialogViews
{
    /// <summary>
    /// 报警配置弹窗视图。代码后置只承担 XAML 表达不了的三件事：
    /// ① 打开「报警组」历史值菜单；② 打开组筛选菜单；③ 新增行后滚到它并聚焦名称格。
    /// 业务逻辑一律在 <c>ScadaAlarmConfigDialogViewModel</c>，这里不碰报警数据。
    /// </summary>
    public partial class ScadaAlarmConfigDialogView : UserControl
    {
        private ScadaAlarmConfigDialogViewModel? _hookedViewModel;

        public ScadaAlarmConfigDialogView()
        {
            InitializeComponent();
            Loaded += OnViewLoaded;
            Unloaded += OnViewUnloaded;
        }

        private void OnViewLoaded(object sender, RoutedEventArgs e)
        {
            UnhookViewModel();
            _hookedViewModel = DataContext as ScadaAlarmConfigDialogViewModel;
            if (_hookedViewModel != null)
                _hookedViewModel.RowAdded += OnRowAdded;
        }

        private void OnViewUnloaded(object sender, RoutedEventArgs e) => UnhookViewModel();

        private void UnhookViewModel()
        {
            if (_hookedViewModel == null)
                return;

            _hookedViewModel.RowAdded -= OnRowAdded;
            _hookedViewModel = null;
        }

        /// <summary>
        /// 打开某一行「报警组」格子里那个小箭头的历史值菜单。
        /// Button 没有"下拉"概念，得手动把 ContextMenu 挂到按钮下方并展开
        /// （与扫描组编辑器的「一键预设」同一手法）。
        /// </summary>
        private void GroupHistoryButton_Click(object sender, RoutedEventArgs e)
            => OpenMenu(sender);

        /// <summary>打开工具栏「组」筛选胶囊的候选菜单（全部 + 本方案用过的组名）。</summary>
        private void GroupFilterButton_Click(object sender, RoutedEventArgs e)
            => OpenMenu(sender);

        private static void OpenMenu(object sender)
        {
            if (sender is not Button button || button.ContextMenu is null)
                return;

            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.Placement = PlacementMode.Bottom;
            button.ContextMenu.IsOpen = true;
        }

        /// <summary>
        /// 新增行后滚到它、选中它、把焦点放进名称格。
        /// 视图做不到（容器还没生成 / 找不到 TextBox）时落一条 Warn，不静默吞掉。
        /// </summary>
        private void OnRowAdded(ScadaAlarmConfigRow row)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                var log = TryResolveLogService();
                try
                {
                    AlarmGrid.UpdateLayout();
                    AlarmGrid.ScrollIntoView(row);

                    if (AlarmGrid.ItemContainerGenerator.ContainerFromItem(row) is not DataGridRow container)
                    {
                        log?.Warn("[报警配置] 新增行已写入方案，但表格容器尚未生成，未能聚焦名称格。");
                        return;
                    }

                    container.ApplyTemplate();
                    var presenter = FindVisualChild<DataGridCellsPresenter>(container);
                    var cell = presenter?.ItemContainerGenerator.ContainerFromIndex(0) as DataGridCell;
                    if (cell == null)
                    {
                        log?.Warn("[报警配置] 新增行已写入方案，但名称格尚未生成，未能聚焦。");
                        return;
                    }

                    cell.ApplyTemplate();
                    var input = FindVisualChild<TextBox>(cell);
                    if (input == null)
                    {
                        log?.Warn("[报警配置] 新增行已写入方案，但名称输入框尚未生成，未能聚焦。");
                        return;
                    }

                    container.IsSelected = true;
                    input.Focus();
                    Keyboard.Focus(input);
                    input.SelectAll();

                    // 详情块是"这一行被选中之后"才撑高的：上面那次 ScrollIntoView 按没有详情的
                    // 行高算，新行就正好贴在底边、展开的详情被裁在窗口外。聚焦完成后容器已经量过，
                    // 再滚一次才把"行 + 详情"整体摆进可视区。
                    AlarmGrid.UpdateLayout();
                    AlarmGrid.ScrollIntoView(row);
                }
                catch (Exception ex)
                {
                    log?.Warn($"[报警配置] 新增行聚焦失败：{ex.Message}");
                }
            }));
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            var count = VisualTreeHelper.GetChildrenCount(parent);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match)
                    return match;

                var nested = FindVisualChild<T>(child);
                if (nested != null)
                    return nested;
            }

            return null;
        }

        private static ILogService? TryResolveLogService()
        {
            try { return ContainerLocator.Container.Resolve<ILogService>(); }
            catch { return null; }
        }
    }
}
