using System.Linq;
using System.Windows;
using System.Windows.Controls;
using VisionMaster.Scada;
using VisionMaster.Scada.Controls;
using VisionMaster.Services;
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

            if (DataContext is not ScadaEditorViewModel viewModel) return;

            // 两条路：工具箱拖图元（类型键，造一个空壳）与拖模板（模板 Id，搬一整块带属性/绑定/事件的内容）。
            // 分开两段写而不是合成一段带分支的：两段要算的落点不一样——空壳用描述符的默认宽高，
            // 模板要用快照自己的外接框，合起来只会互相绊。
            if (ScadaDrag.TryGetTypeKey(e.Data, out string typeKey))
            {
                if (ElementRegistry.Find(typeKey) is not { } descriptor) return;

                // 尺寸取该图元的默认宽高，否则吸附的是"想象中的左上角"，落点会偏半个图元
                Point origin = Canvas.ToDropOrigin(
                    e.GetPosition(Canvas), descriptor.DefaultWidth, descriptor.DefaultHeight);

                if (viewModel.AddElement(typeKey, origin) != null)
                    e.Effects = DragDropEffects.Copy;

                e.Handled = true;
                return;
            }

            if (ScadaDrag.TryGetTemplateId(e.Data, out Guid templateId)
                && ScadaTemplateStore.Shared.GetPayload(templateId) is { IsEmpty: false } payload)
            {
                // 模板是一整块内容，落点要的是"这一块的左上角落在鼠标处"：所以先量出快照的外接框，
                // 拿外接框的宽高去换落点（ToDropOrigin 是"以落点为心"），再反推相对原坐标的偏移——
                // 物化那一步只认偏移，它不认识鼠标，也不该认识。
                ScadaClipboard.GetBounds(payload, out double left, out double top, out double width, out double height);

                Point origin = Canvas.ToDropOrigin(e.GetPosition(Canvas), width, height);

                string label = ScadaTemplateStore.Shared.Templates.FirstOrDefault(t => t.TemplateId == templateId)
                    is { } info
                    ? $"插入模板 [{info.Name}]"
                    : $"插入模板 {payload.Items.Count} 个图元";

                if (viewModel.PlacePayload(payload, origin.X - left, origin.Y - top, label).Count > 0)
                    e.Effects = DragDropEffects.Copy;

                e.Handled = true;
            }
        }

        /// <summary>拖拽经过时能不能放：负载得是我们的协议，且当前有画面可写。
        /// 光看负载不看画面的话，光标一路都是"可放"，松手却什么都不发生——最难查的那种反馈缺失。</summary>
        private bool CanAccept(IDataObject? data)
        {
            if ((DataContext as ScadaEditorViewModel)?.SelectedPage == null)
                return false;

            if (ScadaDrag.TryGetTypeKey(data, out string typeKey))
                return ElementRegistry.Find(typeKey) != null;

            // 模板光判"是不是模板负载"不够，还得看库里取不取得到内容：模板可能在"拖起来之后、
            // 松手之前"被另一个面板删掉，那样光标一路显可放、松手却什么都不发生。
            return ScadaDrag.TryGetTemplateId(data, out Guid templateId)
                   && ScadaTemplateStore.Shared.GetPayload(templateId) is { IsEmpty: false };
        }

        // ---- 图元右键菜单 ----
        // 菜单的"内容"由视图模型产出（BuildElementContextMenu：有哪些图层、哪项打勾、哪项判灰），
        // 这里只做"描述 → 控件"的机械翻译。分这条界的原因见 ScadaMenuItem 的类注释：
        // 菜单内容是最该被断言的部分，而断言工程里没有 Application/Dispatcher，造不了控件。

        /// <summary>
        /// 画布右键菜单：每次弹出都按当前选中图元现装一份。
        ///
        /// 没有选中图元时把事件吃掉（<c>Handled = true</c>）：菜单里每一项都要落在"选中的那个图元"上，
        /// 没有目标时弹出来只会是一排灰按钮，不如不弹。运行态同理——操作员跑画面时不该见到编辑器菜单。
        ///
        /// 注意选中态的<b>对齐</b>不在这里：右键按下的那一刻，画布自己已经把光标下的图元选起来了
        /// （见 ScadaCanvas.Interaction 的 OnPreviewMouseRightButtonDown），
        /// 所以这里读到的 SelectedElement 就是"用户指着的那一个"。
        /// </summary>
        private void OnCanvasContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (sender is not ScadaCanvas canvas
                || canvas.IsReadOnly
                || canvas.ContextMenu is not { } menu
                || DataContext is not ScadaEditorViewModel editor)
            {
                e.Handled = true;
                return;
            }

            var items = editor.BuildElementContextMenu();
            if (items.Count == 0)
            {
                e.Handled = true;
                return;
            }

            menu.Items.Clear();
            foreach (var item in items)
                menu.Items.Add(ToMenuItem(item));
        }

        /// <summary>
        /// 把菜单描述机械地翻译成控件：一个字段对一处赋值，不做任何判断。
        /// 子项递归同一套翻译——图层的勾选、叠放的判灰都已在描述里定好，这里不再算第二遍。
        ///
        /// <c>internal</c>（配 <c>InternalsVisibleTo("ScadaChecks")</c>）是为了让离屏断言直接复用
        /// 这一份翻译：漏抄一个字段（比如忘了搬 <c>Children</c>）在菜单上就是"子菜单点开是空的"，
        /// 而这种缺陷只有真装一次控件才看得出来。断言里另抄一份是验不出它的。
        /// </summary>
        internal static MenuItem ToMenuItem(ScadaMenuItem item)
        {
            var menuItem = new MenuItem
            {
                Header = item.Name,
                Icon = item.Icon,
                Command = item.Command,
                IsCheckable = item.IsCheckable,
                IsChecked = item.IsChecked,
            };

            foreach (var child in item.Children)
                menuItem.Items.Add(ToMenuItem(child));

            return menuItem;
        }
    }
}
