using System.Globalization;
using System.Windows.Data;

namespace G.Styles.Controls;

public class TreeViewKeys
{
    public static ComponentResourceKey Default => new ComponentResourceKey(typeof(TreeViewKeys), "S.TreeView.Default");
}

public class TreeViewItemKeys
{
    public static ComponentResourceKey Default => new ComponentResourceKey(typeof(TreeViewItemKeys), "S.TreeViewItem.Default");
}


public class GetFalseToHiddenConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
            return b ? Visibility.Visible : Visibility.Hidden;
        return Visibility.Hidden;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}