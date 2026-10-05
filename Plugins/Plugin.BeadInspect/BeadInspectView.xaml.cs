using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HalconDotNet;

namespace Plugin.BeadInspect
{
    /// <summary>
    /// 胶路检测的配置视图（方案说明书 §6）。
    ///
    /// 视图不写业务逻辑：点列/配方/学习/提取的都在 BeadInspectPlugin（DataContext）里。
    /// 本文件只做三件事：
    /// ① 按钮事件转发；
    /// ② Loaded 后经视觉树拿到共享控件 ImageEdit 内部的 HSmartWindowControlWPF，
    ///    直接订阅它的鼠标事件（HMouseEventArgsWPF 自带图像坐标 Row/Column，无需自算
    ///    SetPart 缩放换算）——这是**叠加**鼠标事件，不改共享控件源码；
    /// ③ Ctrl+Z 撤销键与点列表格的编辑提交转发。
    ///
    /// 与共享控件的相处（不改 Core.Halcon 的边界）：
    /// · 左键拖动留给「拖点」→ 实例级关闭 HMoveContent（涂抹模式同款处理）；
    /// · 右键 = 删最近点 → 实例级清掉 ImageEdit 在 RegisterMouseMethods 里注册的右键菜单；
    /// · 叠加层（路径/点/矫正四点）走共享控件的 Annotations 通道：整体换 List 触发 RenderAll，
    ///   不直接画 HWindow，避免与控件内部重绘互相覆盖。
    /// </summary>
    public partial class BeadInspectView : UserControl
    {
        private HSmartWindowControlWPF? _hSmart;
        private bool _dragging;

        public BeadInspectView()
        {
            InitializeComponent();
            Loaded += OnViewLoaded;
            PreviewKeyDown += OnViewPreviewKeyDown;
        }

        private BeadInspectPlugin? Plugin => DataContext as BeadInspectPlugin;

        private void OnViewLoaded(object sender, RoutedEventArgs e)
        {
            Plugin?.OnViewLoaded();
            GrabHSmart();
        }

        /// <summary>拿到模板里的 HSmartWindowControlWPF（模板已应用后视觉树里才有）</summary>
        private void GrabHSmart()
        {
            if (_hSmart != null)
                return;
            var smart = FindDescendant<HSmartWindowControlWPF>(this);
            if (smart == null)
                return;
            _hSmart = smart;

            // 左键拖动留给"拖点"：关掉控件自带的拖拽平移（同 HalconBase 涂抹模式的处理）。
            // 缩放走滚轮，"适应图片"走按钮 —— 精确拾取优先
            _hSmart.HMoveContent = false;
            _hSmart.HMouseDown += OnCanvasMouseDown;
            _hSmart.HMouseMove += OnCanvasMouseMove;
            _hSmart.HMouseUp += OnCanvasMouseUp;

            // 右键 = 删最近点：屏蔽 ImageEdit 共享控件注册的 ROI 右键菜单（实例级，不改共享控件）
            Canvas.ContextMenu = null;
        }

        private static T? FindDescendant<T>(DependencyObject root)
            where T : class
        {
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T hit)
                    return hit;
                var deep = FindDescendant<T>(child);
                if (deep != null)
                    return deep;
            }
            return null;
        }

        // ── 画布鼠标：左键加点/拖点，右键删点，四点模式接管左键 ──

        private void OnCanvasMouseDown(object sender, HSmartWindowControlWPF.HMouseEventArgsWPF e)
        {
            var p = Plugin;
            if (p == null || !double.IsFinite(e.Row) || !double.IsFinite(e.Column))
                return;

            if (e.Button == MouseButton.Right)
            {
                if (p.QuadPicking)
                    return;
                p.DeleteNearestPoint(e.Row, e.Column);
                return;
            }
            if (e.Button != MouseButton.Left)
                return;

            if (p.QuadPicking)
            {
                p.QuadPickClick(e.Row, e.Column);
                return;
            }
            if (p.BeginPointDrag(e.Row, e.Column))
            {
                _dragging = true; // 命中已有点：进入拖动，不再当"加点"处理
                return;
            }
            p.AddPoint(e.Row, e.Column);
        }

        private void OnCanvasMouseMove(object sender, HSmartWindowControlWPF.HMouseEventArgsWPF e)
        {
            if (!_dragging)
                return;
            Plugin?.MovePointDrag(e.Row, e.Column);
        }

        private void OnCanvasMouseUp(object sender, HSmartWindowControlWPF.HMouseEventArgsWPF e)
        {
            if (!_dragging)
                return;
            _dragging = false;
            Plugin?.EndPointDrag();
        }

        private void OnViewPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
            {
                Plugin?.UndoLastPick();
                e.Handled = true;
            }
        }

        // ── 按钮转发（业务都在插件） ──

        private void OnAddEntryClick(object sender, RoutedEventArgs e) => Plugin?.AddRecipeEntry();

        private void OnDeleteEntryClick(object sender, RoutedEventArgs e) => Plugin?.DeleteSelectedEntry();

        private void OnSetDefaultClick(object sender, RoutedEventArgs e) => Plugin?.SetRecipeAsDefault();

        private void OnLoadRefClick(object sender, RoutedEventArgs e) => Plugin?.LoadRefImage();

        private void OnLearnClick(object sender, RoutedEventArgs e) => Plugin?.LearnRecipe();

        private void OnAutoExtractClick(object sender, RoutedEventArgs e) => Plugin?.AutoExtractCenterline();

        private void OnUndoClick(object sender, RoutedEventArgs e) => Plugin?.UndoLastPick();

        private void OnClearPointsClick(object sender, RoutedEventArgs e) => Plugin?.ClearPickedPoints();

        private void OnToggleQuadPickClick(object sender, RoutedEventArgs e) => Plugin?.ToggleQuadPick();

        private void OnClearQuadClick(object sender, RoutedEventArgs e) => Plugin?.ClearRectifyQuad();

        private void OnApplyRectClick(object sender, RoutedEventArgs e) => Plugin?.ApplyExplicitRectifyTarget();

        private void OnFitClick(object sender, RoutedEventArgs e) => Canvas.FitToImage();

        // ── 点列表格：编辑提交 / 行删除 ──

        private void OnPointCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            // 等 DataGrid 把编辑值提交给行对象后再整体回写点列（Binding 提交发生在 CellEditEnding 内）
            Dispatcher.BeginInvoke(() => Plugin?.CommitTableToCache());
        }

        private void OnDeletePointRowClick(object sender, RoutedEventArgs e)
        {
            var p = Plugin;
            if (p == null || (sender as FrameworkElement)?.DataContext is not BeadPointRow row)
                return;
            int index = p.PointRows.IndexOf(row);
            if (index >= 0)
                p.DeletePointAt(index);
        }
    }
}
