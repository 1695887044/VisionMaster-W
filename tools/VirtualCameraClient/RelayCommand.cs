using System.Windows.Input;

namespace VirtualCameraClient
{
    /// <summary>
    /// 最小 ICommand 实现：把"按钮点击"转成"调用一个方法"。
    ///
    /// 为什么界面不直接在 Click 事件里写逻辑：
    /// 那样逻辑就散落在 xaml.cs 里，ViewModel 无法独自测试、无法复用。
    /// 用 Command 绑定后，View 只负责"点"，"点了做什么"留在 ViewModel。
    ///
    /// 本实现刻意不带 CommandManager.RequerySuggested 自动刷新：
    /// 本工具的 CanExecute 很少变，需要时显式调 RaiseCanExecuteChanged 即可，避免每帧查询开销。
    /// </summary>
    public sealed class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute == null || _canExecute();

        public void Execute(object parameter) => _execute();

        public event EventHandler CanExecuteChanged;

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
