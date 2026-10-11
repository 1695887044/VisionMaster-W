using System.Globalization;

namespace G.ValueConverter.ItemsControls;

public class GetIsLastItemInItemsControlConverter : MarkupValueConverterBase
{
    public override object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        DependencyObject item = (DependencyObject)value;
        ItemsControl ic = ItemsControl.ItemsControlFromItemContainer(item);
        if (ic == null)
            return false;
        return ic.ItemContainerGenerator.IndexFromContainer(item)
                == ic.Items.Count - 1;
    }
}
