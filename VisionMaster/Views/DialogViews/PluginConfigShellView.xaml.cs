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

            // 窗口 Style 里 Opacity=0（防"先弹大窗再缩小"的闪动）。
            // 显形**不能挂在 Loaded 里**：Loaded 的触发顺序依赖可视树构建过程，外壳若先于
            // AutoPortConfigView 触发，就会先显形成 960×680、再被缩一次（实测用户仍能看到闪动）。
            // 改成把"显形"投递到 DispatcherPriority.ContextIdle：那时本次布局与渲染队列已排空，
            // 子视图的尺寸调整必定做完。
            Loaded += (_, _) =>
            {
                if (Window.GetWindow(this) is not { } window) return;

                window.Dispatcher.BeginInvoke(
                    new Action(() => window.Opacity = 1),
                    System.Windows.Threading.DispatcherPriority.ContextIdle);
            };
        }
    }
}
