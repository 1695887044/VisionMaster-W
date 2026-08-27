using System.Windows.Controls;
using Core.Interfaces;

namespace Plugin.ImageAcquisition
{
    public partial class ImageAcquisitionView : UserControl
    {
        public ImageAcquisitionView()
        {

        }

        /// <summary>
        /// 配置视图构造：DataContext = 插件实例自身（Plugin 与 ViewModel 合一）
        /// </summary>
        public ImageAcquisitionView(IStepConfigData stepData, ImageAcquisitionPlugin plugin)
        {
            InitializeComponent();
            DataContext = plugin;
            plugin.Initialize(stepData);
        }
    }
}
