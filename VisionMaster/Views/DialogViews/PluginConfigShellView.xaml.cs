using System.Windows;
using System.Windows.Controls;

namespace VisionMaster.Views.DialogViews
{
    /// <summary>
    /// 插件配置弹窗的外壳（头部 / 参数区 / 底部）。
    ///
    /// 这里既不做窗口尺寸判断、也不做居中：
    ///   · 尺寸是"参数面板"自己的事（只有它知道自己只有几行、该按内容定高）；
    ///   · 居中由窗口 Style 上的 UI.Behaviors.WindowCenteringBehavior 统一处理。
    /// </summary>
    public partial class PluginConfigShellView : UserControl
    {
        public PluginConfigShellView()
        {
            InitializeComponent();
        }
    }
}
