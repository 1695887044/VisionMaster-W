using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VisionMaster.Scada.Controls;

namespace VisionMaster.Views
{
    /// <summary>
    /// 图元工具箱视图：只干一件事——把某一条图元描述符"拿起来"。
    ///
    /// 放下不在这里。落点要按画布当时的缩放、平移和吸附换算，那是画布的私有知识，
    /// 工具箱去算等于把它不认识的东西复制了一份（见 <c>ScadaCanvas.ToDropOrigin</c>）。
    /// 两边只通过 <see cref="ScadaDrag"/> 这一个协议传递图元类型键。
    /// </summary>
    public partial class ScadaToolboxView : UserControl
    {
        /// <summary>按下的那一条（松手前一直是它；没按下则为 null）</summary>
        private ElementDescriptor? _pending;

        /// <summary>按下时的鼠标位置，用来量"拖出去多远才算起拖"</summary>
        private Point _mouseDown;

        public ScadaToolboxView()
        {
            InitializeComponent();
        }

        // 条目（DataTemplate 里的 Border）按下：只记意，不起拖。
        // 在按下里直接 DoDragDrop 会让单击变成拖拽，Expander 折叠、滚动这些操作就全废了。
        private void OnItemPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: ElementDescriptor descriptor })
            {
                _pending = descriptor;
                _mouseDown = e.GetPosition(null);
            }
        }

        // 移动挂在本视图而不是条目上：条目只有 28 高，鼠标在跨过阈值前就滑出条目区域的话，
        // 挂在条目上的处理器不再触发，表现得像"拖不出来"。挂根节点则整个面板内都能收到。
        private void OnPreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_pending is not { } descriptor) return;

            if (e.LeftButton != MouseButtonState.Pressed)
            {
                _pending = null;
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
            _pending = null;

            // Copy 而不是 Move：工具箱里的条目是注册表的一部分，不是可搬走的东西，
            // 光标上那个"+"也是"放下会新增一个、原来的还在"的即时反馈。
            DragDrop.DoDragDrop(this, ScadaDrag.CreatePayload(descriptor.TypeKey), DragDropEffects.Copy);
        }
    }
}
