using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Threading;
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

        /// <summary>上下文工具条与选中框之间的间距（像素）</summary>
        private const double ToolbarGap = 8;

        /// <summary>上下文工具条离视口边缘的最小留白</summary>
        private const double ToolbarMargin = 4;

        /// <summary>已经"适应窗口"过一次的画面。同一页再次入树不再重新取景，免得切标签回来视角被重置</summary>
        private ScadaPage? _fittedPage;

        private DependencyPropertyDescriptor? _selectionWatch;
        private DependencyPropertyDescriptor? _zoomWatch;
        private DependencyPropertyDescriptor? _offsetWatch;

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

            AttachImageAssetResolver();
            AttachStateListResolver();
        }

        private void OnViewLoaded(object sender, RoutedEventArgs e)
        {
            (DataContext as ScadaEditorViewModel)?.Activate();
            AttachImageAssetResolver();
            AttachStateListResolver();
            AttachCanvasWatchers();
            FitCanvasIfNeeded();
        }

        /// <summary>
        /// 把“方案图片解析器”接到画布上（Hmi.Picture / Hmi.Gif 用）。
        ///
        /// XAML 里虽然也绑了 <c>ImageAssetResolver="{Binding ResolveImageAsset}"</c>，
        /// 但这里再显式赋一次：委托型 DP 的绑定在部分宿主/初始化顺序下会拿到 null，
        /// 表现是“属性面板有缩略图、画布上的图元却一直显示未设置图片”。
        /// 与运行窗口 <c>ScadaRuntimeWindow</c> 的设置方式保持一致。
        /// </summary>
        private void AttachImageAssetResolver()
        {
            if (Canvas is null)
                return;

            var viewModel = DataContext as ScadaEditorViewModel;
            Canvas.ImageAssetResolver = viewModel?.ResolveImageAsset;
            Canvas.ImageBytesResolver = viewModel?.ResolveImageBytes;
        }

        /// <summary>方案级文本列表解析器（Hmi.ComboBox 用），与图片解析器同一注入方式</summary>
        private void AttachStateListResolver()
        {
            if (Canvas is null)
                return;

            var viewModel = DataContext as ScadaEditorViewModel;
            Canvas.StateListResolver = viewModel?.ResolveStateList;
            Canvas.PageResolver = viewModel?.ResolvePage;
            Canvas.PageNameResolver = viewModel?.ResolvePageByName;

            // 设计期文本解析器：图元文本里的 @键 按方案里的当前语言显示（@键 约定见 ScadaTextCatalog）。
            // 与上面几个同一条注入方式——控件不反向抓方案文档；运行态那一路走 ScadaRuntimeContext.Languages。
            // 语言表改完后视图模型会 RaisePropertyChanged(nameof(ResolveText))，XAML 那条绑定
            // 把新委托推进画布的 TextResolver DP，DP 回调再逐个转发给控件 → 控件 Refresh() 重铺文本。
            Canvas.TextResolver = viewModel?.ResolveText;
        }

        private void OnViewUnloaded(object sender, RoutedEventArgs e)
        {
            DetachCanvasWatchers();
            (DataContext as ScadaEditorViewModel)?.Deactivate();
        }

        // ---- 取景 ----
        // 缩放/平移是"这台机器这一次的视角"，既不落盘也不该被别的对象查询，
        // 所以这几个动作刻意写在代码后台而不是视图模型的命令里（做成命令就得让 VM 反过来持有画布实例）。

        private void OnZoomIn(object sender, RoutedEventArgs e) => Canvas.ZoomBy(ZoomStep);

        private void OnZoomOut(object sender, RoutedEventArgs e) => Canvas.ZoomBy(1 / ZoomStep);

        private void OnFitToScreen(object sender, RoutedEventArgs e) => Canvas.FitToScreen();

        private void OnActualSize(object sender, RoutedEventArgs e) => Canvas.ZoomToActualSize();

    /// <summary>
    /// 「计划表」：方案级的时间/事件计划管理器。
    /// 入口刻意留在组态工具栏——它配的是"这个方案怎么自动跑"，与主界面的工程/插件不是一层。
    /// </summary>
    private void OnSchedules(object sender, RoutedEventArgs e)
        => (DataContext as ScadaEditorViewModel)?.OpenSchedules();

        // ---- 顶栏折行 ----
        // 顶栏的排布意图只有一句话："宽屏一行、窄屏折行"。WPF 没有"先按自然宽度排、排不下再整组换行"
        // 的现成面板，所以折行位置由这里现算：
        //   · ToolbarSpacer 的宽度 = 可用宽度 − 两组自然宽度（撑得开时右组贴右边，与设计稿一致）
        //   · 撑不开（算出来是 0）= 本行放不下，WrapPanel 自己把右组折到第二行
        //   · 两组各带 MaxWidth = 可用宽度：连一组都放不下时，组内按钮再各自折行，任何宽度都不裁切
        // 刻意不用 Grid 的 * 列做这件事：* 列只会把两组挤在同一行里互相压，永远不会折行。

        /// <summary>折行后左右两组之间的竖直间距（像素）</summary>
        private const double ToolbarRowGap = 8;

        private void OnToolbarSizeChanged(object sender, SizeChangedEventArgs e) => UpdateToolbarLayout();

        /// <summary>
        /// 按顶栏当前可用宽度重算"撑开宽度 / 组内折行上限 / 折行后的行间距"。
        ///
        /// 三处赋值都会再触发一轮布局，所以每个值都带"变了才写"的守卫——
        /// 无条件写就是"改尺寸 → 布局 → 再改尺寸"的死循环，而它跑在 SizeChanged 里，一旦循环就是卡死。
        /// </summary>
        private void UpdateToolbarLayout()
        {
            if (ToolbarPanel == null || ToolbarSpacer == null
                || ToolbarLeftGroup == null || ToolbarRightGroup == null)
            {
                return;
            }

            double available = ToolbarPanel.ActualWidth;
            if (available <= 0)
                return;   // 首次测量还没有可用宽度，等 SizeChanged 再来

            if (Math.Abs(ToolbarLeftGroup.MaxWidth - available) > 0.5)
                ToolbarLeftGroup.MaxWidth = available;
            if (Math.Abs(ToolbarRightGroup.MaxWidth - available) > 0.5)
                ToolbarRightGroup.MaxWidth = available;

            // 两组在 WrapPanel 里都是"按无限宽测量"的，DesiredSize 就是各自的自然宽度。
            // 被上面的 MaxWidth 夹过也不会算错：夹过说明这一组自己就超宽，下面必然判成折行。
            double needed = ToolbarLeftGroup.DesiredSize.Width + ToolbarRightGroup.DesiredSize.Width;
            bool wrapped = available - needed < 0.5;

            // 折行时给右组补一条上间距：两行都是 28px 高的按钮，不补就是两排按钮的边框贴在一起
            Thickness gap = wrapped ? new Thickness(0, ToolbarRowGap, 0, 0) : default;
            if (ToolbarRightGroup.Margin != gap)
                ToolbarRightGroup.Margin = gap;

            double spacer = wrapped ? 0 : available - needed;
            if (Math.Abs(ToolbarSpacer.Width - spacer) > 0.5)
                ToolbarSpacer.Width = spacer;
        }

        /// <summary>
        /// 首次进入某一页时"适应窗口"，之后不再自动改视角。
        ///
        /// 为什么要自动取景：画面默认 1920×1080 而文档区通常只有 1000 上下，
        /// 100% 打开等于"只看得见左上角一块"，用户既看不出画面边界，也看不出整页布局。
        /// 只做一次（按页记）是给"切标签回来"留余地——那时用户往往已经调过视角。
        /// </summary>
        private void FitCanvasIfNeeded()
        {
            if (DataContext is not ScadaEditorViewModel viewModel)
                return;

            var page = viewModel.SelectedPage;
            if (page == null || ReferenceEquals(page, _fittedPage))
                return;

            _fittedPage = page;

            // 面板刚切出来时 ActualWidth 还是 0，FitToScreen 会自己跳过；排到布局之后再试一次。
            Dispatcher.BeginInvoke(new Action(() =>
            {
                Canvas.FitToScreen();
                UpdateSelectionToolbar();
            }), DispatcherPriority.Loaded);
        }

        // ---- 浮出的上下文格式工具条 ----

        private void AttachCanvasWatchers()
        {
            if (_selectionWatch != null)
                return;

            _selectionWatch = DependencyPropertyDescriptor.FromProperty(
                ScadaCanvas.SelectedElementsProperty, typeof(ScadaCanvas));
            _selectionWatch?.AddValueChanged(Canvas, OnCanvasSelectionChanged);

            _zoomWatch = DependencyPropertyDescriptor.FromProperty(
                ScadaCanvas.ZoomProperty, typeof(ScadaCanvas));
            _zoomWatch?.AddValueChanged(Canvas, OnCanvasViewportChanged);

            _offsetWatch = DependencyPropertyDescriptor.FromProperty(
                ScadaCanvas.OffsetProperty, typeof(ScadaCanvas));
            _offsetWatch?.AddValueChanged(Canvas, OnCanvasViewportChanged);

            Canvas.SizeChanged += OnCanvasSizeChanged;
            UpdateSelectionToolbar();
        }

        private void DetachCanvasWatchers()
        {
            _selectionWatch?.RemoveValueChanged(Canvas, OnCanvasSelectionChanged);
            _zoomWatch?.RemoveValueChanged(Canvas, OnCanvasViewportChanged);
            _offsetWatch?.RemoveValueChanged(Canvas, OnCanvasViewportChanged);
            _selectionWatch = null;
            _zoomWatch = null;
            _offsetWatch = null;

            Canvas.SizeChanged -= OnCanvasSizeChanged;
        }

        private void OnCanvasSelectionChanged(object? sender, EventArgs e) => UpdateSelectionToolbar();

        private void OnCanvasViewportChanged(object? sender, EventArgs e) => UpdateSelectionToolbar();

        private void OnCanvasSizeChanged(object sender, SizeChangedEventArgs e) => UpdateSelectionToolbar();

        /// <summary>
        /// 把上下文工具条摆到"选中集合外接框"的正上方；上方放不下就翻到下方，最后夹进视口。
        ///
        /// 位置必须现算：工具条是浮层，而选中框在画面坐标系里跟着缩放平移走——
        /// 写死一个位置的话，一缩放它就飘到别处去了。
        /// 外接框按<b>轴对齐</b>算（不按旋转后的包围盒）：工具条只需要一个大致锚点，
        /// 为它再算一遍旋转包围盒不值得。
        /// </summary>
        private void UpdateSelectionToolbar()
        {
            if (SelectionToolbar == null || Canvas == null)
                return;

            var selected = Canvas.SelectedElements;

            if (selected == null || selected.Count == 0 || Canvas.ActualWidth <= 0 || Canvas.ActualHeight <= 0)
            {
                SelectionToolbar.Visibility = Visibility.Collapsed;
                return;
            }

            double left = double.MaxValue, top = double.MaxValue;
            double right = double.MinValue, bottom = double.MinValue;

            foreach (var element in selected)
            {
                left = Math.Min(left, element.X);
                top = Math.Min(top, element.Y);
                right = Math.Max(right, element.X + Math.Max(1, element.Width));
                bottom = Math.Max(bottom, element.Y + Math.Max(1, element.Height));
            }

            SelectionToolbar.Visibility = Visibility.Visible;
            SelectionToolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            var size = SelectionToolbar.DesiredSize;
            if (size.Width <= 0)
                size = new Size(SelectionToolbar.ActualWidth, SelectionToolbar.ActualHeight);

            var anchor = Canvas.ToViewportPoint(new Point(left, top));

            double x = anchor.X;
            double y = anchor.Y - size.Height - ToolbarGap;

            if (y < ToolbarMargin)
            {
                // 上方放不下（选中框贴着视口顶边）就翻到下面，宁可压住一点内容也不许飘出视口
                y = Canvas.ToViewportPoint(new Point(left, bottom)).Y + ToolbarGap;
            }

            double maxX = Math.Max(ToolbarMargin, Canvas.ActualWidth - size.Width - ToolbarMargin);
            double maxY = Math.Max(ToolbarMargin, Canvas.ActualHeight - size.Height - ToolbarMargin);

            x = Math.Clamp(x, ToolbarMargin, maxX);
            y = Math.Clamp(y, ToolbarMargin, maxY);

            System.Windows.Controls.Canvas.SetLeft(SelectionToolbar, x);
            System.Windows.Controls.Canvas.SetTop(SelectionToolbar, y);
        }

        // ---- 顶栏下拉菜单 ----
        // WPF 没有内建的"分裂按钮"，而"点一下把挂在自己身上的菜单弹出来"是纯视图动作
        // （菜单位置由控件决定），放进视图模型只会多一层转发。菜单里的项仍是命令绑定，业务判断一条都不在这里。

        private void OnDropDownMenuClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { ContextMenu: { } menu } button)
                return;

            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        /// <summary>
        /// 上下文工具条的"更多"：把完整的排列菜单（分布 / 等尺寸 / 等间距 / 对齐网格 / 叠放 / 图层）
        /// 弹在按钮下面。
        ///
        /// 复用 <see cref="ScadaEditorViewModel.BuildElementContextMenu"/> 与 <see cref="ToMenuItem"/>：
        /// 右键菜单与这里必须是<b>同一份</b>命令集，各写一份就会出现"右键有、工具条没有"的错位。
        /// </summary>
        private void OnMoreFormatClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || DataContext is not ScadaEditorViewModel editor)
                return;

            var items = editor.BuildElementContextMenu();
            if (items.Count == 0)
                return;

            var menu = new ContextMenu
            {
                FontFamily = (System.Windows.Media.FontFamily)FindResource("DialogIconFont"),
                PlacementTarget = button,
                Placement = PlacementMode.Bottom,
            };

            foreach (var item in items)
                menu.Items.Add(ToMenuItem(item));

            menu.IsOpen = true;
        }

        // ---- 从工具箱放置图元 ----
        // 落点换算交给画布（ToDropOrigin：视口→设计、以落点为中心、吸网格、夹进画面），
        // 造模型交给视图模型（AddElement：去重命名、抬 ZIndex、Add 进集合后由画布自己长控件）。
        // 这一层只做"把鼠标位置和数据递过去"，两段知识都不属于它。

        private void OnCanvasDragOver(object sender, System.Windows.DragEventArgs e)
        {
            // Effects 决定光标长相：不是本协议的负载就显"禁止"，别让用户对一个放不下的东西
            // 反复试。这里不兜底当纯文本读——否则从记事本拖几个字进画布也会生图元。
            e.Effects = CanAccept(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void OnCanvasDrop(object sender, System.Windows.DragEventArgs e)
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
                // 拿外接框的宽高去换落点（ToDropOrigin 是"以落点为心"）。
                // 参数化模板的展开/校验由视图模型统一处理（与批量插入同一套），这里只递落点。
                ScadaClipboard.GetBounds(payload, out _, out _, out double width, out double height);

                Point origin = Canvas.ToDropOrigin(e.GetPosition(Canvas), width, height);

                if (viewModel.InsertTemplateAt(templateId, origin.X, origin.Y))
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
