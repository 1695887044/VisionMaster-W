using Core.Interfaces;
using System.Windows.Controls;

namespace Plugin.Calibration
{
    /// <summary>
    /// 标定插件配置视图（DataContext = 插件实例自身：Plugin 与 ViewModel 合一）。
    ///
    /// 画布取点的交互全部走控件既有通道（ActiveRoi / DrawObjectList / HTuples 回写），
    /// 不引入任何新的控件 API——本文件因此没有一行"取点事件"代码。
    /// </summary>
    public partial class CalibrationView : UserControl
    {
        public CalibrationView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 配置视图构造：灌值（Initialize）在构造里完成，与「图像采集」插件同一模式。
        /// </summary>
        public CalibrationView(IStepConfigData stepData, CalibrationPlugin plugin)
        {
            InitializeComponent();
            DataContext = plugin;
            plugin.Initialize(stepData);
        }
    }
}
