namespace G.Extensions.Behvaiors.ContextMenus;

public class MouseOverContextMenuBehavior : Behavior<FrameworkElement>
{
    protected override void OnAttached()
    {
        this.AssociatedObject.MouseEnter += AssociatedObject_MouseEnter;
    }

    private void AssociatedObject_MouseEnter(object sender, MouseEventArgs e)
    {
        this.AssociatedObject.Dispatcher.BeginInvoke(() =>
        {
            this.AssociatedObject.ContextMenu.PlacementTarget = this.AssociatedObject;
            this.AssociatedObject.ContextMenu.IsOpen = true;
        });
    }

    protected override void OnDetaching()
    {
        this.AssociatedObject.MouseEnter -= AssociatedObject_MouseEnter;
    }
}
