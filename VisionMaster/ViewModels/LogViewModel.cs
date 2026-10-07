using Core.Interfaces;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using UI.Models;
using VisionMaster.Helpers;
using VisionMaster.Services;

namespace VisionMaster.ViewModels
{
    /// <summary>
    /// 等级筛选下拉的一项：Text 给界面看（DisplayMemberPath），Value 给过滤用（null = 全部）。
    /// 用对象而不是裸枚举，是为了"全部"这一项能显示成中文又不引入值转换器 ——
    /// 与 ScadaAlarmHistoryView 的 StateFilters/SeverityFilters 同一套做法。
    /// </summary>
    public class LogLevelFilterOption
    {
        public string Text { get; set; } = string.Empty;
        public LogLevel? Value { get; set; }
    }

    public class LogViewModel : BindableBase
    {
        public ObservableCollection<LogItem> SystemLogs { get; set; } = new();

        /// <summary>日志面板的关键字过滤（绑 LogConsole.SearchText；控件侧有 200ms 防抖）</summary>
        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set => SetProperty(ref _searchText, value);
        }

        /// <summary>等级下拉的可选项（顺序即下拉顺序）</summary>
        public IReadOnlyList<LogLevelFilterOption> LevelOptions { get; } = new List<LogLevelFilterOption>
        {
            new LogLevelFilterOption { Text = "全部", Value = null },
            new LogLevelFilterOption { Text = "信息", Value = LogLevel.Info },
            new LogLevelFilterOption { Text = "成功", Value = LogLevel.Success },
            new LogLevelFilterOption { Text = "警告", Value = LogLevel.Warning },
            new LogLevelFilterOption { Text = "错误", Value = LogLevel.Error },
        };

        private LogLevelFilterOption _levelOption;
        public LogLevelFilterOption LevelOption
        {
            get => _levelOption;
            set
            {
                // 派生属性 FilterLevel 要跟着变，否则控件那边的依赖属性不会更新
                if (SetProperty(ref _levelOption, value))
                    RaisePropertyChanged(nameof(FilterLevel));
            }
        }

        /// <summary>当前等级过滤（"全部" = 不限等级），绑 LogConsole.FilterLevel</summary>
        public LogLevel? FilterLevel => _levelOption?.Value;

        /// <summary>是否自动跟随最新日志，绑 LogConsole.AutoScroll</summary>
        private bool _autoScroll = true;
        public bool AutoScroll
        {
            get => _autoScroll;
            set => SetProperty(ref _autoScroll, value);
        }

        public LogViewModel(ILogService _logService)
        {
            _levelOption = LevelOptions[0];   // 默认「全部」

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
