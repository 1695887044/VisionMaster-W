using System.Globalization;

namespace G.ValueConverter.ItemsControls;

public class GetCollpseLastItemItemsControlConverter : MarkupValueConverterBase
{
    public override object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        DependencyObject item = (DependencyObject)value;
        ItemsControl ic = ItemsControl.ItemsControlFromItemContainer(item);
        bool r = ic.ItemContainerGenerator.IndexFromContainer(item)
                == ic.Items.Count - 1;
        return r ? Visibility.Collapsed : Visibility.Visible;
    }
}
