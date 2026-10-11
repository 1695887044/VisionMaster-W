namespace G.Extensions.Behvaiors.TreeViews;

public class TreeViewSelectNoneOnMouseDownBehavior : Behavior<TreeView>
{
    protected override void OnAttached()
    {
        base.OnAttached();

        this.AssociatedObject.MouseDown += AssociatedObject_MouseDown;
    }

    private void AssociatedObject_MouseDown(object sender, MouseButtonEventArgs e)
    {
        Point point = Mouse.GetPosition(this.AssociatedObject);
        var item = this.AssociatedObject.HitTest<TreeViewItem>(point);
        if (item == null)
            this.AssociatedObject.SelectNone();

    }

    protected override void OnDetaching()
    {
        this.AssociatedObject.MouseDown -= AssociatedObject_MouseDown;

    }
}

