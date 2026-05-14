using Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using UI.Helper;
using VisionMaster.Models;

namespace VisionMaster.ViewModels
{
    class ModuleOutputViewModel
    {
    }
    public class SearchSuggestion
    {
        public string DisplayText { get; set; } // UI 显示文字 (如: Match_1 [全部引脚])
        public Guid StepId { get; set; }
        public string StepName { get; set; }
        public string PortName { get; set; }
        public bool IsInput { get; set; }
    }
    public class WatchPortWrapper : BindableBase, IDisposable
    {
        private readonly IPort _port;
        public WatchItemModel OriginalConfig { get; } // 关联原始的序列化配置

        public string DisplayName { get; }
        public string Direction { get; } // "输入" 或 "输出"
        public string TypeName { get; }

        public object CurrentValue
        {
            get => field;
            set
            {
                if (SetProperty(ref field, value))
                    RaisePropertyChanged(nameof(DisplayValue));
            }
        }

        public string DisplayValue => CurrentValue is Array arr ? $"Array [{arr.Length}]" : (CurrentValue?.ToString() ?? "Null");

        public WatchPortWrapper(WatchItemModel config, string portName, bool isInput, IPort port)
        {
            OriginalConfig = config;
            DisplayName = $"{config.StepName}.{portName}";
            Direction = isInput ? "输入" : "输出";
            TypeName = port.DataType?.Name ?? "未知";
            _port = port;

            // 初始化当前值
            CurrentValue = CloneHelper.ShallowCopy(_port.Value);

            // 🌟🌟 核心：订阅算子引脚的变动事件！彻底与引擎解耦！
            if (_port != null)
            {
                _port.ValueChanged += OnPortValueChanged;
            }
        }

        private void OnPortValueChanged(object sender, EventArgs e)
        {
            // 算子在后台线程跑，事件也在后台触发，必须切回主线程刷新 UI！
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                CurrentValue = CloneHelper.ShallowCopy(_port.Value);
            });
        }

        // 当用户在界面上删除了这个监控项，必须注销事件防止内存泄漏
        public void Dispose()
        {
            if (_port != null)
                _port.ValueChanged -= OnPortValueChanged;
        }
    }
}
