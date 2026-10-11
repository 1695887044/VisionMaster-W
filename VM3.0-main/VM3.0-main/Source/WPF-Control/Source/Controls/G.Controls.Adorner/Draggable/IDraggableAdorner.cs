namespace G.Controls.Adorner.Draggable;

public interface IDraggableAdorner
{
    Point Offset { get; set; }
    void UpdatePosition(Point location);
    object GetData();
}

