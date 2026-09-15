using Core.Interfaces;
using System.Collections.ObjectModel;
using System.Windows;
using UI.Models;
using VisionMaster.Helpers;
using VisionMaster.Services;

namespace VisionMaster.ViewModels
{
    public class LogViewModel : BindableBase
    {
        public ObservableCollection<LogItem> SystemLogs { get; set; } = new();

        public LogViewModel(ILogService _logService)
        {
            if (_logService is LogService logService)
            {
                // 异步投递（fire-and-forget）：
                // ① 同步 Invoke 让每条日志的产出线程（流程/引擎）干等 UI，关闭软件时
                //    Dispatcher 取消挂起操作 → TaskCanceledException 沿调用栈抛回插件层崩溃；
                // ② SafeDispatch 在 Dispatcher 关门阶段静默丢弃——日志不丢（文件通道独立），只是不再刷 UI
                logService.OnLogReceived += (log) => SafeDispatch.BeginInvoke(() => SystemLogs.Add(log));
                _logService.Success("软件加载成功");
            }

        }

    }
}
