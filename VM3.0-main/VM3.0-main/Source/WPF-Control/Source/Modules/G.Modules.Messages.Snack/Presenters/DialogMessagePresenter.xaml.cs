global using G.Mvvm.Commands;
using G.Common.Attributes;
using G.Extensions.FontIcon;
using System.ComponentModel.DataAnnotations;

namespace G.Modules.Messages.Snack
{
    [Icon(FontIcons.Unknown)]
    [Display(Name = "对话框消息", Description = "这是一条对话框消息")]
    public class DialogMessagePresenter : MessagePresenterBase
    {
        public Predicate<DialogMessagePresenter> IsMatch { get; set; }
        public RelayCommand SumitCommand => new RelayCommand(e =>
        {
            this.DialogResult = true;
            this.Close();
        });

        public RelayCommand CancelCommand => new RelayCommand(e =>
        {
            this.DialogResult = false;
            this.Close();
        });

        public RelayCommand CloseCommand => new RelayCommand(e =>
        {
            this.Close();
        });

        private void Close()
        {
            _waitHandle.Set();
        }
        private bool? DialogResult { get; set; }
        private ManualResetEvent _waitHandle = new ManualResetEvent(false);
        public async Task<bool?> ShowDialog()
        {
            _waitHandle.Reset();
            return await Task.Run(() =>
            {
                _waitHandle.WaitOne();
                return this.DialogResult;
            });
        }

    }
}
