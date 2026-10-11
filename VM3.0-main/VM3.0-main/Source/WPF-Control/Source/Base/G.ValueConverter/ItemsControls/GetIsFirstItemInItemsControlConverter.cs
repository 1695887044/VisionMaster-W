using System.Globalization;

namespace G.ValueConverter.ItemsControls;

public class GetIsFirstItemInItemsControlConverter : MarkupValueConverterBase
{
    public override object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        DependencyObject item = (DependencyObject)value;
        ItemsControl ic = ItemsControl.ItemsControlFromItemContainer(item);
        foreach (object current in ic.Items)
        {
            UIElement cItem = ic.ItemContainerGenerator.ContainerFromItem(current) as UIElement;
            if (cItem == item)
                return true;
            if (cItem.Visibility == Visibility.Visible)
                return false;
        }
        return false;
    }
}
