using System.ComponentModel;
using System.Windows;

namespace VisionMaster.Views
{
    /// <summary>
    /// 调试命中窗（DWV 第 1 期，非模态）。
    ///
    /// 【为什么非模态】命中只是"到点了，来看一眼停在哪"——不能阻塞主界面：
    /// 用户要能顺手在流程栏/画布/F9 调断点、切到参数窗看一眼，再回来继续。
    /// 弹模态窗会把"看参数 → 继续 → 再跑"这条链断成"关窗再点"。
    ///
    /// 【为什么点 X 只隐藏】窗口是单实例（ShellViewModel 持引用，点 X 仅隐藏，下次命中复用再现），
    /// 避免每次命中新建窗口的句柄/位置抖动；真正的关闭只发生在应用退出链上
    /// （此刻 Dispatcher 已开始关闭，不再取消，放行销毁）。
    /// </summary>
    public partial class DebugHitWindow : Window
    {
        public DebugHitWindow()
        {
            InitializeComponent();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            // 点 X = 仅本次隐藏（下次命中再现）；应用退出链（Dispatcher 已开始关闭）放行真正关闭
            if (Application.Current?.Dispatcher?.HasShutdownStarted != true)
            {
                e.Cancel = true;
                Hide();
            }

            base.OnClosing(e);
        }
    }
}
