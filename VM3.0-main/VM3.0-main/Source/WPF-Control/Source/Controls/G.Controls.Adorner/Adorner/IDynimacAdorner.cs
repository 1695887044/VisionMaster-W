namespace G.Controls.Adorner.Adorner;

public interface IDynimacAdorner
{
    Point Offset { get; set; }
    void UpdatePosition(Point location);
}
