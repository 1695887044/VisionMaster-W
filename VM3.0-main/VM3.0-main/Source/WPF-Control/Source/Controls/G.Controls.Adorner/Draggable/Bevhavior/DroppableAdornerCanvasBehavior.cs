namespace G.Controls.Adorner.Draggable.Bevhavior;

public class DroppableAdornerCanvasBehavior : DropableAdornerBehavior<Canvas>
{
    protected override void DropElement(UIElement element, Point location, Point offset)
    {
        this.AssociatedObject.Children.Add(element);

        Canvas.SetLeft(element, location.X - offset.X);

        Canvas.SetTop(element, location.Y - offset.Y);
    }
}

