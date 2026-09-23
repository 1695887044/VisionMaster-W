using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VisionMaster.Scada.Controls;
using VisionMaster.Services;
using VisionMaster.ViewModels;

namespace VisionMaster.Views
{
    /// <summary>
    /// 图元工具箱视图：干两件事——把某一条图元描述符"拿起来"，或把某一份模板"拿起来"。
    ///
    /// 放下不在这里。落点要按画布当时的缩放、平移和吸附换算，那是画布的私有知识，
    /// 工具箱去算等于把它不认识的东西复制了一份（见 <c>ScadaCanvas.ToDropOrigin</c>）。
    /// 两边只通过 <see cref="ScadaDrag"/> 这一个协议传递"要放什么"：图元传类型键，模板传模板 Id。
    ///
    /// 另外负责模板库通知的<b>入树挂、离树摘</b>（见 <see cref="ScadaToolboxViewModel.Attach"/>）：
    /// 订阅做在视图上是因为视图的生命周期有明确的起点终点，而视图模型的生命周期跟着视图走，
    /// 挂在这里才能保证"离树一定摘干净"。
    /// </summary>
    public partial class ScadaToolboxView : UserControl
    {
        /// <summary>按下的那一条图元（松手前一直是它；没按下则为 null）</summary>
        private ElementDescriptor? _pending;

        /// <summary>按下的那一行模板（与 <see cref="_pending"/> 互斥：一次按下只可能是其中一种）</summary>
        private ScadaTemplateInfo? _pendingTemplate;

        /// <summary>按下时的鼠标位置，用来量"拖出去多远才算起拖"</summary>
        private Point _mouseDown;

        public ScadaToolboxView()
        {
            InitializeComponent();

            // 入树/离树成对挂摘订阅（模板库变更）：AvalonDock 切标签、隐藏面板都会触发 Unloaded，
            // 所以清理必须可逆（离树摘干净让旧 VM 可被 GC，入树重新挂上），不能用一次性 Dispose。
            Loaded += OnViewLoaded;
            Unloaded += OnViewUnloaded;
        }

        // Prism 的 AutoWireViewModel 可能晚于 Loaded 才把 VM 装进 DataContext，
        // 此时入树事件已经过去，需要在这里补挂一次（Attach 幂等，不会重复订阅）
        private void OnViewDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsLoaded)
                (e.NewValue as ScadaToolboxViewModel)?.Attach();
        }

        private void OnViewLoaded(object sender, RoutedEventArgs e)
            => (DataContext as ScadaToolboxViewModel)?.Attach();

        private void OnViewUnloaded(object sender, RoutedEventArgs e)
            => (DataContext as ScadaToolboxViewModel)?.Detach();

        // 图元条目（DataTemplate 里的 Border）按下：只记意，不起拖。
        // 在按下里直接 DoDragDrop 会让单击变成拖拽，Expander 折叠、滚动这些操作就全废了。
        private void OnItemPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: ElementDescriptor descriptor })
            {
                _pending = descriptor;
                _pendingTemplate = null;
                _mouseDown = e.GetPosition(null);
            }
        }

        // 模板行按下：与图元条目同款，只是记的是"哪一份模板"。
        // 两件事分开两个处理器而不是合成一个按类型分派的：它们取的是不同的字段、
        // 拖的是不同的负载，合成一个反而要在这里判两次类型。
        private void OnTemplatePreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: ScadaTemplateInfo template })
            {
                _pendingTemplate = template;
                _pending = null;
                _mouseDown = e.GetPosition(null);
            }
        }

        // 移动挂在本视图而不是条目上：条目只有 28 高，鼠标在跨过阈值前就滑出条目区域的话，
        // 挂在条目上的处理器不再触发，表现得像"拖不出来"。挂根节点则整个面板内都能收到。
        private void OnPreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_pending == null && _pendingTemplate == null) return;

            if (e.LeftButton != MouseButtonState.Pressed)
            {
                ClearPending();
                return;
            }

            Vector delta = e.GetPosition(null) - _mouseDown;
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            // 一次按下只起一次拖：DoDragDrop 是模态的，返回后鼠标多半还按着，
            // 不清掉就会在同一次按下里被反复拉起拖拽。
            ElementDescriptor? descriptor = _pending;
            ScadaTemplateInfo? template = _pendingTemplate;
            ClearPending();

            // Copy 而不是 Move：工具箱里的条目是注册表的一部分、模板是库里的常驻内容，
            // 都不是可搬走的东西，光标上那个"+"也是"放下会新增一个、原来的还在"的即时反馈。
            //
            // 两种负载走两条路：模板拖的是"库里的哪一份内容"，画布那头要按它去取快照再物化，
            // 与"造一个空壳图元"完全是两件事（见 ScadaDrag.TemplateIdFormat 的注释）。
            DataObject payload = template != null
                ? ScadaDrag.CreateTemplatePayload(template.TemplateId)
                : ScadaDrag.CreatePayload(descriptor!.TypeKey);

            DragDrop.DoDragDrop(this, payload, DragDropEffects.Copy);
        }

        private void ClearPending()
        {
            _pending = null;
            _pendingTemplate = null;
        }
    }
}
