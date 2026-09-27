using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Mvvm;

namespace VisionMaster.ViewModels
{
    /// <summary>
    /// 画布上的一根模块级连线：生产方模块 → 消费方模块。
    ///
    /// 与旧端口级连线（CanvasConnectionViewModel：Input/Output 各挂一个端口脚）的本质区别：
    /// 画布上一根线只表达「这两个模块之间存在数据依赖」，具体绑了哪几对端口聚合在
    /// <see cref="Bindings"/> 里，编辑入口在变量绑定弹窗。这样动态输出端口重建、
    /// 端口数量膨胀都不再牵动画布几何。
    ///
    /// 真正的语义仍存放在消费方 StepModel.LinkedSources 里（LinkReference 含端口名），
    /// 本类是渲染投影，不持有第二份真相——Bindings 只是显示用字符串。
    /// </summary>
    public class CanvasLinkViewModel : BindableBase
    {
        public CanvasLinkViewModel(CanvasNodeViewModel source, CanvasNodeViewModel target)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Target = target ?? throw new ArgumentNullException(nameof(target));
        }

        /// <summary>生产方模块（输出脚）</summary>
        public CanvasNodeViewModel Source { get; }

        /// <summary>消费方模块（输入脚）</summary>
        public CanvasNodeViewModel Target { get; }

        /// <summary>这根线承载的端口绑定（与 LinkedSources 同序），右键菜单逐条解绑</summary>
        public List<CanvasLinkBinding> Bindings { get; } = new();

        /// <summary>
        /// 几何端点：由主 VM 按两端节点的 Location 与尺寸直接算出（确定性，不依赖
        /// Connector 控件的锚点回报时机——那个时机在"加节点触发重排"时会拿旧位置）。
        /// 源 = 生产方盒子右缘中点；目标 = 消费方盒子左缘中点。
        /// </summary>
        public System.Windows.Point SourceAnchor
        {
            get => field;
            set => SetProperty(ref field, value);
        }

        public System.Windows.Point TargetAnchor
        {
            get => field;
            set => SetProperty(ref field, value);
        }

        /// <summary>绑定条数角标文本；1 条时不画角标（一根线一对绑定不用解释）</summary>
        public string BindingCountLabel => Bindings.Count > 1 ? $"×{Bindings.Count}" : string.Empty;

        /// <summary>
        /// 解绑单条端口绑定。命令本体在主 VM（要写撤销栈），这里挂个入口
        /// 供连线右键菜单直达——菜单的 DataContext 是本类，够不到 UserControl 资源树。
        /// </summary>
        public DelegateCommand<CanvasLinkBinding?> UnbindCommand { get; set; }

        /// <summary>悬停提示：顺序链显示执行次序；数据线逐行列出承载的绑定，附非法原因</summary>
        public string Tooltip
        {
            get
            {
                if (IsOrderLink)
                    return $"执行顺序：{Source.Header} → {Target.Header}";

                var lines = Bindings.Select(b => $"· {b.Display}");
                return IsIllegal
                    ? string.Join(Environment.NewLine, lines) + Environment.NewLine + $"⚠ {WarningText}"
                    : string.Join(Environment.NewLine, lines);
            }
        }

        /// <summary>
        /// 执行顺序链（true）还是数据依赖线（false，默认）。
        /// 顺序链是"同一集合里相邻两步"的纯视图投影（自上而下带箭头虚线），
        /// 表达"先跑谁后跑谁"；数据线表达"谁喂谁数据"。两者并存、视觉区分。
        /// </summary>
        public bool IsOrderLink
        {
            get => field;
            set
            {
                if (SetProperty(ref field, value))
                    RaisePropertyChanged(nameof(Tooltip));
            }
        }

        /// <summary>
        /// 结构非法：生产方排在消费方之后、跨分支取数、或外层容器晚于消费方。
        /// 图纸改序（流程栏/撤销）会把合法线变非法——这类线画成红色虚线，
        /// 编译期由 FlowCompiler.CheckLinkOrder 报致命错，画布与编译器共用 FlowTopology 判定。
        /// </summary>
        public bool IsIllegal
        {
            get => field;
            set
            {
                if (SetProperty(ref field, value))
                    RaisePropertyChanged(nameof(Tooltip));
            }
        }

        /// <summary>非法原因（供提示；多条绑定时取第一条非法原因）</summary>
        public string? WarningText
        {
            get => field;
            set
            {
                if (SetProperty(ref field, value))
                    RaisePropertyChanged(nameof(Tooltip));
            }
        }

        /// <summary>Bindings 是普通 List（每轮重建整体换内容），聚合完成后由主 VM 调用统一补通知</summary>
        public void NotifyBindingsChanged()
        {
            RaisePropertyChanged(nameof(BindingCountLabel));
            RaisePropertyChanged(nameof(Tooltip));
        }
    }
}
