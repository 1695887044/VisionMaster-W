using System;
using Prism.Mvvm;

namespace VisionMaster.ViewModels
{
    /// <summary>
    /// 工位浮窗的一行（底栏「N 条在跑」点开后的列表）。
    /// 由 ShellViewModel 从 FlowRuntimeVariableSource 的快照增量更新，不整表重建。
    /// </summary>
    public sealed class FlowStatsRow : BindableBase
    {
        public FlowStatsRow(Guid flowId) => FlowId = flowId;

        public Guid FlowId { get; }

        private string _name = string.Empty;
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        private string _station = string.Empty;
        public string Station
        {
            get => _station;
            set => SetProperty(ref _station, value);
        }

        private string _role = string.Empty;
        public string Role
        {
            get => _role;
            set => SetProperty(ref _role, value);
        }

        private string _stateText = "停止";
        public string StateText
        {
            get => _stateText;
            set => SetProperty(ref _stateText, value);
        }

        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            set => SetProperty(ref _isRunning, value);
        }

        private string _cycleText = "—";
        public string CycleText
        {
            get => _cycleText;
            set => SetProperty(ref _cycleText, value);
        }

        private string _okText = "—";
        public string OkText
        {
            get => _okText;
            set => SetProperty(ref _okText, value);
        }

        private string _ngText = "—";
        public string NgText
        {
            get => _ngText;
            set => SetProperty(ref _ngText, value);
        }

        private string _unknownText = "—";
        public string UnknownText
        {
            get => _unknownText;
            set => SetProperty(ref _unknownText, value);
        }

        private string _sourceText = "未配置";
        public string SourceText
        {
            get => _sourceText;
            set => SetProperty(ref _sourceText, value);
        }

        private string _lastRoundText = "—";
        public string LastRoundText
        {
            get => _lastRoundText;
            set => SetProperty(ref _lastRoundText, value);
        }

        private bool _isIncluded = true;
        public bool IsIncluded
        {
            get => _isIncluded;
            set => SetProperty(ref _isIncluded, value);
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
    }
}
