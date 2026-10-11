using G.Common.Attributes;
using G.Extensions.Mvvm.ViewModels.Base;

namespace G.Modules.License
{
    public interface IVipFlagViewPresenter
    {
        void Refresh();
    }

    [Icon("\xE72E")]
    public class VipFlagViewPresenter : DisplayBindableBase, IVipFlagViewPresenter
    {
        public VipFlagViewPresenter()
        {
            this.Refresh();
        }

        private int _level;
        public int Level
        {
            get { return _level; }
            private set
            {
                _level = value;
                RaisePropertyChanged();
            }
        }

        public void Refresh()
        {
            var option = Ioc<ILicenseService>.Instance?.IsVail(out string message);
            this.Level = option.Level;
        }

    }
}
