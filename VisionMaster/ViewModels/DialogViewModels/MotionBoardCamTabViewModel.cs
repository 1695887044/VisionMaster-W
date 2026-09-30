using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Core.Interfaces;
using Prism.Commands;
using Prism.Mvvm;
using VisionMaster.Services;

namespace VisionMaster.ViewModels.DialogViewModels
{
    /// <summary>
    /// 凸轮拐点表里的一行：包一层 INPC，编辑直接写回 <see cref="MotionCamPoint"/>。
    /// 改动通过 <see cref="PointsEdited"/> 通知页签刷新轮廓预览。
    /// </summary>
    public sealed class MotionCamPointRow : BindableBase
    {
        private readonly Action _edited;

        public MotionCamPointRow(MotionCamPoint point, int index, Action edited)
        {
            Point = point;
            Index = index;
            _edited = edited;
        }

        public MotionCamPoint Point { get; }

        /// <summary>行号（1 基，展示用）</summary>
        public int Index { get; private set; }

        public void Reindex(int index)
        {
            if (Index == index) return;
            Index = index;
            RaisePropertyChanged(nameof(Index));
        }

        public double M
        {
            get => Point.M;
            set
            {
                if (Point.M.Equals(value)) return;
                Point.M = value;
                RaisePropertyChanged();
                _edited();
            }
        }

        public double S
        {
            get => Point.S;
            set
            {
                if (Point.S.Equals(value)) return;
                Point.S = value;
                RaisePropertyChanged();
                _edited();
            }
        }

        /// <summary>到下一点的插补（直线/三次曲线）</summary>
        public string Interp
        {
            get => Point.Interp;
            set
            {
                if (Point.Interp == value) return;
                Point.Interp = MotionCamInterp.Normalize(value);
                RaisePropertyChanged();
                _edited();
            }
        }
    }

    /// <summary>
    /// 页签四「电子凸轮」（规格第 6 章）。
    ///
    /// 【概念】凸轮表是**全局实体**（方案的 CamTables 集合，不挂任何卡下）：
    /// 主轴/从轴从所有卡的全部启用轴里任意组合（标签「卡名 · 轴名」，跨卡合法），
    /// 删卡不动表。表定义主轴位置 m → 从轴位置 s 的映射拐点；
    /// 启动同步后主轴恒速循环走（50ms 一拍），从轴 = 按表插值跟随 ——
    /// **纯软件仿真，不依赖任何卡连接，断卡/急停不影响同步**（这是与"轴运动"的本质区别）。
    ///
    /// 【与 Web 版的差异】Web 编辑只改内存、点保存才落库；宿主里表对象本就是方案对象，
    /// 编辑即已"在方案里"，保存按钮保留同一文案但只做闸门校验 + 确认提示
    /// （闸门照旧：&lt;2 拐点 / 主从相同 → 拒绝）。
    /// </summary>
    public class MotionBoardCamTabViewModel : BindableBase
    {
        private readonly MotionProvider _provider;
        private readonly IWorkspaceManager _workspace;
        private readonly MotionBoardViewModel _shell;

        private DispatcherTimer? _syncTimer;

        /// <summary>轮廓预览的绘图区（与 Web 版同一几何：640×200、边距 24）</summary>
        private const double PlotWidth = 640;
        private const double PlotHeight = 200;
        private const double PlotPad = 24;

        public MotionBoardCamTabViewModel(
            MotionProvider provider, IWorkspaceManager workspace, MotionBoardViewModel shell)
        {
            _provider = provider;
            _workspace = workspace;
            _shell = shell;

            StartSyncCommand = new DelegateCommand(StartSync);
            StopSyncCommand = new DelegateCommand(StopSync);
            ResetSyncCommand = new DelegateCommand(ResetSync, () => !IsRunning);
            AddPointCommand = new DelegateCommand(AddPoint);
            RemovePointCommand = new DelegateCommand<MotionCamPointRow>(RemovePoint);
            AddTableCommand = new DelegateCommand(AddTable);
            DeleteTableCommand = new DelegateCommand(DeleteTable);
            SaveTableCommand = new DelegateCommand(SaveTable);
        }

        #region 表集合（全局实体）

        /// <summary>方案里的凸轮表（直接绑定方案集合的实例，编辑即写回）</summary>
        public ObservableCollection<MotionCamTable> Tables { get; } = new();

        private MotionCamTable? _selectedTable;
        public MotionCamTable? SelectedTable
        {
            get => _selectedTable;
            set
            {
                if (!SetProperty(ref _selectedTable, value))
                    return;

                // 切换表 = 自动停同步 + 复位（规格 6.5）
                StopSyncTimer();
                IsRunning = false;
                MasterPos = 0;
                SlavePos = 0;

                ReloadPointRows();
                RaisePropertyChanged(nameof(HasTable));
                RaisePropertyChanged(nameof(PreviewCaption));
                RefreshPreview();
            }
        }

        public bool HasTable => SelectedTable != null;

        /// <summary>外壳打开时把方案里的表搬进界面集合（含空库 seed 已由外壳完成）</summary>
        public void LoadTables()
        {
            Tables.Clear();
            foreach (var table in _workspace.CurrentSolution?.CamTables
                                    ?? new ObservableCollection<MotionCamTable>())
            {
                Tables.Add(table);
            }

            SelectedTable = Tables.FirstOrDefault();
            RefreshAxisOptions();
        }

        private void AddTable()
        {
            var solution = _workspace.CurrentSolution;
            if (solution == null)
            {
                _shell.NotifyError("当前没有打开的方案，无法新建凸轮表");
                return;
            }

            try
            {
                var table = MotionCamMath.CreateBlankTable($"凸轮表 {solution.CamTables.Count + 1}");
                solution.CamTables.Add(table);
                Tables.Add(table);
                SelectedTable = table;
            }
            catch (Exception ex)
            {
                _shell.NotifyError("新建凸轮表失败，请重试（" + ex.Message + "）");
            }
        }

        private void DeleteTable()
        {
            var table = SelectedTable;
            if (table == null) return;

            var solution = _workspace.CurrentSolution;
            solution?.CamTables.Remove(table);
            Tables.Remove(table);

            _shell.NotifyOk($"「{table.Name}」已删除");
            SelectedTable = Tables.FirstOrDefault();
        }

        #endregion

        #region 轴候选（所有卡的全部启用轴，标签「卡名 · 轴名」，跨卡合法）

        public ObservableCollection<string> AxisOptions { get; } = new();

        /// <summary>重建轴候选（打开时 / 卡列表变化后调用）。按标签去重</summary>
        public void RefreshAxisOptions()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var options = new List<string>();

            foreach (var card in _workspace.CurrentSolution?.MotionCards
                                 ?? new ObservableCollection<MotionDescriptor>())
            {
                foreach (var axis in card.Axes.Where(a => a.Enabled))
                {
                    // 标签拼接与解析必须同源（都在 MotionCamAxisRefs），否则改名时对不上
                    var label = MotionCamAxisRefs.Label(card.Caption, axis.LogicalName);
                    if (seen.Add(label)) options.Add(label);
                }
            }

            AxisOptions.Clear();
            foreach (var option in options) AxisOptions.Add(option);
        }

        #endregion

        #region 拐点表

        public ObservableCollection<MotionCamPointRow> PointRows { get; } = new();

        private void ReloadPointRows()
        {
            PointRows.Clear();
            if (SelectedTable == null) return;

            foreach (var point in SelectedTable.Points)
                PointRows.Add(new MotionCamPointRow(point, 0, RefreshPreview));

            ReindexRows();
        }

        private void ReindexRows()
        {
            for (var i = 0; i < PointRows.Count; i++) PointRows[i].Reindex(i + 1);
            RaisePropertyChanged(nameof(CanRemovePoint));
        }

        /// <summary>行数 ≤ 2 禁删（规格 6.2）</summary>
        public bool CanRemovePoint => SelectedTable is { Points.Count: > 2 };

        /// <summary>添加拐点：m=末点+10，s=末点+斜率×10（纯函数见 MotionCamMath）</summary>
        private void AddPoint()
        {
            var table = SelectedTable;
            if (table == null) return;

            var point = MotionCamMath.NextPointBySlope(table.Points);
            table.Points.Add(point);
            PointRows.Add(new MotionCamPointRow(point, PointRows.Count + 1, RefreshPreview));
            ReindexRows();
            RefreshPreview();
        }

        private void RemovePoint(MotionCamPointRow? row)
        {
            var table = SelectedTable;
            if (table == null || row == null || table.Points.Count <= 2) return;

            table.Points.Remove(row.Point);

            var wrapper = PointRows.FirstOrDefault(r => r.Point == row.Point);
            if (wrapper != null) PointRows.Remove(wrapper);
            ReindexRows();
            RefreshPreview();
        }

        #endregion

        #region 同步运行（50ms 一拍，纯仿真）

        private string _camSpeedText = "30";
        /// <summary>主轴速度（mm/s）。空/非法按 30 处理，下限 0.5</summary>
        public string CamSpeedText
        {
            get => _camSpeedText;
            set => SetProperty(ref _camSpeedText, value);
        }

        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            private set
            {
                if (SetProperty(ref _isRunning, value))
                {
                    ResetSyncCommand.RaiseCanExecuteChanged();
                    RaisePropertyChanged(nameof(StateText));
                    RaisePropertyChanged(nameof(HasCursor));
                }
            }
        }

        public string StateText => IsRunning ? "同步运行中" : "待机";

        /// <summary>读数条标签（轴标签缺省显示"主轴/从轴"）</summary>
        public string MasterCaption
        {
            get
            {
                var label = string.IsNullOrWhiteSpace(SelectedTable?.MasterAxis) ? "—" : SelectedTable.MasterAxis;
                return $"主轴 {label} 位置";
            }
        }

        public string SlaveCaption
        {
            get
            {
                var label = string.IsNullOrWhiteSpace(SelectedTable?.SlaveAxis) ? "—" : SelectedTable.SlaveAxis;
                return $"从轴 {label} 位置";
            }
        }

        /// <summary>进度条填充宽度（绘图区 220px，按百分比换算给 Border.Width）</summary>
        public double ProgressWidth => Math.Max(0, Math.Min(220, ProgressPercent / 100 * 220));

        /// <summary>游标是否可见（同步运行中才画）</summary>
        public bool HasCursor => IsRunning && CanPreview && CursorX >= 0;

        /// <summary>游标圆点左上角（圆心 r=5.5）</summary>
        public double CursorDotLeft => CursorX - 5.5;
        public double CursorDotTop => CursorY - 5.5;

        private double _masterPos;
        /// <summary>主轴位置（mm）</summary>
        public double MasterPos
        {
            get => _masterPos;
            private set
            {
                if (SetProperty(ref _masterPos, value))
                {
                    RaisePropertyChanged(nameof(MasterPosText));
                    RaisePropertyChanged(nameof(ProgressPercent));
                    RaisePropertyChanged(nameof(ProgressText));
                }
            }
        }

        public string MasterPosText => $"{MasterPos:F2}";

        private double _slavePos;
        /// <summary>从轴位置（插值跟随）</summary>
        public double SlavePos
        {
            get => _slavePos;
            private set
            {
                if (SetProperty(ref _slavePos, value))
                    RaisePropertyChanged(nameof(SlavePosText));
            }
        }

        public string SlavePosText => $"{SlavePos:F2}";

        /// <summary>本周期进度 0..100（主轴位置 / 周期）</summary>
        public double ProgressPercent
        {
            get
            {
                var cycle = SelectedTable == null ? 0 : MotionCamMath.Cycle(SelectedTable.Points);
                return cycle <= 0 ? 0 : Math.Min(100, MasterPos / cycle * 100);
            }
        }

        public string ProgressText => ProgressPercent.ToString("F0") + "%";

        public DelegateCommand StartSyncCommand { get; }
        public DelegateCommand StopSyncCommand { get; }
        public DelegateCommand ResetSyncCommand { get; }
        public DelegateCommand AddPointCommand { get; }
        public DelegateCommand<MotionCamPointRow> RemovePointCommand { get; }
        public DelegateCommand AddTableCommand { get; }
        public DelegateCommand DeleteTableCommand { get; }
        public DelegateCommand SaveTableCommand { get; }

        /// <summary>启动同步：三条闸门逐字按规格 6.5</summary>
        private void StartSync()
        {
            var table = SelectedTable;
            if (table == null || table.Points.Count < 2)
            {
                _shell.Reject("凸轮表至少需要 2 个拐点");
                return;
            }

            if (string.IsNullOrWhiteSpace(table.MasterAxis) || string.IsNullOrWhiteSpace(table.SlaveAxis))
            {
                _shell.Reject("请先选择主轴与从轴");
                return;
            }

            if (string.Equals(table.MasterAxis, table.SlaveAxis, StringComparison.Ordinal))
            {
                _shell.Reject("主轴与从轴不能相同");
                return;
            }

            IsRunning = true;
            _shell.CountExecuted();
            _shell.NotifyOk($"{table.MasterAxis} → {table.SlaveAxis} 凸轮同步运行中");

            StartSyncTimer();
        }

        private void StopSync()
        {
            if (!IsRunning) return;

            StopSyncTimer();
            IsRunning = false;
            _shell.CountExecuted();
            _shell.NotifyOk("凸轮同步已停止");
        }

        /// <summary>复位：仅停止态可用（读数归 0；按钮在运行中被禁用）</summary>
        private void ResetSync()
        {
            MasterPos = 0;
            SlavePos = 0;
        }

        private void StartSyncTimer()
        {
            StopSyncTimer();
            _syncTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _syncTimer.Tick += (_, _) => SyncTick();
            _syncTimer.Start();
        }

        private void StopSyncTimer()
        {
            _syncTimer?.Stop();
            _syncTimer = null;
        }

        /// <summary>
        /// 同步一拍（逐字对齐规格 6.4）：
        /// master = (master + max(0.5, 输入||30) × 0.05) mod cycle；slave = camInterpAt(pts, master)。
        /// </summary>
        private void SyncTick()
        {
            var points = SelectedTable?.Points;
            if (points == null || points.Count < 2) return;

            var cycle = MotionCamMath.Cycle(points);
            if (cycle <= 0) return;

            var speed = double.TryParse(CamSpeedText, out var parsed) && parsed > 0 ? parsed : 30;
            speed = Math.Max(0.5, speed);

            var next = (_masterPos + speed * 0.05) % cycle;
            MasterPos = next;
            SlavePos = MotionCamMath.InterpAt(points, next);

            UpdateCursor();
            RaisePropertyChanged(nameof(ProgressWidth));
            RaisePropertyChanged(nameof(MasterCaption));
            RaisePropertyChanged(nameof(SlaveCaption));
        }

        /// <summary>外壳关闭：同步随窗口停（下次打开从待机开始，运行态不进方案）</summary>
        public void OnShellClosed() => StopSyncTimer();

        #endregion

        #region 保存（闸门 + 确认 toast；持久化由方案体系承担）

        private void SaveTable()
        {
            var table = SelectedTable;
            if (table == null) return;

            if (table.Points.Count < 2)
            {
                _shell.Reject("凸轮表至少需要 2 个拐点");
                return;
            }

            if (!string.IsNullOrWhiteSpace(table.MasterAxis)
                && string.Equals(table.MasterAxis, table.SlaveAxis, StringComparison.Ordinal))
            {
                _shell.Reject("主轴与从轴不能相同");
                return;
            }

            // 表对象即方案对象（CamTables），编辑已随方案体系持久；这里按规格给出确认 toast
            _shell.CountExecuted();
            _shell.NotifyOk($"「{table.Name}」已保存");
        }

        #endregion

        #region 轮廓预览（每段 24 细分采样 + 运行游标）

        /// <summary>曲线 polyline 的点集（640×200 绘图区坐标；Viewbox 自适应）</summary>
        private PointCollection _polylinePoints = new();
        public PointCollection PolylinePoints
        {
            get => _polylinePoints;
            private set
            {
                _polylinePoints = value;
                RaisePropertyChanged();
            }
        }

        /// <summary>拐点圆点（白底主描边）位置集</summary>
        private List<Point> _markerPoints = new();
        public List<Point> MarkerPoints
        {
            get => _markerPoints;
            private set
            {
                _markerPoints = value;
                RaisePropertyChanged();
            }
        }

        /// <summary>运行游标竖线的 X（待机时 -1 = 不画）</summary>
        public double CursorX { get; private set; } = -1;

        /// <summary>游标圆点的 Y</summary>
        public double CursorY { get; private set; }

        public bool CanPreview { get; private set; } = true;

        private double _masterMin;

        private void RefreshPreview()
        {
            var table = SelectedTable;
            var points = table?.Points;
            if (points == null || points.Count < 2)
            {
                CanPreview = false;
                RaisePropertyChanged(nameof(CanPreview));
                PolylinePoints = new PointCollection();
                MarkerPoints = new List<Point>();
                return;
            }

            var cycle = MotionCamMath.Cycle(points);
            if (cycle <= 0)
            {
                CanPreview = false;
                RaisePropertyChanged(nameof(CanPreview));
                PolylinePoints = new PointCollection();
                MarkerPoints = new List<Point>();
                return;
            }

            CanPreview = true;
            RaisePropertyChanged(nameof(CanPreview));

            var sorted = points.OrderBy(p => p.M).ToList();
            _masterMin = sorted[0].M;

            var sValues = sorted.Select(p => p.S).ToList();
            var sMin = sValues.Min();
            var sSpan = Math.Max(1, sValues.Max() - sMin);

            double X(double m) => PlotPad + (m - _masterMin) / cycle * (PlotWidth - PlotPad * 2);
            double Y(double s) => PlotHeight - PlotPad - (s - sMin) / sSpan * (PlotHeight - PlotPad * 2);

            // 每段 24 细分采样：三次曲线段才能显示为平滑过渡（规格 6.2）
            var samples = new PointCollection();
            for (var i = 0; i < sorted.Count - 1; i++)
            {
                var a = sorted[i];
                var b = sorted[i + 1];
                for (var k = 0; k < 24; k++)
                {
                    var m = a.M + (b.M - a.M) * k / 24;
                    samples.Add(new Point(X(m), Y(MotionCamMath.InterpAt(points, m))));
                }
            }

            var last = sorted[sorted.Count - 1];
            samples.Add(new Point(X(last.M), Y(last.S)));
            PolylinePoints = samples;

            MarkerPoints = sorted.Select(p => new Point(X(p.M), Y(p.S))).ToList();

            UpdateCursor();
        }

        /// <summary>游标位置（主轴折回周期 + 从轴插值）；待机时不画游标</summary>
        private void UpdateCursor()
        {
            var points = SelectedTable?.Points;
            if (points == null || points.Count < 2 || !CanPreview || !IsRunning)
            {
                CursorX = -1;
                RaisePropertyChanged(nameof(CursorX));
                return;
            }

            var cycle = MotionCamMath.Cycle(points);
            if (cycle <= 0) return;

            var sorted = points.OrderBy(p => p.M).ToList();
            var sValues = sorted.Select(p => p.S).ToList();
            var sMin = sValues.Min();
            var sSpan = Math.Max(1, sValues.Max() - sMin);

            var folded = ((MasterPos - _masterMin) % cycle + cycle) % cycle;
            CursorX = PlotPad + folded / cycle * (PlotWidth - PlotPad * 2);
            CursorY = PlotHeight - PlotPad - (SlavePos - sMin) / sSpan * (PlotHeight - PlotPad * 2);

            RaisePropertyChanged(nameof(CursorX));
            RaisePropertyChanged(nameof(CursorY));
        }

        /// <summary>预览底部说明（逐字按规格，轴名缺省显示"主轴/从轴"）</summary>
        public string PreviewCaption
        {
            get
            {
                var table = SelectedTable;
                var master = string.IsNullOrWhiteSpace(table?.MasterAxis) ? "主轴" : table.MasterAxis;
                var slave = string.IsNullOrWhiteSpace(table?.SlaveAxis) ? "从轴" : table.SlaveAxis;
                return $"凸轮轮廓预览（横轴 = {master} 位置 · 纵轴 = {slave} 位置 · 实心圆点 = 表中拐点）";
            }
        }

        #endregion
    }
}
