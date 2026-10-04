using Core.Interfaces;
using System.Windows.Controls;

namespace Plugin.PoseTransform
{
    /// <summary>
    /// 坐标变换插件配置视图（DataContext = 插件实例自身：Plugin 与 ViewModel 合一）。
    ///
    /// 挂接/卸载会通知插件（OnViewAttached/OnViewDetached）：
    /// 叠加预览只在配置窗口开着时刷新——对话框关闭后不再为"没人看的图"做重采样。
    /// </summary>
    public partial class PoseTransformView : UserControl
    {
        public PoseTransformView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 配置视图构造：灌值（Initialize）在构造里完成，与「图像采集」「标定」同一模式。
        /// </summary>
        public PoseTransformView(IStepConfigData stepData, PoseTransformPlugin plugin)
        {
            InitializeComponent();
            DataContext = plugin;

            plugin.Initialize(stepData);
            plugin.OnViewAttached();
            Unloaded += (_, _) => plugin.OnViewDetached();
        }
    }
}
