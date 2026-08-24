using System.Windows.Controls;
using Core.Interfaces;

namespace Plugin.ImageAcquisition
{
    public partial class ImageAcquisitionView : UserControl
    {
        public ImageAcquisitionView(IStepConfigData stepData)
        {
            InitializeComponent();
            var vm = new ImageAcquisitionViewModel();
            DataContext = vm;
            vm.Initialize(stepData);
        }
    }
}
