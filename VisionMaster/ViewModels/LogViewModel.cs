using Core.Interfaces;
using System.Collections.ObjectModel;
using System.Windows;
using UI.Models;
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
                // 空保护：启动阶段（设计器/异常启动路径）Application.Current 可能为 null，直接 Invoke 会崩溃
                logService.OnLogReceived += (log) => Application.Current?.Dispatcher?.Invoke(() => SystemLogs.Add(log));
                _logService.Success("软件加载成功");
            }

        }

    }
}
