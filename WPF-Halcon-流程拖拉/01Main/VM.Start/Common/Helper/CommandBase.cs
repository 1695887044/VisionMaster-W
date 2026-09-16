using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace VM.Start.Common.Helper
{
    [Serializable]
    public class CommandBase : ICommand
    {
        [field: NonSerialized()]
        public event EventHandler CanExecuteChanged;
        public CommandBase() { }
        public CommandBase(Action<object> doExecute)
        {
            DoExecute = doExecute;
            DoCanExecute = new Func<object, bool>(o => true);
        }
        public CommandBase(Action<object> doExecute, Func<object, bool> doCanExecute)
        {
            DoExecute = doExecute;
            DoCanExecute = doCanExecute;
        }

        public bool CanExecute(object parameter)
        {
            return DoCanExecute?.Invoke(parameter) == true;
        }

        public void Execute(object parameter = null)
        {
            if (parameter == null) 
            {
                DoExecute?.Invoke(null);
            }
            else
            {
                DoExecute?.Invoke(parameter);
            }
        }
        [field: NonSerialized()]
        public Action<object> DoExecute { get; set; }
        [field: NonSerialized()]
        public Func<object, bool> DoCanExecute { get; set; }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, new EventArgs());
        }
    }
}
