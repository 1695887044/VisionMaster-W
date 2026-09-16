using System;
using System.Windows.Input;

namespace Core.Commands
{
    /// <summary>
    /// 极简命令（MVVM）：委托转 ICommand，供插件视图绑定按钮。
    ///
    /// 放在公共程序集而非各插件自带：本类会被 PreProcessing、CreateRoi 等多个插件逐字复制，
    /// 属于典型的"基础设施"而非"插件私有实现"。各插件已经引用 Core.Controls，集中一份不增加耦合，
    /// 反而让"按钮为什么该灰不灰"这类问题只有一处需要修。
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;

        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        /// <summary>
        /// 挂在 WPF 全局命令重估上：界面状态一变（选中项、焦点、按钮点击）框架自己来问 CanExecute，
        /// 省掉手写 RaiseCanExecuteChanged 的麻烦。
        /// </summary>
        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

        public void Execute(object? parameter) => _execute(parameter);
    }
}
