#if NET
#endif
namespace G.Extensions.Behvaiors.Adorners;

public interface IHitTestElementDrag : IHitTestElementDrop, IGetDragAdorner
{
    void DragEnter(UIElement element, DragEventArgs e);
    void DragLeave(UIElement element, DragEventArgs e);
    void DragOver(UIElement element, DragEventArgs e);
}

public interface IGetDropAdorner
{
    Adorner GetDropAdorner(UIElement element);
    void RemoveDropAdorner(UIElement element);
}

public interface IGetDragAdorner
{
    Adorner GetDragAdorner(UIElement element);
    void RemoveDragAdorner(UIElement element);
}

public interface IHitTestElementDrop : IGetDropAdorner
{
    bool CanDrop(UIElement element, DragEventArgs e);
    void Drop(UIElement element, DragEventArgs e);
    bool IsHitTest(UIElement element, DragEventArgs e);
}

