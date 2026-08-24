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
                logService.OnLogReceived += (log) => Application.Current.Dispatcher.Invoke(() => SystemLogs.Add(log));
                _logService.Success("软件加载成功");
            }
            
        }

    }
}
