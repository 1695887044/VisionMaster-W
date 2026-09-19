using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// <see cref="ScadaCanvas"/> 的交互部分：选中、拖动移动、四角改尺寸、滚轮缩放、中键平移。
    ///
    /// 单独拆一个 partial 文件的原因：主文件负责"模型 → 可视树"的单向同步（被动渲染），
    /// 这里负责"鼠标 → 模型"的反向写入（主动编辑）。两者的生命周期、变更频率、出错方式完全不同，
    /// 混在一起会让"谁改写了落盘数据"这件事变得难以审计。
    ///
    /// 有一条纪律贯穿本文件：<b>所有编辑都写在 <see cref="ScadaElement"/> 上，绝不直接动控件</b>。
    /// 控件的几何由 <see cref="ScadaElementBase.ApplyGeometry"/> 从模型落地，
    /// 所以拖动时只改 Element.X/Y，画面自己会跟上来（S2 定的单向数据流在这里兑现）。
    /// 好处是编辑器写数据只有这一个出口，将来加撤销栈、加协同、加"脏标记"都只拦这一条路。
    ///
    /// 事件选取说明：编辑态的左键按下走 <see cref="OnPreviewMouseLeftButtonDown"/>（隧道事件，
    /// 先于子控件），而不是 <c>OnMouseLeftButtonDown</c>（冒泡事件）。原因是内置的按钮图元内部是
    /// <c>ButtonBase</c>，它在 MouseLeftButtonDown 上就把事件标成 Handled 并抓走鼠标；
    /// 等冒泡到画布时事件已经"没了"，编辑态点按钮只会按下、选不中。
    /// 运行态（<see cref="IsReadOnly"/>）则一个鼠标都不抢，图元自己的交互才完整。
    /// </summary>
    public partial class ScadaCanvas
    {
        /// <summary>滚轮一格的变化量（1.1 倍，缩到 10% 与放大到 8 倍各约 24 格，手感合适）</summary>
        private const double ZoomStep = 1.1;

        private enum DragMode
        {
            /// <summary>没在拖</summary>
            None,

            /// <summary>左键按住图元移动</summary>
            Move,

            /// <summary>左键按住选中手柄改尺寸</summary>
            Resize,

            /// <summary>中键平移取景器</summary>
            Pan,
        }

        private DragMode _dragMode;
        private ScadaElement? _dragElement;
        private Point _dragStartViewport;
        private Point _dragStartModel;
        private Rect _dragStartBounds;
        private double _dragStartRotation;
        private Vector _resizeSign;
        private Point _panStartOffset;

        /// <summary>
        /// 运行态里"按下"落在哪个图元上（只给"释放"用，见 <see cref="OnPreviewMouseLeftButtonUp"/>）。
        /// 编辑态恒为 null：那条路上根本不该有组态事件。
        /// </summary>
        private ScadaElement? _pressedElement;

        #region 左键：选中 / 移动 / 改尺寸

        protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseLeftButtonDown(e);

            if (IsReadOnly)
            {
                // 运行态：一个鼠标事件都不抢（图元自己的交互要完整），但要把"按下了哪个图元"
                // 报给那个图元的控件，让它发出组态事件 Pressed。往上的执行判断全在运行态会话里，
                // 画布只负责"命中了谁"这一件它自己知道的事。
                _pressedElement = HitTestElement(e.OriginalSource as DependencyObject);

                RaiseElementEvent(_pressedElement, ScadaEventType.Pressed);

                return;
            }

            if (e.Handled)
                return;

            var source = e.OriginalSource as DependencyObject;
            var point = e.GetPosition(this);

            // 手柄优先：选中框叠在图元之上，先判是不是按在了角上
            if (HitTestThumb(source) is { } thumb && SelectedElement is { } selected && CanEditElement(selected))
            {
                BeginDrag(DragMode.Resize, selected, point);
                _resizeSign = (Vector)thumb.Tag!;
                e.Handled = true;
                return;
            }

            var element = HitTestElement(source);

            if (element != null)
            {
                if (!ReferenceEquals(SelectedElement, element))
                    SetCurrentValue(SelectedElementProperty, element);

                if (CanEditElement(element))
                {
                    BeginDrag(DragMode.Move, element, point);
                    e.Handled = true; // 只有"要拖动"才吞掉；锁住的图元放行事件，让图元自己的交互照旧
                }
                else
                {
                    UpdateCursorState();
                }

                // 编辑期聚焦画布，键盘删除/方向键微调（宿主实现）才有落点
                if (!IsKeyboardFocused)
                    Focus();

                return;
            }

            // 空白处只取消选中，不做框选：SelectedElement 是单个对象，
            // 多选要动的是领域层（选中集合 + 每个图元的独立锚点），不是在这里塞一个临时列表。
            if (SelectedElement != null)
                SetCurrentValue(SelectedElementProperty, null);
        }

        protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseLeftButtonUp(e);

            if (!IsReadOnly)
                return;

            // 运行态：同上，一个都不抢，只把"释放"报给图元。
            // 走 Preview（隧道）而不是 MouseLeftButtonUp（冒泡）是必须的：内置按钮图元内部是
            // ButtonBase，它会在冒泡阶段把 MouseLeftButtonUp 标成 Handled 并吃掉 Click，
            // 那时画布再也收不到——事件就哑在最后一层了。
            var released = HitTestElement(e.OriginalSource as DependencyObject);

            // 按下与释放必须是<b>同一个对象</b>才算 Released：
            // 在 A 上按下、滑到 B 上松手，用户的手感是"这一下没成"。商用组态里按钮点下去拖开就该取消，
            // 这里如果照发 Released，就会得到"人已经移开了，机器还是启动了"——现场是不可接受的。
            if (released != null && ReferenceEquals(released, _pressedElement))
                RaiseElementEvent(released, ScadaEventType.Released);

            _pressedElement = null; // 一次按下只配一次释放，判成判败都清账，免得下次误配
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);

            if (_dragMode is DragMode.Move or DragMode.Resize)
                EndDrag();
        }

        /// <summary>
        /// 把"哪个图元发生了哪个组态事件"交给那个图元的控件，由控件往上冒泡。
        ///
        /// 画布停在这一步就收手，不往 <see cref="ScadaRuntime"/> 递话：画布不知道自己挂在哪个会话上
        /// （同一个控件既可能在设计器里、也可能在运行窗口里），硬要递就得让画布认识运行态——依赖方向就翻了。
        /// 冒泡到运行窗口之后，由窗口拿 Source 反查模型再交给会话判定（见 ScadaRuntimeWindow）。
        /// </summary>
        private void RaiseElementEvent(ScadaElement? element, ScadaEventType eventType)
        {
            if (element == null)
                return;

            if (FindContainer(element) is { } control)
                control.RaiseScadaEvent(eventType);
        }

        /// <summary>
        /// 作废那次"按下但还没释放"的记账（退出运行态、切换画面时调用）。
        ///
        /// 严格讲不补这一刀也不会出错：图元的控件都没了，<see cref="HitTestElement"/> 自然命中不到，
        /// 配不上对就不会发出一次凭空的 Released。留着只会让画布钉住一个别的画面的模型对象，
        /// 而"谁还引用着谁"这种事在编辑器里查泄漏时最费时间，顺手清掉比事后解释便宜。
        /// </summary>
        private void CancelPendingPress() => _pressedElement = null;

        private void BeginDrag(DragMode mode, ScadaElement element, Point viewportPoint)
        {
            _dragMode = mode;
            _dragElement = element;
            _dragStartViewport = viewportPoint;
            _dragStartModel = ToModelPoint(viewportPoint);
            _dragStartBounds = new Rect(element.X, element.Y, element.Width, element.Height);
            _dragStartRotation = element.Rotation;

            CaptureMouse(); // 鼠标拖出画布外也要继续（松手在别处也得收到 Up）
            UpdateCursorState();
        }

        private void EndDrag()
        {
            // 先置空模式再释放捕获：ReleaseMouseCapture 会回调 OnLostMouseCapture，
            // 顺序反了就是自己调自己一次（无害，但会把"到底谁结束了拖动"搅浑）。
            _dragMode = DragMode.None;
            _dragElement = null;
            _resizeSign = default;

            ReleaseMouseCapture();
            UpdateCursorState();
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);

            // 别的控件强抢了鼠标（例如宿主弹出菜单）时，拖动必须就地结束，
            // 否则会留下一个"没按下却在跟鼠标"的半截状态。
            if (_dragMode != DragMode.None && !IsMouseCaptured)
            {
                _dragMode = DragMode.None;
                _dragElement = null;
                UpdateCursorState();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (_dragMode == DragMode.None)
                return;

            var point = e.GetPosition(this);

            switch (_dragMode)
            {
                case DragMode.Pan:
                    // 平移量就是视口位移，不参与缩放换算（Offset 本身是屏幕像素）
                    SetCurrentValue(OffsetProperty, new Point(
                        _panStartOffset.X + point.X - _dragStartViewport.X,
                        _panStartOffset.Y + point.Y - _dragStartViewport.Y));
                    break;

                case DragMode.Move:
                    ApplyMove(point);
                    break;

                case DragMode.Resize:
                    ApplyResize(point);
                    break;
            }
        }

        private void ApplyMove(Point viewportPoint)
        {
            if (_dragElement is not { } element)
                return;

            var current = ToModelPoint(viewportPoint);

            double left = _dragStartBounds.X + (current.X - _dragStartModel.X);
            double top = _dragStartBounds.Y + (current.Y - _dragStartModel.Y);

            if (SnapToGrid)
            {
                // 吸附"绝对位置"而不是"相对位移"：这样不管从哪儿按下，落点都必定压在整张网格上。
                // 按位移吸附会让每个图元各自偏移半格，一屏设备永远对不齐。
                double step = GridStep;
                if (step > 0)
                {
                    left = Math.Round(left / step) * step;
                    top = Math.Round(top / step) * step;
                }
            }

            // 同值不写：ScadaElement 的 setter 自带同值不通知，但这里挡住还能省掉每次鼠标移动的
            // 一次依赖属性调度（鼠标事件频率远高于渲染频率）。
            if (Math.Abs(element.X - left) > 1e-6)
                element.X = left;

            if (Math.Abs(element.Y - top) > 1e-6)
                element.Y = top;
        }

        /// <summary>
        /// 四角改尺寸。难点是<b>旋转过的图元</b>：屏幕上向右拖右下角，对转了 90° 的图元来说是"变矮"。
        /// 所以位移要先转到图元的局部坐标系（乘 R(−θ)）算出新宽高，再把"被拖的角相对中心的位移"
        /// 转回设计坐标去挪 X/Y——两步用的是同一个 θ，只是方向相反。
        /// 对心点固定的是<b>对角</b>（被拖的角在动，另一头钉住），这和所有图形编辑器的手感一致。
        /// </summary>
        private void ApplyResize(Point viewportPoint)
        {
            if (_dragElement is not { } element)
                return;

            var current = ToModelPoint(viewportPoint);

            double dx = current.X - _dragStartModel.X;
            double dy = current.Y - _dragStartModel.Y;

            double angle = _dragStartRotation * Math.PI / 180;
            double cos = Math.Cos(angle);
            double sin = Math.Sin(angle);

            // 设计位移 → 局部位移
            double localX = dx * cos + dy * sin;
            double localY = -dx * sin + dy * cos;

            if (SnapToGrid && GridStep > 0)
            {
                double step = GridStep;
                localX = Math.Round(localX / step) * step;
                localY = Math.Round(localY / step) * step;
            }

            double startW = Math.Max(1, _dragStartBounds.Width);
            double startH = Math.Max(1, _dragStartBounds.Height);

            double width = Math.Max(MinElementWidth, startW + _resizeSign.X * localX);
            double height = Math.Max(MinElementHeight, startH + _resizeSign.Y * localY);

            // 中心位移（局部）→ 设计位移
            double shiftLocalX = _resizeSign.X * (width - startW) / 2;
            double shiftLocalY = _resizeSign.Y * (height - startH) / 2;

            double centerXd = _dragStartBounds.X + startW / 2;
            double centerYd = _dragStartBounds.Y + startH / 2;

            double centerX = centerXd + shiftLocalX * cos - shiftLocalY * sin;
            double centerY = centerYd + shiftLocalX * sin + shiftLocalY * cos;

            double left = centerX - width / 2;
            double top = centerY - height / 2;

            if (Math.Abs(element.Width - width) > 1e-6)
                element.Width = width;

            if (Math.Abs(element.Height - height) > 1e-6)
                element.Height = height;

            if (Math.Abs(element.X - left) > 1e-6)
                element.X = left;

            if (Math.Abs(element.Y - top) > 1e-6)
                element.Y = top;
        }

        #endregion

        #region 滚轮缩放 / 中键平移

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);

            if (_dragMode != DragMode.None)
                return; // 拖着东西时滚轮不改变取景，避免"手滑一下滚轮，图元跳到别处"

            // 缩放到光标：锚点必须用视口坐标传进去，ZoomAt 内部会保持该点设计坐标不动
            double factor = e.Delta > 0 ? ZoomStep : 1 / ZoomStep;

            ZoomAt(Zoom * factor, e.GetPosition(this));

            e.Handled = true; // 吞掉：宿主若把画布塞进 ScrollViewer，让滚动条跟着滚会跟缩放打架
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.ChangedButton != MouseButton.Middle)
                return;

            // 中键只管取景器，碰不到数据，所以运行态也允许平移（操作员看大图时有用）
            _dragMode = DragMode.Pan;
            _dragStartViewport = e.GetPosition(this);
            _panStartOffset = Offset;

            CaptureMouse();
            UpdateCursorState();
            e.Handled = true;
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);

            if (e.ChangedButton == MouseButton.Middle && _dragMode == DragMode.Pan)
                EndDrag();
        }

        #endregion

        #region 命中测试与光标

        /// <summary>
        /// 从事件源沿可视树向上找承载图元的控件。
        /// 走到元素层/选中层/画布自身还没找到就算命中空白——这条"停止边界"很重要：
        /// 不写死边界的话，向上遍历会穿过画面找到画布自己的祖先控件（宿主的 ScrollViewer 之类）。
        /// </summary>
        private ScadaElement? HitTestElement(DependencyObject? source)
        {
            while (source != null && !ReferenceEquals(source, this))
            {
                if (ReferenceEquals(source, _elementLayer) || ReferenceEquals(source, _selectionLayer))
                    return null;

                if (source is ScadaElementBase { Element: { } element } control
                    && ReferenceEquals(FindContainer(element), control))
                {
                    return element;
                }

                source = source is Visual ? VisualTreeHelper.GetParent(source) : null;
            }

            return null;
        }

        /// <summary>命中选中手柄（四个角）；手柄在选中层，所以遍历到选中层就停</summary>
        private Rectangle? HitTestThumb(DependencyObject? source)
        {
            if (_thumbs == null || SelectedElement is not { } selected || !CanEditElement(selected))
                return null;

            while (source != null && !ReferenceEquals(source, this))
            {
                if (ReferenceEquals(source, _elementLayer))
                    return null;

                if (source is Rectangle rect && rect.Tag is Vector)
                {
                    for (int i = 0; i < _thumbs.Length; i++)
                    {
                        if (ReferenceEquals(_thumbs[i], rect))
                            return rect;
                    }
                }

                source = source is Visual ? VisualTreeHelper.GetParent(source) : null;
            }

            return null;
        }

        /// <summary>
        /// 光标状态：<see cref="IsReadOnlyProperty"/> 与平移期是唯一两个变量。
        /// 手柄自己的光标在建手柄时就设在 Rectangle.Cursor 上，这里不重复管
        /// （鼠标悬停判定要额外做 hit test，白给一次遍历换不到手感差别）。
        /// </summary>
        private void UpdateCursorState()
        {
            Cursor = _dragMode switch
            {
                DragMode.Pan => Cursors.Hand,
                DragMode.Move => Cursors.SizeAll,
                _ => Cursors.Arrow,
            };
        }

        #endregion
    }
}
