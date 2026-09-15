﻿﻿using Core.Interfaces;
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
    /// 监视项值格式化器接口（扩展点）：
    /// 不同数据类型可注册专用格式化器（如 HImage 显示"尺寸+像素格式"、HRegion 显示"面积+包围盒"），
    /// 未命中注册类型时回退默认 ToString / Array 摘要
    /// </summary>
    public interface IWatchValueFormatter
    {
        /// <summary>
        /// 是否能处理该值（按类型或内容判断）
        /// </summary>
        bool CanFormat(object value);

        /// <summary>
        /// 格式化为显示文本
        /// </summary>
        string Format(object value);
    }

    /// <summary>
    /// 值类型数组格式化器（内置）：展开元素值显示，超出截断。
    /// 例：double[4] → [12.5, 45.2, 88.9, 120]；元素为对象/嵌套数组时回退默认摘要。
    /// 复杂类型可注册自定义 IWatchValueFormatter 覆盖（如 HImage 显示尺寸）。
    /// </summary>
    public class ArrayWatchValueFormatter : IWatchValueFormatter
    {
        private const int MaxElements = 16;

        public bool CanFormat(object value)
        {
            if (value is not Array arr || arr.Rank != 1) return false;
            var elementType = arr.GetType().GetElementType();
            return elementType != null
                && (elementType.IsPrimitive
                    || elementType == typeof(string)
                    || elementType == typeof(decimal)
                    || elementType.IsEnum);
        }

        public string Format(object value)
        {
            var arr = (Array)value;
            if (arr.Length == 0) return "[]";

            var sb = new StringBuilder("[");
            int shown = Math.Min(arr.Length, MaxElements);
            for (int i = 0; i < shown; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(FormatElement(arr.GetValue(i)));
            }
            if (arr.Length > shown)
                sb.Append($", …共{arr.Length}个");
            sb.Append(']');
            return sb.ToString();
        }

        private static string FormatElement(object element)
        {
            if (element == null) return "null";
            // 浮点截断小数位，避免长尾占满行宽
            if (element is double d) return d.ToString("G6");
            if (element is float f) return f.ToString("G5");
            return element.ToString();
        }
    }

    /// <summary>
    /// 监视端口包装器
    /// 用于绑定和监视端口或全局变量的值变化；
    /// 支持失效状态（监视目标不存在时变色提示）与值显示格式化扩展
    /// </summary>
    public class WatchPortWrapper : BindableBase, IDisposable
    {
        /// <summary>
        /// 值格式化器注册表（静态扩展点：新类型只需注册 IWatchValueFormatter 实现）
        /// </summary>
        private static readonly List<IWatchValueFormatter> Formatters = new();

        static WatchPortWrapper()
        {
            // 内置：值类型数组展开元素显示（int[]/double[]/string[]/bool[] 等）
            RegisterFormatter(new ArrayWatchValueFormatter());
        }

        /// <summary>
        /// 注册自定义值格式化器（程序启动或插件加载时调用）
        /// </summary>
        public static void RegisterFormatter(IWatchValueFormatter formatter)
        {
            if (formatter != null && !Formatters.Contains(formatter))
                Formatters.Insert(0, formatter); // 后注册优先（允许覆盖内置行为）
        }

        private readonly IPort _port;
        private readonly IVariable _globalVar;

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

        private bool _isMissing;
        /// <summary>
        /// 监视目标是否失效（变量/算子/端口不存在，如方案切换后、步骤被删后）；
        /// UI 据此变色提示，失效项保留在列表中便于用户清理或等待重新编译
        /// </summary>
        public bool IsMissing
        {
            get => _isMissing;
            private set
            {
                if (SetProperty(ref _isMissing, value))
                {
                    RaisePropertyChanged(nameof(DisplayValue));
                    RaisePropertyChanged(nameof(StatusTooltip));
                }
            }
        }

        /// <summary>
        /// 失效提示文本（ToolTip 显示：为什么失效）
        /// </summary>
        public string StatusTooltip => IsMissing ? "监视目标不存在（变量或算子端口已被删除/更名，重新编译或移除此项）" : null;

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
        /// 显示值（格式化后）：失效项显示占位符；数组显示长度摘要；其余走格式化器注册表
        /// </summary>
        public string DisplayValue
        {
            get
            {
                if (IsMissing) return "— 目标不存在 —";
                return FormatValue(CurrentValue);
            }
        }

        /// <summary>
        /// 值格式化：先查注册表，未命中回退内置规则（数组摘要 / ToString / Null）
        /// </summary>
        private static string FormatValue(object value)
        {
            if (value == null) return "Null";
            foreach (var f in Formatters)
            {
                try
                {
                    if (f.CanFormat(value)) return f.Format(value);
                }
                catch { /* 格式化器异常不阻断显示，回退默认 */ }
            }
            if (value is Array arr) return $"Array [{arr.Length}]";
            var s = value.ToString();
            return string.IsNullOrEmpty(s) ? value.GetType().Name : s;
        }

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
        public WatchPortWrapper(WatchItemModel config, IVariable gv)
        {
            OriginalConfig = config;
            DisplayName = gv.Name;
            Direction = "全局";
            TypeName = gv.DataType?.Name ?? "未知";
            _globalVar = gv;

            CurrentValue = CloneHelper.ShallowCopy(gv.Value);
            if (_globalVar != null) _globalVar.ValueChanged += OnGlobalVarValueChanged;
        }

        /// <summary>
        /// 失效项构造器：目标不存在时使用（无事件可订阅，保留条目供 UI 变色提示）
        /// </summary>
        public WatchPortWrapper(WatchItemModel config, string displayName, string direction, string typeName)
        {
            OriginalConfig = config;
            DisplayName = displayName;
            Direction = direction;
            TypeName = typeName ?? "未知";
            IsMissing = true;
        }

        /// <summary>
        /// 端口值变更处理
        /// SafeDispatch 封送：事件多在流程线程触发，裸用 Application.Current 在关闭瞬间为 null 会 NRE，
        /// BeginInvoke 排队的操作也可能在 Dispatcher 关机时被取消
        /// </summary>
        private void OnPortValueChanged(object sender, EventArgs e)
        {
            SafeDispatch.BeginInvoke(() => { CurrentValue = CloneHelper.ShallowCopy(_port.Value); });
        }

        /// <summary>
        /// 全局变量值变更处理
        /// </summary>
        private void OnGlobalVarValueChanged(object sender, EventArgs e)
        {
            SafeDispatch.BeginInvoke(() => { CurrentValue = CloneHelper.ShallowCopy(_globalVar.Value); });
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
