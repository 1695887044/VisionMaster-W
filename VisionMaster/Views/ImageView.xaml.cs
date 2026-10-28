using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Core.Events;
using Core.Halcon.Controls;
using Core.Halcon.Helpers;
using VisionMaster.EventModel;
using VisionMaster.Models;
using VisionMaster.Services;

namespace VisionMaster.Views
{
    /// <summary>
    /// 画布（视觉图像页）。
    ///
    /// 每一格都是**一张完整的画布**：上行一张当前图，下行一条横向滚动的缩略图条；
    /// 选中条上任意一张，上行立即切换成它（悬停看标题 / 插件注入信息 / 来源）。
    ///
    /// 格号 ↔ 插件的"显示窗口号"
    /// ---------
    /// 第 N 格只收 <c>ViewIndex == N</c> 的图，每格各自维护自己的列表、各自可清空/导出 ——
    /// 于是"1×2"不是"两格抢同一路图"，而是两个互不干扰的画布。
    ///
    /// 数据来自 <see cref="ImageCollectionService"/>：插件用 <c>PublishPreview</c> 注入的图逐条进列表，
    /// 每轮流程开始时按设置"清空整张画布"或"只覆盖本流程上一轮的图"。
    /// 本视图只负责"摆格子"和把格子接到采集服务上，不持有也不释放任何图像。
    /// </summary>
    public partial class ImageView : UserControl
    {
        public ImageView()
        {
            InitializeComponent();

            ShowCanvas(CurrentMode);

            // 构造期就订阅：断言宿主与"还没进可视树"的实例也要能收到布局切换（多格布局的
            // 离线断言就是这么驱动的）。真正会泄漏的是"加载过又被丢弃"的实例（布局重建、
            // 方案切换）——它们一定会走 Unloaded，在那里摘掉即可（见 OnViewUnloaded）。
            GlobalEventBus.Subscribe<ImageCanvasChangeEvent>(OnCanvasChangeRequested);

            Loaded += OnViewLoaded;
            Unloaded += OnViewUnloaded;
        }

        /// <summary>
        /// 重新挂接全局事件并按"当前形态"重摆一次。
        ///
        /// 订阅在构造期就挂（未进树的实例/离线断言也要能收到布局切换），但 **Unloaded 必须摘**：
        /// 本视图不止一份（Shell 一份、运行驾驶舱一份、布局重建时 LayoutHelper 还会再 new），
        /// 加载过又被丢弃的实例若不摘，静态事件表会把整棵可视树钉住——切几次布局就多几份
        /// 幽灵格子（每帧都跑一次 Recompute）。
        /// 顺带 ShowCanvas(CurrentMode)：卸载期间可能换过布局（静态值是全局唯一来源）。
        /// </summary>
        private void OnViewLoaded(object sender, RoutedEventArgs e)
        {
            // 先摘再挂：Loaded 可能重复触发（重新加入可视树），重复订阅会重复摆格子
            GlobalEventBus.Unsubscribe<ImageCanvasChangeEvent>(OnCanvasChangeRequested);
            GlobalEventBus.Subscribe<ImageCanvasChangeEvent>(OnCanvasChangeRequested);

            ShowCanvas(CurrentMode);

            var collection = ImageCollectionService.Instance;
            if (collection != null) collection.Changed += OnCollectionChanged;

            ApplyStripVisibility();
            RefreshHints();
        }

        private void OnViewUnloaded(object sender, RoutedEventArgs e)
        {
            GlobalEventBus.Unsubscribe<ImageCanvasChangeEvent>(OnCanvasChangeRequested);

            var collection = ImageCollectionService.Instance;
            if (collection != null) collection.Changed -= OnCollectionChanged;
        }

        /// <summary>"切换画布布局"：重新摆一次格子（老方案存过的布局值据此恢复）</summary>
        private void OnCanvasChangeRequested(ImageCanvasChangeEvent e)
        {
            // 换布局 = 退出放大：用户刚选了新形态，不该再被某一格的放大态盖住
            _maximizedViewIndex = null;
            ShowCanvas(e.ViewMode);
        }

        private void OnCollectionChanged(object? sender, EventArgs e) => ApplyStripVisibility();

        /// <summary>
        /// 每格缩略图条该不该显示。
        ///
        /// 判据是"这格可能不止一张图吗"：
        /// · 收容关（默认）—— 每个（流程，窗口号）只留最新一张，条子永远只有一张，
        ///   纯粹占高度（3×3 下能吃掉四成），直接不显示；
        /// · 收容开 —— 有历史可回看，按用户在设置里（或右键菜单里）的选择显示；
        /// · 采集总闸关 —— 本来就没有图，条子只是空占位。
        /// </summary>
        private static bool ShouldShowStrip(ImageGallerySettings? cfg)
            => SupportsFrameHistory(cfg) && cfg.ShowThumbnailStrip;

        /// <summary>
        /// 这一格有没有"多张图"的可能。为 false 时锁定跟随/对比模式/缩略图条一律置灰
        /// （那时它们点了也没有可观察的效果，留着可点等于让人以为软件没反应）。
        /// 与 <see cref="ShouldShowStrip"/> 分开：条子还额外受用户自己的显示开关控制。
        /// </summary>
        private static bool SupportsFrameHistory(ImageGallerySettings? cfg)
            => cfg != null && cfg.Enabled && cfg.KeepInjectedHistory;

        /// <summary>
        /// 把配置推给每一格（已摆出去的，含当前没显示的那些）。
        /// 条子显隐与"能不能用锁定/对比"是两件事，一起在这里刷。
        /// </summary>
        private void ApplyStripVisibility()
        {
            var cfg = ImageCollectionService.Instance?.Settings;
            var show = ShouldShowStrip(cfg);
            var history = SupportsFrameHistory(cfg);

            foreach (var canvas in _canvases.Values)
            {
                canvas.SupportsFrameHistory = history;
                canvas.ShowThumbnailStrip = show;
            }
        }

        /// <summary>采集配置可能改了"为什么没图"：提示文案要跟着重算（否则还停在旧原因上）</summary>
        private void RefreshHints()
        {
            foreach (var pair in _canvases)
                pair.Value.HintText = BuildHint(pair.Key);
        }

        /// <summary>当前形态（供方案配置保存时读取，见 <c>SolutionConfigApplier.Capture</c>）</summary>
        public static eViewMode CurrentMode { get; private set; } = eViewMode.One;

        /// <summary>
        /// 只把形态记到静态值（不摆格子）。
        ///
        /// 为什么需要：全局事件订阅已改成"随 Loaded/Unloaded 挂摘"（否则每建一份视图就永久留一个
        /// 订阅）。于是"当前可能一份 ImageView 都不在树上"——此时发布事件没人收，
        /// 方案里存的布局会在下次显示时丢掉。恢复方案/菜单切换的入口先调本方法落值，
        /// 事件只负责通知已在树上的实例重摆。
        /// </summary>
        internal static void ApplyModeForRestore(eViewMode mode)
            => CurrentMode = IsKnown(mode) ? mode : eViewMode.One;

        /// <summary>
        /// 各格画布，键 = 归属窗口号（1~9）。
        /// 懒建 + 复用：切布局只是把它们重新摆进 Grid，格子里已显示的图与列表都还在
        /// （重建会丢选中项还要重新拉图，切一下布局就白一下，很难看）。
        /// </summary>
        private readonly Dictionary<int, ImageGallery> _canvases = new();

        /// <summary>
        /// 本实例当前摆着的布局（放大/还原只按它重摆）。
        /// 不读静态 <see cref="CurrentMode"/>：Shell 与运行驾驶舱各有一份 ImageView，
        /// 静态值是谁最后摆谁说了算，另一份照它重摆会把格子摆错。
        /// </summary>
        private LayoutSpec _layout = SpecOf(eViewMode.One);

        /// <summary>按形态重新摆格子</summary>
        private void ShowCanvas(eViewMode mode)
        {
            // 越界的形态值（方案文件被改坏、或存过已下线的编号）回落到单画面：
            // 摆一个不存在的铺位会让画布整块空白，比"回到单画面"糟得多
            if (!IsKnown(mode)) mode = eViewMode.One;

            var spec = SpecOf(mode);
            _layout = spec;
            CurrentMode = mode;

            grid.Children.Clear();
            grid.RowDefinitions.Clear();
            grid.ColumnDefinitions.Clear();
            grid.Background = new SolidColorBrush(Colors.White);

            for (int r = 0; r < spec.Rows; r++) grid.RowDefinitions.Add(new RowDefinition());
            for (int c = 0; c < spec.Cols; c++) grid.ColumnDefinitions.Add(new ColumnDefinition());

            for (int i = 0; i < spec.Panes.Length; i++)
            {
                // 第 i+1 格 ↔ 显示窗口号 i+1
                grid.Children.Add(GetCanvas(i + 1));
            }

            // 统一摆位（含"某格被双击放大"的覆盖）
            ApplyPaneLayout(spec);
        }

        /// <summary>每格与画布边框之间的留白（放大时归零，让图真正占满）</summary>
        private static readonly Thickness PaneMargin = new(5);

        /// <summary>
        /// 当前被双击放大的格（显示窗口号；null = 正常平铺）。
        ///
        /// 放大只改 Grid 的摆位与其余格的可见性，**不重建、不摘树**：格子里的图、列表、
        /// 选中项都原样保留，还原时也不会有"白一下再刷回来"的观感。
        /// 其余格用 Collapsed 而不是"垫在下面"——HALCON 画布是 HwndHost（独立 HWND），
        /// WPF 的 ZIndex 压不住它，叠着放会露出邻居窗口。
        /// </summary>
        private int? _maximizedViewIndex;

        /// <summary>按布局表摆好每一格，并套用当前放大态</summary>
        private void ApplyPaneLayout(LayoutSpec spec)
        {
            var panes = grid.Children.OfType<ImageGallery>().ToList();
            bool multi = panes.Count > 1;

            // 单格布局没有"让位"可谈；放大的那一格不在当前布局里（换过形态）同样作废
            if (!multi) _maximizedViewIndex = null;
            if (_maximizedViewIndex is int current && !panes.Any(p => p.ViewIndexFilter == current))
                _maximizedViewIndex = null;

            for (int i = 0; i < panes.Count; i++)
            {
                var pane = panes[i];
                var slot = spec.Panes[i];
                bool maximized = multi && _maximizedViewIndex == pane.ViewIndexFilter;

                pane.IsPaneMaximizable = multi;
                pane.IsPaneMaximized = maximized;
                pane.Margin = maximized ? new Thickness(0) : PaneMargin;
                pane.Visibility = multi && _maximizedViewIndex != null && !maximized
                    ? Visibility.Collapsed
                    : Visibility.Visible;

                if (maximized)
                {
                    Grid.SetRow(pane, 0);
                    Grid.SetColumn(pane, 0);
                    Grid.SetRowSpan(pane, spec.Rows);
                    Grid.SetColumnSpan(pane, spec.Cols);
                }
                else
                {
                    Grid.SetRow(pane, slot.Row);
                    Grid.SetColumn(pane, slot.Col);
                    Grid.SetRowSpan(pane, slot.RowSpan);
                    Grid.SetColumnSpan(pane, slot.ColSpan);
                }
            }
        }

        /// <summary>
        /// 某一格双击了（或点了右键菜单"最大化本格"）：放大它 / 还原平铺。
        /// 只重摆位，不重建格子——见 <see cref="_maximizedViewIndex"/> 的注释。
        /// </summary>
        private void OnCanvasMaximizeToggleRequested(object? sender, EventArgs e)
        {
            if (sender is not ImageGallery source) return;

            var index = source.ViewIndexFilter;
            _maximizedViewIndex = _maximizedViewIndex == index ? null : index;

            ApplyPaneLayout(_layout);
        }

        private static bool IsKnown(eViewMode mode) => mode >= eViewMode.One && mode <= eViewMode.Night;

        private ImageGallery GetCanvas(int viewIndex)
        {
            if (_canvases.TryGetValue(viewIndex, out var ready)) return ready;

            var canvas = CreateCanvas(viewIndex);
            _canvases[viewIndex] = canvas;
            return canvas;
        }

        /// <summary>
        /// 建一格画布。<paramref name="viewIndex"/> 就是这一格的显示窗口号，它只收窗口号相同的图。
        /// 提示文案随采集开关变化，所以每次摆格子都重算（见 <see cref="BuildHint"/>）。
        /// </summary>
        private ImageGallery CreateCanvas(int viewIndex)
        {
            var cfg = ImageCollectionService.Instance?.Settings;
            var canvas = new ImageGallery
            {
                Margin = new Thickness(5),
                ViewIndexFilter = viewIndex,
                HintText = BuildHint(viewIndex),
                ShowThumbnailStrip = ShouldShowStrip(cfg),
                SupportsFrameHistory = SupportsFrameHistory(cfg),
                // 数据源是采集服务的只读集合：控件只负责显示与筛选，不持有/不释放图像
                ItemsSource = ImageCollectionService.Instance?.Frames
            };

            canvas.ClearRequested += (_, __) => ImageCollectionService.Instance?.Clear();
            canvas.ExportRequested += OnCanvasExportRequested;
            canvas.ThumbnailStripToggleRequested += OnThumbnailStripToggleRequested;
            canvas.MaximizeToggleRequested += OnCanvasMaximizeToggleRequested;
            return canvas;
        }

        /// <summary>
        /// 右键菜单里切了"显示缩略图条"：写回配置并让所有格一起跟随。
        ///
        /// 为什么由宿主落盘：控件在 Core.Halcon 里，拿不到 AppSettingsService（分层边界）。
        /// 为什么必须写盘：这个选择是"这台机器的用法"，重启后应当还是关着的——
        /// 只改当次界面等于每次开机都要再点一遍。
        /// 多格布局下点哪一格都是同一个全局开关，所以写完统一广播一次（见 ApplyStripVisibility）。
        /// </summary>
        private void OnThumbnailStripToggleRequested(object? sender, EventArgs e)
        {
            // 以触发者那一格的当前值为准（它自己已经在点击时翻好了）
            if (sender is not ImageGallery source) return;

            var show = source.ShowThumbnailStrip;

            // 先把其余各格同步过来：即使下面写盘失败，界面也是自洽的（不会一半显示一半折叠）
            foreach (var canvas in _canvases.Values)
                canvas.ShowThumbnailStrip = show;

            var settings = TryGetSettings();
            var gallery = settings?.Current.ImageGallery;
            if (gallery == null) return;

            var previous = gallery.ShowThumbnailStrip;
            if (previous == show) return;

            gallery.ShowThumbnailStrip = show;
            try
            {
                settings!.Save();
            }
            catch (Exception ex)
            {
                // 写盘失败（装 Program Files / 普通用户运行）不能让界面回滚到"点了没反应"，
                // 但要如实说明：本次有效、重启后回到旧值。
                gallery.ShowThumbnailStrip = previous;
                var restored = ShouldShowStrip(ImageCollectionService.Instance?.Settings);
                foreach (var canvas in _canvases.Values)
                    canvas.ShowThumbnailStrip = restored;

                System.Windows.MessageBox.Show(
                    Window.GetWindow(this),
                    $"缩略图条设置没能写入 AppConfig.json，重启后会回到原来的状态：{ex.Message}",
                    "画布", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// 取软件配置服务。<b>无 Prism 容器时返回 null</b>——断言宿主与设计器都会直接 new 本视图，
        /// 那时 ContainerLocator.Current 为空，硬解会抛（"设置了属性时引发异常"那类难查的崩）。
        /// </summary>
        private static AppSettingsService? TryGetSettings()
        {
            try
            {
                return Prism.Ioc.ContainerLocator.Container?.Resolve<AppSettingsService>();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 某一格的空提示：把"这格收什么、为什么空"说清楚。
        /// 三种原因（采集总开关关着 / 不收插件注入 / 没有插件往这个窗口号发图）的处置方式完全不同，
        /// 含糊一句"还没有图像"等于让人去猜。
        /// </summary>
        private static string BuildHint(int viewIndex)
        {
            var cfg = ImageCollectionService.Instance?.Settings;

            if (cfg == null)
                return "画布尚未就绪。";

            if (!cfg.Enabled)
                return "画布采集当前是关闭的。到「系统参数设置 → 画布」勾选「启用画布采集」，"
                     + Environment.NewLine + "流程跑出来的图才会收进来。";

            if (!cfg.IncludeRealtimePreviews)
                return "已关闭「收录插件注入的图像」。到「系统参数设置 → 画布」勾选它，"
                     + Environment.NewLine + "插件用 PublishPreview 注入的图才会出现在这里。";

            return $"本格收的是插件「显示窗口号 = {viewIndex}」的图。"
                 + Environment.NewLine
                 + $"没有图就把插件的显示窗口号设成 {viewIndex}；"
                 + Environment.NewLine
                 + "或在「系统参数设置 → 画布」里打开「兜底采集所有输出端口」。"
                 + Environment.NewLine
                 + (cfg.KeepInjectedHistory
                     ? "本格保留历史帧，下方缩略图条可回看前几张。"
                     : "本格只显示该窗口最新一张；要回看历史请打开「保留插件注入图的历史帧」。");
        }

        /// <summary>
        /// 画布导出：选目录 → 按流程分文件夹落盘 PNG → 回报实际成功张数。
        /// 目录选择交给宿主（控件不知道也不该弹系统对话框），落盘用 <see cref="ImageExporter"/> 统一实现。
        /// </summary>
        private void OnCanvasExportRequested(object? sender, GalleryExportEventArgs e)
        {
            var frames = e?.Frames;
            if (frames == null || frames.Count == 0) return;

            string folder;
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = $"选择导出目录（共 {frames.Count} 张，按流程分文件夹保存为 PNG）",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true
            })
            {
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                folder = dialog.SelectedPath;
            }

            int saved;
            try
            {
                saved = ImageExporter.SaveFrames(frames, folder, groupByFlow: true);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    Window.GetWindow(this), $"导出失败：{ex.Message}", "画布导出",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            System.Windows.MessageBox.Show(
                Window.GetWindow(this),
                $"已导出 {saved} / {frames.Count} 张到：\n{folder}",
                "画布导出", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        #region 布局表

        /// <summary>一格占哪几行几列（0 基；跨度为 1 = 不跨）</summary>
        private readonly record struct PaneSlot(int Row, int Col, int RowSpan, int ColSpan);

        /// <summary>一种布局：Cols×Rows 等分，Panes[i] 是第 i+1 格（↔ 显示窗口号 i+1）</summary>
        private readonly record struct LayoutSpec(int Cols, int Rows, PaneSlot[] Panes);

        /// <summary>
        /// 形态 → 布局。做成表驱动而不是九个 switch 分支：
        /// 加一种布局只要加一行数据，原来九个分支里九成是重复的 Grid.SetRow/SetColumn。
        /// </summary>
        private static LayoutSpec SpecOf(eViewMode mode) => mode switch
        {
            eViewMode.One => new(1, 1, new[] { new PaneSlot(0, 0, 1, 1) }),

            eViewMode.Two => new(2, 1, new[]
            {
                new PaneSlot(0, 0, 1, 1), new PaneSlot(0, 1, 1, 1)
            }),

            // 主从：左格占满两行，右侧上下各一格
            eViewMode.Three => new(2, 2, new[]
            {
                new PaneSlot(0, 0, 2, 1),
                new PaneSlot(0, 1, 1, 1), new PaneSlot(1, 1, 1, 1)
            }),

            eViewMode.Four => new(2, 2, Fill(2, 2)),

            // 多视口：上行一格横跨两列，其余四格铺满右上与第二行
            eViewMode.Five => new(3, 2, new[]
            {
                new PaneSlot(0, 0, 1, 2), new PaneSlot(0, 2, 1, 1),
                new PaneSlot(1, 0, 1, 1), new PaneSlot(1, 1, 1, 1), new PaneSlot(1, 2, 1, 1)
            }),

            eViewMode.Six => new(3, 2, Fill(3, 2)),

            // 矩阵 3+4：左侧一列大格占满两行，右侧 3×2 共六格
            eViewMode.Seven => new(4, 2, new[]
            {
                new PaneSlot(0, 0, 2, 1),
                new PaneSlot(0, 1, 1, 1), new PaneSlot(0, 2, 1, 1), new PaneSlot(0, 3, 1, 1),
                new PaneSlot(1, 1, 1, 1), new PaneSlot(1, 2, 1, 1), new PaneSlot(1, 3, 1, 1)
            }),

            eViewMode.Eight => new(4, 2, Fill(4, 2)),

            eViewMode.Night => new(3, 3, Fill(3, 3)),

            // 越界值（方案文件被改坏）：回落到单画面
            _ => new(1, 1, new[] { new PaneSlot(0, 0, 1, 1) })
        };

        /// <summary>等分铺满 cols×rows（按行优先，正好对应窗口号 1,2,3…）</summary>
        private static PaneSlot[] Fill(int cols, int rows)
        {
            var slots = new PaneSlot[cols * rows];
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    slots[(r * cols) + c] = new PaneSlot(r, c, 1, 1);
                }
            }
            return slots;
        }

        #endregion
    }
}
