namespace G.Extensions.Behvaiors.ItemsControls;

public class ButtonClearItemBehavior : ButtonBehaviorBase
{
    protected override void OnClick()
    {
        if (this.ItemsSource == null)
            return;
        this.ItemsSource.Clear();
    }
}