﻿﻿﻿﻿﻿﻿﻿using Core.Interfaces;
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
    /// <summary>
    /// 监视端口包装器
    /// 用于绑定和监视端口或全局变量的值变化
    /// </summary>
    public class WatchPortWrapper : BindableBase, IDisposable
    {
        private readonly IPort _port;
        private readonly GlobalVariableModel _globalVar;

        /// <summary>
        /// 原始配置
        /// </summary>
        public WatchItemModel OriginalConfig { get; }

        /// <summary>
        /// 显示名称
        /// </summary>
        public string DisplayName { get; }

        /// <summary>
        /// 方向（输入/输出/全局变量）
        /// </summary>
        public string Direction { get; }

        /// <summary>
        /// 类型名称
        /// </summary>
        public string TypeName { get; }

        private object _currentValue;
        /// <summary>
        /// 当前值
        /// </summary>
        public object CurrentValue
        {
            get => _currentValue;
            set
            {
                if (SetProperty(ref _currentValue, value))
                    RaisePropertyChanged(nameof(DisplayValue));
            }
        }

        /// <summary>
        /// 显示值（格式化后）
        /// </summary>
        public string DisplayValue => CurrentValue is Array arr ? $"Array [{arr.Length}]" : (CurrentValue?.ToString() ?? "Null");

        /// <summary>
        /// 创建算子引脚监视包装器
        /// </summary>
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

        /// <summary>
        /// 创建全局变量监视包装器
        /// </summary>
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

        /// <summary>
        /// 端口值变更处理
        /// </summary>
        private void OnPortValueChanged(object sender, EventArgs e)
        {
            Application.Current.Dispatcher.BeginInvoke(() => { CurrentValue = CloneHelper.ShallowCopy(_port.Value); });
        }

        /// <summary>
        /// 全局变量值变更处理
        /// </summary>
        private void OnGlobalVarValueChanged(object sender, EventArgs e)
        {
            Application.Current.Dispatcher.BeginInvoke(() => { CurrentValue = CloneHelper.ShallowCopy(_globalVar.Value); });
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (_port != null) _port.ValueChanged -= OnPortValueChanged;
            if (_globalVar != null) _globalVar.ValueChanged -= OnGlobalVarValueChanged;
        }
    }
}
