using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using HalconDotNet;
using Microsoft.Win32;

namespace Plugin.BeadInspect
{
    /// <summary>
    /// 胶路检测的配置视图（Web 基线 v1）。
    ///
    /// 视图不写业务逻辑：点列/配方/学习/提取的都在 BeadInspectPlugin（DataContext）里。
    /// 本文件只做四件事：
    /// ① 按钮命令转发（UiCommand → 插件现有公开方法）；
    /// ② Loaded 后从主画布 ImageEdit 的视觉树拿到 HSmartWindowControlWPF，订阅鼠标事件；
    /// ③ Ctrl+Z 撤销键与点列表格的编辑提交转发；
    /// ④ 纯显示转换器（极性 dark/light 的中文显示）。
    ///
    /// 与共享控件的相处（不改 Core.Halcon 的边界）：
    /// · 左键拖动留给「拖点」→ 实例级关闭 HMoveContent；
    /// · 右键 = 删最近点 → 实例级清掉 ImageEdit 的右键菜单；
    /// · 叠加层走共享控件的 Annotations 通道，不直接画 HWindow。
    /// </summary>
    public partial class BeadInspectView : UserControl
    {
        private HSmartWindowControlWPF? _hSmart;
        private bool _dragging;

        public BeadInspectView()
        {
            InitializeComponent();

            LearnCommand = new UiCommand(() => Plugin?.LearnRecipe());
            AutoExtractCommand = new UiCommand(() => Plugin?.AutoExtractCenterline());
            UndoCommand = new UiCommand(() => Plugin?.UndoLastPick());
            ClearPointsCommand = new UiCommand(() => Plugin?.ClearPickedPoints());
            ToggleQuadPickCommand = new UiCommand(() => Plugin?.ToggleQuadPick());
            ClearQuadCommand = new UiCommand(() => Plugin?.ClearRectifyQuad());
            FitCommand = new UiCommand(() => Canvas.FitToImage());
            AddEntryCommand = new UiCommand(() => Plugin?.AddRecipeEntry());
            DeleteEntryCommand = new UiCommand(() => Plugin?.DeleteSelectedEntry());
            SetDefaultCommand = new UiCommand(() => Plugin?.SetRecipeAsDefault());
            LoadRefCommand = new UiCommand(LoadRef);
            ApplyRectCommand = new UiCommand(() => Plugin?.ApplyExplicitRectifyTarget());

            Loaded += OnViewLoaded;
            PreviewKeyDown += OnViewPreviewKeyDown;
        }

        private BeadInspectPlugin? Plugin => DataContext as BeadInspectPlugin;

        // 按钮命令：DataContext 是插件实例，所以命令挂在视图上、用 RelativeSource 绑定。
        // 只转发到插件现有的公开方法，不新增任何业务行为。
        public ICommand LearnCommand { get; }
        public ICommand AutoExtractCommand { get; }
        public ICommand UndoCommand { get; }
        public ICommand ClearPointsCommand { get; }
        public ICommand ToggleQuadPickCommand { get; }
        public ICommand ClearQuadCommand { get; }
        public ICommand FitCommand { get; }
        public ICommand AddEntryCommand { get; }
        public ICommand DeleteEntryCommand { get; }
        public ICommand SetDefaultCommand { get; }
        public ICommand LoadRefCommand { get; }
        public ICommand ApplyRectCommand { get; }

        private void OnViewLoaded(object sender, RoutedEventArgs e)
        {
            Plugin?.OnViewLoaded();
            GrabHSmart();
        }

        /// <summary>
        /// 拿到主画布 ImageEdit 模板里的 HSmartWindowControlWPF。
        /// 必须从 Canvas 子树找，不能从整个视图找：结果预览里的 ImageReadOnly 也是 HSmart 宿主，
        /// 从视图根找可能先命中预览，把点拾取事件挂到只读预览上。
        /// </summary>
        private void GrabHSmart()
        {
            if (_hSmart != null)
                return;
            var smart = FindDescendant<HSmartWindowControlWPF>(Canvas);
            if (smart == null)
                return;
            _hSmart = smart;

            // 左键拖动留给"拖点"：关掉控件自带的拖拽平移（同 HalconBase 涂抹模式的处理）。
            // 缩放走滚轮，"适应图片"走按钮 —— 精确拾取优先。
            _hSmart.HMoveContent = false;
            _hSmart.HMouseDown += OnCanvasMouseDown;
            _hSmart.HMouseMove += OnCanvasMouseMove;
            _hSmart.HMouseUp += OnCanvasMouseUp;

            // 右键 = 删最近点：屏蔽 ImageEdit 共享控件注册的 ROI 右键菜单（实例级，不改共享控件）。
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

        // ── 命令实现（业务都在插件） ──

        /// <summary>
        /// 「载入」= 载入当前配方的参考图（平面可变形对齐的模板 + 坐标系基准）。
        /// 路径为空或文件不存在时弹文件选择框，选定后写回 RefImagePath（INPC 刷新路径框）再载入；
        /// 路径有效时直接载入；取消选择 = 什么都不动。真正的读图/切底图/写状态仍在插件里。
        /// </summary>
        private void LoadRef()
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
                entry.RefImagePath = picked; // INPC → 路径框立刻回显
            }
            plugin.LoadRefImage();
        }

        /// <summary>
        /// 「载入」选文件的委托。默认 null = 走 <see cref="PickRefImageFile"/>（真 OpenFileDialog）；
        /// 离屏探针 / 断言宿主里没有交互桌面，把它换成一个返回固定路径的替身即可。
        /// </summary>
        public Func<string?, string?>? RefImagePicker { get; set; }

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

    /// <summary>
    /// 极性显示转换器：持久化值仍是 "dark" / "light"，只把下拉里的文字换成中文。
    /// 不改变 SelectedItem 的类型，也不改变配方条目的 Polarity 字符串。
    /// </summary>
    public sealed class PolarityNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string text)
            {
                if (string.Equals(text, "dark", StringComparison.OrdinalIgnoreCase))
                    return "暗胶（dark）";
                if (string.Equals(text, "light", StringComparison.OrdinalIgnoreCase))
                    return "亮胶（light）";
            }
            return value ?? string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            Binding.DoNothing;
    }

    /// <summary>视图内按钮用的最小 ICommand 实现：只执行、不做 CanExecute 门槛。</summary>
    internal sealed class UiCommand : ICommand
    {
        private readonly Action _execute;

        public UiCommand(Action execute) => _execute = execute;

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => _execute();

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }
}
