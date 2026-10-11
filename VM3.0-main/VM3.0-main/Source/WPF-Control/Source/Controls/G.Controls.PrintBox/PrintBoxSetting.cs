using G.Extensions.Setting;

namespace G.Controls.PrintBox
{
    [Display(Name = "打印设置", GroupName = SettingGroupNames.GroupControl)]
    public class PrintBoxSetting : Settable<PrintBoxSetting>
    {
        private double _printableAreaWidth;
        public double PrintableAreaWidth
        {
            get { return _printableAreaWidth; }
            set
            {
                _printableAreaWidth = value;
                RaisePropertyChanged();
            }
        }
    }
}
