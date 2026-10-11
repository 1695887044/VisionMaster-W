namespace G.Extensions.Behvaiors.ItemsControls;

public class ButtonRemoveItemBehavior : ButtonBehaviorBase
{
    protected override void OnClick()
    {
        if (this.ItemsSource == null)
            return;
        if (this.Item == null)
            return;
        this.ItemsSource.Remove(this.Item);
    }
}