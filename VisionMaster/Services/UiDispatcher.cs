using System;
using System.Windows;
using System.Windows.Threading;

namespace VisionMaster.Services
{
    /// <summary>
    /// 回 UI 线程的**唯一**入口。
    ///
    /// 为什么必须有它：运动卡的设备事件（<c>MotionProvider.DevicesChanged</c>、
    /// <c>IMotionDevice.StateChanged</c> / <c>FaultOccurred</c>）都在**非 UI 线程**触发
    /// （命令线程 / 轮询线程）。订阅方若在事件里直接改 <c>ObservableCollection</c>
    /// 或发属性通知，WPF 会抛"集合正在被另一个线程修改"这类异常，而且时机随机 ——
    /// 现场表现为"偶尔崩一下"。
    ///
    /// 之前只有运动卡合并窗口做了调度，旧的设置弹窗没有，于是"同一个事件、两条路径、一套会崩"。
    /// 统一到这里，订阅方不必各自记得写调度。
    /// </summary>
    public static class UiDispatcher
    {
        /// <summary>
        /// 把动作投到 UI 线程执行。已在 UI 线程则直接执行（避免不必要的异步延迟）；
        /// 拿不到 Dispatcher（如单元测试宿主）时同步兜底执行。
        /// </summary>
        public static void Post(Action action)
        {
            if (action == null) return;

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
            {
                action();
                return;
            }

            if (dispatcher.CheckAccess()) action();
            else dispatcher.BeginInvoke(action);
        }
    }
}
