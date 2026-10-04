using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Core.Events;
using Core.Halcon.Controls;
using Core.Halcon.Helpers;
using VisionMaster.EventModel;
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

            // 收到"切换画布布局"：重新摆一次格子（老方案存过的布局值据此恢复）
            GlobalEventBus.Subscribe<ImageCanvasChangeEvent>(e => ShowCanvas(e.ViewMode));
        }

        /// <summary>当前形态（供方案配置保存时读取，见 <c>SolutionConfigApplier.Capture</c>）</summary>
        public static eViewMode CurrentMode { get; private set; } = eViewMode.One;

        /// <summary>
        /// 各格画布，键 = 归属窗口号（1~9）。
        /// 懒建 + 复用：切布局只是把它们重新摆进 Grid，格子里已显示的图与列表都还在
        /// （重建会丢选中项还要重新拉图，切一下布局就白一下，很难看）。
        /// </summary>
        private readonly Dictionary<int, ImageGallery> _canvases = new();

        /// <summary>按形态重新摆格子</summary>
        private void ShowCanvas(eViewMode mode)
        {
            // 越界的形态值（方案文件被改坏、或存过已下线的编号）回落到单画面：
            // 摆一个不存在的铺位会让画布整块空白，比"回到单画面"糟得多
            if (!IsKnown(mode)) mode = eViewMode.One;

            var spec = SpecOf(mode);
            CurrentMode = mode;

            grid.Children.Clear();
            grid.RowDefinitions.Clear();
            grid.ColumnDefinitions.Clear();
            grid.Background = new SolidColorBrush(Colors.White);

            for (int r = 0; r < spec.Rows; r++) grid.RowDefinitions.Add(new RowDefinition());
            for (int c = 0; c < spec.Cols; c++) grid.ColumnDefinitions.Add(new ColumnDefinition());

            for (int i = 0; i < spec.Panes.Length; i++)
            {
                var slot = spec.Panes[i];

                // 第 i+1 格 ↔ 显示窗口号 i+1
                var canvas = GetCanvas(i + 1);

                grid.Children.Add(canvas);
                Grid.SetRow(canvas, slot.Row);
                Grid.SetColumn(canvas, slot.Col);
                Grid.SetRowSpan(canvas, slot.RowSpan);
                Grid.SetColumnSpan(canvas, slot.ColSpan);
            }
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
            var canvas = new ImageGallery
            {
                Margin = new Thickness(5),
                ViewIndexFilter = viewIndex,
                HintText = BuildHint(viewIndex),
                // 数据源是采集服务的只读集合：控件只负责显示与筛选，不持有/不释放图像
                ItemsSource = ImageCollectionService.Instance?.Frames
            };

            canvas.ClearRequested += (_, __) => ImageCollectionService.Instance?.Clear();
            canvas.ExportRequested += OnCanvasExportRequested;
            return canvas;
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
                 + "或在「系统参数设置 → 画布」里打开「兜底采集所有输出端口」。";
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
