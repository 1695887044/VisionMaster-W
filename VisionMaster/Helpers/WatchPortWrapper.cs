using Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using UI.Helper;
using VisionMaster.Models;

namespace VisionMaster.Helpers
{
    public class WatchPortWrapper : BindableBase, IDisposable
    {
        private readonly IPort _port;
        private readonly GlobalVariableModel _globalVar;

        public WatchItemModel OriginalConfig { get; }
        public string DisplayName { get; }
        public string Direction { get; } // "输入", "输出", "全局变量"
        public string TypeName { get; }

        private object _currentValue;
        public object CurrentValue
        {
            get => _currentValue;
            set
            {
                if (SetProperty(ref _currentValue, value))
                    RaisePropertyChanged(nameof(DisplayValue));
            }
        }

        public string DisplayValue => CurrentValue is Array arr ? $"Array [{arr.Length}]" : (CurrentValue?.ToString() ?? "Null");

        // 🎯 针对【算子引脚】的构造
        public WatchPortWrapper(WatchItemModel config, string portName, bool isInput, IPort port)
        {
            OriginalConfig = config;
            DisplayName = $"{config.StepName}.{portName}";
            Direction = isInput ? "输入" : "输出";
            TypeName = port.DataType?.Name ?? "未知";
            _port = port;

            CurrentValue = CloneHelper.ShallowCopy(_port.Value);
            if (_port != null) _port.ValueChanged += OnPortValueChanged;
        }

        // 🎯 针对【全局变量】的构造
        public WatchPortWrapper(WatchItemModel config, GlobalVariableModel gv)
        {
            OriginalConfig = config;
            DisplayName = gv.Name;
            Direction = "全局";
            TypeName = gv.DataType?.Name ?? "未知";
            _globalVar = gv;

            CurrentValue = CloneHelper.ShallowCopy(_globalVar.Value);
            if (_globalVar != null) _globalVar.ValueChanged += OnGlobalVarValueChanged;
        }

        private void OnPortValueChanged(object sender, EventArgs e)
        {
            Application.Current.Dispatcher.BeginInvoke(() => { CurrentValue = CloneHelper.ShallowCopy(_port.Value); });
        }

        private void OnGlobalVarValueChanged(object sender, EventArgs e)
        {
            Application.Current.Dispatcher.BeginInvoke(() => { CurrentValue = CloneHelper.ShallowCopy(_globalVar.Value); });
        }

        public void Dispose()
        {
            if (_port != null) _port.ValueChanged -= OnPortValueChanged;
            if (_globalVar != null) _globalVar.ValueChanged -= OnGlobalVarValueChanged;
        }
    }
}
