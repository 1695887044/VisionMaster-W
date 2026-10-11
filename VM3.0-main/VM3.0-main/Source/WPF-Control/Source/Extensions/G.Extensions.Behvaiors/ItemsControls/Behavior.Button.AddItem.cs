namespace G.Extensions.Behvaiors.ItemsControls;

public class ButtonAddItemBehavior : AddItemButtonBehaviorBase
{
    protected override void OnClick()
    {
        object addItem = CreateNewItem();
        if (addItem == null)
            return;
        this.ItemsSource.Add(addItem);
    }
}