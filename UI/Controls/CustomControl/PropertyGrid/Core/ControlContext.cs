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

        /// <summary>
        /// 本网格实例专属的 SharedSizeGroup 名（基于 Guid，避免不同属性网格之间
        /// 的标签列宽互相牵制）。布局处理器据此设置标签列的 SharedSizeGroup。
        /// </summary>
        public string SharedLabelGroup { get; set; } = "PropertyGridLabel";

        /// <summary>
        /// 是否是本分组内的最后一行。扁平式靠它去掉最后一行的底边线（由外框收口），
        /// 否则外框圆角内侧会多出一条压在描边上的分隔线。卡片式忽略此值。
        /// </summary>
        public bool IsLastRow { get; set; }

        /// <summary>
        /// 是否处于校验失败态（由 <see cref="ValidationProcessor"/> 置位）。
        /// 布局处理器在它**之前**执行，所以不靠它渲染 —— 校验器自己改容器底色。
        /// 留这个字段是给"外部处理器想在错误态下改布局"用的扩展点。
        /// </summary>
        public bool HasValidationError { get; set; }
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
