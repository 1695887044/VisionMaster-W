using System;
using System.ComponentModel;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Core.Interfaces;
using UI.Models;
using VisionMaster.Services;
using VisionMaster.ViewModels;
using VisionMaster.Views;

namespace VisionMaster
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class Shell : Window
    {
        public Shell()
        {
            InitializeComponent();
            ApplyDockTheme();
        }

        /// <summary>
        /// 应用 AvalonDock 停靠主题：VS2013Light 控件模板 + Fluent Light 调色板画刷覆盖
        /// （切深色主题时把 Source 换成 DarkTheme.xaml 即可，模板复用同一套）
        /// </summary>
        private void ApplyDockTheme()
        {
            dockManager.Theme = new FluentLightTheme();
        }

        /// <summary>
        /// 胶囊点击：走 Click 事件而不是 InputBinding/Command 绑定。
        /// MouseBinding 不在可视树里、RelativeSource 解析不到 DataContext；Button 的 Command
        /// 绑定在实测里只拿到焦点没有执行，所以这里直接由 DataContext 取命令并执行。
        /// </summary>
        private void OnInspectorCapsuleClick(object sender, RoutedEventArgs e)
        {
            // 点击链路留痕：本仓 Logs\yyyy-MM-dd.log 只由 ILogService 写（NLog 无配置），
            // 没有这两行就无法区分"没点中"和"点了但守卫没通过"。
            var log = TryResolveLogService();
            log?.Warn($"[胶囊] Click 到达 sender={sender?.GetType().Name} " +
                      $"item={(sender as FrameworkElement)?.DataContext?.GetType().Name} " +
                      $"shellDc={DataContext?.GetType().Name}");

            if (sender is FrameworkElement { DataContext: InspectorCapsuleItem item }
                && DataContext is ShellViewModel vm)
            {
                vm.ShowInspectorCapsuleCommand.Execute(item);
            }
            else
            {
                log?.Warn("[胶囊] Click 到达但守卫未通过（item 或 ShellViewModel 不匹配）");
            }
        }

        private static ILogService? TryResolveLogService()
        {
            try { return Prism.Ioc.ContainerLocator.Container.Resolve<ILogService>(); }
            catch { return null; }
        }

        /// <summary>
        /// 状态栏「系统资源」浮窗：点 CPU/MEM 打开。
        /// Popup 用 <c>StaysOpen=False</c>（点外面自动关），代价是"开着的时候再点按钮"这一下
        /// 会先被 Popup 自己关掉、再触发 Click 又立刻打开——看起来像关不掉。
        /// 所以在 PreviewMouseLeftButtonDown 里先把"已开着"的那一下吃掉，Click 只负责打开。
        /// </summary>
        private void PerfButton_Click(object sender, RoutedEventArgs e) => PerfPopup.IsOpen = true;

        private void PerfButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!PerfPopup.IsOpen)
                return;

            PerfPopup.IsOpen = false;
            e.Handled = true;
        }

        /// <summary>
        /// F10（单步）的窗口级截获（DWV 第 1 期）。
        ///
        /// 【为什么不用 InputBindings】F10 是 Windows / WPF 的"菜单激活键"：带 Menu 的窗口按 F10
        /// 会进入菜单模式（平台约定、Menu 内建参与处理）。普通 KeyBinding 不保证先于该行为拿到 F10；
        /// 在隧道阶段（PreviewKeyDown）先行截获并 e.Handled=true，既能确定性执行"单步"，
        /// 也顺带挡掉"按 F10 弹菜单"的干扰（任务书预案，结论见交付报告）。
        ///
        /// 【修饰键】只认无修饰键的 F10：Shift+F10（系统上下文菜单键）、Alt+F10 等一律放行。
        /// （参数类型用全限定名：本文件同时 using System.Windows.Forms，KeyEventArgs 存在二义）
        /// </summary>
        private void OnShellPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (DataContext is ShellViewModel shellVm)
            {
                if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
                {
                    shellVm.OpenCommandPaletteCommand.Execute();
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.Escape && shellVm.IsCommandPaletteOpen)
                {
                    shellVm.CloseCommandPaletteCommand.Execute();
                    e.Handled = true;
                    return;
                }
            }

            if (e.Key != Key.F10 || Keyboard.Modifiers != ModifierKeys.None) return;

            if (DataContext is ShellViewModel vm
                && vm.ExecutionCommand.CanExecute(ExecutionAction.StepOnce))
            {
                vm.ExecutionCommand.Execute(ExecutionAction.StepOnce);
            }

            e.Handled = true;
        }

        private void CommandPaletteOverlay_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is not true) return;

            Dispatcher.BeginInvoke((Action)(() =>
            {
                CommandPaletteBox.Focus();
                Keyboard.Focus(CommandPaletteBox);
            }));
        }

        private void CommandPaletteScrim_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is ShellViewModel vm)
                vm.CloseCommandPaletteCommand.Execute();
        }

        private void CommandPaletteBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (DataContext is not ShellViewModel vm) return;

            switch (e.Key)
            {
                case Key.Down:
                    MovePaletteSelection(vm, 1);
                    e.Handled = true;
                    break;
                case Key.Up:
                    MovePaletteSelection(vm, -1);
                    e.Handled = true;
                    break;
                case Key.Enter:
                    vm.ExecuteCommandPaletteItemCommand.Execute();
                    e.Handled = true;
                    break;
                case Key.Escape:
                    vm.CloseCommandPaletteCommand.Execute();
                    e.Handled = true;
                    break;
            }
        }

        private void CommandPaletteList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is ShellViewModel vm && vm.SelectedCommandPaletteItem != null)
            {
                vm.ExecuteCommandPaletteItemCommand.Execute();
                e.Handled = true;
            }
        }

        private void MovePaletteSelection(ShellViewModel vm, int delta)
        {
            if (vm.CommandPaletteItems.Count == 0) return;

            var index = CommandPaletteList.SelectedIndex;
            index = Math.Clamp(index + delta, 0, vm.CommandPaletteItems.Count - 1);
            CommandPaletteList.SelectedIndex = index;
            CommandPaletteList.ScrollIntoView(CommandPaletteList.SelectedItem);
        }

        /// <summary>
        /// DictionaryTheme 是抽象类，需派生具体主题传入自定义字典
        /// </summary>
        private sealed class FluentLightTheme : AvalonDock.Themes.DictionaryTheme
        {
            public FluentLightTheme()
                : base(new ResourceDictionary
                {
                    Source = new Uri(
                        "pack://application:,,,/VisionMaster;component/Themes/DockThemes/FluentLight.xaml")
                })
            {
            }
        }

        /// <summary>
        /// 窗口渲染完成后恢复上次的布局（文件不存在或损坏时使用 XAML 默认布局），
        /// 随后按软件配置自动加载默认启动方案
        /// </summary>
        protected override async void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            bool layoutLoaded = LayoutHelper.Load();

            if (DataContext is ShellViewModel vm)
            {
                // 无条件补挂一次活动栏监听：布局恢复失败时（首次启动没有 Layout.xml，
                // 或磁盘上的旧布局被结构版本闸拒绝）LayoutHelper.LayoutLoaded 不会触发，
                // 只靠构造函数里那一句订阅就没人挂监听 → 五个 Toggle 停在初值 false，
                // 面板明明都在、活动栏却一个高亮都没有，直到用户点一次开关才恢复。
                // AttachDockWatchers 先解绑再挂、幂等，重复调用安全；
                // 事件那一路仍然保留，负责"用户中途加载/重置布局后重挂"（LayoutContent 实例会被换掉）。
                vm.AttachDockWatchers();

                // 再按模式收敛右侧面板组：磁盘上的 Layout.xml 会把上次退出时的可见性原样恢复，
                // 必须在这里盖一次，否则"组态三面板只在组态设计时显示"这条改造对老布局等于没发生。
                vm.ApplyPanelMode();

                // A 方案（2026-10-26）：首次启动没有 Layout.xml 时，调试/自由默认把底部控制台
                // 收成 32，把 200px 还给图像。有布局文件时由 LoadFromString 的 ConsoleState
                // 属性决定（老布局没有该属性 → 按模式默认，同样是 32）。
                if (!layoutLoaded && vm.CurrentMode is ShellMode.Debug or ShellMode.Free)
                    LayoutHelper.CollapseConsole();

                await vm.AutoLoadStartupSolutionAsync();
            }
        }

        /// <summary>
        /// 关闭窗口时自动保存布局
        /// </summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            LayoutHelper.Save();
        }
    }

}