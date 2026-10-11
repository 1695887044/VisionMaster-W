using G.Mvvm.ViewModels.Base;
using System.Windows.Documents;

namespace G.Controls.PrintBox
{
    public class DocumentViewPresenter : BindableBase
    {
        private IDocumentPaginatorSource _document;
        public IDocumentPaginatorSource Document
        {
            get { return _document; }
            set
            {
                _document = value;
                RaisePropertyChanged();
            }
        }
    }
}
