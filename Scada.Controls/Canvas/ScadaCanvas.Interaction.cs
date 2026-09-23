using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// <see cref="ScadaCanvas"/> 的交互部分：选中、框选、拖动移动、四角改尺寸、滚轮缩放、中键平移。
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

        /// <summary>
        /// "算拖动还是算点了一下"的门限（<b>屏幕</b>像素，判定时按缩放折算回设计坐标）。
        ///
        /// 为什么需要它：框退化成一点时，若拿零宽高的矩形去做相交判定，"点落在图元包围盒里"
        /// 也会算命中——而命中测试判的是像素。两者不等价的地方正好是最常见的那类图元：
        /// 圆形、旋转过的图元、无填充的文字，它们的包围盒四角都是空的。
        /// 于是"在圆形图元旁边的空白处点一下"会莫名其妙把它选上，而这正是用户用来取消选中的动作。
        /// 与系统拖拽门限（<c>SystemParameters.MinimumHorizontalDragDistance</c> = 4）同量级即可。
        /// </summary>
        private const double BandSlopPixels = 3;

        private enum DragMode
        {
            /// <summary>没在拖</summary>
            None,

            /// <summary>左键按住图元移动</summary>
            Move,

            /// <summary>左键按住选中手柄改尺寸</summary>
            Resize,

            /// <summary>左键从空白处拖出橡皮筋框选</summary>
            RubberBand,

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
        /// 整组拖动时"每个成员按下时的 X/Y"（单元素拖动时为 null）。
        ///
        /// 为什么记起点而不是每帧在当前位置上累加：吸附是"绝对位置取整"，累加会把取整误差滚起来，
        /// 拖着拖着整组就相对错位了。有起点，任意时刻都能由"锚点目标位置 − 锚点起点"一次算出总位移，
        /// 与鼠标报了多少次事件无关。
        ///
        /// 用值元组数组而不是字典：成员就那么多、每次拖动重建一次，数组比字典少一次哈希开销，
        /// 顺序还天然与选中集合一致（调试时一眼能对上）。
        /// </summary>
        private (ScadaElement Element, double X, double Y)[]? _dragGroupStart;

        /// <summary>框选起点（设计坐标；按下时就换算好，中途缩放/平移取景器也不会让框跑偏）</summary>
        private Point _bandStartModel;

        /// <summary>
        /// 框选开始时那一批选中项的副本，用于"加选"时做并集。
        ///
        /// 必须是副本：<see cref="SetSelection"/> 换的是引用不是内容，抓着当前值做增量会算错。
        /// </summary>
        private IReadOnlyList<ScadaElement> _bandBaseSelection = Array.Empty<ScadaElement>();

        /// <summary>本次框选是"加选"（按住 Ctrl/Shift）还是"重选"</summary>
        private bool _bandAdditive;

        /// <summary>
        /// 本次拖动/改尺寸的撤销作用域（S9-d）。<b>必须用字段持有而不是 <c>using</c></b>：
        /// 一次拖动横跨 Down → 若干次 Move → Up 三个事件，作用域的开与关落在不同的回调里，
        /// 没法写成一个 using 块。中键平移不碰数据，全程为 null。
        /// </summary>
        private IScadaChangeScope? _dragScope;

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
                // Ctrl/Shift 是业界通用的"加选"修饰键（框选那一侧也认这两个），这里保持同一套语义。
                bool additive = (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0;

                if (additive && !IsSelected(element))
                {
                    // 并进集合：主选中仍是原来的首项（SetSelection 的口径），新加的排在末尾。
                    // 拖起来时它已经在集合里，所以这一下按完直接就能把新组一起搬走，不必再点第二次。
                    // 加选同样按组展开——Ctrl 点一下与直接点一下不该选出不同的一批；
                    // 已在集合里的成员跳过（把整组并进来时，其中几个往往早就选着了）。
                    var next = new List<ScadaElement>(SelectedElements);

                    foreach (var member in ExpandPointSelection(element))
                    {
                        if (!ContainsRef(next, member))
                            next.Add(member);
                    }

                    SetSelection(next);
                }
                else if (!IsSelected(element))
                {
                    // 点了个没选中的：换掉整份选中。走集合入口而不是直接写 SelectedElement，
                    // 是为了让"主选中 == 集合首项"这条不变式只有 SetSelection 一个地方需要维持。
                    // 点中的是组员就整组选上，且它自己排在首位（主选中、属性面板都跟着它走）。
                    SetSelection(ExpandPointSelection(element));
                }

                // 已经在集合里的：整组原样不动 —— 拖它就是拖整组（见 ApplyMove），
                // 点一下就把别人的选中打散，是组选里最招人烦的行为。

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

            // 空白处：起框选。
            //
            // 选中集合放在编辑器层（画布只多一条只读的多选 DP 作为渲染输入），所以这里不碰领域层，
            // 只记下起点与"这次是加选还是重选"，真正的命中计算留到松手时一次做完——
            // 拖的过程中每动一像素就把全画面图元遍历一遍，图元一多就是白白烧 CPU。
            //
            // 顺带把老行为收进同一条路径：空白处点一下（没拖，框的宽高为 0）算出空集，
            // 于是"点空白取消选中"与"重选"是同一段代码，不必再留一个分支。
            _bandAdditive = (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0;
            _bandBaseSelection = _bandAdditive ? SnapshotSelection() : Array.Empty<ScadaElement>();

            BeginDrag(DragMode.RubberBand, null, point);
            e.Handled = true;

            if (!IsKeyboardFocused)
                Focus();
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

            // 框选的"落账"必须在 EndDrag 之前：EndDrag 会把拖动状态清空，
            // 而落账要用到起点（_bandStartModel）与加选基线（_bandBaseSelection）。
            if (_dragMode == DragMode.RubberBand)
                CommitRubberBand(e.GetPosition(this));

            if (_dragMode is DragMode.Move or DragMode.Resize or DragMode.RubberBand)
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
        ///
        /// S9-d 起它还要顺手中断拖动：切画面/退编辑态可能正好发生在拖动中途（程序化切页、宿主改
        /// <see cref="IsReadOnly"/>），此时若不收口，那个撤销作用域会一直悬着，后面每一次属性写
        /// 都会被记到"移动图元"名下 —— 撤销时莫名其妙地连带挪一个图元，这种 bug 最难查。
        /// </summary>
        private void CancelPendingPress()
        {
            _pressedElement = null;
            AbortDrag();
        }

        /// <summary>
        /// 中断进行中的拖动（若有）：收作用域、清状态、还鼠标。
        ///
        /// 与 <see cref="EndDrag"/> 的分工：EndDrag 是"鼠标抬起了，正常收尾"，
        /// AbortDrag 是"拖到一半世界变了"（画面被换走、编辑态被关掉），压根等不到鼠标抬起。
        /// 两者共用 <see cref="CloseDragScope"/>，所以无论从哪条路结束，这一次拖动都只产出一条撤销记录。
        /// 已经拖出的位移照常记账（不丢），用户按 Ctrl+Z 能回到按下之前。
        /// </summary>
        private void AbortDrag()
        {
            if (_dragMode == DragMode.None)
                return;

            // 先置空模式：ReleaseMouseCapture 会回调 OnLostMouseCapture，
            // 顺序反了那边就会再走一遍收尾（幂等但白跑）。
            _dragMode = DragMode.None;
            _dragElement = null;
            _resizeSign = default;
            _dragGroupStart = null;

            CloseDragScope();
            HideRubberBand();

            if (IsMouseCaptured)
                ReleaseMouseCapture();

            UpdateCursorState();
        }

        private void BeginDrag(DragMode mode, ScadaElement? element, Point viewportPoint)
        {
            _dragMode = mode;
            _dragElement = element;
            _dragStartViewport = viewportPoint;
            _dragStartModel = ToModelPoint(viewportPoint);
            _dragGroupStart = null; // 上一次拖动的残留绝不能带进这一次（单元素拖动就是靠它为 null 分辨的）

            if (element != null)
            {
                _dragStartBounds = new Rect(element.X, element.Y, element.Width, element.Height);
                _dragStartRotation = element.Rotation;
            }

            // 整组拖动：按住的那一个当"锚点"（吸附以它为准），其余成员记下按下时的位置，
            // 之后每帧只算一次总位移，全员照着走。
            // 只在 Move 上做：改尺寸的手柄多选时本来就不给（见 UpdateSelectionVisual），
            // 框选/平移压根不碰数据。
            if (mode == DragMode.Move && SelectedElements.Count > 1)
            {
                var selection = SelectedElements;
                var group = new (ScadaElement Element, double X, double Y)[selection.Count];

                for (int i = 0; i < group.Length; i++)
                    group[i] = (selection[i], selection[i].X, selection[i].Y);

                _dragGroupStart = group;
            }

            // 一次拖动 = 一次撤销（S9-d）。鼠标每动一像素就写一次 X/Y，几十次写不合并的话
            // 撤销就成了逐帧回放；作用域把它们收成一条"移动图元"。
            // 作用域是线程级的（ScadaChangeScope），所以整组拖动只在锚点上开一次就够，
            // 组里其余图元的写入同样会汇进这一条记录——不必给每个成员各开一个。
            // 框选与中键平移只动选中态/取景器、不碰数据，所以不给它们开作用域（开了也是空记录，白走一趟）。
            _dragScope = (mode, element) switch
            {
                (DragMode.Move, { } anchor) => anchor.BeginEdit("移动图元"),
                (DragMode.Resize, { } target) => target.BeginEdit("改图元尺寸"),
                _ => null,
            };

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
            _dragGroupStart = null;

            // 收作用域放在释放捕获之前：此刻最后一次鼠标移动早已处理完，改动全都写进了 sink，
            // 关掉它就正好产出"这一次拖动"的撤销记录。零改动（按下去没动）时 sink 为空，不产记录。
            CloseDragScope();
            HideRubberBand();

            ReleaseMouseCapture();
            UpdateCursorState();
        }

        /// <summary>
        /// 收掉当前拖动的作用域（幂等）。三条结束路径都要走到这里：
        /// 正常松手（<see cref="EndDrag"/>）、鼠标被别处抢走（<see cref="OnLostMouseCapture"/>）、
        /// 以及切换画面/退出编辑态。漏一条就会留下一个"开着的作用域"，
        /// 后面每一次属性写都会悄悄记到那次拖动名下。
        /// </summary>
        private void CloseDragScope()
        {
            _dragScope?.Dispose();
            _dragScope = null;
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
                _dragGroupStart = null;
                CloseDragScope(); // 同上：异常路径也要把撤销记录收口，否则后面全记错地方
                HideRubberBand(); // 框选到一半被抢走鼠标：框不能留在画面上（它已经不跟鼠标了）
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

                case DragMode.RubberBand:
                    UpdateRubberBand(point);
                    break;
            }
        }

        /// <summary>
        /// 拖动移动。<b>多选时整组一起走</b>：按住的那个当"锚点"（吸附以它为准），
        /// 其余成员各自从按下时的位置加上同一个总位移——于是组内的相对位置一个像素都不会变。
        ///
        /// 吸附<b>只</b>作用在锚点上：若给每个成员各自吸附，它们各自的取整余量不同，
        /// 一次拖动下来整组会被拆成"各自对齐网格"的散件，而用户拖的是"这一组"。
        /// </summary>
        private void ApplyMove(Point viewportPoint)
        {
            if (_dragElement is not { } anchor)
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

            // 总位移只算一次（锚点的目标位置 − 锚点的起点位置），组内全员照搬。
            // 这也是"记起点"而不是"每帧累加"的原因：与鼠标报了多少次 Move 事件无关，不会有累积误差。
            double dx = left - _dragStartBounds.X;
            double dy = top - _dragStartBounds.Y;

            if (_dragGroupStart is { } group)
            {
                for (int i = 0; i < group.Length; i++)
                {
                    var (element, startX, startY) = group[i];

                    MoveTo(element, startX + dx, startY + dy);
                }

                return;
            }

            MoveTo(anchor, left, top);
        }

        /// <summary>
        /// 写一个图元的位置（同值不写）。
        ///
        /// 同值判断本来 <see cref="ScadaElement"/> 的 setter 里也有，这里挡一道是为了省掉每次鼠标移动的
        /// 一次依赖属性调度——鼠标事件频率远高于渲染频率，而整组拖动时这一道是乘以成员数的。
        /// </summary>
        private static void MoveTo(ScadaElement element, double x, double y)
        {
            if (Math.Abs(element.X - x) > 1e-6)
                element.X = x;

            if (Math.Abs(element.Y - y) > 1e-6)
                element.Y = y;
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

        #region 橡皮筋框选

        /// <summary>
        /// 拖拽期间更新橡皮筋矩形。
        ///
        /// 起点用的是按下时就换算好的<b>设计坐标</b>（<see cref="_bandStartModel"/>），不是视口坐标：
        /// 拖到一半滚轮缩放或中键平移时，框的起点是"画面上的某个位置"，理应钉在那个位置不动；
        /// 若存的是视口坐标，缩放一下起点就漂到别处，框会跟鼠标脱节。
        ///
        /// 宽高与线宽同样要除以 Zoom——矩形画在画面坐标系里，线宽不补偿的话放大后会变成一条粗边。
        /// </summary>
        private void UpdateRubberBand(Point viewportPoint)
        {
            if (_rubberBand == null)
                return;

            var current = ToModelPoint(viewportPoint);

            _rubberBand.Width = Math.Abs(current.X - _bandStartModel.X);
            _rubberBand.Height = Math.Abs(current.Y - _bandStartModel.Y);
            _rubberBand.StrokeThickness = 1 / Math.Max(0.01, Zoom); // 屏幕上恒为 1 像素
            _rubberBand.Visibility = Visibility.Visible;

            Canvas.SetLeft(_rubberBand, Math.Min(_bandStartModel.X, current.X));
            Canvas.SetTop(_rubberBand, Math.Min(_bandStartModel.Y, current.Y));
        }

        /// <summary>收起橡皮筋（松手、中断、鼠标被别处抢走时都要走到）</summary>
        private void HideRubberBand()
        {
            if (_rubberBand != null && _rubberBand.Visibility != Visibility.Collapsed)
                _rubberBand.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// 松手落账：把框住的图元选起来。
        ///
        /// 为什么命中计算放在这里而不是拖动过程中：拖一次鼠标会来几十上百个 Move 事件，
        /// 每个都遍历全画面图元并逐个算包围盒，图元上千就是白白烧 CPU；而用户在拖动过程中
        /// 本来也只需要看到那个框。一次遍历换"全程流畅"，这笔账很清楚。
        ///
        /// 命中判据是"图元的<b>轴对齐包围盒</b>与框相交"，不是"图元中心落在框内"：
        /// 用户画框的意思是"圈住的东西"，一个只露一角的大图元当然算圈住；反过来要求中心落框内，
        /// 画个小框去框大图元的左上角就会一无所获，手感很反直觉。
        ///
        /// 遍历顺序照 <see cref="ItemsSource"/> 原序，于是新选中集合的顺序与画面叠放次序一致，
        /// 主选中（集合首项）是叠在最下面的那个。后续的撤销、对齐看到的是稳定次序，不会随点选先后乱跳。
        /// </summary>
        private void CommitRubberBand(Point viewportPoint)
        {
            var current = ToModelPoint(viewportPoint);

            double left = Math.Min(_bandStartModel.X, current.X);
            double top = Math.Min(_bandStartModel.Y, current.Y);
            double width = Math.Abs(current.X - _bandStartModel.X);
            double height = Math.Abs(current.Y - _bandStartModel.Y);

            var band = new Rect(left, top, width, height);

            // 框退化成一点（没拖，就是"点了一下空白"）→ 选出空集。
            // 这条路径顺带把老行为收编了：取消选中与框选是同一段代码，不必再留一个分支。
            // 门限见 BandSlopPixels 的注释（不设门限的话，"点在图元包围盒空角上"会误选中它）。
            double slop = BandSlopPixels / Math.Max(0.01, Zoom);

            bool degenerate = width < slop && height < slop;

            List<ScadaElement>? hits = null;

            if (!degenerate)
            {
                foreach (var element in EnumerateItems())
                {
                    // 看不见的不入选：它紧接着就会被 PruneSelection 剔掉，先选上等于白闪一下。
                    // 锁住的<b>照选</b>——左键单击本来就选得中锁住的图元（只是拖不动），
                    // 框选若把它们排除在外，同一批图元"点得中、框不中"会让人以为框选坏了。
                    if (!IsElementShown(element))
                        continue;

                    if (!band.IntersectsWith(BoundsOf(element)))
                        continue;

                    (hits ??= new List<ScadaElement>()).Add(element);
                }
            }

            IReadOnlyList<ScadaElement> next;

            if (!_bandAdditive || _bandBaseSelection.Count == 0)
            {
                next = (IReadOnlyList<ScadaElement>?)hits ?? Array.Empty<ScadaElement>();
            }
            else
            {
                // 加选：基线在前、新命中的在后。已在基线里的不再追加——
                // 框住一个本来就选中的，不该把它从队首挪到队尾（主选中会跟着换人）。
                var union = new List<ScadaElement>(_bandBaseSelection);

                if (hits != null)
                {
                    foreach (var element in hits)
                    {
                        if (!ContainsRef(union, element))
                            union.Add(element);
                    }
                }

                next = union;
            }

            _bandBaseSelection = Array.Empty<ScadaElement>(); // 基线是一次性的，用完即弃（下次框选重新快照）

            // 结果与现状一模一样时一个字都不写：SetSelection 会连着改两个依赖属性，
            // 而"框了一下但没框到新东西"是很常见的动作，没必要为它推一次绑定通知与一次重绘。
            if (!SameSelection(SelectedElements, next))
                SetSelection(next);
        }

        #endregion

        #region 右键：弹出编辑菜单前的选中对齐

        /// <summary>
        /// 右键按下时，把光标下的图元选起来。
        ///
        /// 为什么需要这一下：上下文菜单（图层归属、叠放次序）里每一项都要落在"选中的那个图元"上，
        /// 而 <see cref="SelectedElement"/> 记的是<b>上一次左键</b>选中的东西。少了这一步，
        /// 在 B 上点右键却会改到 A —— 用户指着哪儿就该改哪儿。
        ///
        /// 刻意<b>不</b>把事件标成 Handled：本类只借这次点击对齐选中态，菜单由
        /// <c>ContextMenuService</c> 在右键抬起时弹出，谁都不该抢它的路（同样地，
        /// 空白处右键<b>不</b>清选中——那会让菜单里原有的目标凭空消失）。
        ///
        /// 运行态不参与：操作员在跑画面，那里不该出现任何编辑器菜单。
        /// </summary>
        protected override void OnPreviewMouseRightButtonDown(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseRightButtonDown(e);

            if (IsReadOnly)
                return;

            if (HitTestElement(e.OriginalSource as DependencyObject) is { } element)
                SetCurrentValue(SelectedElementProperty, element);
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
        /// 光标状态：跟着当前拖动模式走（拖动模式是唯一的变量）。
        /// 手柄自己的光标在建手柄时就设在 Rectangle.Cursor 上，这里不重复管
        /// （鼠标悬停判定要额外做 hit test，白给一次遍历换不到手感差别）。
        /// </summary>
        private void UpdateCursorState()
        {
            Cursor = _dragMode switch
            {
                DragMode.Pan => Cursors.Hand,
                DragMode.Move => Cursors.SizeAll,
                DragMode.RubberBand => Cursors.Cross, // 十字准星：画框类操作的通用语言
                _ => Cursors.Arrow,
            };
        }

        #endregion
    }
}
