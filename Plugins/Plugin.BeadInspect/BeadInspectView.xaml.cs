using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HalconDotNet;
using Microsoft.Win32;

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

        /// <summary>
        /// 「载入」= 载入当前配方的参考图（平面可变形对齐的模板 + 坐标系基准）。
        ///
        /// 2026-10-09 用户真机第一问「这个载入是不是没有效果」的修复：
        /// 本插件此前没有任何文件选择器（全插件 grep OpenFileDialog 零命中），用户必须在 200% 缩放的
        /// 窗口里手打完整图片路径才能用「载入」——空路径时 LoadRefImage 只写一行状态文字就 return，
        /// 屏幕上就是"点了没反应"（用户截图：路径框空、底部一行"还没有参考图路径…"）。
        /// 现在：路径为空或文件已失效 → 弹文件选择框（<see cref="RefImagePicker"/> /
        /// <see cref="PickRefImageFile"/>）→ 选定后写回 RefImagePath（INPC 自动刷新路径框）
        /// 再载入；路径有效 → 保持原行为（直接载入）。取消选择 = 什么都不动。
        /// 业务逻辑一行不动：真正的"读图/切底图/写状态"仍全在 BeadInspectPlugin.LoadRefImage 里。
        /// </summary>
        private void OnLoadRefClick(object sender, RoutedEventArgs e)
        {
            var plugin = Plugin;
            if (plugin?.EditingEntry is not { } entry)
                return; // 没有可编辑条目：和原来一样什么都不做（状态栏由插件侧提示）

            string? path = entry.RefImagePath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                var picked = (RefImagePicker ?? PickRefImageFile)(path);
                if (string.IsNullOrWhiteSpace(picked))
                    return; // 用户取消：路径框与画布都保持原样
                entry.RefImagePath = picked; // INPC → 路径框立刻回显（不用用户再手打一遍）
            }
            plugin.LoadRefImage();
        }

        /// <summary>
        /// 「载入」选文件的委托。默认 null = 走下面的 <see cref="PickRefImageFile"/>（真 OpenFileDialog）；
        /// 离屏探针 / 断言宿主里没有交互桌面（模态框弹不出来），把它换成一个返回固定路径的替身，
        /// 就能在无人值守下走通"空路径 → 选文件 → 写回 RefImagePath → LoadRefImage"整条链
        /// （见 _BeadViewProbe 的 P1 自检）。实例级而非 static：视图之间互不影响。
        /// </summary>
        public Func<string?, string?>? RefImagePicker { get; set; }

        /// <summary>
        /// 选参考图文件的弹框（「载入」在路径为空/文件不存在时调用）。
        /// Filter / Title 的写法照 <c>Plugin.ImageAcquisition.ImageAcquisitionPlugin.BrowseFile</c>；
        /// InitialDirectory 优先路径框现有目录，其次仓库自带的 <c>Image\bead</c>（存在才用）。
        /// </summary>
        private string? PickRefImageFile(string? currentPath)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "图像文件|*.bmp;*.jpg;*.jpeg;*.png;*.tif;*.tiff|所有文件|*.*",
                Title = "选择参考图（平面可变形对齐的模板）",
                CheckFileExists = true,
            };

            string? dir = string.IsNullOrWhiteSpace(currentPath) ? null : Path.GetDirectoryName(currentPath);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                dlg.InitialDirectory = dir;
            else
            {
                string guess = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Image", "bead");
                if (Directory.Exists(guess))
                    dlg.InitialDirectory = guess;
            }

            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

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
