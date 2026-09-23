using System.Windows;
using System.Windows.Controls;
using VisionMaster.ViewModels;

namespace VisionMaster.Views
{
    /// <summary>
    /// ScadaLayerView.xaml 的交互逻辑（图层面板）。
    ///
    /// 本文件刻意只剩"可逆挂摘"这一件事：行的双击改名走 XAML 的 InputBindings，
    /// 图标/文案全部来自 <see cref="ScadaLayerItem"/> 快照，这里不掺任何判断。
    /// 与 ScadaEditorView / ScadaToolboxView / ScadaPropertyView 同一套写法：
    /// AvalonDock 把面板拖成浮动窗口、切标签、隐藏都会走 Unloaded，
    /// 只有"离树摘干净、入树再挂上"才不会攒出一堆僵尸订阅（旧画面被面板钉住不放）。
    /// </summary>
    public partial class ScadaLayerView : UserControl
    {
        public ScadaLayerView()
        {
            InitializeComponent();

            Loaded += OnViewLoaded;
            Unloaded += OnViewUnloaded;
        }

        // Prism 的 AutoWireViewModel 可能晚于 Loaded 才把 VM 装进 DataContext，
        // 此时入树事件已经过去，需要在这里补挂一次（Activate 幂等，不会重复订阅）
        private void OnViewDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsLoaded)
                (e.NewValue as ScadaLayerViewModel)?.Activate();
        }

        private void OnViewLoaded(object sender, RoutedEventArgs e)
            => (DataContext as ScadaLayerViewModel)?.Activate();

        private void OnViewUnloaded(object sender, RoutedEventArgs e)
            => (DataContext as ScadaLayerViewModel)?.Deactivate();
    }
}
