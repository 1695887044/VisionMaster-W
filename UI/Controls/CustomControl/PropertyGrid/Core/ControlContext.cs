using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace UI.CustomControl.PropertyGrid
{
    /// <summary>
    /// 控件在流水线中流转的上下文对象
    /// </summary>
    public class ControlContext
    {
        public PropertyInfo Property { get; set; } = null!;
        public object BindingSource { get; set; } = null!;
        public FrameworkElement Control { get; set; } = null!;
        public Panel WrapPanel { get; set; } = null!;
        public Grid RootCellGrid { get; set; } = null!;

        /// <summary>
        /// 事件清理注册器，防止 UI 刷新引发内存泄漏
        /// </summary>
        public Action<Action> RegisterCleanup { get; set; } = _ => { };
    }

    /// <summary>
    /// 控件生成器接口
    /// </summary>
    public interface IControlGenerator
    {
        int Priority { get; }
        bool CanProcess(PropertyInfo prop, Type targetType, bool isReadOnly = false);
        FrameworkElement Create(PropertyInfo prop, object bindingSource, bool isReadOnly = false);
    }

    /// <summary>
    /// 控件拦截/处理器接口
    /// </summary>
    public interface IControlProcessor
    {
        void Execute(ControlContext context);
    }
}
