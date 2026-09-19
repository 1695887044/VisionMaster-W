using System.Windows;
using System.Windows.Controls;
using VisionMaster.ViewModels;

namespace VisionMaster.Views
{
    /// <summary>
    /// ScadaPropertyView.xaml 的交互逻辑（图元属性面板）
    /// </summary>
    public partial class ScadaPropertyView : UserControl
    {
        public ScadaPropertyView()
        {
            InitializeComponent();

            // 与 ScadaEditorView / ScadaToolboxView 同一套可逆挂摘：AvalonDock 把面板拖成浮动窗口、
            // 切标签、隐藏都会走 Unloaded，只有"离树摘干净、入树再挂上"才不会攒出一堆僵尸订阅。
            Loaded += OnViewLoaded;
            Unloaded += OnViewUnloaded;
        }

        // Prism 的 AutoWireViewModel 可能晚于 Loaded 才把 VM 装进 DataContext，
        // 此时入树事件已经过去，需要在这里补挂一次（Activate 幂等，不会重复订阅）
        private void OnViewDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsLoaded)
                (e.NewValue as ScadaPropertyViewModel)?.Activate();
        }

        private void OnViewLoaded(object sender, RoutedEventArgs e)
            => (DataContext as ScadaPropertyViewModel)?.Activate();

        private void OnViewUnloaded(object sender, RoutedEventArgs e)
            => (DataContext as ScadaPropertyViewModel)?.Deactivate();
    }
}
