using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Core.Interfaces;
using Prism.Commands;
using VisionMaster.Services;
using Prism.Mvvm;

namespace VisionMaster.ViewModels.DialogViewModels
{
    /// <summary>
    /// 轴点位表（每轴固定 16 行 P0–P15）。
    ///
    /// 【持久化】点位行就是方案对象（MotionDescriptor.Points）里的实例，
    /// DataGrid 单元格提交即写进对象 —— 与「卡设置」同一套持久化约定
    /// （直接改方案里的对象，落盘由工作区统一负责），不需要额外的保存按钮。
    ///
    /// 【走此点】直接 Enqueue 定位命令：未连接/未使能/未回零/忙 这些闸门
    /// 全部由 MotionDeviceBase 的门禁统一把守（与流程里的「轴运动」同一套规则），
    /// 这里不重复实现，只负责把拒绝原因显示出来 —— 门禁逻辑只有一份才不会两处说得不一样。
    ///
    /// 【为什么不按设计稿做左右两栏】轴选择用两个下拉（卡 → 轴）代替：
    /// 面板高度有限，表格 16 行已经需要滚动，再塞一栏轴列表会更挤；
    /// 两级下拉的"选卡 → 选轴"链路同样清晰，左右栏作为后续优化项。
    /// </summary>
    public class AxisPointsViewModel : BindableBase, IDialogAware
    {
        private readonly IWorkspaceManager _workspace;
        private readonly IMotionProvider _motions;

        public AxisPointsViewModel(IWorkspaceManager workspace, IMotionProvider motions)
        {
            _workspace = workspace;
            _motions = motions;

            // 候选文本要短：表格列宽有限，长描述会被截断（"梯形（恒速、末端减速…"）。
            // 形状说明放列表下方的图例里，选项本身只留名字。
            CurveOptions = new List<KeyValuePair<MotionCurve, string>>
            {
                new(MotionCurve.Trapezoid, "梯形"),
                new(MotionCurve.SCurve, "S 曲线"),
            };

            WalkPointCommand = new DelegateCommand<MotionPoint>(WalkPoint, p => p != null && !IsBusy);
            CloseCommand = new DelegateCommand(() => RequestClose.Invoke(Prism.Dialogs.ButtonResult.Cancel));
        }

        /// <summary>自绘头部的关闭按钮（无边框窗口没有系统关闭键，仓库断言在盯）</summary>
        public DelegateCommand CloseCommand { get; }

        /// <summary>曲线候选（值 = 枚举，显示 = 中文说明）</summary>
        public List<KeyValuePair<MotionCurve, string>> CurveOptions { get; }

        public ObservableCollection<MotionDescriptor> Cards { get; } = new();

        private MotionDescriptor _selectedCard;
        public MotionDescriptor SelectedCard
        {
            get => _selectedCard;
            set
            {
                if (SetProperty(ref _selectedCard, value)) LoadAxes();
            }
        }

        public ObservableCollection<AxisMapping> Axes { get; } = new();

        private AxisMapping _selectedAxis;
        public AxisMapping SelectedAxis
        {
            get => _selectedAxis;
            set
            {
                if (SetProperty(ref _selectedAxis, value)) LoadPoints();
            }
        }

        private ObservableCollection<MotionPoint> _points = new();
        public ObservableCollection<MotionPoint> Points
        {
            get => _points;
            private set => SetProperty(ref _points, value);
        }

        private string _statusText = "选择卡与轴后，这里列出该轴的 16 个点位";
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value)) WalkPointCommand.RaiseCanExecuteChanged();
            }
        }

        public DelegateCommand<MotionPoint> WalkPointCommand { get; }

        // IDialogAware（Prism 弹窗契约）：本窗口用系统标题栏，关闭由系统处理，这里只是满足接口
        public bool CanCloseDialog() => true;
        public void OnDialogClosed() { }
        public void OnDialogOpened(IDialogParameters parameters) { }
        public DialogCloseListener RequestClose { get; }

        /// <summary>面板可见时刷新卡列表（方案可能刚加了卡）。选中项尽量保持</summary>
        public void Refresh()
        {
            var cards = _workspace.CurrentSolution?.MotionCards
                        ?? new System.Collections.ObjectModel.ObservableCollection<MotionDescriptor>();

            var previous = SelectedCard?.Address;
            Cards.Clear();
            foreach (var card in cards) Cards.Add(card);

            SelectedCard = Cards.FirstOrDefault(c => c.Address == previous) ?? Cards.FirstOrDefault();
        }

        private void LoadAxes()
        {
            Axes.Clear();
            SelectedAxis = null;

            if (SelectedCard == null)
            {
                StatusText = "请选择运动卡";
                return;
            }

            foreach (var axis in SelectedCard.Axes.Where(a => a.Enabled))
                Axes.Add(axis);

            StatusText = Axes.Count > 0 ? "选择轴" : "该卡没有启用的轴映射，请到「卡设置」里配置";
            SelectedAxis = Axes.FirstOrDefault();
        }

        private void LoadPoints()
        {
            Points.Clear();

            if (SelectedCard == null || SelectedAxis == null) return;

            // 懒 seed + 旧数据补名：该轴首次打开补齐 16 行；早期 seed 的空名行补「点位{序号}」
            SelectedCard.FillDefaultNames(SelectedAxis.LogicalName);
            foreach (var point in SelectedCard.GetAxisPoints(SelectedAxis.LogicalName))
                Points.Add(point);

            StatusText = $"轴 {SelectedAxis.LogicalName} 的点位表（改动即生效，随方案保存）";
        }

        /// <summary>
        /// 走此点：按该行的速度/加减速/曲线发起绝对定位。
        /// 闸门（未连接/未使能/未回零/忙）由设备的命令门禁统一判断，这里只呈现结果。
        /// </summary>
        private void WalkPoint(MotionPoint point)
        {
            if (SelectedCard == null || SelectedAxis == null || point == null) return;

            if (!_motions.TryGetByKey(SelectedCard.Address, out var device))
            {
                StatusText = $"找不到运动卡「{SelectedCard.Address}」：请确认该卡已配置并连接";
                return;
            }

            IsBusy = true;
            try
            {
                var result = device.Enqueue(new MotionCommand
                {
                    Kind = MotionCommandKind.MoveAbsolute,
                    PhysicalAxis = SelectedAxis.PhysicalIndex,
                    LogicalAxis = SelectedAxis.LogicalName,
                    TargetMm = point.PositionMm,
                    VelocityMmPerS = point.SpeedMmPerS,
                    AccelMmPerS2 = point.AccelMmPerS2,
                    Curve = point.Curve,
                    Timeout = TimeSpan.FromMilliseconds(Math.Max(3000, SelectedCard.Params?.CommandTimeoutMs ?? 30000)),
                });

                StatusText = result == MotionCommandResult.Accepted
                    ? $"已下发 {point.Caption} → {point.PositionMm:F3} mm（下发即返回，不等到位）"
                    : $"命令被拒绝：{device.LastFault?.Message ?? result.ToString()}";
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
