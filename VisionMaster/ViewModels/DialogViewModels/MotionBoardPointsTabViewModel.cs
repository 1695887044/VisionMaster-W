using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Core.Interfaces;
using Prism.Commands;
using Prism.Mvvm;
using VisionMaster.Services;

namespace VisionMaster.ViewModels.DialogViewModels
{
    /// <summary>
    /// 点位列表左栏的一根轴：轴名 + 实时位置 + 固定副行「16 点位 · P0–P15」。
    /// 位置由外壳定时器刷新 —— 左栏与右侧表格"每轴各持一表"的从属关系靠它可视。
    /// </summary>
    public sealed class MotionPointsAxisRow : BindableBase
    {
        public MotionPointsAxisRow(AxisMapping mapping)
        {
            Mapping = mapping;
        }

        public AxisMapping Mapping { get; }

        /// <summary>轴名（读 Mapping，改名方在「卡设置」页签 —— 数据源变了由页签 VM 补通知）</summary>
        public string Name =>
            string.IsNullOrWhiteSpace(Mapping.LogicalName) ? $"轴{Mapping.PhysicalIndex}" : Mapping.LogicalName;

        /// <summary>轴名数据源被外部改了，由页签 VM 调用补通知</summary>
        public void RaiseNameChanged() => RaisePropertyChanged(nameof(Name));

        public string Subtitle => "16 点位 · P0–P15";

        private double _positionMm;
        public double PositionMm
        {
            get => _positionMm;
            internal set
            {
                if (_positionMm.Equals(value)) return;
                _positionMm = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(PositionText));
            }
        }

        public string PositionText => $"{PositionMm:F1} mm";
    }

    /// <summary>
    /// 页签三「点位列表」（规格第 5 章）：左轴栏 220px + 右 16 行点位表。
    ///
    /// 【持久化】点位行就是方案对象（MotionDescriptor.Points）里的实例，
    /// 表格单元格提交即写进对象 —— 与「卡设置」同一套持久化约定（改动随方案落盘），
    /// 没有额外的保存按钮（图例文案「改完即存」说的就是这件事）。
    ///
    /// 【走此点】与「定位」同一套闸门（未连接/未使能/未回零/忙，文案逐字一致），
    /// 速度 ≤0 回退卡默认，按该行的曲线类型下发绝对定位。
    /// </summary>
    public class MotionBoardPointsTabViewModel : BindableBase
    {
        private readonly MotionProvider _provider;
        private readonly MotionBoardViewModel _shell;

        public MotionBoardPointsTabViewModel(
            MotionProvider provider, IWorkspaceManager workspace, MotionBoardViewModel shell)
        {
            _provider = provider;
            _shell = shell;

            CurveOptions = new[]
            {
                new KeyValuePair<MotionCurve, string>(MotionCurve.Trapezoid, "梯形"),
                new KeyValuePair<MotionCurve, string>(MotionCurve.SCurve, "S曲线"),
            };

            WalkPointCommand = new DelegateCommand<MotionPoint>(WalkPoint);
        }

        /// <summary>曲线候选（正运动契约只提供梯形/S曲线两档；「匀速」见图例说明行）</summary>
        public IReadOnlyList<KeyValuePair<MotionCurve, string>> CurveOptions { get; }

        private MotionDescriptor? _selectedDescriptor;
        public MotionDescriptor? SelectedDescriptor
        {
            get => _selectedDescriptor;
            set
            {
                if (!SetProperty(ref _selectedDescriptor, value))
                    return;

                RaisePropertyChanged(nameof(HasSelection));
                ReloadAxisRows();
            }
        }

        public bool HasSelection => _selectedDescriptor != null;

        #region 左栏轴列表

        public ObservableCollection<MotionPointsAxisRow> AxisRows { get; } = new();

        private MotionPointsAxisRow? _selectedAxisRow;
        public MotionPointsAxisRow? SelectedAxisRow
        {
            get => _selectedAxisRow;
            set
            {
                if (!SetProperty(ref _selectedAxisRow, value))
                    return;

                LoadPoints();
                RaisePropertyChanged(nameof(HeaderAxisName));
                RaisePropertyChanged(nameof(CurrentPositionText));
            }
        }

        private void ReloadAxisRows()
        {
            AxisRows.Clear();
            _selectedAxisRow = null;
            Points.Clear();

            if (_selectedDescriptor == null) return;

            foreach (var mapping in _selectedDescriptor.Axes.Where(a => a.Enabled))
                AxisRows.Add(new MotionPointsAxisRow(mapping));

            SelectedAxisRow = AxisRows.FirstOrDefault();
        }

        /// <summary>定时器刷新：左栏实时位置 + 右上读数（读轮询快照）</summary>
        public void RefreshRuntime()
        {
            var device = CurrentDevice;
            foreach (var row in AxisRows)
            {
                var status = device?.GetAxisStatus(row.Mapping.PhysicalIndex);
                row.PositionMm = status?.PositionMm ?? 0;
            }

            RaisePropertyChanged(nameof(CurrentPositionText));
        }

        private IMotionDevice? CurrentDevice =>
            _selectedDescriptor != null && _provider.TryGetDevice(_selectedDescriptor.Id, out var device)
                ? device
                : null;

        /// <summary>「卡设置」里行内改了轴名：左栏行与表头大字补一次通知</summary>
        public void NotifyAxisNamesChanged()
        {
            foreach (var row in AxisRows)
                row.RaiseNameChanged();

            RaisePropertyChanged(nameof(HeaderAxisName));
        }

        #endregion

        #region 右侧 16 行点位表

        public ObservableCollection<MotionPoint> Points { get; } = new();

        public string HeaderAxisName => SelectedAxisRow?.Name ?? "—";

        public string CurrentPositionText => $"{SelectedAxisRow?.PositionMm ?? 0:F3}";

        private void LoadPoints()
        {
            Points.Clear();
            if (_selectedDescriptor == null || SelectedAxisRow == null) return;

            // 懒 seed（速度/加减速=卡默认，规格 S3-2）+ 旧数据补名，然后整表换入
            _selectedDescriptor.FillDefaultNames(SelectedAxisRow.Name);
            foreach (var point in _selectedDescriptor.GetAxisPoints(SelectedAxisRow.Name))
                Points.Add(point);
        }

        #endregion

        #region 走此点

        public DelegateCommand<MotionPoint> WalkPointCommand { get; }

        /// <summary>
        /// 走此点：按该行的速度/加减速/曲线发起绝对定位。
        /// 闸门（未连接/未使能/未回零/忙）与「定位」逐字一致；速度 ≤0 回退卡默认。
        /// </summary>
        private void WalkPoint(MotionPoint? point)
        {
            var descriptor = _selectedDescriptor;
            var axisRow = SelectedAxisRow;
            if (descriptor == null || axisRow == null || point == null) return;

            if (!_provider.TryGetByKey(descriptor.Address, out var device)
                || device.State != MotionCardState.Online)
            {
                _shell.Reject("运动卡未连接");
                return;
            }

            var status = device.GetAxisStatus(axisRow.Mapping.PhysicalIndex);
            if (status == null)
            {
                _shell.Reject("运动卡未连接");
                return;
            }

            if (!status.Enabled)
            {
                _shell.Reject($"{axisRow.Name} 未使能，请先伺服使能");
                return;
            }

            if (!device.IsHomed)
            {
                _shell.Reject($"{axisRow.Name} 需要回零后才能运动");
                return;
            }

            if (status.Moving)
            {
                _shell.Reject($"{axisRow.Name} 正在运动，请等待完成");
                return;
            }

            var result = device.Enqueue(new MotionCommand
            {
                Kind = MotionCommandKind.MoveAbsolute,
                PhysicalAxis = axisRow.Mapping.PhysicalIndex,
                LogicalAxis = axisRow.Name,
                TargetMm = point.PositionMm,
                VelocityMmPerS = point.SpeedMmPerS > 0
                    ? point.SpeedMmPerS
                    : descriptor.Params.DefaultVelocityMmPerS,
                AccelMmPerS2 = point.AccelMmPerS2,
                Curve = point.Curve,
                Timeout = TimeSpan.FromMilliseconds(Math.Max(3000, descriptor.Params.CommandTimeoutMs)),
            });

            if (result != MotionCommandResult.Accepted)
                _shell.NotifyError($"走此点被拒绝：{device.LastFault?.Suggestion ?? device.StateDetail}");
        }

        #endregion
    }
}
