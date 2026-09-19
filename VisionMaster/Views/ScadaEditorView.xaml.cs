using System.Windows;
using System.Windows.Controls;
using VisionMaster.Scada.Controls;
using VisionMaster.ViewModels;

namespace VisionMaster.Views
{
    /// <summary>
    /// ScadaEditorView.xaml 的交互逻辑（组态画面编辑器宿主面板）
    /// </summary>
    public partial class ScadaEditorView : UserControl
    {
        /// <summary>工具栏缩放一档的倍率，与滚轮那一档取值一致（见 <c>ScadaCanvas.Interaction</c>）</summary>
        private const double ZoomStep = 1.1;

        public ScadaEditorView()
        {
            InitializeComponent();

            // 入树/离树成对挂摘订阅：AvalonDock 切标签、隐藏面板都会触发 Unloaded，
            // 所以清理必须可逆（离树摘干净让旧 VM 可被 GC，入树重新挂上），不能用一次性 Dispose。
            Loaded += OnViewLoaded;
            Unloaded += OnViewUnloaded;
        }

        // Prism 的 AutoWireViewModel 可能晚于 Loaded 才把 VM 装进 DataContext，
        // 此时入树事件已经过去，需要在这里补挂一次（Activate 幂等，不会重复订阅）
        private void OnViewDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsLoaded)
                (e.NewValue as ScadaEditorViewModel)?.Activate();
        }

        private void OnViewLoaded(object sender, RoutedEventArgs e)
            => (DataContext as ScadaEditorViewModel)?.Activate();

        private void OnViewUnloaded(object sender, RoutedEventArgs e)
            => (DataContext as ScadaEditorViewModel)?.Deactivate();

        // ---- 取景按钮 ----
        // 这四个动作刻意写在代码后台而不是视图模型的命令里：缩放/平移是"这台机器这一次的视角"，
        // 既不落盘也不该被别的对象查询。做成命令就得让 VM 反过来持有画布实例，
        // 那是拿可测试性换一个用不上的抽象。

        private void OnZoomIn(object sender, RoutedEventArgs e) => Canvas.ZoomBy(ZoomStep);

        private void OnZoomOut(object sender, RoutedEventArgs e) => Canvas.ZoomBy(1 / ZoomStep);

        private void OnFitToScreen(object sender, RoutedEventArgs e) => Canvas.FitToScreen();

        private void OnActualSize(object sender, RoutedEventArgs e) => Canvas.ZoomToActualSize();

        // ---- 从工具箱放置图元 ----
        // 落点换算交给画布（ToDropOrigin：视口→设计、以落点为中心、吸网格、夹进画面），
        // 造模型交给视图模型（AddElement：去重命名、抬 ZIndex、Add 进集合后由画布自己长控件）。
        // 这一层只做"把鼠标位置和数据递过去"，两段知识都不属于它。

        private void OnCanvasDragOver(object sender, DragEventArgs e)
        {
            // Effects 决定光标长相：不是本协议的负载就显"禁止"，别让用户对一个放不下的东西
            // 反复试。这里不兜底当纯文本读——否则从记事本拖几个字进画布也会生图元。
            e.Effects = CanAccept(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void OnCanvasDrop(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.None;

            if (!ScadaDrag.TryGetTypeKey(e.Data, out string typeKey)) return;
            if (ElementRegistry.Find(typeKey) is not { } descriptor) return;
            if (DataContext is not ScadaEditorViewModel viewModel) return;

            // 尺寸取该图元的默认宽高，否则吸附的是"想象中的左上角"，落点会偏半个图元
            Point origin = Canvas.ToDropOrigin(
                e.GetPosition(Canvas), descriptor.DefaultWidth, descriptor.DefaultHeight);

            if (viewModel.AddElement(typeKey, origin) != null)
                e.Effects = DragDropEffects.Copy;

            e.Handled = true;
        }

        /// <summary>拖拽经过时能不能放：负载得是我们的协议，且当前有画面可写。
        /// 光看负载不看画面的话，光标一路都是"可放"，松手却什么都不发生——最难查的那种反馈缺失。</summary>
        private bool CanAccept(IDataObject? data)
            => ScadaDrag.TryGetTypeKey(data, out string typeKey)
               && ElementRegistry.Find(typeKey) != null
               && (DataContext as ScadaEditorViewModel)?.SelectedPage != null;
    }
}
