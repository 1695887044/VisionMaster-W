using System;
using System.Windows;
using System.Windows.Threading;

namespace VisionMaster.Helpers
{
    /// <summary>
    /// Dispatcher 安全封送工具（放 Core 层：引擎侧 WatchPortWrapper 与 UI 侧共用一份）。
    /// 治疗"关闭软件时后台线程还在往 UI 派发"的竞态：WPF 关闭中 Dispatcher 会取消
    /// 所有挂起操作，同步 Invoke 把 TaskCanceledException 从调用方栈头一路抛回引擎/插件层
    /// （表现为运行中关闭软件崩溃）。
    /// 原则：关闭阶段的 UI 更新已无受众，静默丢弃即可；日志等数据不会丢（文件通道独立于 UI）。
    /// </summary>
    public static class SafeDispatch
    {
        /// <summary>
        /// 异步投递到 UI 线程（fire-and-forget，不阻塞流程/引擎线程）。
        /// 仅用于"只想更新界面、不要求返回值"的场景：日志追加、树刷新、监视值同步等。
        /// </summary>
        public static void BeginInvoke(Action action)
        {
            if (action == null) return;

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null
                || dispatcher.HasShutdownStarted
                || dispatcher.HasShutdownFinished)
                return; // 关门阶段：不再投递，调用方零异常

            try
            {
                dispatcher.BeginInvoke(action, DispatcherPriority.Normal);
            }
            catch (Exception)
            {
                // 检查与投递之间的极小竞态窗口（Dispatcher 恰好开始关闭）：
                // 这次更新本就做给一个正在消失的界面，丢弃是唯一正确答案，绝不让异常穿透回引擎层
            }
        }
    }
}
