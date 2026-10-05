namespace VisionMaster.ViewModels
{
    /// <summary>
    /// 调试命中窗（断点命中 / 单步暂停）的展示 VM（DWV 第 1 期）。
    ///
    /// 【职责边界】只负责"显示什么 + 勾选框状态"；三个运行按钮直接绑 Shell 的 ExecutionCommand
    /// （CommandParameter 区分 继续 / 单步 / 停止），CanExecute 与主界面按钮共用同一条互锁矩阵 ——
    /// 本类不复制任何执行 / 互锁逻辑。「打开模块参数」转发 Shell 的方法（内部再走共享帮助类）。
    ///
    /// 【线程】内容更新、勾选框回写都由 ShellViewModel 在 UI 线程调用（会话事件已做过调度）。
    /// 【单实例】窗口由 ShellViewModel 持单例，本 VM 每次命中用 Update 刷新内容。
    /// </summary>
    public class DebugHitViewModel : BindableBase
    {
        private readonly Action _openParameters;
        private readonly Action<bool> _suppressChanged;

        /// <summary>Shell 的运行控制命令（同一实例：继续 / 单步 / 停止三种动作都从这里走）</summary>
        public DelegateCommand<ExecutionAction?> ExecutionCommand { get; }

        /// <summary>打开命中步骤的模块参数（复用流程栏同一帮助类；未定位到步骤时置灰）</summary>
        public DelegateCommand OpenParametersCommand { get; }

        /// <summary>标题：「断点命中」/「单步暂停」（由 ShellViewModel 按 PauseReason 填）</summary>
        public string Header
        {
            get => field;
            private set => SetProperty(ref field, value);
        }

        /// <summary>命中流程名</summary>
        public string FlowName
        {
            get => field;
            private set => SetProperty(ref field, value);
        }

        /// <summary>命中步骤名（未定位到时为占位文案）</summary>
        public string StepName
        {
            get => field;
            private set => SetProperty(ref field, value);
        }

        /// <summary>
        /// 「本次运行不再提示」：勾选即回写 Shell 的抑制标志（断点/单步照常停，只是不再弹本窗）。
        /// 复位（新一轮运行/回到未启动）由 ShellViewModel 统一做，本类只做双向同步。
        /// </summary>
        public bool SuppressThisRun
        {
            get => field;
            set
            {
                if (SetProperty(ref field, value))
                    _suppressChanged?.Invoke(value);
            }
        }

        /// <summary>命中步骤是否可打开参数（未定位到步骤时为 false → 按钮置灰）</summary>
        public bool CanOpenParameters
        {
            get => field;
            private set
            {
                if (SetProperty(ref field, value))
                    OpenParametersCommand.RaiseCanExecuteChanged();
            }
        }

        public DebugHitViewModel(
            DelegateCommand<ExecutionAction?> executionCommand,
            Action openParameters,
            Action<bool> suppressChanged)
        {
            ExecutionCommand = executionCommand;
            _openParameters = openParameters;
            _suppressChanged = suppressChanged;
            OpenParametersCommand = new DelegateCommand(OpenParameters, () => CanOpenParameters);
        }

        /// <summary>命中窗每次弹出前刷新内容（单实例复用）</summary>
        public void Update(string header, string flowName, string stepName, bool canOpenParameters)
        {
            Header = header;
            FlowName = flowName;
            StepName = stepName;
            CanOpenParameters = canOpenParameters;
        }

        private void OpenParameters() => _openParameters?.Invoke();
    }
}
