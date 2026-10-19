using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Core.Interfaces;
using Core.Interfaces.Io;
using VisionMaster.Binding;
using VisionMaster.Communications;
using VisionMaster.Models;
using VisionMaster.Services;
using VisionMaster.Services.Io;
using VisionMaster.Services.Health;
using VisionMaster.ViewModels.DialogViewModels;
using VisionMaster.Views.DialogViews;

namespace IoChecks
{
    /// <summary>
    /// IO 视图渲染冒烟：在 STA 线程 + 屏幕外窗口里真实例化 XAML，
    /// 验证列头不丢、行模板是 GridViewRowPresenter、健康面板能绑定并渲染出多行。
    ///
    /// 为什么要真窗口（而不是纯 VM 断言）：R19/R20 那两族缺陷（列头消失、行塌成一列）
    /// 只在模板实例化时暴露，VM 断言全绿也拦不住。
    /// </summary>
    internal static class IoViewSmoke
    {
        public static void Run(Action<string, bool, string?> check)
        {
            var thread = new Thread(() => RunCore(check)) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join(TimeSpan.FromSeconds(30));
        }

        private static void RunCore(Action<string, bool, string?> check)
        {
            Application? app = null;
            try
            {
                // 断言宿主没有 Prism 应用级资源：显式建一个 Application 挂主题字典，
                // 否则控件没有隐式样式、模板套不上（R53）。
                app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/UI;component/Themes/Generic.xaml")
                });

                var window = new Window
                {
                    Left = -4000,
                    Top = -4000,
                    Width = 1100,
                    Height = 700,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                    ShowActivated = false
                };

                var (vm, host) = BuildHealthViewModel();
                var view = new IoHealthPanelView { DataContext = vm };
                window.Content = view;
                window.Show();
                Pump(200);

                vm.OnDialogOpened(new Prism.Dialogs.DialogParameters());
                Pump(300);

                check("健康面板：视图可实例化并挂载", view.IsLoaded, view.IsLoaded.ToString());

                var tabs = FindDescendant<TabControl>(view);
                check("健康面板：四个页签（设备 / 总线 / 点位 / 写入审计）",
                    tabs != null && tabs.Items.Count == 4,
                    tabs?.Items.Count.ToString() ?? "找不到 TabControl");

                // 2026-10-19 真机事故：TabItem 模板依赖 Grid 行高分配，页签区只剩约 20px，
                // 文字被裁成一条。加页签时容易再次踩到，所以把"高度够不够"钉成断言。
                // 容器缺失时判红而不是跳过：跳过会让"结构没了"伪装成通过。
                var clipped = new List<string>();
                if (tabs != null)
                {
                    foreach (var item in tabs.Items)
                    {
                        if (item is TabItem tab && tab.ActualHeight < 30)
                            clipped.Add($"{tab.Header}={tab.ActualHeight:F0}px");
                    }
                }
                check("健康面板：页签高度 ≥30px（不重现「只剩一条文字」）",
                    tabs != null && clipped.Count == 0,
                    tabs == null
                        ? "找不到 TabControl"
                        : (clipped.Count == 0 ? "全部 ≥30px" : string.Join("，", clipped)));

                if (tabs != null)
                {
                    vm.SelectedTab = 3;
                    view.UpdateLayout();
                    Pump(250);
                }

                var auditList = FindListViewByItemsSource(view, vm.AuditRows);
                check("写入审计页签：表格已渲染", auditList != null,
                    auditList == null ? "按 ItemsSource 定位失败（页签没切过去或视图结构变了）" : null);
                check("写入审计页签：6 列（结果灯 / 时间 / 结果 / 操作者 / 点位 / 详情）",
                    auditList?.View is GridView auditGrid && auditGrid.Columns.Count == 6,
                    (auditList?.View as GridView)?.Columns.Count.ToString() ?? "非 GridView");
                check("写入审计页签：无记录时给出空态说明",
                    vm.AuditRows.Count == 0 && vm.AuditSummary == "暂无写入记录",
                    $"{vm.AuditRows.Count} 条 / {vm.AuditSummary}");

                if (tabs != null)
                {
                    vm.SelectedTab = 0;
                    view.UpdateLayout();
                    Pump(200);
                }

                // 设备表按 ItemsSource 定位，不再用"第一个 ListView"：
                // 页签顺序一变，"第一个"就会指到别的表上，断言会假绿。
                var listView = FindListViewByItemsSource(view, vm.Devices);
                check("健康面板：找得到设备表（按 ItemsSource 定位）",
                    listView != null, listView == null ? "按 ItemsSource 定位失败" : null);

                check("健康面板：ListView 挂了 GridListViewStyle",
                    listView != null
                    && listView.ReadLocalValue(FrameworkElement.StyleProperty) != DependencyProperty.UnsetValue,
                    listView == null ? "找不到设备表" : "Style=" + listView.Style?.GetHashCode());

                // 行模板检查：BAML 编译后的 ControlTemplate.VisualTree 为 null，
                // 静态读模板树读不到任何东西（会误判）。改为检查**已生成的行容器**——
                // 这才是"屏幕上那一行"的真实结构。
                check("健康面板：设备行已渲染",
                    listView != null && listView.Items.Count == 1,
                    listView == null ? "找不到设备表" : listView.Items.Count.ToString());

                ListViewItem? rowItem = null;
                GridViewRowPresenter? presenter = null;
                if (listView != null)
                {
                    listView.UpdateLayout();
                    Pump(150);
                    rowItem = FindDescendant<ListViewItem>(listView);
                    presenter = rowItem == null ? null : FindDescendant<GridViewRowPresenter>(rowItem);
                }
                check("健康面板：行容器已生成", rowItem != null,
                    rowItem == null ? "没生成 ListViewItem" : null);
                check("健康面板：行模板是 GridViewRowPresenter", presenter != null,
                    presenter == null ? "行容器里没有 GridViewRowPresenter（列布局会塌）" : null);
                check("健康面板：GridView 列数 > 0",
                    listView?.View is GridView gv && gv.Columns.Count > 0,
                    (listView?.View as GridView)?.Columns.Count.ToString() ?? "非 GridView");

                // ---- 壳冒烟：系统健康 5 页签 + 概览 4 张域卡 ----
                // 与 IO 面板共用同一个 Application（同一 AppDomain 只能有一个）。
                var shellVm = new SystemHealthViewModel(
                    new CommunicationHealthMonitor(new AdvancedCommunicationManager()),
                    new CameraHealthMonitor(NullCameraProvider.Instance),
                    new MotionHealthMonitor(NullMotionProvider.Instance),
                    vm);

                var shellView = new SystemHealthView { DataContext = shellVm };
                var shellWindow = new Window
                {
                    Left = -4200,
                    Top = -4000,
                    Width = 1180,
                    Height = 700,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                    ShowActivated = false
                };
                shellWindow.Content = shellView;
                shellWindow.Show();
                Pump(200);

                shellVm.OnDialogOpened(new Prism.Dialogs.DialogParameters());
                Pump(250);

                var shellTabs = FindDescendant<TabControl>(shellView);
                check("系统健康：五个页签（概览 / 通讯 / 相机 / 运动 / IO）",
                    shellTabs != null && shellTabs.Items.Count == 5,
                    shellTabs?.Items.Count.ToString() ?? "找不到 TabControl");

                var shellCards = FindDescendant<ItemsControl>(shellView);
                check("系统健康：概览四张域卡",
                    shellCards != null && shellCards.Items.Count == 4,
                    shellCards?.Items.Count.ToString() ?? "找不到 ItemsControl");

                shellVm.OnDialogClosed();
                shellWindow.Close();
                Pump(120);

                RunIoConfigSmoke(check);

                // 不调 vm.CloseCommand：它走 Prism 的 RequestClose，
                // 而这个冒烟没有 DialogService 初始化回调（会抛 InvalidOperationException）。
                // 停表由 OnDialogClosed 负责，直接调它即可。
                vm.OnDialogClosed();
                window.Close();
                Pump(150);
            }
            catch (Exception ex)
            {
                check("健康面板：渲染冒烟未抛异常", false, ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                try { app?.Shutdown(); } catch { }
            }
        }

        /// <summary>
        /// IO 设置弹窗冒烟：三栏卡片 + 点位表 + 空态。
        /// <para>只验"结构渲染出来了"（卡片、列数、空态），业务逻辑在 Program.cs 的 U 段。</para>
        /// </summary>
        private static void RunIoConfigSmoke(Action<string, bool, string?> check)
        {
            // IO 设置用了 App 级的 Fluent 别名（DialogIconButton / Icon 字体），
            // 断言宿主默认只合并 Generic.xaml，缺这一层会在解析期抛"找不到资源"。
            if (Application.Current != null)
            {
                // 视图用了 App 级的 Icon 字体族（App.xaml 定义）；断言宿主没有 App.xaml，
                // 这里补一个等价字体，避免解析期抛"找不到资源"。
                if (Application.Current.Resources["Icon"] == null)
                    Application.Current.Resources["Icon"] = new System.Windows.Media.FontFamily("Segoe MDL2 Assets");

                try
                {
                    Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri("pack://application:,,,/UI;component/Themes/Fluent/FluentAliases.xaml")
                    });
                }
                catch
                {
                    // 已合并过 / 该宿主不需要：忽略，让后面的断言给出真实结论。
                }
            }

            var workspace = new WorkspaceContext();
            workspace.SwitchSolution(new SolutionModel { SolutionId = Guid.NewGuid() });

            var registry = new IoDriverRegistry();
            var provider = new IoProvider(registry, new NullLogService());
            var catalog = new IoVariableCatalog(workspace, provider, new NullLogService());
            var vm = new IoConfigViewModel(workspace, provider, catalog, new SilentNotifier(), registry);
            vm.OnDialogOpened(new Prism.Dialogs.DialogParameters());

            var view = new IoConfigView { DataContext = vm };
            var window = new Window
            {
                Left = -4400,
                Top = -4000,
                Width = 1180,
                Height = 720,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                ShowActivated = false
            };
            window.Content = view;
            window.Show();
            Pump(220);

            check("IO 设置：视图可实例化并挂载", view.IsLoaded, view.IsLoaded.ToString());

            var cardStyle = view.TryFindResource("DialogColumnCard");
            int cards = CountDescendants<Border>(view, b => ReferenceEquals(b.Style, cardStyle));
            check("IO 设置：三栏 + 属性共 4 张卡片", cards == 4, $"实际 {cards} 张");

            var pointList = FindListViewByItemsSource(view, vm.FilteredPoints);
            check("IO 设置：点位表已渲染", pointList != null,
                pointList == null ? "按 ItemsSource 定位失败" : null);
            check("IO 设置：点位表 5 列（点位/类型/地址/显示名/说明）",
                pointList?.View is GridView grid && grid.Columns.Count == 5,
                (pointList?.View as GridView)?.Columns.Count.ToString() ?? "非 GridView");

            window.Close();
            Pump(100);
        }

        private static int CountDescendants<T>(DependencyObject root, Func<T, bool> predicate) where T : DependencyObject
        {
            int count = 0;
            if (root is T hit && predicate(hit)) count++;

            int children = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < children; i++)
                count += CountDescendants<T>(VisualTreeHelper.GetChild(root, i), predicate);

            return count;
        }

        private static (IoHealthViewModel vm, IoProvider provider) BuildHealthViewModel()
        {
            // 审计环形队列是进程级静态状态：前面的用例写过 IO 审计，
            // 不清掉的话"无记录空态"这条断言会受执行顺序影响（偶发红）。
            // ClearRecentForTests 只清内存，不碰 audit_logs 文件。
            VisionMaster.Core.AuditLogger.ClearRecentForTests();

            var workspace = new WorkspaceContext();
            workspace.SwitchSolution(new SolutionModel { SolutionId = Guid.NewGuid() });

            var bus = new IoBusDescriptor
            {
                BusId = "BUS_1", DisplayName = "总线1", TransportKind = IoTransportKind.Tcp
            };
            var device = new IoDeviceDescriptor
            {
                DeviceId = "IO_1", DisplayName = "站1", BusId = "BUS_1", DriverId = "simulated",
                Points = new List<IoPointDescriptor>
                {
                    new() { PointId = "DI_1", Kind = IoPointKind.DigitalInput, DataType = IoDataType.Bool, Address = "1" }
                }
            };
            workspace.CurrentSolution.IoBuses = new List<IoBusDescriptor> { bus };
            workspace.CurrentSolution.IoDevices = new List<IoDeviceDescriptor> { device };

            var provider = new IoProvider(new IoDriverRegistry(), new NullLogService());
            var catalog = new IoVariableCatalog(workspace, provider, new NullLogService());
            provider.Apply(new[] { bus }, new[] { device }, autoStart: false);
            catalog.Rebuild();

            return (new IoHealthViewModel(new IoHealthMonitor(provider, catalog)), provider);
        }

        private static bool HasGridViewRowPresenter(Style? rowStyle)
        {
            if (rowStyle?.Setters == null) return false;
            foreach (var setter in rowStyle.Setters)
            {
                if (setter is not Setter s || s.Property != Control.TemplateProperty) continue;
                if (s.Value is ControlTemplate) return true;   // XAML 里声明了模板
            }
            return false;
        }

        private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            if (root is T hit) return hit;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var found = FindDescendant<T>(VisualTreeHelper.GetChild(root, i));
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>按 ItemsSource 实例定位 ListView（多页签时"第一个"会随选中页签变化）。</summary>
        private static ListView? FindListViewByItemsSource(DependencyObject root, object source)
        {
            if (root is ListView list && ReferenceEquals(list.ItemsSource, source)) return list;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var found = FindListViewByItemsSource(VisualTreeHelper.GetChild(root, i), source);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>手动泵 Dispatcher（断言宿主没有真实消息循环）。</summary>
        private static void Pump(int milliseconds)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(milliseconds)
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                frame.Continue = false;
            };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }

        private sealed class SilentNotifier : IUserNotifier
        {
            public void ShowInfo(string message) { }
            public void ShowWarn(string message) { }
            public void ShowError(string message) { }
        }

        private sealed class NullLogService : ILogService
        {
            public void Success(params string[] messages) { }
            public void Error(params Exception[] messages) { }
            public void Error(params string[] messages) { }
            public void Info(params string[] messages) { }
            public void Warn(params string[] messages) { }
        }
    }
}
